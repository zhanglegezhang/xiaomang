using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    public partial class MainForm
    {
        private bool mainAppearanceReady;

        private bool IsActiveNavigation(ToolStripItem item)
        {
            if (item == tsbHotkeySettings) return inlineSettingsKind is InlineSettingsKind.Hotkeys or InlineSettingsKind.HotkeyTask;
            if (item == tsbApplicationSettings) return inlineSettingsKind == InlineSettingsKind.Application;
            if (item == tsbAbout) return inlineSettingsKind == InlineSettingsKind.About;
            if (item.Name == "tsbInputMethod") return inlineSettingsKind == InlineSettingsKind.InputMethod;
            if (item == tsbScreenshotsFolder) return screenshotFolderPageActive;
            if (item.Text == "便签文件夹") return noteLogPageActive;
            if (item.Text == "临时文件夹") return temporaryFilePageActive;
            return false;
        }

        private void ConfigureMainMenuAppearance()
        {
            mainAppearanceReady = true;
            BackColor = MainMenuAppearance.Background;
            pMain.BackColor = MainMenuAppearance.Background;
            pToolbars.AutoSize = false;
            pToolbars.Width = MainMenuAppearance.Scale(this, 168);
            pToolbars.BackColor = MainMenuAppearance.Sidebar;
            tsMain.AutoSize = false;
            tsMain.MinimumSize = Size.Empty;
            tsMain.Width = MainMenuAppearance.Scale(this, 168);
            tsMain.Padding = new Padding(MainMenuAppearance.Scale(this, 6), MainMenuAppearance.Scale(this, 8), MainMenuAppearance.Scale(this, 6), 0);
            tsMain.Font = MainMenuAppearance.MenuFont;
            tsMain.DrawCustomBorder = false;
            tsMain.BackColor = MainMenuAppearance.Sidebar;
            tsMain.ForeColor = MainMenuAppearance.Text;
            tsMain.ImageScalingSize = new Size(MainMenuAppearance.Scale(this, 18), MainMenuAppearance.Scale(this, 18));
            tsMain.Renderer = new MainMenuRenderer(IsActiveNavigation);
            foreach (ToolStripItem item in tsMain.Items)
            {
                item.AutoSize = false;
                item.Size = new Size(MainMenuAppearance.Scale(this, 156), MainMenuAppearance.Scale(this, 38));
                item.Margin = new Padding(0, 0, 0, MainMenuAppearance.Scale(this, 3));
                item.Padding = Padding.Empty;
                item.ForeColor = MainMenuAppearance.Text;
            }
            tsMain.PerformLayout();
            RefreshMainPagesAppearance();
            tsMain.Invalidate();
        }

        private void RefreshMainPagesAppearance()
        {
            if (!mainAppearanceReady) return;
            if (inlineSettingsHost != null) inlineSettingsHost.BackColor = MainMenuAppearance.Background;
            if (inlineSettingsViewport != null) inlineSettingsViewport.BackColor = MainMenuAppearance.Background;
            if (inlineSettingsForm != null) MainMenuAppearance.Apply(inlineSettingsForm);
            if (cachedApplicationSettingsForm != null && !cachedApplicationSettingsForm.IsDisposed && cachedApplicationSettingsForm != inlineSettingsForm)
                MainMenuAppearance.Apply(cachedApplicationSettingsForm);
            if (cachedInputMethodForm != null && !cachedInputMethodForm.IsDisposed && cachedInputMethodForm != inlineSettingsForm)
                MainMenuAppearance.Apply(cachedInputMethodForm);
            if (suspendedHotkeySettingsForm != null) MainMenuAppearance.Apply(suspendedHotkeySettingsForm);
            if (cachedHotkeySettingsForm != null && cachedHotkeySettingsForm != inlineSettingsForm && cachedHotkeySettingsForm != suspendedHotkeySettingsForm)
                MainMenuAppearance.Apply(cachedHotkeySettingsForm);
            if (noteLogViewer != null) MainMenuAppearance.Apply(noteLogViewer);
            if (screenshotFolderViewer != null) MainMenuAppearance.Apply(screenshotFolderViewer);
            if (temporaryFileViewer != null) MainMenuAppearance.Apply(temporaryFileViewer);
            tsMain.Invalidate();
        }
    }
}
