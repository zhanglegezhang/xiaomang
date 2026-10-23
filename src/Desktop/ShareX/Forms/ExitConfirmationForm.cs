using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class ExitConfirmationForm : Form
    {
        private readonly Button cancel, exit;
        private readonly Font titleFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
        private readonly Font bodyFont = new Font("Microsoft YaHei UI", 9f);
        private readonly Font closeFont = new Font("Microsoft YaHei UI", 14f);
        internal ExitConfirmationForm()
        {
            Text = "退出小蟒？";
            FormBorderStyle = FormBorderStyle.None;
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(420, 178);
            BackColor = Color.FromArgb(29, 39, 47);
            ForeColor = Color.FromArgb(226, 234, 240);
            Font = bodyFont;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;
            var icon = new Panel { Bounds = new Rectangle(22, 23, 28, 28) };
            icon.Paint += (_, e) => MainMenuRenderer.DrawGlyph(e.Graphics, "power", icon.ClientRectangle, ForeColor);
            var title = new Label { Text = Text, Font = titleFont, AutoSize = false, Bounds = new Rectangle(62, 20, 290, 35), ForeColor = ForeColor };
            var body = new Label { Text = "退出后，贴片和快捷键将停止运行。", Bounds = new Rectangle(22, 70, 380, 30), ForeColor = Color.FromArgb(180, 192, 201) };
            cancel = MakeButton("取消", new Rectangle(190, 122, 94, 34), DialogResult.Cancel, false);
            exit = MakeButton("退出小蟒", new Rectangle(296, 122, 102, 34), DialogResult.OK, true);
            var close = MakeButton("×", new Rectangle(380, 9, 30, 30), DialogResult.Cancel, false);
            close.FlatAppearance.BorderSize = 0;
            close.Font = closeFont;
            Controls.AddRange(new Control[] { icon, title, body, cancel, exit, close });
            cancel.TabIndex = 0; exit.TabIndex = 1; close.TabIndex = 2;
            CancelButton = cancel;
            AcceptButton = cancel;
            Shown += (_, _) => cancel.Focus();
            AccessibleName = Text;
            ResumeLayout(false);
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitContent();
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            FitContent();
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
            using var path = MainMenuAppearance.Round(control.ClientRectangle, MainMenuAppearance.Scale(control, 4));
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
            using var path = MainMenuAppearance.Round(new Rectangle(0, 0, Width - 1, Height - 1), MainMenuAppearance.Scale(this, 4));
            using var pen = new Pen(Color.FromArgb(94, 109, 120));
            e.Graphics.DrawPath(pen, path);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { titleFont.Dispose(); bodyFont.Dispose(); closeFont.Dispose(); }
        }
    }
    public partial class MainForm
    {
        private bool exitConfirmationOpen;
        internal void RequestUserExit()
        {
            if (exitConfirmationOpen || IsDisposed || Disposing) return;
            exitConfirmationOpen = true;
            try
            {
                using var dialog = new ExitConfirmationForm();
                if (!Visible)
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                    dialog.Location = new Point(area.Left + (area.Width - dialog.Width) / 2, area.Top + (area.Height - dialog.Height) / 2);
                }
                if (dialog.ShowDialog(this) == DialogResult.OK) ForceClose();
            }
            finally { exitConfirmationOpen = false; }
        }
    }
}
