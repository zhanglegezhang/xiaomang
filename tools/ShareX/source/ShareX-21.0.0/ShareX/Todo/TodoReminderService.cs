using System;
using System.Collections.Generic;
using System.Linq;

namespace ShareX
{
    internal sealed class TodoReminderService : IDisposable
    {
        private readonly HashSet<string> pending = new();
        private readonly Func<TodoReminderForm> createWindow;
        private TodoReminderForm window;
        internal TodoReminderService() : this(() => new TodoReminderForm()) { }
        internal TodoReminderService(Func<TodoReminderForm> createWindow) => this.createWindow = createWindow;

        internal void Check(TodoSettings settings, DateTime now, Action save)
        {
            if (settings.ReminderVersion < 1)
            {
                foreach (TodoItem item in settings.Items.Where(x => !x.IsCompleted && x.ReminderAt.HasValue)) item.DueReminderSent = false;
                settings.ReminderVersion = 1; save();
            }
            foreach (TodoItem item in settings.Items.ToArray())
            {
                TodoReminderStage stage = TodoReminderPolicy.GetStage(item, now);
                if (stage == TodoReminderStage.None) continue;
                string key = item.Id + ":" + stage;
                if (pending.Contains(key)) continue;
                if (window == null || window.IsDisposed) window = createWindow();
                window.AddReminder(item, stage, () =>
                {
                    item.OneHourReminderSent = true;
                    if (stage == TodoReminderStage.Due) item.DueReminderSent = true;
                    save();
                });
                if (!window.Visible) window.Show();
                // A visible reminder must exist before delivery is tracked; acknowledgement
                // is persisted only when the user dismisses it, preserving restart recovery.
                pending.Add(key);
            }
        }
        internal void Remove(Guid id)
        {
            window?.RemoveItem(id);
            pending.Remove(id + ":" + TodoReminderStage.Due);
            pending.Remove(id + ":" + TodoReminderStage.OneHour);
        }
        public void Dispose() => window?.Dispose();
    }
}
