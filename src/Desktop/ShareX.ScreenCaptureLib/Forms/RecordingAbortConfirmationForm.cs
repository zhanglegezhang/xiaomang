using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal sealed class RecordingAbortConfirmationForm : Form
    {
        private readonly Button cancel, exit;
        private readonly Font titleFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
        private readonly Font bodyFont = new Font("Microsoft YaHei UI", 9f);
        private readonly Font closeFont = new Font("Microsoft YaHei UI", 14f);
        private readonly Form anchor;
        internal RecordingAbortConfirmationForm(Form anchor)
        {
            this.anchor = anchor; anchor.LocationChanged += AnchorChanged; anchor.SizeChanged += AnchorChanged; Text = "放弃本次录制？";
            FormBorderStyle = FormBorderStyle.None;
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(420, 178);
            BackColor = Color.FromArgb(29, 39, 47);
            ForeColor = Color.FromArgb(226, 234, 240);
            Font = bodyFont;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            var icon = new Panel { Bounds = new Rectangle(22, 23, 28, 28) };
            icon.Paint += (_, e) => DrawStop(e.Graphics, icon.ClientRectangle);
            var title = new Label { Text = Text, Font = titleFont, AutoSize = false, Bounds = new Rectangle(62, 20, 290, 35), ForeColor = ForeColor };
            var body = new Label { Text = "放弃后，本次录制内容不会保存", Bounds = new Rectangle(22, 70, 380, 30), ForeColor = Color.FromArgb(180, 192, 201) };
            cancel = MakeButton("继续录制", new Rectangle(190, 122, 94, 34), DialogResult.Cancel, false);
            exit = MakeButton("放弃录制", new Rectangle(296, 122, 102, 34), DialogResult.OK, true);
            var close = MakeButton("×", new Rectangle(380, 9, 30, 30), DialogResult.Cancel, false);
            close.FlatAppearance.BorderSize = 0;
            close.Font = closeFont;
            Controls.AddRange(new Control[] { icon, title, body, cancel, exit, close });
            cancel.TabIndex = 0; exit.TabIndex = 1; close.TabIndex = 2;
            CancelButton = cancel;
            AcceptButton = cancel;
            Shown += (_, _) => { PositionNearAnchor(); cancel.Focus(); };
            AccessibleName = Text;
            ResumeLayout(false);
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitContent(); PositionNearAnchor();
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            FitContent(); PositionNearAnchor();
        }
        private void FitContent()
        {
            int S(int n) => (int)Math.Ceiling(n * DeviceDpi / 96f);
            Label title = null, body = null;
            Panel icon = null;
            Button close = null;
            foreach (Control c in Controls)
            {
                if (c is Label label) { if (label.Text == Text) title = label; else body = label; }
                if (c is Panel panel) icon = panel;
                if (c is Button button && button.Text == "×") close = button;
            }
            if (title == null || body == null || icon == null || close == null) return;
            Size Measure(Control c) => TextRenderer.MeasureText(c.Text, c.Font, Size.Empty, TextFormatFlags.SingleLine);
            SuspendLayout();
            int margin = S(16), gap = S(8);
            int width = Math.Max(S(350), Measure(body).Width + margin * 2);
            int titleHeight = Math.Max(S(30), Measure(title).Height + S(4));
            int bodyY = margin + titleHeight + S(8);
            int bodyHeight = Measure(body).Height + S(4);
            int buttonsY = bodyY + bodyHeight + S(12);
            int buttonHeight = Math.Max(S(30), Measure(exit).Height + S(10));
            int exitWidth = Math.Max(S(90), Measure(exit).Width + S(24));
            int cancelWidth = Math.Max(S(80), Measure(cancel).Width + S(24));
            ClientSize = new Size(width, buttonsY + buttonHeight + margin);
            icon.Bounds = new Rectangle(margin, margin + (titleHeight - S(28)) / 2, S(28), S(28));
            title.Bounds = new Rectangle(S(54), margin, width - S(100), titleHeight);
            body.Bounds = new Rectangle(margin, bodyY, width - margin * 2, bodyHeight);
            exit.Bounds = new Rectangle(width - margin - exitWidth, buttonsY, exitWidth, buttonHeight);
            cancel.Bounds = new Rectangle(exit.Left - gap - cancelWidth, buttonsY, cancelWidth, buttonHeight);
            close.Bounds = new Rectangle(width - S(40), S(9), S(30), S(30));
            ApplySmallCorners(this);
            ApplySmallCorners(cancel);
            ApplySmallCorners(exit);
            ResumeLayout(false);
        }
        private static void ApplySmallCorners(Control control)
        {
            using var path = Round(control.ClientRectangle, (int)Math.Round(4 * control.DeviceDpi / 96f));
            var oldRegion = control.Region;
            control.Region = new Region(path);
            oldRegion?.Dispose();
        }
        private Button MakeButton(string text, Rectangle bounds, DialogResult result, bool primary)
        {
            var button = new Button { Text = text, Bounds = bounds, DialogResult = result,
                FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(255, 139, 67) : BackColor,
                ForeColor = primary ? Color.FromArgb(25, 33, 39) : ForeColor, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = Color.FromArgb(110, 126, 139);
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 157, 94) : Color.FromArgb(47, 67, 83);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(230, 119, 50) : Color.FromArgb(53, 76, 94);
            return button;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Round(new Rectangle(0, 0, Width - 1, Height - 1), (int)Math.Round(4 * DeviceDpi / 96f));
            using var pen = new Pen(Color.FromArgb(94, 109, 120));
            e.Graphics.DrawPath(pen, path);
        }
        private void AnchorChanged(object sender, EventArgs e) => PositionNearAnchor();
        private void PositionNearAnchor()
        {
            if (anchor.IsDisposed || IsDisposed) return;
            Location = Place(anchor.Bounds, Size, Screen.FromControl(anchor).WorkingArea, (int)Math.Round(8 * DeviceDpi / 96f));
        }
        internal static Point Place(Rectangle anchorBounds, Size size, Rectangle area, int gap)
        {
            int x = anchorBounds.Left + (anchorBounds.Width - size.Width) / 2;
            int y = anchorBounds.Top - gap - size.Height;
            if (y < area.Top) y = anchorBounds.Bottom + gap;
            return new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width)),
                Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
        }
        private static GraphicsPath Round(Rectangle rect, int radius)
        {
            var path = new GraphicsPath(); int d = radius * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right-d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right-d, rect.Bottom-d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom-d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        private static void DrawStop(Graphics g, Rectangle rect)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float unit = Math.Min(rect.Width, rect.Height) / 28f;
            using var pen = new Pen(Color.FromArgb(255,139,67), 2 * unit);
            g.DrawEllipse(pen, 2*unit, 2*unit, 24*unit, 24*unit);
            using var brush = new SolidBrush(pen.Color);
            g.FillRectangle(brush, 9*unit, 9*unit, 10*unit, 10*unit);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { anchor.LocationChanged -= AnchorChanged; anchor.SizeChanged -= AnchorChanged; titleFont.Dispose(); bodyFont.Dispose(); closeFont.Dispose(); }
        }
    }
}