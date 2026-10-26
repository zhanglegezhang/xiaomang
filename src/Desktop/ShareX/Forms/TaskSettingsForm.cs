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
using ShareX.ScreenCaptureLib;
using ShareX.UploadersLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ShareX
{
    public partial class TaskSettingsForm : Form
    {
        public TaskSettings TaskSettings { get; private set; }
        public bool IsDefault { get; private set; }

        private bool loaded;
        private int deferredPageDpi;
        private readonly HashSet<string> initializedPages = new();
        private readonly Dictionary<TextBox,CodeMenu> pickerFormatMenus = new();
        internal CodeMenu GetPickerFormatMenu(TextBox field) => pickerFormatMenus.TryGetValue(field,out var menu)?menu:null;

        public TaskSettingsForm(TaskSettings hotkeySetting, bool isDefault = false, bool loadOnDemand = false)
        {
            TaskSettings = hotkeySetting;
            IsDefault = isDefault;
            if (loadOnDemand)
            {
                if (!IsDefault) throw new ArgumentException(nameof(loadOnDemand));
                components = new System.ComponentModel.Container();
                ShareXResources.ApplyTheme(this, true);
                return;
            }

            InitializeComponent();
            if (!IsDefault)
            {
                InitializeTaskPageComponents();
                InitializeGeneralMainPageComponents();
            }
            ShareXResources.ApplyTheme(this, true);

            UpdateWindowTitle();
            if (IsDefault)
            {
                tcTaskSettings.TabPages.Remove(tpTask);
                tcTaskSettings.TabPages.Remove(tpGeneral);
            }
            else
            {
                tpNotifications.Controls.Add(new NotificationSettingsControl(() => TaskSettings.GeneralSettings)
                {
                    Dock = DockStyle.Fill
                });
                AddEnumItemsContextMenu<HotkeyType>(x =>
                {
                    TaskSettings.Job = x;
                    UpdateWindowTitle();
                }, cmsTask);
                SetEnumCheckedContextMenu(TaskSettings.Job, cmsTask);
                tbDescription.Text = TaskSettings.Description;
                cbOverrideAfterCaptureSettings.Checked = !TaskSettings.UseDefaultAfterCaptureJob;
                btnAfterCapture.Enabled = !TaskSettings.UseDefaultAfterCaptureJob;
                AddMultiEnumItemsContextMenu<AfterCaptureTasks>(x => TaskSettings.AfterCaptureJob = TaskSettings.AfterCaptureJob.Swap(x), cmsAfterCapture);
                SetMultiEnumCheckedContextMenu(TaskSettings.AfterCaptureJob, cmsAfterCapture);
                cbOverrideScreenshotsFolder.Checked = TaskSettings.OverrideScreenshotsFolder;
                CodeMenu menu = CodeMenu.Create<CodeMenuEntryFilename>(txtScreenshotsFolder, CodeMenuEntryFilename.t, CodeMenuEntryFilename.pn,
                    CodeMenuEntryFilename.i, CodeMenuEntryFilename.width, CodeMenuEntryFilename.height, CodeMenuEntryFilename.n);
                menu.MenuLocationBottom = true;
                txtScreenshotsFolder.Text = TaskSettings.ScreenshotsFolder;
                txtScreenshotsFolder.Enabled = btnScreenshotsFolderBrowse.Enabled = TaskSettings.OverrideScreenshotsFolder;
                cbOverrideGeneralSettings.Checked = !TaskSettings.UseDefaultGeneralSettings;
                UpdateTaskTabMenuNames();
            }

            UpdateDefaultSettingVisibility();
            tttvMain.MainTabControl = tcTaskSettings;
            loaded = true;
        }

        private void InitializeCapturePage()
        {
            cbShowCursor.Checked = TaskSettings.CaptureSettings.ShowCursor;
            cbCaptureTransparent.Checked = TaskSettings.CaptureSettings.CaptureTransparent;
            cbCaptureShadow.Enabled = TaskSettings.CaptureSettings.CaptureTransparent;
            cbCaptureShadow.Checked = TaskSettings.CaptureSettings.CaptureShadow;
            nudCaptureShadowOffset.SetValue(TaskSettings.CaptureSettings.CaptureShadowOffset);
            cbCaptureAutoHideDesktopIcons.Checked = TaskSettings.CaptureSettings.CaptureAutoHideDesktopIcons;
            SimplifyCaptureSettings();
        }

        private void InitializeRegionPage()
        {
            cbRegionCaptureMouseRightClickAction.Items.AddRange(Helpers.GetLocalizedEnumDescriptions<RegionCaptureAction>().Take(4).ToArray());
            cbRegionCaptureMouseRightClickAction.SelectedIndex = (int)TaskSettings.CaptureSettings.SurfaceOptions.RegionCaptureActionRightClick;
            nudRegionCaptureBackgroundDimStrength.SetValue(TaskSettings.CaptureSettings.SurfaceOptions.BackgroundDimStrength);
            cbRegionCaptureShowInfo.Checked = TaskSettings.CaptureSettings.SurfaceOptions.ShowInfo;
        }

        private void InitializeRecorderPage()
        {
            if (HelpersOptions.DevMode)
            {
                nudScreenRecordFPS.Maximum = 300;
                nudGIFFPS.Maximum = 60;
            }

            nudScreenRecordFPS.SetValue(TaskSettings.CaptureSettings.ScreenRecordFPS);
            nudGIFFPS.SetValue(TaskSettings.CaptureSettings.GIFFPS);
            cbScreenRecorderFixedDuration.Checked = nudScreenRecorderDuration.Enabled = TaskSettings.CaptureSettings.ScreenRecordFixedDuration;
            nudScreenRecorderDuration.SetValue((decimal)TaskSettings.CaptureSettings.ScreenRecordDuration);
            cbScreenRecordAutoStart.Checked = nudScreenRecorderStartDelay.Enabled = TaskSettings.CaptureSettings.ScreenRecordAutoStart;
            nudScreenRecorderStartDelay.SetValue((decimal)TaskSettings.CaptureSettings.ScreenRecordStartDelay);
            cbScreenRecorderShowCursor.Checked = TaskSettings.CaptureSettings.ScreenRecordShowCursor;
            cbScreenRecordTwoPassEncoding.Checked = TaskSettings.CaptureSettings.ScreenRecordTwoPassEncoding;
            cbScreenRecordTransparentRegion.Checked = TaskSettings.CaptureSettings.ScreenRecordTransparentRegion;
            cbScreenRecordConfirmAbort.Checked = TaskSettings.CaptureSettings.ScreenRecordAskConfirmationOnAbort;
        }

        private void InitializeOCRPage()
        {
            OCROptions ocrOptions = TaskSettings.CaptureSettings.OCROptions;

            try
            {
                OCRLanguage[] languages = OCRHelper.AvailableLanguages.OrderBy(x => x.DisplayName).ToArray();

                if (languages.Length > 0)
                {
                    cbCaptureOCRDefaultLanguage.Items.AddRange(languages);

                    if (ocrOptions.Language == null)
                    {
                        cbCaptureOCRDefaultLanguage.SelectedIndex = 0;
                        ocrOptions.Language = languages[0].LanguageTag;
                    }
                    else
                    {
                        int index = Array.FindIndex(languages, x => x.LanguageTag.Equals(ocrOptions.Language, StringComparison.OrdinalIgnoreCase));

                        if (index >= 0)
                        {
                            cbCaptureOCRDefaultLanguage.SelectedIndex = index;
                        }
                        else
                        {
                            cbCaptureOCRDefaultLanguage.SelectedIndex = 0;
                            ocrOptions.Language = languages[0].LanguageTag;
                        }
                    }
                }
            }
            catch
            {
                cbCaptureOCRDefaultLanguage.Enabled = false;
            }

            cbCaptureOCRSilent.Checked = ocrOptions.Silent;
            cbCaptureOCRAutoCopy.Enabled = !ocrOptions.Silent;
            cbCaptureOCRAutoCopy.Checked = ocrOptions.AutoCopy;
            cbCloseWindowAfterOpenServiceLink.Checked = ocrOptions.CloseWindowAfterOpeningServiceLink;
        }

        private void InitializePickerPage()
        {
            pickerFormatMenus[txtToolsScreenColorPickerFormat] = CodeMenu.Create<CodeMenuEntryPixelInfo>(txtToolsScreenColorPickerFormat);
            txtToolsScreenColorPickerFormat.Text = TaskSettings.ToolsSettings.ScreenColorPickerFormat;

            pickerFormatMenus[txtToolsScreenColorPickerFormatCtrl] = CodeMenu.Create<CodeMenuEntryPixelInfo>(txtToolsScreenColorPickerFormatCtrl);
            txtToolsScreenColorPickerFormatCtrl.Text = TaskSettings.ToolsSettings.ScreenColorPickerFormatCtrl;

            pickerFormatMenus[txtToolsScreenColorPickerInfoText] = CodeMenu.Create<CodeMenuEntryPixelInfo>(txtToolsScreenColorPickerInfoText);
            txtToolsScreenColorPickerInfoText.Text = TaskSettings.ToolsSettings.ScreenColorPickerInfoText;
        }

        private void TaskSettingsForm_Resize(object sender, EventArgs e)
        {
            Refresh();
        }

        internal TabPage LoadDefaultSettingsPage(string name, int displayDpi)
        {
            if (!IsDefault) throw new InvalidOperationException("Only default task settings can be merged into application settings.");
            deferredPageDpi = displayDpi;
            return EnsureTaskSettingsPage(name);
        }

        private TabPage CreateDeferredPage()
        {
            var page = new TabPage();
            components.Add(page);
            return page;
        }

        private TabPage EnsureTaskSettingsPage(string name)
        {
            bool wasLoaded = loaded;
            loaded = false;
            bool initializePage = !initializedPages.Contains(name);
            try
            {
                TabPage page;
                switch (name)
                {
                    case "tpCaptureGeneral":
                        page = tpCaptureGeneral ??= CreateDeferredPage();
                        if (!initializedPages.Contains(name))
                        {
                            InitializeCaptureGeneralPageComponents();
                            cbOverrideCaptureSettings.Visible = !IsDefault;
                            cbOverrideCaptureSettings.Checked = !TaskSettings.UseDefaultCaptureSettings;
                            InitializeCapturePage();
                        }
                        break;
                    case "tpRegionCapture":
                        page = tpRegionCapture ??= CreateDeferredPage();
                        if (!initializedPages.Contains(name)) { InitializeRegionCapturePageComponents(); InitializeRegionPage(); }
                        break;
                    case "tpScreenRecorder":
                        page = tpScreenRecorder ??= CreateDeferredPage();
                        if (!initializedPages.Contains(name)) { InitializeScreenRecorderPageComponents(); InitializeRecorderPage(); }
                        break;
                    case "tpOCR":
                        page = tpOCR ??= CreateDeferredPage();
                        if (!initializedPages.Contains(name)) { InitializeOCRPageComponents(); InitializeOCRPage(); }
                        break;
                    case "tpToolsGeneral":
                        page = tpToolsGeneral ??= CreateDeferredPage();
                        if (!initializedPages.Contains(name))
                        {
                            InitializeToolsGeneralPageComponents();
                            cbOverrideToolsSettings.Visible = !IsDefault;
                            cbOverrideToolsSettings.Checked = !TaskSettings.UseDefaultToolsSettings;
                            InitializePickerPage();
                        }
                        break;
                    default: return null;
                }
                if (initializePage)
                {
                    float scale = (deferredPageDpi > 0 ? deferredPageDpi : DeviceDpi) / 96f;
                    if (scale != 1f)
                    {
                        foreach (Control control in page.Controls) control.Scale(new SizeF(scale, scale));
                    }
                    if (name == "tpRegionCapture") SimplifyRegionCaptureSettings();
                    if (name == "tpCaptureGeneral" && IsDefault) LayoutCaptureSettings();
                    initializedPages.Add(name);
                }
                UpdateDefaultSettingVisibility();
                ShareXResources.ApplyCustomThemeToControl(page);
                return page;
            }
            finally { loaded = wasLoaded; }
        }

        private void tttvMain_TabChanged(TabPage tabPage)
        {
            EnsureTaskSettingsPage(tabPage.Name);
            if (tabPage == tpGeneralMain) tttvMain.SelectChildNode();
        }

        private void UpdateWindowTitle()
        {
            if (IsDefault)
            {
                Text = Program.DisplayName + " - " + Resources.TaskSettingsForm_UpdateWindowTitle_Task_settings;
            }
            else
            {
                Text = Program.DisplayName + " - " + string.Format(Resources.TaskSettingsForm_UpdateWindowTitle_Task_settings_for__0_, TaskSettings);
            }
        }

        private void UpdateDefaultSettingVisibility()
        {
            if (IsDefault) return;
            tpNotifications.Enabled = !TaskSettings.UseDefaultGeneralSettings;
            if (pCapture != null) pCapture.Enabled = !TaskSettings.UseDefaultCaptureSettings;
            tpRegionCapture.Enabled = tpScreenRecorder.Enabled = tpOCR.Enabled = !TaskSettings.UseDefaultCaptureSettings;
            if (pTools != null) pTools.Enabled = !TaskSettings.UseDefaultToolsSettings;
        }

        #region Task

        private void AddEnumItemsContextMenu<T>(Action<T> selectedEnum, params ToolStripDropDown[] parents) where T : Enum
        {
            EnumInfo[] enums = Helpers.GetEnums<T>().OfType<Enum>().Select(x => new EnumInfo(x)).ToArray();

            foreach (ToolStripDropDown parent in parents)
            {
                foreach (EnumInfo enumInfo in enums)
                {
                    if (typeof(T) == typeof(HotkeyType))
                    {
                        HotkeyType action = (HotkeyType)enumInfo.Value;
                        if (action == HotkeyType.None ||
                            !SystemOptions.IsHotkeyActionAvailable(action) ||
                            (enumInfo.Category == HotkeyType.RectangleRegion.GetLocalizedCategory() &&
                             action != HotkeyType.RectangleRegion) ||
                            (enumInfo.Category == HotkeyType.ScreenRecorder.GetLocalizedCategory() &&
                             action != HotkeyType.ScreenRecorder) ||
                            (enumInfo.Category == HotkeyType.OCR.GetLocalizedCategory() &&
                             action != HotkeyType.ScreenColorPicker &&
                             action != HotkeyType.OCR &&
                             action != HotkeyType.ActiveWindowTopMost &&
                             action != HotkeyType.OpenDomesticNews &&
                             action != HotkeyType.OpenCalculator &&
                             action != HotkeyType.OpenScratchText &&
                             action != HotkeyType.OpenScratchMarkdown &&
                             action != HotkeyType.OpenDrawingCanvas &&
                             action != HotkeyType.SwitchToXiaomangInputMethod) ||
                            action == HotkeyType.ToggleActionsToolbar ||
                            action == HotkeyType.ToggleTrayMenu)
                        {
                            continue;
                        }
                    }

                    ToolStripMenuItem tsmi = new ToolStripMenuItem(enumInfo.Description.Replace("&", "&&"));
                    tsmi.Image = TaskHelpers.FindMenuIcon(enumInfo.Value);
                    tsmi.Tag = enumInfo;

                    tsmi.Click += (sender, e) =>
                    {
                        SetEnumCheckedContextMenu(enumInfo, parents);

                        selectedEnum((T)enumInfo.Value);

                        UpdateTaskTabMenuNames();
                    };

                    if (!string.IsNullOrEmpty(enumInfo.Category))
                    {
                        ToolStripMenuItem tsmiParent = parent.Items.OfType<ToolStripMenuItem>().FirstOrDefault(x => x.Text == enumInfo.Category);

                        if (tsmiParent == null)
                        {
                            tsmiParent = new ToolStripMenuItem(enumInfo.Category);
                            parent.Items.Add(tsmiParent);
                        }

                        tsmiParent.DropDownItems.Add(tsmi);
                    }
                    else
                    {
                        parent.Items.Add(tsmi);
                    }
                }
            }
        }

        private void SetEnumCheckedContextMenu(Enum value, params ToolStripDropDown[] parents)
        {
            SetEnumCheckedContextMenu(new EnumInfo(value), parents);
        }

        private void SetEnumCheckedContextMenu(EnumInfo enumInfo, params ToolStripDropDown[] parents)
        {
            foreach (ToolStripDropDown parent in parents)
            {
                foreach (ToolStripMenuItem tsmiParent in parent.Items)
                {
                    EnumInfo currentEnumInfo;

                    if (tsmiParent.DropDownItems.Count > 0)
                    {
                        foreach (ToolStripMenuItem tsmiCategoryParent in tsmiParent.DropDownItems)
                        {
                            currentEnumInfo = (EnumInfo)tsmiCategoryParent.Tag;
                            tsmiCategoryParent.Checked = currentEnumInfo.Value.Equals(enumInfo.Value);
                        }
                    }
                    else
                    {
                        currentEnumInfo = (EnumInfo)tsmiParent.Tag;
                        tsmiParent.Checked = currentEnumInfo.Value.Equals(enumInfo.Value);
                    }
                }
            }
        }

        private void AddMultiEnumItemsContextMenu<T>(Action<T> selectedEnum, params ToolStripDropDown[] parents) where T : Enum
        {
            string[] enums = Helpers.GetLocalizedEnumDescriptions<T>().Skip(1).Select(x => x.Replace("&", "&&")).ToArray();

            foreach (ToolStripDropDown parent in parents)
            {
                for (int i = 0; i < enums.Length; i++)
                {
                    ToolStripMenuItem tsmi = new ToolStripMenuItem(enums[i]);
                    tsmi.Image = TaskHelpers.FindMenuIcon<T>(i + 1);

                    int index = i;

                    tsmi.Click += (sender, e) =>
                    {
                        foreach (ToolStripDropDown parent2 in parents)
                        {
                            ToolStripMenuItem tsmi2 = (ToolStripMenuItem)parent2.Items[index];
                            tsmi2.Checked = !tsmi2.Checked;
                        }

                        selectedEnum((T)Enum.ToObject(typeof(T), 1 << index));

                        UpdateTaskTabMenuNames();
                    };

                    parent.Items.Add(tsmi);
                }
            }
        }

        private void SetMultiEnumCheckedContextMenu(Enum value, params ToolStripDropDown[] parents)
        {
            for (int i = 0; i < parents[0].Items.Count; i++)
            {
                foreach (ToolStripDropDown parent in parents)
                {
                    ToolStripMenuItem tsmi = (ToolStripMenuItem)parent.Items[i];
                    tsmi.Checked = value.HasFlag(1 << i);
                }
            }
        }

        private void UpdateTaskTabMenuNames()
        {
            btnTask.Text = TaskSettings.Job.GetLocalizedDescription();
            btnTask.Image = TaskHelpers.FindMenuIcon(TaskSettings.Job);

            btnAfterCapture.Text = string.Format(Resources.TaskSettingsForm_UpdateUploaderMenuNames_After_capture___0_,
                string.Join(", ", TaskSettings.AfterCaptureJob.GetFlags().Select(x => x.GetLocalizedDescription())));

        }

        private void tbDescription_TextChanged(object sender, EventArgs e)
        {
            TaskSettings.Description = tbDescription.Text;
            UpdateWindowTitle();
        }

        private void cbUseDefaultAfterCaptureSettings_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.UseDefaultAfterCaptureJob = !cbOverrideAfterCaptureSettings.Checked;
            btnAfterCapture.Enabled = !TaskSettings.UseDefaultAfterCaptureJob;
        }

        private void cbOverrideScreenshotsFolder_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.OverrideScreenshotsFolder = cbOverrideScreenshotsFolder.Checked;
            txtScreenshotsFolder.Enabled = btnScreenshotsFolderBrowse.Enabled = TaskSettings.OverrideScreenshotsFolder;
        }

        private void txtScreenshotsFolder_TextChanged(object sender, EventArgs e)
        {
            TaskSettings.ScreenshotsFolder = txtScreenshotsFolder.Text;
        }

        private void btnScreenshotsFolderBrowse_Click(object sender, EventArgs e)
        {
            FileHelpers.BrowseFolder(Resources.ApplicationSettingsForm_btnBrowseCustomScreenshotsPath_Click_Choose_screenshots_folder_path,
                txtScreenshotsFolder, TaskSettings.ScreenshotsFolder, true);
        }

        #endregion Task

        #region General

        private void cbUseDefaultGeneralSettings_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.UseDefaultGeneralSettings = !cbOverrideGeneralSettings.Checked;
            UpdateDefaultSettingVisibility();
        }

        #endregion General

        #region Capture

        #region General

        private void cbUseDefaultCaptureSettings_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.UseDefaultCaptureSettings = !cbOverrideCaptureSettings.Checked;
            UpdateDefaultSettingVisibility();
        }

        private void cbShowCursor_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.ShowCursor = cbShowCursor.Checked;
        }

        private void cbCaptureTransparent_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.CaptureTransparent = cbCaptureTransparent.Checked;
            cbCaptureShadow.Enabled = TaskSettings.CaptureSettings.CaptureTransparent;
        }

        private void cbCaptureShadow_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.CaptureShadow = cbCaptureShadow.Checked;
        }

        private void nudCaptureShadowOffset_ValueChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.CaptureShadowOffset = (int)nudCaptureShadowOffset.Value;
        }

        private void cbCaptureAutoHideDesktopIcons_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.CaptureAutoHideDesktopIcons = cbCaptureAutoHideDesktopIcons.Checked;
        }

        #endregion General

        #region Region capture

        private void cbRegionCaptureMouseRightClickAction_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbRegionCaptureMouseRightClickAction.SelectedIndex >= 0)
                TaskSettings.CaptureSettings.SurfaceOptions.RegionCaptureActionRightClick = (RegionCaptureAction)cbRegionCaptureMouseRightClickAction.SelectedIndex;
        }

        private void nudRegionCaptureBackgroundDimStrength_ValueChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.SurfaceOptions.BackgroundDimStrength = (int)nudRegionCaptureBackgroundDimStrength.Value;
        }

        private void cbRegionCaptureShowInfo_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.SurfaceOptions.ShowInfo = cbRegionCaptureShowInfo.Checked;
        }

        #endregion Region capture

        #region Screen recorder

        private void btnScreenRecorderFFmpegOptions_Click(object sender, EventArgs e)
        {
            ScreenRecordingOptions options = new ScreenRecordingOptions
            {
                IsRecording = true,
                FFmpeg = TaskSettings.CaptureSettings.FFmpegOptions,
                FPS = TaskSettings.CaptureSettings.ScreenRecordFPS,
                Duration = TaskSettings.CaptureSettings.ScreenRecordFixedDuration ? TaskSettings.CaptureSettings.ScreenRecordDuration : 0,
                OutputPath = "output.mp4",
                CaptureArea = Screen.PrimaryScreen.Bounds,
                DrawCursor = TaskSettings.CaptureSettings.ScreenRecordShowCursor
            };

            using (FFmpegOptionsForm form = new FFmpegOptionsForm(options))
            {
                form.ShowDialog();

                TaskSettings.CaptureSettings.FFmpegOptions = form.Options.FFmpeg;
            }
        }

        private void nudScreenRecordFPS_ValueChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.ScreenRecordFPS = (int)nudScreenRecordFPS.Value;
        }

        private void nudGIFFPS_ValueChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.GIFFPS = (int)nudGIFFPS.Value;
        }

        private void cbScreenRecorderFixedDuration_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.ScreenRecordFixedDuration = cbScreenRecorderFixedDuration.Checked;
            nudScreenRecorderDuration.Enabled = TaskSettings.CaptureSettings.ScreenRecordFixedDuration;
        }

        private void nudScreenRecorderDuration_ValueChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.ScreenRecordDuration = (float)nudScreenRecorderDuration.Value;
        }

        private void cbScreenRecordAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.ScreenRecordAutoStart = cbScreenRecordAutoStart.Checked;
            nudScreenRecorderStartDelay.Enabled = cbScreenRecordAutoStart.Checked;
        }

        private void nudScreenRecorderStartDelay_ValueChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.ScreenRecordStartDelay = (float)nudScreenRecorderStartDelay.Value;
        }

        private void cbScreenRecorderShowCursor_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.ScreenRecordShowCursor = cbScreenRecorderShowCursor.Checked;
        }

        private void cbScreenRecordTwoPassEncoding_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.ScreenRecordTwoPassEncoding = cbScreenRecordTwoPassEncoding.Checked;
        }

        private void cbScreenRecordTransparentRegion_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.ScreenRecordTransparentRegion = cbScreenRecordTransparentRegion.Checked;
        }

        private void cbScreenRecordConfirmAbort_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.ScreenRecordAskConfirmationOnAbort = cbScreenRecordConfirmAbort.Checked;
        }

        #endregion Screen recorder

        #region OCR

        private void cbCaptureOCRDefaultLanguage_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loaded)
            {
                TaskSettings.CaptureSettings.OCROptions.Language = ((OCRLanguage)cbCaptureOCRDefaultLanguage.SelectedItem).LanguageTag;
            }
        }

        private void btnCaptureOCRHelp_Click(object sender, EventArgs e)
        {
            URLHelpers.OpenURL(Links.DocsOCR);
        }

        private void cbCaptureOCRSilent_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.OCROptions.Silent = cbCaptureOCRSilent.Checked;
            cbCaptureOCRAutoCopy.Enabled = !TaskSettings.CaptureSettings.OCROptions.Silent;
        }

        private void cbCaptureOCRAutoCopy_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.OCROptions.AutoCopy = cbCaptureOCRAutoCopy.Checked;
        }

        private void cbCloseWindowAfterOpenServiceLink_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.CaptureSettings.OCROptions.CloseWindowAfterOpeningServiceLink = cbCloseWindowAfterOpenServiceLink.Checked;
        }

        #endregion OCR

        #endregion Capture

        #region Tools

        #region General

        private void cbUseDefaultToolsSettings_CheckedChanged(object sender, EventArgs e)
        {
            TaskSettings.UseDefaultToolsSettings = !cbOverrideToolsSettings.Checked;
            UpdateDefaultSettingVisibility();
        }

        private void txtToolsScreenColorPickerFormat_TextChanged(object sender, EventArgs e)
        {
            TaskSettings.ToolsSettings.ScreenColorPickerFormat = txtToolsScreenColorPickerFormat.Text;
        }

        private void txtToolsScreenColorPickerFormatCtrl_TextChanged(object sender, EventArgs e)
        {
            TaskSettings.ToolsSettings.ScreenColorPickerFormatCtrl = txtToolsScreenColorPickerFormatCtrl.Text;
        }

        private void txtToolsScreenColorPickerInfoText_TextChanged(object sender, EventArgs e)
        {
            TaskSettings.ToolsSettings.ScreenColorPickerInfoText = txtToolsScreenColorPickerInfoText.Text;
        }

        #endregion

        #endregion Tools

    }
}
