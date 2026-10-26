#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib.Properties;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    public partial class ScreenRecordForm : Form
    {
        public event Action StopRequested;

        private ScreenRecordingStatus status;

        public ScreenRecordingStatus Status
        {
            get
            {
                return status;
            }
            private set
            {
                status = value;
            }
        }

        public TimeSpan Countdown { get; set; }
        public bool IsCountdown { get; private set; }
        public Stopwatch Timer { get; private set; }
        public ManualResetEvent RecordResetEvent { get; set; }

        public bool ActivateWindow { get; set; } = true;
        public float Duration { get; set; } = 0;
        public bool AskConfirmationOnAbort { get; set; } = false;

        public Rectangle RecordingRegion
        {
            get
            {
                return GetRecordingRegion(Location);
            }
        }

        protected override bool ShowWithoutActivation
        {
            get
            {
                return !ActivateWindow;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams createParams = base.CreateParams;
                createParams.ExStyle |= (int)(WindowStyles.WS_EX_TOPMOST | WindowStyles.WS_EX_TOOLWINDOW);
                return createParams;
            }
        }

        private Color borderColor = Color.Red;
        private Rectangle borderRectangle;
        private Rectangle borderRectangle0Based;
        private bool dragging;
        private bool isStartCountdown;
        private KeyboardHook countdownEscapeHook;
        private Point initialPointerLocation;
        private Rectangle initialRecordingRegion;
        private static int lastIconStatus = -1;
        private static readonly Color toolbarTransparencyKey = Color.FromArgb(31, 33, 37);
        private static readonly Color toolbarColor = Color.FromArgb(17, 28, 37);
        private Control toolbarGrip;
        private Font toolbarFont;
        private Font timerFont;
        private bool toolbarLayoutReady;
        private bool updatingToolbarLayout;
        private bool toolbarOverlapsCapture;
        private bool toolbarExcludedFromCapture;

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

        public ScreenRecordForm(Rectangle regionRectangle)
        {
            InitializeComponent();
            ShareXResources.ApplyTheme(this);
            niTray.Icon = ShareXResources.Icon;
            ConfigureRecordingToolbar();
            recordingActionTip = new ToolTip(components);

            toolbarLayoutReady = true;
            LayoutRecordingRegion(regionRectangle);

            Timer = new Stopwatch();
            UpdateTimer();

            RecordResetEvent = new ManualResetEvent(false);

            ChangeState(ScreenRecordState.Waiting);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopCountdownEscapeHook();
                if (components != null)
                {
                    components.Dispose();
                }

                if (RecordResetEvent != null)
                {
                    RecordResetEvent.Dispose();
                }
                toolbarFont?.Dispose();
                timerFont?.Dispose();
            }

            base.Dispose(disposing);
        }

        private Rectangle GetRecordingRegion(Point windowLocation)
        {
            return new Rectangle(windowLocation.X + borderRectangle0Based.X + 1,
                windowLocation.Y + borderRectangle0Based.Y + 1, borderRectangle.Width - 2, borderRectangle.Height - 2);
        }

        public void StartStopRecording()
        {
            if (isStartCountdown && Status == ScreenRecordingStatus.Waiting) return;
            if (Status == ScreenRecordingStatus.Working)
            {
                AbortRecording();
            }
            else if (Status == ScreenRecordingStatus.Recording)
            {
                Status = ScreenRecordingStatus.Stopped;
                OnStopRequested();
            }
            else if (Status == ScreenRecordingStatus.Paused)
            {
                Status = ScreenRecordingStatus.Stopped;
                RecordResetEvent?.Set();
            }
            else
            {
                RecordResetEvent?.Set();
            }
        }

        public void PauseResumeRecording()
        {
            if (isStartCountdown && Status == ScreenRecordingStatus.Waiting) return;
            if (Status == ScreenRecordingStatus.Recording)
            {
                Status = ScreenRecordingStatus.Paused;
                RecordResetEvent?.Reset();
                OnStopRequested();
            }
            else
            {
                RecordResetEvent?.Set();
            }
        }

        public void AbortRecording()
        {
            if (isStartCountdown)
            {
                StopCountdownEscapeHook();
                Timer.Reset();
                timerRefresh.Stop();
                IsCountdown = false;
                isStartCountdown = false;
            }
            Status = ScreenRecordingStatus.Aborted;
            OnStopRequested();
            RecordResetEvent?.Set();
        }

        protected void OnStopRequested()
        {
            StopRequested?.Invoke();
        }

        public void StartCountdown(int milliseconds)
        {
            IsCountdown = true;
            isStartCountdown = true;
            Countdown = TimeSpan.FromMilliseconds(milliseconds);
            countdownEscapeHook = new KeyboardHook();
            countdownEscapeHook.KeyDown += CountdownEscapeHook_KeyDown;

            Timer.Restart();
            timerRefresh.Start();
            UpdateActionAvailability();
            UpdateTimer();
        }

        public void StartRecordingTimer()
        {
            if (isStartCountdown)
            {
                Timer.Reset();
                IsCountdown = false;
            }
            isStartCountdown = false;
            StopCountdownEscapeHook();

            if (Duration > 0)
            {
                IsCountdown = true;
                Countdown = TimeSpan.FromSeconds(Duration);
            }

            borderColor = Color.Lime;
            Refresh();

            Timer.Start();
            timerRefresh.Start();
            UpdateTimer();
        }

        public void StopRecordingTimer()
        {
            Timer.Stop();
            timerRefresh.Stop();
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            if (!IsDisposed)
            {
                TimeSpan timer;

                if (IsCountdown)
                {
                    timer = Countdown - Timer.Elapsed;
                    if (timer.Ticks < 0) timer = TimeSpan.Zero;
                }
                else
                {
                    timer = Timer.Elapsed;
                }

                lblTimer.CountdownMode = isStartCountdown;
                lblTimer.RecordingMode = Status == ScreenRecordingStatus.Recording;
                lblTimer.PausedMode = Status == ScreenRecordingStatus.Paused;
                lblTimer.Text = isStartCountdown ? $"{Math.Max(1, (int)Math.Ceiling(timer.TotalSeconds))} 秒后开始" :
                    FormatRecordingTime(timer);
                lblTimer.Invalidate();
            }
        }

        public static string FormatRecordingTime(TimeSpan elapsed)
        {
            return $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }

        private void UpdateUI()
        {
            switch (Status)
            {
                case ScreenRecordingStatus.Working:
                    string trayTextWorking = "小蟒 - " + Resources.ScreenRecordForm_StartRecording_Click_tray_icon_to_stop_recording_;
                    niTray.Text = trayTextWorking.Truncate(63);
                    niTray.Icon = Resources.control_record.ToIcon();
                    btnStart.Text = "完成";
                    tsmiStart.Text = "完成";
                    tsmiStart.Image = Resources.tick;
                    break;
                case ScreenRecordingStatus.Waiting:
                case ScreenRecordingStatus.Paused:
                    if (Status == ScreenRecordingStatus.Paused)
                    {
                        string trayTextWaiting = "小蟒 - " + Resources.ScreenRecordForm_StartRecording_Click_tray_icon_to_stop_recording_;
                        niTray.Text = trayTextWaiting.Truncate(63);
                    }
                    else
                    {
                        string trayTextBeforeStart = "小蟒 - " + Resources.ScreenRecordForm_StartRecording_Click_tray_icon_to_start_recording_;
                        niTray.Text = trayTextBeforeStart.Truncate(63);
                    }
                    niTray.Icon = Resources.control_record_yellow.ToIcon();
                    btnPause.Text = Resources.Resume;
                    tsmiPause.Text = Resources.Resume;
                    tsmiPause.Image = Resources.control;
                    lblTimer.Cursor = Cursors.SizeAll;
                    borderColor = Color.FromArgb(241, 196, 27);
                    Refresh();
                    break;
                case ScreenRecordingStatus.Recording:
                    niTray.Icon = Resources.control_record.ToIcon();
                    btnPause.Text = Resources.Pause;
                    tsmiPause.Text = Resources.Pause;
                    tsmiPause.Image = Resources.control_pause;
                    lblTimer.Cursor = Cursors.Default;
                    break;
            }
            UpdateActionAvailability();
        }

        private readonly ToolTip recordingActionTip;

        private void UpdateActionAvailability()
        {
            recordingActionTip?.SetToolTip(btnStart, Status == ScreenRecordingStatus.Waiting ? "开始录制" : "结束录制并保存");
            btnStart.IsStartAction = Status == ScreenRecordingStatus.Waiting;
            btnPause.IsResumeAction = Status == ScreenRecordingStatus.Paused;
            if (Status == ScreenRecordingStatus.Waiting) btnPause.Text = Resources.Pause;
            btnStart.Enabled = !isStartCountdown;
            btnPause.Enabled = !isStartCountdown &&
                (Status == ScreenRecordingStatus.Recording || Status == ScreenRecordingStatus.Paused);
            btnAbort.Enabled = true;
            if (toolbarGrip != null)
            {
                toolbarGrip.Cursor = !isStartCountdown && (Status == ScreenRecordingStatus.Waiting || Status == ScreenRecordingStatus.Paused)
                    ? Cursors.SizeAll : Cursors.Default;
            }
        }

        private void ConfigureRecordingToolbar()
        {
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = toolbarTransparencyKey;
            TransparencyKey = toolbarTransparencyKey;
            int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);
            Font previousToolbarFont = toolbarFont, previousTimerFont = timerFont;
            toolbarFont = new Font("Microsoft YaHei UI", 9f);
            timerFont = new Font("Consolas", 9.5f);
            pInfo.AutoSize = false;
            pInfo.BackColor = toolbarColor;
            if (toolbarGrip == null)
            {
                toolbarGrip = new Control { Name = "recordingDragGrip", AccessibleName = "移动录屏选区" };
                toolbarGrip.Paint += ToolbarGrip_Paint;
                toolbarGrip.MouseDown += lblTimer_MouseDown;
                toolbarGrip.MouseMove += lblTimer_MouseMove;
                toolbarGrip.MouseUp += lblTimer_MouseUp;
                pInfo.Controls.Add(toolbarGrip);
                pInfo.Paint += RecordingToolbar_Paint;
            }
            int itemHeight = S(28), inset = S(2), gap = S(2);
            int ButtonWidth(params string[] labels)
            {
                int width = S(54);
                foreach (string label in labels)
                {
                    width = Math.Max(width, TextRenderer.MeasureText(label, toolbarFont, Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + S(30));
                }
                return width;
            }
            toolbarGrip.Bounds = new Rectangle(inset, inset, S(14), itemHeight);
            btnStart.Bounds = new Rectangle(toolbarGrip.Right + gap, inset,
                ButtonWidth(Resources.ScreenRecordForm_Start, "完成"), itemHeight);
            btnPause.Bounds = new Rectangle(btnStart.Right + gap, inset, ButtonWidth(Resources.Pause, Resources.Resume), itemHeight);
            btnAbort.Bounds = new Rectangle(btnPause.Right + gap, inset, ButtonWidth(btnAbort.Text), itemHeight);
            btnStart.Font = btnPause.Font = btnAbort.Font = toolbarFont;
            lblTimer.Font = timerFont;
            int timerWidth = ScreenRecordTimerLabel.MeasureCountdownWidth(lblTimer.Font, DeviceDpi);
            lblTimer.Bounds = new Rectangle(btnAbort.Right + S(8), inset, timerWidth, itemHeight);
            pInfo.Size = new Size(lblTimer.Right + inset, S(32));
            using (GraphicsPath outline = RoundedRectangle(new Rectangle(0, 0, pInfo.Width, pInfo.Height), S(5)))
            {
                Region previous = pInfo.Region;
                pInfo.Region = new Region(outline);
                previous?.Dispose();
            }
            previousToolbarFont?.Dispose();
            previousTimerFont?.Dispose();
            UpdateActionAvailability();
        }

        private void ToolbarGrip_Paint(object sender, PaintEventArgs e)
        {
            float scale = DeviceDpi / 96f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush dot = new SolidBrush(Color.FromArgb(125, 147, 162)))
            {
                for (int row = 0; row < 3; row++)
                for (int column = 0; column < 2; column++)
                    e.Graphics.FillEllipse(dot, (4 + column * 4) * scale,
                        toolbarGrip.Height / 2f + (row - 1) * 4 * scale - scale, 2 * scale, 2 * scale);
            }
        }

        private void RecordingToolbar_Paint(object sender, PaintEventArgs e)
        {
            int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath outline = RoundedRectangle(new Rectangle(0, 0, pInfo.Width - 1, pInfo.Height - 1), S(5)))
            using (Pen edge = new Pen(Color.FromArgb(41, 62, 76)))
            {
                e.Graphics.DrawPath(edge, outline);
                int x = lblTimer.Left - S(4);
                e.Graphics.DrawLine(edge, x, S(8), x, pInfo.Height - S(8));
            }
        }

        // Keep the capture coordinates independent of where the toolbar fits.
        // The shaped window consists only of the selection border and toolbar;
        // the selection interior and the gap remain click-through.
        private void LayoutRecordingRegion(Rectangle recordingRegion)
        {
            if (updatingToolbarLayout) return;
            updatingToolbarLayout = true;
            try
            {
                borderRectangle = recordingRegion.Offset(1);
                Rectangle screen = Screen.FromRectangle(recordingRegion).Bounds;
                int gap = Math.Max(1, (int)Math.Round(2 * DeviceDpi / 96f));
                Rectangle toolbar = PlaceRecordingToolbar(borderRectangle, pInfo.Size, screen, gap);
                Rectangle window = Rectangle.Union(borderRectangle, toolbar);
                Bounds = window;
                borderRectangle0Based = new Rectangle(borderRectangle.X - window.X, borderRectangle.Y - window.Y,
                    borderRectangle.Width, borderRectangle.Height);
                pInfo.Location = new Point(toolbar.X - window.X, toolbar.Y - window.Y);
                Region visible = new Region(borderRectangle0Based);
                visible.Exclude(borderRectangle0Based.Offset(-1));
                using (GraphicsPath outline = RoundedRectangle(pInfo.Bounds, Math.Max(1, (int)Math.Round(5 * DeviceDpi / 96f))))
                    visible.Union(outline);
                Region previous = Region;
                Region = visible;
                previous?.Dispose();
                toolbarOverlapsCapture = toolbar.IntersectsWith(recordingRegion);
                UpdateToolbarCaptureExclusion();
                Invalidate();
            }
            finally { updatingToolbarLayout = false; }
        }

        internal static Rectangle PlaceRecordingToolbar(Rectangle selectionBorder, Size toolbar, Rectangle screen, int gap)
        {
            int x = Math.Clamp(selectionBorder.Left, screen.Left, Math.Max(screen.Left, screen.Right - toolbar.Width));
            if (selectionBorder.Bottom + gap + toolbar.Height <= screen.Bottom)
                return new Rectangle(x, selectionBorder.Bottom + gap, toolbar.Width, toolbar.Height);
            if (selectionBorder.Top - gap - toolbar.Height >= screen.Top)
                return new Rectangle(x, selectionBorder.Top - gap - toolbar.Height, toolbar.Width, toolbar.Height);
            int y = Math.Clamp(selectionBorder.Top, screen.Top, Math.Max(screen.Top, screen.Bottom - toolbar.Height));
            if (selectionBorder.Right + gap + toolbar.Width <= screen.Right)
                return new Rectangle(selectionBorder.Right + gap, y, toolbar.Width, toolbar.Height);
            if (selectionBorder.Left - gap - toolbar.Width >= screen.Left)
                return new Rectangle(selectionBorder.Left - gap - toolbar.Width, y, toolbar.Width, toolbar.Height);
            // Full-screen regions have no exterior space. Keep controls reachable
            // and exclude this overlay from capture on supported Windows systems.
            y = Math.Clamp(selectionBorder.Bottom - gap - toolbar.Height, screen.Top, Math.Max(screen.Top, screen.Bottom - toolbar.Height));
            return new Rectangle(x, y, toolbar.Width, toolbar.Height);
        }

        private void UpdateToolbarCaptureExclusion()
        {
            if (!IsHandleCreated) return;
            toolbarExcludedFromCapture = toolbarOverlapsCapture && SetWindowDisplayAffinity(Handle, 0x11);
            if (!toolbarOverlapsCapture) SetWindowDisplayAffinity(Handle, 0);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            if (!toolbarLayoutReady || updatingToolbarLayout) { base.OnDpiChanged(e); return; }
            Rectangle capture = RecordingRegion;
            updatingToolbarLayout = true;
            try
            {
                base.OnDpiChanged(e);
                ConfigureRecordingToolbar();
            }
            finally { updatingToolbarLayout = false; }
            LayoutRecordingRegion(capture);
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (toolbarLayoutReady && !updatingToolbarLayout && !IsDisposed)
                LayoutRecordingRegion(RecordingRegion);
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void ChangeState(ScreenRecordState state)
        {
            this.InvokeSafe(() =>
            {
                switch (state)
                {
                    case ScreenRecordState.Waiting:
                        string trayTextWaiting = "小蟒 - " + Resources.ScreenRecordForm_StartRecording_Waiting___;
                        niTray.Text = trayTextWaiting.Truncate(63);
                        niTray.Icon = Resources.control_record_yellow.ToIcon();
                        cmsMain.Enabled = false;
                        niTray.Visible = true;
                        break;
                    case ScreenRecordState.BeforeStart:
                        cmsMain.Enabled = true;
                        UpdateUI();
                        break;
                    case ScreenRecordState.AfterStart:
                        dragging = false;
                        Status = ScreenRecordingStatus.Working;
                        UpdateUI();
                        break;
                    case ScreenRecordState.AfterRecordingStart:
                        Status = ScreenRecordingStatus.Recording;
                        StartRecordingTimer();
                        UpdateUI();
                        break;
                    case ScreenRecordState.RecordingEnd:
                        StopRecordingTimer();
                        UpdateUI();
                        break;
                    case ScreenRecordState.Encoding:
                        Hide();
                        cmsMain.Enabled = false;
                        string trayTextAfterStop = "小蟒 - " + Resources.ScreenRecordForm_StartRecording_Encoding___;
                        niTray.Text = trayTextAfterStop.Truncate(63);
                        niTray.Icon = Resources.camcorder__pencil.ToIcon();
                        break;
                }
            });
        }

        public void ChangeStateProgress(int progress)
        {
            niTray.Text = $"小蟒 - {Resources.ScreenRecordForm_StartRecording_Encoding___} {progress}%";

            if (niTray.Visible && lastIconStatus != progress)
            {
                Icon icon;

                if (progress >= 0)
                {
                    try
                    {
                        icon = Helpers.GetProgressIcon(progress, Color.FromArgb(140, 0, 36));
                    }
                    catch (Exception e)
                    {
                        DebugHelper.WriteException(e);
                        progress = -1;
                        if (lastIconStatus == progress) return;
                        icon = Resources.camcorder__pencil.ToIcon();
                    }
                }
                else
                {
                    icon = Resources.camcorder__pencil.ToIcon();
                }

                using (Icon oldIcon = niTray.Icon)
                {
                    niTray.Icon = icon;
                    oldIcon.DisposeHandle();
                }

                lastIconStatus = progress;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (Pen pen1 = new Pen(ShareXResources.Theme.BorderColor) { DashPattern = new float[] { 5, 5 } })
            using (Pen pen2 = new Pen(borderColor) { DashPattern = new float[] { 5, 5 }, DashOffset = 5 })
            {
                e.Graphics.DrawRectangleProper(pen1, borderRectangle0Based);
                e.Graphics.DrawRectangleProper(pen2, borderRectangle0Based);
            }

            base.OnPaint(e);
        }

        private void ScreenRegionForm_Shown(object sender, EventArgs e)
        {
            LayoutRecordingRegion(RecordingRegion);
            if (ActivateWindow)
            {
                this.ForceActivate();
            }
        }

        private void ScreenRecordForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (Status != ScreenRecordingStatus.Stopped)
            {
                AbortRecording();
            }
        }

        private void CountdownEscapeHook_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape || !isStartCountdown || IsDisposed || Disposing) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            BeginInvoke(new Action(AbortRecording));
        }

        private void StopCountdownEscapeHook()
        {
            if (countdownEscapeHook == null) return;
            countdownEscapeHook.KeyDown -= CountdownEscapeHook_KeyDown;
            countdownEscapeHook.Dispose();
            countdownEscapeHook = null;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && isStartCountdown)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                AbortRecording();
                return;
            }
            base.OnKeyDown(e);
        }

        private void timerRefresh_Tick(object sender, EventArgs e)
        {
            UpdateTimer();
        }

        private void btnStart_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                StartStopRecording();
            }
        }

        private void btnPause_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                PauseResumeRecording();
            }
        }

        private void btnAbort_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                using var confirmation = new RecordingAbortConfirmationForm(this);
                if (!AskConfirmationOnAbort || confirmation.ShowDialog(this) == DialogResult.OK)
                {
                    AbortRecording();
                }
            }
        }

        private void lblTimer_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !isStartCountdown &&
                (Status == ScreenRecordingStatus.Waiting || Status == ScreenRecordingStatus.Paused))
            {
                dragging = true;
                initialPointerLocation = Control.MousePosition;
                initialRecordingRegion = RecordingRegion;
                ((Control)sender).Capture = true;
            }
        }

        private void lblTimer_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                Point pointer = Control.MousePosition;
                Rectangle recordingRegion = initialRecordingRegion;
                recordingRegion.Offset(pointer.X - initialPointerLocation.X, pointer.Y - initialPointerLocation.Y);
                if (CaptureHelpers.GetScreenBounds().Contains(recordingRegion))
                {
                    LayoutRecordingRegion(recordingRegion);
                    Update();
                }
            }
        }

        private void lblTimer_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = false;
                ((Control)sender).Capture = false;
            }
        }

        private enum ScreenRecordButtonKind { Stop, Pause, Cancel }

        private sealed class ScreenRecordToolbarButton : Button
        {
            private int cornerRadius => Math.Max(3, (int)Math.Round(4f * DeviceDpi / 96f));
            private readonly ScreenRecordButtonKind kind;
            private bool hovered;
            private bool isStartAction;
            private bool isResumeAction;
            public bool IsResumeAction
            {
                get => isResumeAction;
                set
                {
                    if (isResumeAction == value) return;
                    isResumeAction = value;
                    Invalidate();
                }
            }

            public bool IsStartAction
            {
                get => isStartAction;
                set
                {
                    if (isStartAction == value) return;
                    isStartAction = value;
                    Invalidate();
                }
            }

            public ScreenRecordToolbarButton(ScreenRecordButtonKind kind)
            {
                this.kind = kind;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (Width < cornerRadius * 2 || Height < cornerRadius * 2) return;
                using (GraphicsPath outline = RoundedRectangle(new Rectangle(0, 0, Width, Height), cornerRadius))
                {
                    Region previous = Region;
                    Region = new Region(outline);
                    previous?.Dispose();
                }
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                hovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hovered = false;
                Invalidate();
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(toolbarColor);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath tile = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), cornerRadius))
                using (SolidBrush fill = new SolidBrush(Enabled && hovered ? Color.FromArgb(38, 57, 69) :
                    kind == ScreenRecordButtonKind.Stop ? Color.FromArgb(56, 37, 29) :
                    kind == ScreenRecordButtonKind.Pause ? Color.FromArgb(24, 40, 52) : toolbarColor))
                {
                    e.Graphics.FillPath(fill, tile);
                }

                Color accent = !Enabled ? Color.FromArgb(111, 115, 120) : kind == ScreenRecordButtonKind.Stop ?
                    Color.FromArgb(255, 109, 63) : kind == ScreenRecordButtonKind.Pause ?
                    Color.FromArgb(255, 185, 72) : Color.FromArgb(208, 223, 233);
                float scale = DeviceDpi / 96f;
                using (Pen line = new Pen(accent, 1.8f * scale))
                using (SolidBrush iconFill = new SolidBrush(accent))
                {
                    GraphicsState state = e.Graphics.Save();
                    e.Graphics.ScaleTransform(scale, scale);
                    line.Width = 1.8f;
                    float centerY = Height / (2 * scale);
                    line.StartCap = LineCap.Round;
                    line.EndCap = LineCap.Round;
                    if (kind == ScreenRecordButtonKind.Stop)
                    {
                        if (isStartAction) e.Graphics.FillEllipse(iconFill, 8, centerY - 4.5f, 9, 9);
                        else e.Graphics.FillRectangle(iconFill, 8, centerY - 4.5f, 9, 9);
                    }
                    else if (kind == ScreenRecordButtonKind.Pause)
                    {
                        if (IsResumeAction) e.Graphics.FillPolygon(iconFill, new[] { new PointF(8, centerY - 5), new PointF(18, centerY), new PointF(8, centerY + 5) });
                        else
                        {
                            e.Graphics.FillRectangle(iconFill, 8, centerY - 5, 3, 10);
                            e.Graphics.FillRectangle(iconFill, 14, centerY - 5, 3, 10);
                        }
                    }
                    else
                    {
                        e.Graphics.DrawLine(line, 8, centerY - 4, 16, centerY + 4);
                        e.Graphics.DrawLine(line, 16, centerY - 4, 8, centerY + 4);
                    }
                    e.Graphics.Restore(state);
                }

                TextRenderer.DrawText(e.Graphics, Text, Font,
                    new Rectangle((int)(23 * scale), 0, Width - (int)(27 * scale), Height),
                    Enabled ? Color.FromArgb(230, 237, 241) : Color.FromArgb(141, 145, 150),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
        }

        private sealed class ScreenRecordTimerLabel : Label
        {
            private int cornerRadius => Math.Max(3, (int)Math.Round(4f * DeviceDpi / 96f));
            private readonly Font countdownFont = new Font("Microsoft YaHei UI", 9f);
            public bool CountdownMode { get; set; }
            public bool RecordingMode { get; set; }
            public bool PausedMode { get; set; }

            public ScreenRecordTimerLabel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) countdownFont.Dispose();
                base.Dispose(disposing);
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (Width < cornerRadius * 2 || Height < cornerRadius * 2) return;
                using (GraphicsPath outline = RoundedRectangle(new Rectangle(0, 0, Width, Height), cornerRadius))
                {
                    Region previous = Region;
                    Region = new Region(outline);
                    previous?.Dispose();
                }
            }

            public static int MeasureCountdownWidth(Font baseFont, int dpi)
            {
                using (Font hintFont = new Font("Microsoft YaHei UI", 9f))
                {
                    const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                    int hintWidth = TextRenderer.MeasureText("999 秒后开始", hintFont, Size.Empty, flags).Width;
                    int elapsedWidth = TextRenderer.MeasureText("9999:59:59", baseFont, Size.Empty, flags).Width;
                    return Math.Max(elapsedWidth, hintWidth) + (int)Math.Round(24 * dpi / 96f);
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(toolbarColor);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = DeviceDpi / 96f;
                using (SolidBrush dot = new SolidBrush(CountdownMode || PausedMode ? Color.FromArgb(244, 188, 80) :
                    RecordingMode ? Color.FromArgb(239, 94, 105) : Color.FromArgb(139, 147, 154)))
                {
                    e.Graphics.FillEllipse(dot, 7 * scale, (Height - 6 * scale) / 2, 6 * scale, 6 * scale);
                }
                if (CountdownMode)
                {
                    TextRenderer.DrawText(e.Graphics, Text, countdownFont,
                        new Rectangle((int)(20 * scale), 0, Width - (int)(24 * scale), Height),
                        Color.FromArgb(255, 214, 132), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
                else
                {
                    TextRenderer.DrawText(e.Graphics, Text, Font,
                        new Rectangle((int)(20 * scale), 0, Width - (int)(24 * scale), Height), Color.FromArgb(230, 237, 241),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
            }
        }
    }
}
