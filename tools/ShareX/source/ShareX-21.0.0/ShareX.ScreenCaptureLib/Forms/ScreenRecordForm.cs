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
        private Point initialLocation;
        private static int lastIconStatus = -1;
        private const int panelOffset = 3;
        private static readonly Color toolbarTransparencyKey = Color.FromArgb(31, 33, 37);
        private ToolbarBackdropForm toolbarBackdrop;

        public ScreenRecordForm(Rectangle regionRectangle)
        {
            InitializeComponent();
            ShareXResources.ApplyTheme(this);
            niTray.Icon = ShareXResources.Icon;
            ConfigureRecordingToolbar();

            borderRectangle = regionRectangle.Offset(1);
            borderRectangle0Based = new Rectangle(0, 0, borderRectangle.Width, borderRectangle.Height);

            Location = borderRectangle.Location;
            int windowWidth = Math.Max(borderRectangle.Width, pInfo.Width);
            Size = new Size(windowWidth, borderRectangle.Height + panelOffset + pInfo.Height);
            pInfo.Location = new Point(0, borderRectangle.Height + panelOffset);

            Region region = new Region(ClientRectangle);
            region.Exclude(borderRectangle0Based.Offset(-1));
            region.Exclude(new Rectangle(0, borderRectangle.Height, windowWidth, panelOffset));
            if (borderRectangle.Width < pInfo.Width)
            {
                region.Exclude(new Rectangle(borderRectangle.Width, 0, pInfo.Width - borderRectangle.Width, borderRectangle.Height));
            }
            else if (borderRectangle.Width > pInfo.Width)
            {
                region.Exclude(new Rectangle(pInfo.Width, borderRectangle.Height + panelOffset, borderRectangle.Width - pInfo.Width, pInfo.Height));
            }
            using (GraphicsPath toolbarOutline = RoundedRectangle(pInfo.Bounds, 12))
            using (Region squareToolbarCorners = new Region(pInfo.Bounds))
            {
                squareToolbarCorners.Exclude(toolbarOutline);
                region.Exclude(squareToolbarCorners);
            }
            Region = region;

            Timer = new Stopwatch();
            UpdateTimer();

            RecordResetEvent = new ManualResetEvent(false);

            ChangeState(ScreenRecordState.Waiting);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeToolbarBackdrop();
                StopCountdownEscapeHook();
                if (components != null)
                {
                    components.Dispose();
                }

                if (RecordResetEvent != null)
                {
                    RecordResetEvent.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private Rectangle GetRecordingRegion(Point windowLocation)
        {
            return new Rectangle(windowLocation.X + 1, windowLocation.Y + 1, borderRectangle.Width - 2, borderRectangle.Height - 2);
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
                    btnStart.Text = Resources.ScreenRecordForm_Stop;
                    tsmiStart.Text = Resources.ScreenRecordForm_Stop;
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

        private void UpdateActionAvailability()
        {
            btnStart.IsStartAction = Status == ScreenRecordingStatus.Waiting;
            btnPause.IsResumeAction = Status == ScreenRecordingStatus.Paused;
            if (Status == ScreenRecordingStatus.Waiting) btnPause.Text = Resources.Pause;
            btnStart.Enabled = !isStartCountdown;
            btnPause.Enabled = !isStartCountdown &&
                (Status == ScreenRecordingStatus.Recording || Status == ScreenRecordingStatus.Paused);
            btnAbort.Enabled = true;
        }

        private void ConfigureRecordingToolbar()
        {
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            TransparencyKey = toolbarTransparencyKey;
            int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);
            Font toolbarFont = new Font("Microsoft YaHei UI", 9f);
            pInfo.AutoSize = false;
            pInfo.BackColor = toolbarTransparencyKey;
            int itemHeight = S(36), inset = S(4), gap = S(4), buttonWidth = S(78);
            btnStart.Bounds = new Rectangle(inset, inset, buttonWidth, itemHeight);
            btnPause.Bounds = new Rectangle(btnStart.Right + gap, inset, buttonWidth, itemHeight);
            btnAbort.Bounds = new Rectangle(btnPause.Right + gap, inset, buttonWidth, itemHeight);
            btnStart.Font = btnPause.Font = btnAbort.Font = toolbarFont;
            lblTimer.Font = new Font("Consolas", 10f);
            int timerWidth = ScreenRecordTimerLabel.MeasureCountdownWidth(lblTimer.Font, DeviceDpi);
            lblTimer.Bounds = new Rectangle(btnAbort.Right + gap, inset, timerWidth, itemHeight);
            pInfo.Size = new Size(lblTimer.Right + inset, S(44));
            using (GraphicsPath outline = RoundedRectangle(new Rectangle(0, 0, pInfo.Width, pInfo.Height), 12))
            {
                pInfo.Region = new Region(outline);
            }
            UpdateActionAvailability();
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
            toolbarBackdrop = new ToolbarBackdropForm(pInfo.Size);
            toolbarBackdrop.Location = pInfo.PointToScreen(Point.Empty);
            toolbarBackdrop.Show();
            Owner = toolbarBackdrop;
            BringToFront();
            LocationChanged += ScreenRecordForm_LocationChanged;
            if (ActivateWindow)
            {
                this.ForceActivate();
            }
        }

        private void ScreenRecordForm_LocationChanged(object sender, EventArgs e)
        {
            if (toolbarBackdrop != null && !toolbarBackdrop.IsDisposed)
            {
                toolbarBackdrop.Location = pInfo.PointToScreen(Point.Empty);
            }
        }

        private void DisposeToolbarBackdrop()
        {
            if (toolbarBackdrop == null) return;
            LocationChanged -= ScreenRecordForm_LocationChanged;
            Owner = null;
            toolbarBackdrop.Dispose();
            toolbarBackdrop = null;
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
                if (!AskConfirmationOnAbort || MessageBox.Show(Resources.ScreenRecordForm_ConfirmCancel, "小蟒", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    AbortRecording();
                }
            }
        }

        private void lblTimer_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && (Status == ScreenRecordingStatus.Waiting || Status == ScreenRecordingStatus.Paused))
            {
                dragging = true;
                initialLocation = e.Location;
            }
        }

        private void lblTimer_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                Point newLocation = new Point(Location.X + e.X - initialLocation.X, Location.Y + e.Y - initialLocation.Y);
                Rectangle recordingRegion = GetRecordingRegion(newLocation);
                if (CaptureHelpers.GetScreenBounds().Contains(recordingRegion))
                {
                    Location = newLocation;
                    Update();
                }
                else
                {
                    initialLocation = e.Location;
                }
            }
        }

        private void lblTimer_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = false;
            }
        }

        private enum ScreenRecordButtonKind { Stop, Pause, Cancel }

        private sealed class ToolbarBackdropForm : Form
        {
            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams createParams = base.CreateParams;
                    createParams.ExStyle |= (int)(WindowStyles.WS_EX_TRANSPARENT | WindowStyles.WS_EX_TOOLWINDOW);
                    return createParams;
                }
            }

            public ToolbarBackdropForm(Size size)
            {
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                BackColor = Color.Black;
                Opacity = 0.1;
                Size = size;
                using (GraphicsPath outline = RoundedRectangle(new Rectangle(0, 0, Width, Height), 12))
                {
                    Region = new Region(outline);
                }
            }
        }

        private sealed class ScreenRecordToolbarButton : Button
        {
            private int cornerRadius => Math.Max(4, (int)Math.Round(7f * DeviceDpi / 96f));
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
                e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(31, 33, 37));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath tile = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), cornerRadius))
                using (SolidBrush fill = new SolidBrush(Enabled && hovered ? Color.FromArgb(48, 63, 75) : Color.FromArgb(34, 43, 51)))
                using (Pen edge = new Pen(Color.FromArgb(53, 67, 77)))
                {
                    e.Graphics.FillPath(fill, tile);
                    e.Graphics.DrawPath(edge, tile);
                }

                Color accent = !Enabled ? Color.FromArgb(111, 115, 120) : kind == ScreenRecordButtonKind.Stop ?
                    Color.FromArgb(239, 94, 105) : kind == ScreenRecordButtonKind.Pause ?
                    (IsResumeAction ? Color.FromArgb(84, 207, 151) : Color.FromArgb(244, 188, 80)) : Color.FromArgb(220, 230, 237);
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
                        if (isStartAction) e.Graphics.FillEllipse(iconFill, 12, centerY - 5, 10, 10);
                        else e.Graphics.FillRectangle(iconFill, 12, centerY - 5, 10, 10);
                    }
                    else if (kind == ScreenRecordButtonKind.Pause)
                    {
                        if (IsResumeAction) e.Graphics.FillPolygon(iconFill, new[] { new PointF(12, centerY - 6), new PointF(23, centerY), new PointF(12, centerY + 6) });
                        else
                        {
                            e.Graphics.FillRectangle(iconFill, 12, centerY - 6, 3, 12);
                            e.Graphics.FillRectangle(iconFill, 19, centerY - 6, 3, 12);
                        }
                    }
                    else
                    {
                        e.Graphics.DrawLine(line, 12, centerY - 5, 22, centerY + 5);
                        e.Graphics.DrawLine(line, 22, centerY - 5, 12, centerY + 5);
                    }
                    e.Graphics.Restore(state);
                }

                TextRenderer.DrawText(e.Graphics, Text, Font,
                    new Rectangle((int)(28 * scale), 0, Width - (int)(34 * scale), Height),
                    Enabled ? Color.White : Color.FromArgb(141, 145, 150),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class ScreenRecordTimerLabel : Label
        {
            private int cornerRadius => Math.Max(4, (int)Math.Round(7f * DeviceDpi / 96f));
            public bool CountdownMode { get; set; }
            public bool RecordingMode { get; set; }
            public bool PausedMode { get; set; }

            public ScreenRecordTimerLabel()
            {
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

            public static int MeasureCountdownWidth(Font baseFont, int dpi)
            {
                using (Font hintFont = new Font("Microsoft YaHei UI", 9.5f))
                {
                    const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                    int hintWidth = TextRenderer.MeasureText("999 秒后开始", hintFont, Size.Empty, flags).Width;
                    int elapsedWidth = TextRenderer.MeasureText("9999:59:59", baseFont, Size.Empty, flags).Width;
                    return Math.Max(elapsedWidth, hintWidth) + (int)Math.Round(34 * dpi / 96f);
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(31, 33, 37));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath tile = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), cornerRadius))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(34, 43, 51)))
                {
                    e.Graphics.FillPath(fill, tile);
                }
                float scale = DeviceDpi / 96f;
                using (SolidBrush dot = new SolidBrush(CountdownMode || PausedMode ? Color.FromArgb(244, 188, 80) :
                    RecordingMode ? Color.FromArgb(239, 94, 105) : Color.FromArgb(139, 147, 154)))
                {
                    e.Graphics.FillEllipse(dot, 10 * scale, (Height - 7 * scale) / 2, 7 * scale, 7 * scale);
                }
                if (CountdownMode)
                {
                    using (Font hintFont = new Font("Microsoft YaHei UI", 9.5f))
                    {
                        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter |
                            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                        TextRenderer.DrawText(e.Graphics, Text, hintFont,
                            new Rectangle((int)(24 * scale), 0, Width - (int)(30 * scale), Height),
                            Color.FromArgb(255, 214, 132), flags);
                    }
                }
                else
                {
                    TextRenderer.DrawText(e.Graphics, Text, Font,
                        new Rectangle((int)(24 * scale), 0, Width - (int)(30 * scale), Height), Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}
