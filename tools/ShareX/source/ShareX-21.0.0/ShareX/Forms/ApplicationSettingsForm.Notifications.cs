using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private TabPage notificationsTab;
        private NotificationSettingsControl notificationSettingsControl;

        private void InitializeNotificationSettingsTab()
        {
            notificationsTab = new TabPage(SettingsPageLayout.Caption("截图通知", "Capture notifications"))
            {
                Name = "tpNotifications",
                BackColor = tpGeneral.BackColor
            };
            RegisterSettingsPage(notificationsTab, () =>
            {
                notificationSettingsControl = new NotificationSettingsControl(() => Program.DefaultTaskSettings.GeneralSettings, true)
                {
                    Dock = DockStyle.Fill
                };
                notificationsTab.Controls.Add(notificationSettingsControl);
            });
            tcSettings.TabPages.Insert(1, notificationsTab);
        }
    }
}
