#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using ShareX.Properties;
using ShareX.UploadersLib;
using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm : Form
    {
        private bool ready;

        public ApplicationSettingsForm()
        {
            InitializeControls();
            ShareXResources.ApplyTheme(this, true);
            DesignSettingsPage(tpGeneral);
            InitializeChangeTracking();
        }

        private void SettingsForm_Shown(object sender, EventArgs e)
        {
            this.ForceActivate();
        }

        private void SettingsForm_Resize(object sender, EventArgs e)
        {
            Invalidate();
        }

        private void tttvMain_TabChanged(TabPage tabPage)
        {
            loadingSettingsPage = true;
            try
            {
                if (!EnsureSettingsPage(tabPage)) return;
                MainMenuAppearance.ApplyDpi(tabPage);
                if (ready && tabPage == tpGeneral)
                {
                    UpdateStartWithWindows();
                }
                else if (tabPage == notificationsTab)
                {
                    notificationSettingsControl.LoadSettings();
                }
                else if (tabPage == tpPaths)
                {
                    RefreshPathLocations();
                }
            }
            finally { loadingSettingsPage = false; }
        }

        private void InitializeControls()
        {
            InitializeComponent();
            tcSettings.TabPages.Remove(tpAdvanced);
            components.Add(tpAdvanced);
            tpPaths.Text = SettingsPageLayout.Caption("存储位置", "Storage locations");
            LayoutGeneralSettings();
            InitializeNotificationSettingsTab();
            InitializeDailyNewsSettingsTab();
            InitializeVoiceTranscriptionSettingsTab();
            RegisterSettingsPage(tpPaths, InitializePathLocations);
            RegisterSettingsPage(tpAdvanced, () =>
            {
                InitializeAdvancedPageComponents();
                InitializeAdvancedSettings();
            });

            InitializeRegistrationTab();
            UpdateControls();
            UpdateStartWithWindows();
        }

        private void LayoutGeneralSettings()
        {
            tpGeneral.SuspendLayout();
            var layout = new SettingsPageLayout(this);
            var content = layout.Stack();
            content.SuspendLayout();
            layout.Heading(content, "启动和页签", "Startup and tabs");
            layout.Add(content, cbStartWithWindows);
            layout.Heading(content, "个人数据", "Personal data");
            var export = CreatePersonalDataButton(layout, SettingsPageLayout.Caption("导出个人数据…", "Export personal data…"), "export");
            var import = CreatePersonalDataButton(layout, SettingsPageLayout.Caption("导入个人数据…", "Import personal data…"), "import");
            export.Click += async (_, _) => await PersonalDataTransferForm.Open(this, false);
            import.Click += async (_, _) => await PersonalDataTransferForm.Open(this, true);
            layout.Add(content, layout.Inline(export));
            layout.Add(content, layout.Inline(import));
            var pendingInput = CreatePersonalDataButton(layout, "恢复待处理输入法数据…", "restore");
            pendingInput.Click += async (_, _) => await PersonalDataManager.OpenDeferredInput(this);
            layout.Add(content, layout.Inline(pendingInput));
            content.ResumeLayout(false);
            layout.Install(tpGeneral, content);
            tpGeneral.ResumeLayout(true);
        }

        private static Button CreatePersonalDataButton(SettingsPageLayout layout, string text, string glyph)
        {
            var button = new Button
            {
                Name = "personalData_" + glyph, Text = text, AutoSize = true,
                MinimumSize = new Size(layout.Scale(200), layout.Scale(32)),
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText
            };
            void RefreshIcon()
            {
                int size = MainMenuAppearance.Scale(button, 16);
                var image = new Bitmap(size, size);
                using (Graphics graphics = Graphics.FromImage(image))
                    MainMenuRenderer.DrawGlyph(graphics, glyph, new Rectangle(0, 0, size, size), button.ForeColor);
                Image previous = button.Image;
                button.Image = image;
                previous?.Dispose();
            }
            button.ForeColorChanged += (_, _) => RefreshIcon();
            button.DpiChangedAfterParent += (_, _) => RefreshIcon();
            button.Disposed += (_, _) => { Image image = button.Image; button.Image = null; image?.Dispose(); };
            RefreshIcon();
            return button;
        }

        private void UpdateControls()
        {
            ready = false;

            InitializeTaskSettingsPages();
            tttvMain.BatchPageLayout = true;
            tttvMain.MainTabControl = tcSettings;
            ready = true;
        }

        private void UpdateStartWithWindows()
        {
            ready = false;

            cbStartWithWindows.Text = Resources.ApplicationSettingsForm_cbStartWithWindows_Text;
            cbStartWithWindows.Enabled = false;

            try
            {
                StartupState state = StartupManager.State;
                cbStartWithWindows.Checked = state == StartupState.Enabled || state == StartupState.EnabledByPolicy;

                if (state == StartupState.DisabledByUser)
                {
                    cbStartWithWindows.Text = Resources.ApplicationSettingsForm_cbStartWithWindows_DisabledByUser_Text;
                }
                else if (state == StartupState.DisabledByPolicy)
                {
                    cbStartWithWindows.Text = Resources.ApplicationSettingsForm_cbStartWithWindows_DisabledByPolicy_Text;
                }
                else if (state == StartupState.EnabledByPolicy)
                {
                    cbStartWithWindows.Text = Resources.ApplicationSettingsForm_cbStartWithWindows_EnabledByPolicy_Text;
                }
                else
                {
                    cbStartWithWindows.Enabled = true;
                }
            }
            catch (Exception e)
            {
                e.ShowError();
            }

            ready = true;
        }

        private void cbStartWithWindows_CheckedChanged(object sender, EventArgs e)
        {
            if (ready)
            {
                Program.Settings.StartupPreferenceInitialized = true;
                try
                {
                    StartupManager.State = cbStartWithWindows.Checked ? StartupState.Enabled : StartupState.Disabled;
                    UpdateStartWithWindows();
                }
                catch (Exception ex)
                {
                    ex.ShowError();
                }
            }
        }
    }
}
