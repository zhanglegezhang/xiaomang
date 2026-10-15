using System;

namespace ShareX
{
    public enum TodoReminderStage { None, OneHour, Due }

    public static class TodoReminderPolicy
    {
        public static TodoReminderStage GetStage(TodoItem item, DateTime now)
        {
            if (item.IsCompleted || !item.ReminderAt.HasValue) return TodoReminderStage.None;
            DateTime due = item.ReminderAt.Value;
            if (due.Kind == DateTimeKind.Utc) due = due.ToLocalTime();
            if (now.Kind == DateTimeKind.Utc) now = now.ToLocalTime();
            // A missed deadline triggers only the due reminder, never two competing popups.
            if (now >= due) return item.DueReminderSent ? TodoReminderStage.None : TodoReminderStage.Due;
            DateTime created = item.CreatedAt.Kind == DateTimeKind.Utc ? item.CreatedAt.ToLocalTime() : item.CreatedAt;
            if (due - created >= TimeSpan.FromHours(1) && !item.OneHourReminderSent && now >= due.AddHours(-1)) return TodoReminderStage.OneHour;
            return TodoReminderStage.None;
        }
    }
}
