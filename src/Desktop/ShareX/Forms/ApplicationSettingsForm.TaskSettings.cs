using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private TaskSettingsForm defaultTaskSettingsForm;
        private bool taskSettingsPagesRegistered;

        private void InitializeTaskSettingsPages()
        {
            if (taskSettingsPagesRegistered) return;
            taskSettingsPagesRegistered = true;
            var resources = new ComponentResourceManager(typeof(TaskSettingsForm));
            tcSettings.TabPages.Add(CreateCombinedCapturePage(resources.GetString("tpCapture.Text")));
            tcSettings.TabPages.Add(CreateTaskSettingsPage("tpScreenRecorder", resources));
            tcSettings.TabPages.Add(CreateTaskSettingsPage("tpOCR", resources));


            // Reorder page containers before building navigation; leave their contents unloaded.
            string[] order = { "tpGeneral", "tpRegistration", "tpCaptureGeneral", "tpNotifications", "tpScreenRecorder", "tpOCR", "tpPaths",
                "tpDailyNews", "tpVoiceTranscription", "tpAdvanced" };
            TabPage[] pages = tcSettings.TabPages.Cast<TabPage>()
                .OrderBy(page =>
                {
                    int index = System.Array.IndexOf(order, page.Name);
                    return index < 0 ? order.Length : index;
                }).ToArray();
            tcSettings.TabPages.Clear();
            tcSettings.TabPages.AddRange(pages);
        }

        private TaskSettingsForm GetDefaultTaskSettingsForm()
        {
            if (defaultTaskSettingsForm == null)
            {
                defaultTaskSettingsForm = new TaskSettingsForm(Program.DefaultTaskSettings, true, loadOnDemand: true);
                components.Add(defaultTaskSettingsForm);
            }
            return defaultTaskSettingsForm;
        }

        private TabPage CreateCombinedCapturePage(string title)
        {
            var page = new TabPage(title) { Name = "tpCaptureGeneral", AutoScroll = true };
            RegisterSettingsPage(page, () =>
            {
                TaskSettingsForm owner = GetDefaultTaskSettingsForm();
                TabPage capture = owner.LoadDefaultSettingsPage("tpCaptureGeneral", DeviceDpi);
                TabPage region = owner.LoadDefaultSettingsPage("tpRegionCapture", DeviceDpi);
                using var layout = new SettingsPageLayout(this, deferLayout: true);
                var rows = layout.Stack();
                layout.Heading(rows, "截图设置", "Capture settings");
                var capturePanel = (Panel)capture.Controls["pCapture"];
                capturePanel.Padding = Padding.Empty;
                layout.Add(rows, capturePanel);
                layout.Heading(rows, "区域截图", "Region capture");
                layout.Add(rows, layout.Row(region.Controls["lblRegionCaptureMouseRightClickAction"], region.Controls["cbRegionCaptureMouseRightClickAction"]));
                layout.Add(rows, layout.Row(region.Controls["lblRegionCaptureBackgroundDimStrength"],
                    layout.Inline(region.Controls["nudRegionCaptureBackgroundDimStrength"], region.Controls["lblRegionCaptureBackgroundDimStrengthHint"])));
                layout.Add(rows, region.Controls["cbRegionCaptureShowInfo"]);
                layout.Install(page, rows);
            });
            return page;
        }

        private TabPage CreateTaskSettingsPage(string name, ComponentResourceManager resources, string title = null)
        {
            var page = new TabPage(title ?? resources.GetString(name + ".Text")) { Name = name, AutoScroll = true };
            RegisterSettingsPage(page, () =>
            {
                TabPage source = GetDefaultTaskSettingsForm().LoadDefaultSettingsPage(name, DeviceDpi);
                foreach (Control control in source.Controls.Cast<Control>().ToArray()) page.Controls.Add(control);
            });
            return page;
        }
    }
}


