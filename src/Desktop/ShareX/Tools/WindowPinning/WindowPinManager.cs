#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License as published by
    the Free Software Foundation; either version 2 of the License, or
    (at your option) any later version.
*/

#endregion License Information (GPL v3)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShareX
{
    /// <summary>
    /// Lightweight, in-process DeskPins replacement. Window handles are intentionally kept
    /// in memory only; every window pinned by this instance is restored when the app exits.
    /// </summary>
    internal sealed class WindowPinManager : IDisposable
    {
        private const uint EventSystemForeground = 0x0003;
        private const uint EventSystemMinimizeStart = 0x0016;
        private const uint EventSystemMinimizeEnd = 0x0017;
        private const uint EventObjectDestroy = 0x8001;
        private const uint EventObjectShow = 0x8002;
        private const uint EventObjectHide = 0x8003;
        private const uint EventObjectLocationChange = 0x800B;
        private const uint WineventOutOfContext = 0x0000;
        private const uint WineventSkipOwnProcess = 0x0002;
        private const int ObjidWindow = 0;
        private const uint GaRoot = 2;
        private const int GwlExstyle = -20;
        private const long WsExTopmost = 0x00000008L;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoOwnerZorder = 0x0200;
        private const int VkLButton = 0x01;
        private const int VkRButton = 0x02;
        private const int VkMButton = 0x04;
        private const int VkXButton1 = 0x05;
        private const int VkXButton2 = 0x06;
        private const int GestureCooldownMilliseconds = 2000;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly IntPtr HwndNotTopmost = new IntPtr(-2);

        private readonly Control owner;
        private readonly Dictionary<IntPtr, WindowPinMarkerForm> pinnedWindows = new Dictionary<IntPtr, WindowPinMarkerForm>();
        private readonly object syncRoot = new object();
        private readonly WinEventDelegate winEventDelegate;
        private readonly Timer mouseShakeTimer;
        private readonly List<IntPtr> winEventHooks = new List<IntPtr>();
        private readonly HorizontalMouseShakeDetector mouseShakeDetector = new HorizontalMouseShakeDetector();
        private WindowPinSelectionForm selectionForm;
        private long gestureCooldownUntil;
        private bool gestureQueued;
        private bool disposed;

        internal WindowPinManager(Control owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            winEventDelegate = OnWinEvent;
            mouseShakeTimer = new Timer { Interval = 25 };
            mouseShakeTimer.Tick += MouseShakeTimer_Tick;
            AddWinEventHook(EventObjectDestroy, EventObjectHide);
            AddWinEventHook(EventObjectLocationChange, EventObjectLocationChange);
            AddWinEventHook(EventSystemMinimizeStart, EventSystemMinimizeEnd);
            AddWinEventHook(EventSystemForeground, EventSystemForeground);
        }

        internal int PinnedCount
        {
            get
            {
                lock (syncRoot) return pinnedWindows.Count;
            }
        }

        internal bool IsSelecting => selectionForm != null && !selectionForm.IsDisposed;

        internal void BeginSelection()
        {
            if (disposed) return;

            if (IsSelecting)
            {
                selectionForm.Activate();
                return;
            }

            selectionForm = new WindowPinSelectionForm();
            selectionForm.WindowSelected += SelectionForm_WindowSelected;
            selectionForm.SelectionCancelled += SelectionForm_SelectionCancelled;
            selectionForm.FormClosed += SelectionForm_FormClosed;
            selectionForm.Show();
        }

        internal void CancelSelection()
        {
            WindowPinSelectionForm form = selectionForm;
            selectionForm = null;
            if (form != null && !form.IsDisposed) form.Close();
        }

        private void SelectionForm_WindowSelected(Point screenPoint)
        {
            WindowPinSelectionForm form = selectionForm;
            selectionForm = null;
            if (form != null && !form.IsDisposed) form.Close();

            IntPtr handle = WindowFromPoint(new NativePoint(screenPoint.X, screenPoint.Y));
            handle = GetAncestor(handle, GaRoot);
            ToggleWindow(handle);
        }

        private void SelectionForm_SelectionCancelled(object sender, EventArgs e)
        {
            selectionForm = null;
        }

        private void SelectionForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (ReferenceEquals(selectionForm, sender)) selectionForm = null;
        }

        internal bool ToggleWindow(IntPtr handle)
        {
            if (disposed) return false;

            lock (syncRoot)
            {
                if (pinnedWindows.ContainsKey(handle))
                {
                    UnpinWindowCore(handle, true);
                    Notify("已取消窗口置顶。");
                    return true;
                }
            }

            if (!CanPinWindow(handle, out string error))
            {
                Notify(error);
                return false;
            }

            if (IsTopmost(handle))
            {
                Notify("该窗口已经处于置顶状态。");
                return false;
            }

            if (!SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZorder))
            {
                Notify("权限不足，无法置顶该窗口。");
                return false;
            }

            WindowPinMarkerForm marker = new WindowPinMarkerForm(handle, GetWindowDpi(handle));
            marker.UnpinRequested += (sender, e) => UnpinWindow(handle);
            lock (syncRoot) pinnedWindows[handle] = marker;
            RefreshWindow(handle, EventObjectShow);
            bool gestureAvailable = EnsureMouseShakeMonitoring();
            Notify(gestureAvailable
                ? "窗口已置顶；点击图钉或左右快速甩动鼠标可取消。"
                : "窗口已置顶；鼠标甩动监测启动失败，仍可点击图钉取消。");
            return true;
        }

        internal bool IsPinned(IntPtr handle)
        {
            lock (syncRoot) return pinnedWindows.ContainsKey(handle);
        }

        internal bool IsMouseShakeMonitoring => mouseShakeTimer.Enabled;

        internal void UnpinWindow(IntPtr handle)
        {
            if (disposed) return;
            UnpinWindowCore(handle, true);
            Notify("已取消窗口置顶。");
        }

        private void UnpinWindowCore(IntPtr handle, bool restoreWindow)
        {
            WindowPinMarkerForm marker;
            lock (syncRoot)
            {
                if (!pinnedWindows.TryGetValue(handle, out marker)) return;
                pinnedWindows.Remove(handle);
            }

            if (restoreWindow && IsWindow(handle))
            {
                SetWindowPos(handle, HwndNotTopmost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZorder);
            }

            marker.Close();
            marker.Dispose();
            StopMouseShakeMonitoringIfIdle();
        }

        private bool EnsureMouseShakeMonitoring()
        {
            if (mouseShakeTimer.Enabled) return true;
            mouseShakeDetector.Reset();
            mouseShakeTimer.Start();
            return mouseShakeTimer.Enabled;
        }

        private void StopMouseShakeMonitoringIfIdle()
        {
            if (!mouseShakeTimer.Enabled || PinnedCount > 0) return;
            mouseShakeTimer.Stop();
            mouseShakeDetector.Reset();
            gestureQueued = false;
        }

        private void MouseShakeTimer_Tick(object sender, EventArgs e)
        {
            if (disposed || !mouseShakeTimer.Enabled) return;

            Point point = Cursor.Position;
            long now = Environment.TickCount64;

            if (now < gestureCooldownUntil || ShouldSuppressMouseShake(point))
            {
                mouseShakeDetector.Reset();
            }
            else if (mouseShakeDetector.Add(point, now))
            {
                gestureCooldownUntil = now + GestureCooldownMilliseconds;
                QueueUnpinAllFromGesture();
            }
        }

        private bool ShouldSuppressMouseShake(Point point)
        {
            if (IsSelecting || IsMouseButtonDown()) return true;

            IntPtr hovered = GetAncestor(WindowFromPoint(new NativePoint(point.X, point.Y)), GaRoot);
            if (hovered == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hovered, out uint processId);
            return processId == (uint)Environment.ProcessId;
        }

        private static bool IsMouseButtonDown()
        {
            return IsKeyDown(VkLButton) || IsKeyDown(VkRButton) || IsKeyDown(VkMButton) ||
                IsKeyDown(VkXButton1) || IsKeyDown(VkXButton2);
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private void QueueUnpinAllFromGesture()
        {
            if (gestureQueued || !owner.IsHandleCreated || owner.IsDisposed) return;
            gestureQueued = true;
            try
            {
                owner.BeginInvoke((Action)(() =>
                {
                    gestureQueued = false;
                    UnpinAllFromGesture();
                }));
            }
            catch (InvalidOperationException)
            {
                gestureQueued = false;
            }
        }

        private void UnpinAllFromGesture()
        {
            IntPtr[] handles;
            lock (syncRoot)
            {
                handles = new IntPtr[pinnedWindows.Count];
                pinnedWindows.Keys.CopyTo(handles, 0);
            }

            int success = 0;
            int failed = 0;
            foreach (IntPtr handle in handles)
            {
                if (!IsWindow(handle))
                {
                    UnpinWindowCore(handle, false);
                    success++;
                }
                else if (SetWindowPos(handle, HwndNotTopmost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZorder))
                {
                    UnpinWindowCore(handle, false);
                    success++;
                }
                else
                {
                    failed++;
                }
            }

            Notify(failed == 0
                ? $"已取消 {success} 个置顶窗口。"
                : $"已取消 {success} 个置顶窗口，{failed} 个取消失败。");
        }

        private bool CanPinWindow(IntPtr handle, out string error)
        {
            error = null;
            if (handle == IntPtr.Zero || !IsWindow(handle) || !IsWindowVisible(handle) ||
                handle == GetDesktopWindow() || handle == GetShellWindow())
            {
                error = "没有选中可置顶的窗口。";
                return false;
            }

            string className = GetWindowClassName(handle);
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd" ||
                className == "Shell_SecondaryTrayWnd")
            {
                error = "桌面和任务栏不能置顶。";
                return false;
            }

            GetWindowThreadProcessId(handle, out uint processId);
            if (processId == (uint)Environment.ProcessId)
            {
                error = "小蟒同学自己的窗口不需要置顶。";
                return false;
            }

            return true;
        }

        private void AddWinEventHook(uint eventMin, uint eventMax)
        {
            IntPtr hook = SetWinEventHook(eventMin, eventMax, IntPtr.Zero, winEventDelegate, 0, 0,
                WineventOutOfContext | WineventSkipOwnProcess);
            if (hook != IntPtr.Zero) winEventHooks.Add(hook);
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr handle, int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (disposed || handle == IntPtr.Zero || (objectId != ObjidWindow && eventType >= EventObjectDestroy)) return;
            lock (syncRoot)
            {
                if (!pinnedWindows.ContainsKey(handle)) return;
            }

            if (!owner.IsHandleCreated || owner.IsDisposed) return;
            try
            {
                owner.BeginInvoke((Action)(() => RefreshWindow(handle, eventType)));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void RefreshWindow(IntPtr handle, uint eventType)
        {
            WindowPinMarkerForm marker;
            lock (syncRoot)
            {
                if (!pinnedWindows.TryGetValue(handle, out marker)) return;
            }

            if (eventType == EventObjectDestroy || !IsWindow(handle))
            {
                UnpinWindowCore(handle, false);
                return;
            }

            if (eventType == EventObjectHide || IsIconic(handle) || !IsWindowVisible(handle))
            {
                marker.Hide();
                return;
            }

            if (!TryGetWindowLayout(handle, out Rectangle windowBounds, out uint dpi))
            {
                marker.Hide();
                return;
            }

            marker.UpdateTargetDpi(dpi);
            Rectangle markerBounds = CalculateMarkerBounds(windowBounds, marker.Size);
            marker.Bounds = markerBounds;
            if (!marker.Visible) marker.Show();
            marker.BringToFront();
        }

        private static bool TryGetWindowLayout(IntPtr handle, out Rectangle bounds, out uint dpi)
        {
            bounds = Rectangle.Empty;
            dpi = 96;
            if (!GetWindowRect(handle, out NativeRect windowRect)) return false;

            bounds = Rectangle.FromLTRB(windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom);
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;

            Rectangle workingArea = Screen.FromHandle(handle).WorkingArea;
            bounds = NormalizeWindowBoundsForMarker(bounds, workingArea, IsZoomed(handle));

            dpi = GetWindowDpi(handle);
            return true;
        }

        internal static Rectangle NormalizeWindowBoundsForMarker(Rectangle windowBounds, Rectangle workingArea, bool maximized)
        {
            if (!maximized) return windowBounds;

            int visibleTop = Math.Max(windowBounds.Top, workingArea.Top);
            if (visibleTop >= windowBounds.Bottom) return windowBounds;
            return Rectangle.FromLTRB(windowBounds.Left, visibleTop, windowBounds.Right, windowBounds.Bottom);
        }

        internal static Rectangle CalculateMarkerBounds(Rectangle windowBounds, Size markerSize)
        {
            int x = windowBounds.Left + (windowBounds.Width - markerSize.Width) / 2;
            int maxX = Math.Max(windowBounds.Left, windowBounds.Right - markerSize.Width);
            x = Math.Max(windowBounds.Left, Math.Min(x, maxX));

            int y = windowBounds.Top + 3;
            int maxY = Math.Max(windowBounds.Top, windowBounds.Bottom - markerSize.Height);
            y = Math.Max(windowBounds.Top, Math.Min(y, maxY));
            return new Rectangle(x, y, markerSize.Width, markerSize.Height);
        }

        private static uint GetWindowDpi(IntPtr handle)
        {
            try
            {
                uint dpi = GetDpiForWindow(handle);
                return dpi == 0 ? 96u : dpi;
            }
            catch (EntryPointNotFoundException)
            {
                return 96u;
            }
        }

        private static bool IsTopmost(IntPtr handle)
        {
            return (GetWindowLongPtr(handle, GwlExstyle).ToInt64() & WsExTopmost) != 0;
        }

        private static string GetWindowClassName(IntPtr handle)
        {
            char[] buffer = new char[128];
            int length = GetClassName(handle, buffer, buffer.Length);
            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }

        private static IntPtr GetWindowLongPtr(IntPtr handle, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : new IntPtr(GetWindowLong32(handle, index));
        }

        private static void Notify(string text)
        {
            TaskHelpers.ShowNotificationTip(text, "DeskPins", 5000);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CancelSelection();

            IntPtr[] handles;
            lock (syncRoot)
            {
                handles = new IntPtr[pinnedWindows.Count];
                pinnedWindows.Keys.CopyTo(handles, 0);
            }
            foreach (IntPtr handle in handles) UnpinWindowCore(handle, true);

            mouseShakeTimer.Stop();
            mouseShakeTimer.Tick -= MouseShakeTimer_Tick;
            mouseShakeTimer.Dispose();
            mouseShakeDetector.Reset();

            foreach (IntPtr hook in winEventHooks) UnhookWinEvent(hook);
            winEventHooks.Clear();
        }

        private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr handle, int objectId, int childId, uint eventThread, uint eventTime);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativePoint
        {
            internal readonly int X;
            internal readonly int Y;
            internal NativePoint(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr handle, [Out] char[] className, int maxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback,
            uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

    }

    internal sealed class HorizontalMouseShakeDetector
    {
        private const int GestureWindowMilliseconds = 1200;
        private const int ReversalHysteresis = 35;
        private const int MinimumStrokePixels = 300;
        private const int MinimumTotalHorizontalPixels = 1200;
        private const int RequiredReversals = 5;

        private bool hasSample;
        private long startedAt;
        private Point lastPoint;
        private int direction;
        private int turnX;
        private int extremeX;
        private int reversals;
        private int totalHorizontal;
        private int totalVertical;

        internal bool Add(Point point, long timestamp)
        {
            if (!hasSample || timestamp < startedAt || timestamp - startedAt > GestureWindowMilliseconds)
            {
                Start(point, timestamp);
                return false;
            }

            int dx = point.X - lastPoint.X;
            int dy = point.Y - lastPoint.Y;
            lastPoint = point;
            totalHorizontal += Math.Abs(dx);
            totalVertical += Math.Abs(dy);

            if (direction == 0)
            {
                int offset = point.X - turnX;
                if (Math.Abs(offset) >= ReversalHysteresis)
                {
                    direction = Math.Sign(offset);
                    extremeX = point.X;
                }
            }
            else if (direction > 0)
            {
                if (point.X > extremeX)
                {
                    extremeX = point.X;
                }
                else if (extremeX - point.X >= ReversalHysteresis)
                {
                    RegisterReversal(-1, point.X, timestamp, extremeX - turnX);
                }
            }
            else
            {
                if (point.X < extremeX)
                {
                    extremeX = point.X;
                }
                else if (point.X - extremeX >= ReversalHysteresis)
                {
                    RegisterReversal(1, point.X, timestamp, turnX - extremeX);
                }
            }

            if (reversals >= RequiredReversals && totalHorizontal >= MinimumTotalHorizontalPixels &&
                totalHorizontal >= Math.Max(1, totalVertical) * 2)
            {
                Reset();
                return true;
            }

            return false;
        }

        private void RegisterReversal(int newDirection, int currentX, long timestamp, int completedStroke)
        {
            if (completedStroke >= MinimumStrokePixels)
            {
                reversals++;
            }
            else
            {
                reversals = 0;
                totalHorizontal = Math.Abs(extremeX - currentX);
                totalVertical = 0;
                startedAt = timestamp;
            }

            turnX = extremeX;
            direction = newDirection;
            extremeX = currentX;
        }

        private void Start(Point point, long timestamp)
        {
            hasSample = true;
            startedAt = timestamp;
            lastPoint = point;
            direction = 0;
            turnX = point.X;
            extremeX = point.X;
            reversals = 0;
            totalHorizontal = 0;
            totalVertical = 0;
        }

        internal void Reset()
        {
            hasSample = false;
            direction = 0;
            reversals = 0;
            totalHorizontal = 0;
            totalVertical = 0;
        }
    }

    internal sealed class WindowPinSelectionForm : Form
    {
        private readonly Cursor pinCursor;
        private readonly IntPtr pinCursorHandle;
        private bool pinCursorReleased;

        internal event Action<Point> WindowSelected;
        internal event EventHandler SelectionCancelled;

        internal WindowPinSelectionForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Bounds = SystemInformation.VirtualScreen;
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;
            Opacity = 0.01;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            pinCursor = WindowPinVisuals.CreatePinCursor(out pinCursorHandle);
            Cursor = pinCursor ?? Cursors.Cross;
        }

        protected override bool ShowWithoutActivation => false;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Point screenPoint = PointToScreen(e.Location);
                Hide();
                WindowSelected?.Invoke(screenPoint);
            }
            else if (e.Button == MouseButtons.Right)
            {
                Cancel();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Cancel();
                return;
            }
            base.OnKeyDown(e);
        }

        private void Cancel()
        {
            Hide();
            SelectionCancelled?.Invoke(this, EventArgs.Empty);
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && pinCursor != null && !pinCursorReleased)
            {
                pinCursorReleased = true;
                Cursor = Cursors.Default;
                pinCursor.Dispose();
                WindowPinVisuals.ReleasePinCursor(pinCursorHandle);
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class WindowPinMarkerForm : Form
    {
        private const string PinHelpText = "单击：取消当前窗口置顶\r\n左右快速甩动鼠标：取消全部置顶";
        private const int WsExToolwindow = 0x00000080;
        private const int WsExLayered = 0x00080000;
        private const int WsExNoactivate = 0x08000000;
        private readonly ToolTip pinToolTip;
        private uint targetDpi;
        private bool pointerInside;

        internal IntPtr TargetHandle { get; }
        internal bool LastLayerUpdateSucceeded { get; private set; }
        internal string ToolTipText => pinToolTip.GetToolTip(this);
        internal event EventHandler UnpinRequested;

        internal WindowPinMarkerForm(IntPtr targetHandle, uint dpi)
        {
            TargetHandle = targetHandle;
            AutoScaleMode = AutoScaleMode.None;
            Cursor = Cursors.Hand;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            pinToolTip = new ToolTip
            {
                AutoPopDelay = 7000,
                InitialDelay = 450,
                ReshowDelay = 100,
                ShowAlways = true
            };
            pinToolTip.SetToolTip(this, PinHelpText);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            UpdateTargetDpi(dpi);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolwindow | WsExLayered | WsExNoactivate;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RenderLayeredPin();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            pointerInside = true;
            RenderLayeredPin();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            pointerInside = false;
            RenderLayeredPin();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) UnpinRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) pinToolTip.Dispose();
            base.Dispose(disposing);
        }

        internal void UpdateTargetDpi(uint dpi)
        {
            dpi = dpi == 0 ? 96u : dpi;
            int side = Math.Max(20, (int)Math.Round(20 * dpi / 96d));
            if (targetDpi == dpi && ClientSize.Width == side) return;
            targetDpi = dpi;
            ClientSize = new Size(side, side);
            if (IsHandleCreated) RenderLayeredPin();
        }

        private void RenderLayeredPin()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;

            Color lineColor = pointerInside ? WindowPinVisuals.MarkerHoverColor : WindowPinVisuals.MarkerColor;
            using Bitmap bitmap = WindowPinVisuals.CreateRefinedPinBitmap(new Size(Width, Height), lineColor);
            UpdateLayeredBitmap(bitmap, Location);
        }

        private void UpdateLayeredBitmap(Bitmap bitmap, Point location)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = SelectObject(memoryDc, bitmapHandle);
            try
            {
                NativePoint destination = new NativePoint(location.X, location.Y);
                NativeSize size = new NativeSize(bitmap.Width, bitmap.Height);
                NativePoint source = new NativePoint(0, 0);
                BlendFunction blend = new BlendFunction
                {
                    BlendOp = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = 1
                };
                LastLayerUpdateSucceeded = UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                    memoryDc, ref source, 0, ref blend, 2);
            }
            finally
            {
                SelectObject(memoryDc, oldBitmap);
                DeleteObject(bitmapHandle);
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
            internal NativePoint(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            internal int Width;
            internal int Height;
            internal NativeSize(int width, int height) { Width = width; Height = height; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            internal byte BlendOp;
            internal byte BlendFlags;
            internal byte SourceConstantAlpha;
            internal byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr handle, IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr handle, IntPtr destinationDc, ref NativePoint destination,
            ref NativeSize size, IntPtr sourceDc, ref NativePoint source, int colorKey, ref BlendFunction blend, int flags);
    }

    internal static class WindowPinVisuals
    {
        internal static Color MarkerColor => Color.FromArgb(242, 229, 57, 53);
        internal static Color MarkerHoverColor => Color.FromArgb(255, 255, 82, 82);

        internal static Bitmap CreateRefinedPinBitmap(Size size, Color lineColor)
        {
            Bitmap bitmap = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float scale = Math.Min(size.Width, size.Height) / 20f;
            RectangleF frameBounds = new RectangleF(0.5f * scale, 0.5f * scale,
                size.Width - scale, size.Height - scale);
            using (GraphicsPath frame = CreateRoundedRectanglePath(frameBounds, 4f * scale))
            using (Brush background = new SolidBrush(Color.FromArgb(225, 214, 225, 219)))
            using (Pen border = new Pen(Color.FromArgb(145, 255, 255, 255), Math.Max(1f, scale)))
            {
                graphics.FillPath(background, frame);
                graphics.DrawPath(border, frame);
            }

            int inset = Math.Max(3, (int)Math.Round(3 * scale));
            DrawRefinedPin(graphics, Rectangle.Inflate(new Rectangle(Point.Empty, size), -inset, -inset), lineColor);
            return bitmap;
        }

        private static GraphicsPath CreateRoundedRectanglePath(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        internal static Bitmap CreateMenuImage()
        {
            Bitmap bitmap = new Bitmap(24, 24);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            DrawPin(graphics, new Rectangle(2, 2, 20, 20), MarkerColor, Color.White);
            return bitmap;
        }

        internal static Cursor CreatePinCursor(out IntPtr handle)
        {
            handle = IntPtr.Zero;
            try
            {
                Bitmap bitmap = new Bitmap(32, 32);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    DrawPin(graphics, new Rectangle(2, 1, 24, 24), MarkerHoverColor, Color.White);
                }
                handle = bitmap.GetHicon();
                bitmap.Dispose();
                return new Cursor(handle);
            }
            catch
            {
                if (handle != IntPtr.Zero)
                {
                    DestroyIcon(handle);
                    handle = IntPtr.Zero;
                }
                return null;
            }
        }

        internal static void ReleasePinCursor(IntPtr handle)
        {
            if (handle != IntPtr.Zero) DestroyIcon(handle);
        }

        internal static void DrawPin(Graphics graphics, Rectangle bounds, Color fill, Color outline)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Math.Min(bounds.Width, bounds.Height) / 20f;
            float cx = bounds.Left + bounds.Width / 2f;
            using GraphicsPath path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(cx - 5f * scale, bounds.Top + 3f * scale),
                new PointF(cx + 5f * scale, bounds.Top + 3f * scale),
                new PointF(cx + 3.2f * scale, bounds.Top + 8f * scale),
                new PointF(cx + 6f * scale, bounds.Top + 11f * scale),
                new PointF(cx + 1.2f * scale, bounds.Top + 12.5f * scale),
                new PointF(cx, bounds.Top + 19f * scale),
                new PointF(cx - 1.2f * scale, bounds.Top + 12.5f * scale),
                new PointF(cx - 6f * scale, bounds.Top + 11f * scale),
                new PointF(cx - 3.2f * scale, bounds.Top + 8f * scale)
            });
            using Brush brush = new SolidBrush(fill);
            using Pen pen = new Pen(outline, Math.Max(1f, scale));
            graphics.FillPath(brush, path);
            graphics.DrawPath(pen, path);
        }

        internal static void DrawRefinedPin(Graphics graphics, Rectangle bounds, Color lineColor)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = Math.Min(bounds.Width, bounds.Height) / 18f;
            graphics.TranslateTransform(bounds.Left, bounds.Top);
            graphics.ScaleTransform(scale, scale);

            using GraphicsPath path = new GraphicsPath();
            path.StartFigure();
            path.AddLine(5.2f, 3.2f, 12.8f, 3.2f);
            path.AddLine(11.9f, 7.1f, 14.1f, 9.5f);
            path.AddLine(9.8f, 9.5f, 9f, 15.7f);
            path.AddLine(8.2f, 9.5f, 3.9f, 9.5f);
            path.AddLine(6.1f, 7.1f, 5.2f, 3.2f);
            path.CloseFigure();

            using Pen contrastPen = new Pen(Color.FromArgb(185, 255, 255, 255), 3.2f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using Pen linePen = new Pen(lineColor, 1.45f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using Brush fillBrush = new SolidBrush(Color.FromArgb(Math.Min(150, (int)lineColor.A), lineColor));
            graphics.DrawPath(contrastPen, path);
            graphics.FillPath(fillBrush, path);
            graphics.DrawPath(linePen, path);
            graphics.ResetTransform();
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
