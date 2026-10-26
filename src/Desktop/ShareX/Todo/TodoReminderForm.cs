using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ShareX
{
    // Persistent reminders: closing acknowledges all currently displayed entries.
    public sealed class TodoReminderForm : Form
    {
        private readonly Dictionary<Guid, (string Text, string Time, Action Acknowledge)> entries = new();
        private readonly TextBox message;
        private readonly Button acknowledge, close;
        private Font titleFont, bodyFont, smallFont, closeFont;
        private int layoutDpi;
        private Rectangle bellBounds, clockBounds, titleBounds, timeBounds;
        private string timeText = "";
        private bool positioned;
        protected override bool ShowWithoutActivation => true;

        public TodoReminderForm()
        {
            Text = "待办提醒";
            AccessibleName = Text;
            ShowInTaskbar = false;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(25, 35, 42);
            ForeColor = Color.FromArgb(226, 234, 240);
            message = new TextBox { Name = "reminderContent", Multiline = true, ReadOnly = true,
                WordWrap = true, BorderStyle = BorderStyle.None, BackColor = BackColor,
                ForeColor = ForeColor, TabStop = false, AccessibleName = "提醒内容" };
            acknowledge = MakeButton("知道了", true);
            close = MakeButton("×", false);
            close.AccessibleName = "关闭提醒";
            Controls.AddRange(new Control[] { message, acknowledge, close });
            acknowledge.TabIndex = 0;
            close.TabIndex = 1;
            AcceptButton = acknowledge;
            CancelButton = close;
            LayoutContent(DeviceDpi);
            Shown += (_, _) => { var area = Screen.FromPoint(Cursor.Position).WorkingArea; Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16); positioned = true; LayoutContent(DeviceDpi); };
        }

        private Button MakeButton(string text, bool primary)
        {
            var button = new Button { Text = text, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(255, 135, 60) : BackColor,
                ForeColor = primary ? Color.FromArgb(25, 33, 39) : Color.FromArgb(170, 187, 199),
                UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 157, 94) : Color.FromArgb(47, 60, 70);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(230, 119, 50) : Color.FromArgb(57, 70, 80);
            button.Click += (_, _) => Close();
            return button;
        }

        public void AddReminder(TodoItem item, TodoReminderStage stage, Action acknowledge)
        {
            DateTime due = item.ReminderAt.Value;
            if (due.Kind == DateTimeKind.Utc) due = due.ToLocalTime();
            string time = due.ToString("MM月dd日 HH:mm");
            if (stage != TodoReminderStage.Due) time = "不足 1 小时 · " + time;
            entries[item.Id] = (item.Text, time, acknowledge);
            RefreshEntries();
        }

        public void RemoveItem(Guid id)
        {
            entries.Remove(id);
            if (entries.Count == 0) Close();
            else RefreshEntries();
        }

        private void RefreshEntries()
        {
            bool single = entries.Count == 1;
            message.Text = single ? entries.Values.First().Text : string.Join(Environment.NewLine + Environment.NewLine,
                entries.Values.Select(e => e.Text + Environment.NewLine + e.Time));
            timeText = single ? entries.Values.First().Time : $"共 {entries.Count} 项提醒";
            LayoutContent(DeviceDpi);
        }

        private void LayoutContent(int dpi)
        {
            int S(int value) => (int)Math.Round(value * dpi / 96f);
            if (layoutDpi != dpi)
            {
                var old = new[] { titleFont, bodyFont, smallFont, closeFont };
                titleFont = new Font("Microsoft YaHei UI", 12 * dpi / 96f, FontStyle.Bold, GraphicsUnit.Pixel);
                bodyFont = new Font("Microsoft YaHei UI", 15 * dpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
                smallFont = new Font("Microsoft YaHei UI", 11 * dpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
                closeFont = new Font("Microsoft YaHei UI", 20 * dpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
                message.Font = bodyFont; acknowledge.Font = smallFont; close.Font = closeFont;
                layoutDpi = dpi;
                foreach (var font in old) font?.Dispose();
            }
            Rectangle area = positioned ? Screen.FromControl(this).WorkingArea : Screen.FromPoint(Cursor.Position).WorkingArea;
            int margin = S(14), gap = S(10), buttonWidth = S(72), buttonHeight = S(28);
            int width = Math.Min(S(360), area.Width - S(16));
            int contentWidth = width - margin * 2;
            int measured = TextRenderer.MeasureText(message.Text.Length == 0 ? " " : message.Text, bodyFont,
                new Size(Math.Max(1, contentWidth - SystemInformation.VerticalScrollBarWidth - S(4)), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height + S(4);
            int maxContent = Math.Max(S(28), Math.Min(S(240), area.Height - S(120)));
            int contentHeight = Math.Max(S(28), Math.Min(measured, maxContent));
            message.ScrollBars = measured > maxContent ? ScrollBars.Vertical : ScrollBars.None;
            int contentY = S(50), footerY = contentY + contentHeight + gap;
            ClientSize = new Size(width, footerY + buttonHeight + margin);
            message.SetBounds(margin, contentY, contentWidth, contentHeight);
            bellBounds = new Rectangle(margin, S(14), S(20), S(20));
            titleBounds = new Rectangle(S(42), S(12), width - S(90), S(24));
            close.SetBounds(width - S(39), S(7), S(30), S(30));
            acknowledge.SetBounds(width - margin - buttonWidth, footerY, buttonWidth, buttonHeight);
            clockBounds = new Rectangle(margin, footerY + S(6), S(16), S(16));
            timeBounds = new Rectangle(margin + S(23), footerY, acknowledge.Left - gap - margin - S(23), buttonHeight);
            RoundControl(this, S(6)); RoundControl(acknowledge, S(4));
            if (positioned)
                Location = new Point(Math.Max(area.Left, area.Right - Width - S(12)), Math.Max(area.Top, area.Bottom - Height - S(12)));
            Invalidate();
        }

        private static void RoundControl(Control control, int radius)
        {
            using var path = MainMenuAppearance.Round(control.ClientRectangle, radius);
            var old = control.Region;
            control.Region = new Region(path);
            old?.Dispose();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            LayoutContent(e.DeviceDpiNew);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = MainMenuAppearance.Round(new Rectangle(0, 0, Width - 1, Height - 1), (int)Math.Round(6 * layoutDpi / 96f));
            using var border = new Pen(Color.FromArgb(53, 70, 80));
            e.Graphics.DrawPath(border, outline);
            MainMenuRenderer.DrawGlyph(e.Graphics, "bell", bellBounds, Color.FromArgb(255, 135, 60));
            TextRenderer.DrawText(e.Graphics, Text, titleFont, titleBounds, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            using var clock = new Pen(Color.FromArgb(151, 168, 181), Math.Max(1, layoutDpi / 96f));
            Rectangle r = Rectangle.Inflate(clockBounds, -1, -1);
            e.Graphics.DrawEllipse(clock, r);
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            e.Graphics.DrawLine(clock, cx, cy, cx, r.Top + r.Height * .22f);
            e.Graphics.DrawLine(clock, cx, cy, r.Right - r.Width * .2f, cy);
            TextRenderer.DrawText(e.Graphics, timeText, smallFont, timeBounds, Color.FromArgb(151, 168, 181),
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel && e.CloseReason == CloseReason.UserClosing)
                foreach (var entry in entries.Values.ToArray()) entry.Acknowledge();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { titleFont?.Dispose(); bodyFont?.Dispose(); smallFont?.Dispose(); closeFont?.Dispose(); }
        }
    }
}
