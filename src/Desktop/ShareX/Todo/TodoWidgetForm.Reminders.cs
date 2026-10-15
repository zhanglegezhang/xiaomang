using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;
using ShareX.Properties;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private readonly TodoReminderService reminderService = new TodoReminderService();
        private readonly Timer reminderTimer;

        private void CheckReminders()
        {
            if (PersonalDataManager.Busy) return;
            if (IsDisposed || !IsHandleCreated) return;
            DateTime now = DateTime.Now;
            if (lastLogCleanupDate != now.Date)
            {
                try
                {
                    noteLogStore.CleanupExpired();
                    lastLogCleanupDate = now.Date;
                }
                catch (Exception e)
                {
                    DebugHelper.WriteException(e, "便签日志清理失败。");
                }
            }
            reminderService.Check(Settings, now, SettingManager.SaveApplicationConfigAsync);
        }

    }
}
