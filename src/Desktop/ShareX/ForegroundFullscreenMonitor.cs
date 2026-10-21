using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShareX
{
    // Window events give prompt transitions; the low-rate fallback also covers games
    // which do not publish a location event. Disabled means no hooks and no polling.
    internal sealed class ForegroundFullscreenMonitor : IDisposable
    {
        private readonly Control owner;
        private readonly Action<bool> changed;
        private readonly WinEventProc callback;
        private readonly Timer timer;
        private IntPtr foregroundHook, locationHook;
        private bool enabled, queued, disposed;

        internal ForegroundFullscreenMonitor(Control owner, Action<bool> changed)
        {
            this.owner = owner;
            this.changed = changed;
            callback = WindowChanged;
            timer = new Timer { Interval = 500 };
            timer.Tick += (_, _) => Sample();
            owner.Disposed += OwnerDisposed;
        }

        internal void Update(bool value)
        {
            if (disposed) return;
            if (enabled != value)
            {
                enabled = value;
                if (enabled)
                {
                    foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0);
                    locationHook = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, callback, 0, 0, 0);
                    timer.Start();
                }
                else Stop();
            }
            Sample();
        }

        private void WindowChanged(IntPtr hook, uint kind, IntPtr window, int objectId, int child, uint thread, uint time)
        {
            if (disposed || !enabled || queued || !owner.IsHandleCreated || owner.IsDisposed) return;
            if (kind == 0x800B && (objectId != 0 || window != NativeMethods.GetForegroundWindow())) return;
            queued = true;
            try
            {
                owner.BeginInvoke(new Action(() => { queued = false; if (!disposed && enabled) Sample(); }));
            }
            catch (InvalidOperationException) { queued = false; }
        }

        private void Sample() => changed(enabled && IsForegroundFullscreen());

        private void OwnerDisposed(object sender, EventArgs e) => Dispose();

        internal static bool IsForegroundFullscreen()
        {
            IntPtr window = NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero || IsIconic(window)) return false;
            string name = new WindowInfo(window).ClassName;
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
            if (!GetClientRect(window, out Rect client) || client.Right <= 0 || client.Bottom <= 0) return false;
            Point origin = Point.Empty;
            if (!ClientToScreen(window, ref origin)) return false;
            Rectangle bounds = new(origin.X, origin.Y, client.Right, client.Bottom);
            Rectangle screen = Screen.FromHandle(window).Bounds;
            // Client bounds exclude a maximized window's caption and invisible resize border.
            return bounds.Left <= screen.Left && bounds.Top <= screen.Top &&
                bounds.Right >= screen.Right && bounds.Bottom >= screen.Bottom;
        }

        private void Stop()
        {
            timer.Stop();
            if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
            if (locationHook != IntPtr.Zero) UnhookWinEvent(locationHook);
            foregroundHook = locationHook = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.Disposed -= OwnerDisposed;
            Stop();
            timer.Dispose();
        }

        private delegate void WinEventProc(IntPtr hook, uint kind, IntPtr window, int objectId, int child, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    }
}
