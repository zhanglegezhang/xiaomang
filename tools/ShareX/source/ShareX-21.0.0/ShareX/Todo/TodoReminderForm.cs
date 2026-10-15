using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    // Remains visible until acknowledged; deliberately independent of transient screenshot toasts.
    public sealed class TodoReminderForm : Form
    {
        private readonly Dictionary<Guid, (string Text, Action Acknowledge)> entries = new Dictionary<Guid, (string, Action)>();
        private readonly TextBox message;
        protected override bool ShowWithoutActivation => true;

        public TodoReminderForm()
        {
            Text = "待办提醒";
            ShowInTaskbar = false;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei", 8.5f);
            BackColor = ShareXResources.Theme.BackgroundColor;
            ForeColor = ShareXResources.Theme.TextColor;
            int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
            ClientSize = new Size(Scale(330), Scale(190));
            Padding = new Padding(Scale(12));
            message = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None, BackColor = BackColor, ForeColor = ForeColor, TabStop = false };
            var acknowledge = new Button { Text = "知道了", Dock = DockStyle.Bottom, Height = Scale(30), FlatStyle = FlatStyle.Flat };
            acknowledge.Click += (_, _) => Close();
            Controls.Add(message);
            Controls.Add(acknowledge);
            ActiveControl = acknowledge;
            Shown += (_, _) =>
            {
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
            };
        }

        public void AddReminder(TodoItem item, TodoReminderStage stage, Action acknowledge)
        {
            DateTime due = item.ReminderAt.Value;
            if (due.Kind == DateTimeKind.Utc) due = due.ToLocalTime();
            entries[item.Id] = ((stage == TodoReminderStage.Due ? "已到提醒时间" : "距离提醒时间不足 1 小时")
                + " · " + due.ToString("MM-dd HH:mm") + Environment.NewLine + item.Text, acknowledge);
            message.Text = string.Join(Environment.NewLine + Environment.NewLine, entries.Values.Select(e => e.Text));
        }

        public void RemoveItem(Guid id)
        {
            entries.Remove(id);
            if (entries.Count == 0) Close();
            else message.Text = string.Join(Environment.NewLine + Environment.NewLine, entries.Values.Select(e => e.Text));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel && e.CloseReason == CloseReason.UserClosing)
            {
                foreach (var entry in entries.Values) entry.Acknowledge();
            }
        }
    }
}
