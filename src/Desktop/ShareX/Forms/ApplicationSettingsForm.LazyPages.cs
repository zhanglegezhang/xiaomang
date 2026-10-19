using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private readonly Dictionary<TabPage, Action> pendingSettingsPages = new();
        private readonly HashSet<TabPage> ownedSettingsPages = new();

        private void RegisterSettingsPage(TabPage page, Action initialize)
        {
            pendingSettingsPages[page] = initialize;
            if (trackingSettingsChanges) TrackSettingsControls(page);
            if (ownedSettingsPages.Add(page)) Disposed += (sender, e) => page.Dispose();
        }

        private bool EnsureSettingsPage(TabPage page)
        {
            if (!pendingSettingsPages.TryGetValue(page, out Action initialize)) return true;
            if (page.Controls["settingsLoadRetry"] is Control oldRetry) oldRetry.Dispose();
            bool previousReady = ready;
            ready = false;
            page.SuspendLayout();
            try
            {
                initialize();
                ShareXResources.ApplyCustomThemeToControl(page);
                MainMenuAppearance.RefreshIfStyled(page);
                DesignSettingsPage(page);
                pendingSettingsPages.Remove(page);
                return true;
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Failed to load settings page: " + page.Name);
                var retry = new Button { Name = "settingsLoadRetry", Text = "加载失败，点击重试", AutoSize = true, Dock = DockStyle.Top };
                retry.Click += (sender, e) => { page.Controls.Remove(retry); retry.Dispose(); EnsureSettingsPage(page); };
                page.Controls.Add(retry);
                return false;
            }
            finally
            {
                page.ResumeLayout(true);
                ready = previousReady;
            }
        }
    }
}
