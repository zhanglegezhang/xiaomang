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
using ShareX.ImageEffectsLib;
using ShareX.MediaLib;
using ShareX.Properties;
using ShareX.ScreenCaptureLib;
using ShareX.UploadersLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    public partial class MainForm : HotkeyForm
    {
        public bool IsReady { get; private set; }
        internal ContextMenuStrip TrayMenu => cmsTray;
        private ImageCollageForm collageWindow;
        private TextCompareForm textCompareWindow;

        private bool forceClose, trayMenuSaveSettings = true;
        private bool exitPreparationPending;
        private bool exitPreparationComplete;
        private bool exitOldIgnoreHotkeys;
        private bool exitOldWidgetEnabled;
        private bool exitOldSettingsEnabled;
        private bool exitStateCaptured;
        private HookProc trayMenuMouseHookProc;
        private IntPtr trayMenuMouseHookHandle;
        private volatile Rectangle[] trayMenuScreenBounds = Array.Empty<Rectangle>();
        private volatile bool trayMenuOutsideClickPending;
        private Timer trayMenuOutsideClickTimer;
        private UploadInfoManager uim;
        private TodoWidgetForm todoWidget;
        private NoteLogStore noteLogStore;
        private NoteLogViewerControl noteLogViewer;
        private bool noteLogPageActive;
        private ScreenshotFolderViewerControl screenshotFolderViewer;
        private bool screenshotFolderPageActive;
        private TemporaryFileViewerControl temporaryFileViewer;
        private bool temporaryFilePageActive;
        private enum InlineSettingsKind { None, Application, DefaultTask, Hotkeys, HotkeyTask, About, InputMethod }
        private InlineSettingsKind inlineSettingsKind;
        private Panel inlineSettingsHost;
        private Panel inlineSettingsViewport;
        private Form inlineSettingsForm;
        private ApplicationSettingsForm cachedApplicationSettingsForm;
        private InputMethodPageForm cachedInputMethodForm;
        private HotkeySettingsForm cachedHotkeySettingsForm;
        private Point applicationSettingsScrollPosition;
        private HotkeySettingsForm suspendedHotkeySettingsForm;
        private Action hotkeyTaskUpdated;
        private bool firstMainMenuPagePending = true;
        private PresentationCanvasWindow presentationCanvasWindow;
        private ScratchEditorForm scratchMarkdownWindow;
        private ScratchEditorForm scratchTextWindow;
        private DailyNewsManager dailyNewsManager;
        private DailyNewsScheduler dailyNewsScheduler;
        private DailyNewsWindow domesticNewsWindow;
        private DailyNewsWindow internationalNewsWindow;
        private bool clipboardListenerActive;
        private bool mainHandleInitialized;
        private ClipboardTextMonitor clipboardTextMonitor;
        private VoiceTypingController voiceTyping;
        private InputMethodSwitchController inputMethodSwitchController;
        private ClipboardTextItem lastClipboardItem;
        private Process everythingProcess;
        private bool everythingStartedByThisApp;
        private WindowPinManager windowPinManager;
        private ImageFilesCache actionsMenuIconCache = new ImageFilesCache();

        public MainForm()
        {
            InitializeControls();
        }

        private async void MainForm_HandleCreated(object sender, EventArgs e)
        {
            clipboardListenerActive = AddClipboardFormatListener(Handle);
            DebugHelper.WriteLine("Clipboard plain-text monitor started. Native listener: " + clipboardListenerActive);

            // Handle recreation must restore the listener, but must not create another
            // widget, timer or set of startup jobs.
            if (mainHandleInitialized) return;
            mainHandleInitialized = true;

            RunPuushTasks();
            todoWidget = new TodoWidgetForm(noteLogStore);
            todoWidget.Show();
            DebugHelper.WriteLine("Todo widget started. Visible: " + todoWidget.Visible + "; Bounds: " + todoWidget.Bounds);

            NativeMethods.UseImmersiveDarkMode(Handle, ShareXResources.IsDarkTheme);

            await UpdateControls();
            ShowFirstMainMenuPage();
            DebugHelper.WriteLine("Tray icon initialized. Visible: " + niTray.Visible);
            clipboardTextMonitor = new ClipboardTextMonitor(OnClipboardItemRead);
            clipboardTextMonitor.Start();
            dailyNewsScheduler.Start();

            DebugHelper.WriteLine("Startup time: {0} ms", Program.StartTimer.ElapsedMilliseconds);

            await Program.CLI.UseCommandLineArgs();

            if (Program.Settings.ActionsToolbarRunAtStartup)
            {
                TaskHelpers.OpenActionsToolbar();
            }

        }

        private void InitializeControls()
        {
            InitializeComponent();
            if (SystemOptions.DisableUpdateCheck) Program.Settings.AutoCheckUpdate = false;
            DailyNewsStorageMaintenance.ApplyPendingCacheCleanup(Program.PersonalFolder);
            noteLogStore = new NoteLogStore(Program.PersonalFolder);
            dailyNewsManager = new DailyNewsManager(Program.PersonalFolder);
            dailyNewsScheduler = new DailyNewsScheduler(dailyNewsManager);
            dailyNewsScheduler.StatusChanged += (message, isError) => todoWidget?.SetNewsUpdateStatus(message, isError);
            windowPinManager = new WindowPinManager(this);

            // Keep the preferred tray-relative placement, but give the popup a real owner so
            // clicking outside it (including another app) closes it normally.
            niTray.ContextMenuStrip = null;
            cmsTray.AutoClose = true;
            trayMenuOutsideClickTimer = new Timer { Interval = 15 };
            trayMenuOutsideClickTimer.Tick += TrayMenuOutsideClickTimer_Tick;

            ShareXResources.UseWhiteIcon = Program.Settings.UseWhiteShareXIcon;

            Icon = ShareXResources.Icon;
            niTray.Icon = ShareXResources.Icon;
            niTray.MouseDoubleClick += niTray_MouseDoubleClick;
            Text = Program.Title;

            UpdateTheme();

            ToolStripButton tsbNoteFolder = new ToolStripButton("便签文件夹", Properties.Resources.folder_open_image);
            tsbNoteFolder.AutoSize = false;
            tsbNoteFolder.Size = tsbApplicationSettings.Size;
            tsbNoteFolder.Margin = tsbApplicationSettings.Margin;
            tsbNoteFolder.Padding = tsbApplicationSettings.Padding;
            tsbNoteFolder.DisplayStyle = tsbApplicationSettings.DisplayStyle;
            tsbNoteFolder.ImageAlign = tsbApplicationSettings.ImageAlign;
            tsbNoteFolder.TextAlign = tsbApplicationSettings.TextAlign;
            tsbNoteFolder.TextImageRelation = tsbApplicationSettings.TextImageRelation;
            tsbNoteFolder.Click += (sender, e) => OpenNoteLogs(GetPreferredNoteLogKind());
            tsbScreenshotsFolder.Text = "截图文件夹";
            tsbScreenshotsFolder.AutoSize = false;
            tsbScreenshotsFolder.Size = tsbApplicationSettings.Size;
            tsbScreenshotsFolder.Margin = tsbApplicationSettings.Margin;
            tsbScreenshotsFolder.Padding = tsbApplicationSettings.Padding;
            tsbScreenshotsFolder.DisplayStyle = tsbApplicationSettings.DisplayStyle;
            tsbScreenshotsFolder.ImageAlign = tsbApplicationSettings.ImageAlign;
            tsbScreenshotsFolder.TextAlign = tsbApplicationSettings.TextAlign;
            tsbScreenshotsFolder.TextImageRelation = tsbApplicationSettings.TextImageRelation;
            ToolStripButton tsbTemporaryFiles = new ToolStripButton("临时文件夹", Properties.Resources.folder_open_image)
            {
                AutoSize = false,
                Size = tsbApplicationSettings.Size,
                Margin = tsbApplicationSettings.Margin,
                Padding = tsbApplicationSettings.Padding,
                DisplayStyle = tsbApplicationSettings.DisplayStyle,
                ImageAlign = tsbApplicationSettings.ImageAlign,
                TextAlign = tsbApplicationSettings.TextAlign,
                TextImageRelation = tsbApplicationSettings.TextImageRelation
            };
            tsbTemporaryFiles.Click += (sender, e) => OpenTemporaryFilePage();
            ToolStripButton tsbInputMethod = new ToolStripButton("输入法", Properties.Resources.keyboard)
            {
                Name = "tsbInputMethod",
                DisplayStyle = tsbApplicationSettings.DisplayStyle,
                ImageAlign = tsbApplicationSettings.ImageAlign,
                TextAlign = tsbApplicationSettings.TextAlign,
                TextImageRelation = tsbApplicationSettings.TextImageRelation
            };
            tsbInputMethod.Click += (sender, e) => OpenInlineSettings(InlineSettingsKind.InputMethod);
            // Rebuild the main navigation so every visible entry shares the same width and
            // alignment. Only commands used by this navigation are constructed.
            tsMain.Items.Clear();
            tsMain.Items.AddRange(new ToolStripItem[]
            {
                tsbApplicationSettings,
                tsbHotkeySettings,
                tsbInputMethod,
                tsbNoteFolder,
                tsbScreenshotsFolder,
                tsbTemporaryFiles,
                tsbAbout
            });
            ConfigureMainMenuAppearance();

            // Rebuild the tray menu explicitly so removed commands cannot reappear through the
            // original designer collection or the recent-items submenu.
            cmsTray.Items.Clear();
            cmsTray.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripMenuItem("全局搜索", null, (_, _) => UnifiedSearchForm.Open()),
                tsmiTrayToggleHotkeys,
                new ToolStripSeparator(),
                tsmiTrayExit
            });
            CompactContextMenuStyle.Apply(cmsTray);

            this.CloseOnEscape();
            cmsTray.IgnoreSeparatorClick();
            cmsTaskInfo.IgnoreSeparatorClick();
            tsmiOpen.HideImageMargin();
            tsmiCopy.HideImageMargin();
            tsmiShortenSelectedURL.HideImageMargin();
            tsmiShareSelectedURL.HideImageMargin();

            lvUploads.SupportCustomTheme();

            ImageList il = new ImageList();
            il.ColorDepth = ColorDepth.Depth32Bit;
            il.Images.Add(Resources.navigation_090_button);
            il.Images.Add(Resources.cross_button);
            il.Images.Add(Resources.tick_button);
            il.Images.Add(Resources.navigation_000_button);
            il.Images.Add(Resources.clock);
            lvUploads.SmallImageList = il;

            TaskManager.TaskListView = new TaskListView(lvUploads);
            TaskManager.TaskThumbnailView = ucTaskThumbnailView;
            uim = new UploadInfoManager();

            HandleCreated += MainForm_HandleCreated;
        }

        public async Task UpdateControls()
        {
            IsReady = false;

            niTray.Visible = Program.Settings.ShowTray;

            TaskManager.UpdateMainFormTip();
            TaskManager.RecentManager.InitItems();

            bool isPositionChanged = false;

            if (Program.Settings.RememberMainFormPosition && !Program.Settings.MainFormPosition.IsEmpty &&
                CaptureHelpers.GetScreenBounds().IntersectsWith(new Rectangle(Program.Settings.MainFormPosition, Program.Settings.MainFormSize)))
            {
                StartPosition = FormStartPosition.Manual;
                Location = Program.Settings.MainFormPosition;
                isPositionChanged = true;
            }

            tsMain.Width = MainMenuAppearance.Scale(this, 168);
            int height = Math.Max(Size.Height + tsMain.PreferredSize.Height - tsMain.Height, MinimumSize.Height);
            MinimumSize = new Size(MinimumSize.Width, height);

            if (Program.Settings.RememberMainFormSize && !Program.Settings.MainFormSize.IsEmpty)
            {
                Rectangle availableArea = Screen.FromRectangle(Bounds).WorkingArea;
                Size = new Size(Math.Min(Program.Settings.MainFormSize.Width, availableArea.Width),
                    Math.Min(Program.Settings.MainFormSize.Height, availableArea.Height));

                if (!isPositionChanged)
                {
                    StartPosition = FormStartPosition.Manual;
                    Rectangle activeScreen = CaptureHelpers.GetActiveScreenBounds();
                    Location = GetCenteredLocation(activeScreen, Size);
                }
            }
            else
            {
                Size = new Size(Size.Width, height);
            }

            if (isPositionChanged)
            {
                Rectangle workingArea = Screen.FromRectangle(Bounds).WorkingArea;
                Location = ClampLocationToScreen(workingArea, Size, Location);
            }

            if (Program.Settings.PreviewSplitterDistance > 0)
            {
                scMain.SplitterDistance = Program.Settings.PreviewSplitterDistance;
            }

            if (Program.Settings.TaskListViewColumnWidths != null)
            {
                int len = Math.Min(lvUploads.Columns.Count - 1, Program.Settings.TaskListViewColumnWidths.Count);

                for (int i = 0; i < len; i++)
                {
                    lvUploads.Columns[i].Width = Program.Settings.TaskListViewColumnWidths[i];
                }
            }

            TaskbarManager.Enabled = Program.Settings.TaskbarProgressEnabled;

            UpdateCheckStates();
            UpdateUploaderMenuNames();
            UpdateToggleHotkeyButton();
            AfterTaskSettingsJobs();
            AfterApplicationSettingsJobs();

            await InitHotkeys();

            IsReady = true;
            try
            {
                inputMethodSwitchController = new InputMethodSwitchController(() => IsReady && !PersonalDataManager.Busy && !Program.Settings.DisableHotkeys &&
                    Program.HotkeyManager?.IgnoreHotkeys != true,
                    text => TaskHelpers.ShowNotificationTip(text, "小蟒文本输入法", 5000));
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Input method shortcut startup failed.");
                TaskHelpers.ShowNotificationTip("输入法切换快捷键未能启动：" + error.Message, "小蟒文本输入法", 5000);
            }
            try
            {
                voiceTyping = new VoiceTypingController(this, () => !PersonalDataManager.Busy && IsReady && !Program.Settings.DisableHotkeys &&
                    Program.HotkeyManager?.IgnoreHotkeys != true, () => todoWidget);
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Voice typing startup failed.");
                TaskHelpers.ShowNotificationTip("语音打字未能启动：" + error.Message, "语音打字", 5000);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)WindowsMessages.QUERYENDSESSION)
            {
                // Calling ToInt64 because the int conversion operator (called when directly casting the IntPtr to the enum)
                // enforces checked semantics thus crashes any 64 bits build. ToInt64() and long -> enum conversion doesn't.
                EndSessionReasons reason = (EndSessionReasons)m.LParam.ToInt64();
                if (reason.HasFlag(EndSessionReasons.ENDSESSION_CLOSEAPP))
                {
                    // Register for restart. This allows our application to automatically restart when it is installing an update from the Store.
                    // Also allows it to restart if it gets terminated for other reasons (see description of ENDSESSION_CLOSEAPP).
                    // Add the silent switch to avoid ShareX popping up in front of the user when the application restarts.
                    NativeMethods.RegisterApplicationRestart("-silent", 0);
                }
                m.Result = new IntPtr(1); // "Applications should respect the user's intentions and return TRUE."
            }
            else if (m.Msg == (int)WindowsMessages.ENDSESSION)
            {
                if (m.WParam != IntPtr.Zero)
                {
                    // If wParam is not equal to false (0), the application can be terminated at any moment after processing this message
                    // thus should save its data while processing the message.
                    Program.CloseSequence();
                }
                m.Result = IntPtr.Zero; // "If an application processes this message, it should return zero."
            }
            else if (m.Msg == (int)WindowsMessages.CLIPBOARDUPDATE)
            {
                if (IsReady) clipboardTextMonitor?.SignalClipboardChanged();
                base.WndProc(ref m);
            }
            else
            {
                base.WndProc(ref m);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (clipboardListenerActive)
            {
                RemoveClipboardFormatListener(Handle);
                clipboardListenerActive = false;
            }
            base.OnHandleDestroyed(e);
        }

        private void OnClipboardItemRead(ClipboardTextItem item)
        {
            if (PersonalDataManager.Busy || IsDisposed || Disposing) return;

            void ApplyClipboardItem()
            {
                if (PersonalDataManager.Busy || IsDisposed || Disposing) return;
                if (item == null || string.IsNullOrEmpty(item.Text))
                {
                    lastClipboardItem = null;
                    return;
                }
                if (item.HasSameContent(lastClipboardItem)) return;
                lastClipboardItem = item;
                if (todoWidget == null || todoWidget.IsDisposed) todoWidget = new TodoWidgetForm(noteLogStore);
                bool stored = todoWidget.AddClipboardItem(item);
                DebugHelper.WriteLine("Clipboard history update received. Stored: " + stored + "; Length: " + item.Text.Length);
            }

            try
            {
                if (InvokeRequired) BeginInvoke((Action)ApplyClipboardItem);
                else ApplyClipboardItem();
            }
            catch (InvalidOperationException)
            {
                // The application is already closing and the UI handle is no longer available.
            }
        }

        private static Point GetCenteredLocation(Rectangle screenBounds, Size windowSize)
        {
            int x = screenBounds.Left + Math.Max(0, (screenBounds.Width - windowSize.Width) / 2);
            int y = screenBounds.Top + Math.Max(0, (screenBounds.Height - windowSize.Height) / 2);
            return ClampLocationToScreen(screenBounds, windowSize, new Point(x, y));
        }

        private static Point ClampLocationToScreen(Rectangle screenBounds, Size windowSize, Point desired)
        {
            int maxX = Math.Max(screenBounds.Left, screenBounds.Right - windowSize.Width);
            int maxY = Math.Max(screenBounds.Top, screenBounds.Bottom - windowSize.Height);
            return new Point(Math.Clamp(desired.X, screenBounds.Left, maxX),
                Math.Clamp(desired.Y, screenBounds.Top, maxY));
        }

        private void AfterShownJobs()
        {
            if (!Program.Settings.ShowMostRecentTaskFirst && lvUploads.Items.Count > 0)
            {
                lvUploads.Items[lvUploads.Items.Count - 1].EnsureVisible();
            }

            if (Program.SteamFirstTimeConfig)
            {
                using (FirstTimeConfigForm firstTimeConfigForm = new FirstTimeConfigForm())
                {
                    firstTimeConfigForm.ShowDialog();
                }
            }
            else
            {
                this.ForceActivate();
            }
        }

        private async Task InitHotkeys()
        {
            await Task.Run(() => SettingManager.WaitHotkeysConfig());

            if (Program.HotkeyManager == null)
            {
                Program.HotkeyManager = new HotkeyManager(this);
                Program.HotkeyManager.HotkeyTrigger += HandleHotkeys;
            }

            Program.HotkeyManager.UpdateHotkeys(Program.HotkeysConfig.Hotkeys, !Program.IgnoreHotkeyWarning);
            if (Program.HotkeysConfig.InputMethodSwitchDefaultConflict)
            {
                Program.HotkeysConfig.InputMethodSwitchDefaultConflict = false;
                TaskHelpers.ShowNotificationTip("Ctrl+Shift+J 已用于其他功能。请在「快捷键」为「切换到小蟒输入法」选择其他按键。",
                    "小蟒文本输入法", 5000);
            }

            DebugHelper.WriteLine("HotkeyManager started.");

            if (Program.WatchFolderManager == null)
            {
                Program.WatchFolderManager = new WatchFolderManager();
            }

            Program.WatchFolderManager.UpdateWatchFolders();

            DebugHelper.WriteLine("WatchFolderManager started.");

            UpdateWorkflowsMenu();

            if (pHotkeys.Visible)
            {
                pHotkeys.Focus();
            }
        }

        private async void HandleHotkeys(HotkeySettings hotkeySetting)
        {
            DebugHelper.WriteLine("Hotkey triggered. " + hotkeySetting);

            await TaskHelpers.ExecuteJob(hotkeySetting.TaskSettings);
        }

        internal Task SwitchToXiaomangInputMethodAsync()
        {
            return inputMethodSwitchController?.SwitchAsync() ?? Task.CompletedTask;
        }

        private void UpdateWorkflowsMenu()
        {
            UpdateMainFormTip();
        }

        private void UpdateMainFormTip()
        {
            TaskManager.UpdateMainFormTip();

            dgvHotkeys.Rows.Clear();

            foreach (HotkeySettings hotkey in Program.HotkeysConfig.Hotkeys.Where(x => x.HotkeyInfo.IsValidHotkey))
            {
                int index = dgvHotkeys.Rows.Add();
                DataGridViewRow row = dgvHotkeys.Rows[index];
                row.Cells[0].Style.BackColor = row.Cells[0].Style.SelectionBackColor =
                    hotkey.HotkeyInfo.Status == HotkeyStatus.Registered ? Color.FromArgb(80, 160, 80) : Color.FromArgb(200, 80, 80);
                row.Cells[1].Value = hotkey.HotkeyInfo.ToString();
                row.Cells[2].Value = hotkey.TaskSettings.ToString();
            }
        }

        public static void Uncheck(params ToolStripDropDownItem[] lists)
        {
            foreach (ToolStripDropDownItem parent in lists)
            {
                foreach (ToolStripItem dropDownItem in parent.DropDownItems)
                {
                    ((ToolStripMenuItem)dropDownItem).Checked = false;
                }
            }
        }

        private static void SetEnumChecked(Enum value, params ToolStripDropDownItem[] parents)
        {
            if (value == null)
            {
                return;
            }

            int index = value.GetIndex();

            foreach (ToolStripDropDownItem parent in parents)
            {
                ((ToolStripMenuItem)parent.DropDownItems[index]).RadioCheck();
            }
        }

        private void UpdateInfoManager()
        {
            cmsTaskInfo.SuspendLayout();

            tsmiStopUpload.Visible = tsmiOpen.Visible = tsmiCopy.Visible = tsmiShowErrors.Visible = tsmiShowResponse.Visible =
                tsmiAnalyzeImage.Visible = tsmiGoogleLens.Visible = tsmiBingVisualSearch.Visible = tsmiShowQRCode.Visible = tsmiOCRImage.Visible =
                tsmiCombineImages.Visible = tsmiUploadSelectedFile.Visible = tsmiDownloadSelectedURL.Visible = tsmiEditSelectedFile.Visible =
                tsmiBeautifyImage.Visible = tsmiAddImageEffects.Visible = tsmiPinSelectedFile.Visible = tsmiRunAction.Visible =
                tsmiDeleteSelectedItem.Visible = tsmiDeleteSelectedFile.Visible = tsmiShortenSelectedURL.Visible = tsmiShareSelectedURL.Visible = false;

            if (Program.Settings.TaskViewMode == TaskViewMode.ListView)
            {
                pbPreview.Reset();
                uim.UpdateSelectedItems(lvUploads.SelectedItems.Cast<ListViewItem>().Select(x => x.Tag as WorkerTask));

                switch (Program.Settings.ImagePreview)
                {
                    case ImagePreviewVisibility.Show:
                        scMain.Panel2Collapsed = false;
                        break;
                    case ImagePreviewVisibility.Hide:
                        scMain.Panel2Collapsed = true;
                        break;
                    case ImagePreviewVisibility.Automatic:
                        scMain.Panel2Collapsed = !uim.IsItemSelected || (!uim.SelectedItem.IsImageFile && !uim.SelectedItem.IsImageURL);
                        break;
                }

                switch (Program.Settings.ImagePreviewLocation)
                {
                    case ImagePreviewLocation.Side:
                        scMain.Orientation = Orientation.Vertical;
                        break;
                    case ImagePreviewLocation.Bottom:
                        scMain.Orientation = Orientation.Horizontal;
                        break;
                }
            }
            else if (Program.Settings.TaskViewMode == TaskViewMode.ThumbnailView)
            {
                uim.UpdateSelectedItems(ucTaskThumbnailView.SelectedPanels.Select(x => x.Task));
            }

            if (uim.IsItemSelected)
            {
                // Open
                tsmiOpen.Visible = true;

                tsmiOpenURL.Enabled = uim.SelectedItem.IsURLExist;
                tsmiOpenShortenedURL.Enabled = uim.SelectedItem.IsShortenedURLExist;
                tsmiOpenThumbnailURL.Enabled = uim.SelectedItem.IsThumbnailURLExist;
                tsmiOpenDeletionURL.Enabled = uim.SelectedItem.IsDeletionURLExist;

                tsmiOpenFile.Enabled = uim.SelectedItem.IsFileExist;
                tsmiOpenFolder.Enabled = uim.SelectedItem.IsFileExist;
                tsmiOpenThumbnailFile.Enabled = uim.SelectedItem.IsThumbnailFileExist;

                if (uim.SelectedItems != null && uim.SelectedItems.Any(x => x.Task.IsWorking))
                {
                    tsmiStopUpload.Visible = true;
                }
                else
                {
                    tsmiShowErrors.Visible = uim.SelectedItem.Info.Result.IsError;

                    // Copy
                    tsmiCopy.Visible = true;

                    tsmiCopyURL.Enabled = uim.SelectedItems.Any(x => x.IsURLExist);
                    tsmiCopyShortenedURL.Enabled = uim.SelectedItems.Any(x => x.IsShortenedURLExist);
                    tsmiCopyThumbnailURL.Enabled = uim.SelectedItems.Any(x => x.IsThumbnailURLExist);
                    tsmiCopyDeletionURL.Enabled = uim.SelectedItems.Any(x => x.IsDeletionURLExist);

                    tsmiCopyFile.Enabled = uim.SelectedItem.IsFileExist;
                    tsmiCopyImage.Enabled = uim.SelectedItem.IsImageFile;
                    tsmiCopyImageDimensions.Enabled = uim.SelectedItem.IsImageFile;
                    tsmiCopyText.Enabled = uim.SelectedItem.IsTextFile;
                    tsmiCopyThumbnailFile.Enabled = uim.SelectedItem.IsThumbnailFileExist;
                    tsmiCopyThumbnailImage.Enabled = uim.SelectedItem.IsThumbnailFileExist;

                    tsmiCopyHTMLLink.Enabled = uim.SelectedItems.Any(x => x.IsURLExist);
                    tsmiCopyHTMLImage.Enabled = uim.SelectedItems.Any(x => x.IsImageURL);
                    tsmiCopyHTMLLinkedImage.Enabled = uim.SelectedItems.Any(x => x.IsImageURL && x.IsThumbnailURLExist);

                    tsmiCopyForumLink.Enabled = uim.SelectedItems.Any(x => x.IsURLExist);
                    tsmiCopyForumImage.Enabled = uim.SelectedItems.Any(x => x.IsImageURL && x.IsURLExist);
                    tsmiCopyForumLinkedImage.Enabled = uim.SelectedItems.Any(x => x.IsImageURL && x.IsThumbnailURLExist);

                    tsmiCopyMarkdownLink.Enabled = uim.SelectedItems.Any(x => x.IsURLExist);
                    tsmiCopyMarkdownImage.Enabled = uim.SelectedItems.Any(x => x.IsImageURL);
                    tsmiCopyMarkdownLinkedImage.Enabled = uim.SelectedItems.Any(x => x.IsImageURL && x.IsThumbnailURLExist);

                    tsmiCopyFilePath.Enabled = uim.SelectedItems.Any(x => x.IsFilePathValid);
                    tsmiCopyFileName.Enabled = uim.SelectedItems.Any(x => x.IsFilePathValid);
                    tsmiCopyFileNameWithExtension.Enabled = uim.SelectedItems.Any(x => x.IsFilePathValid);
                    tsmiCopyFolder.Enabled = uim.SelectedItems.Any(x => x.IsFilePathValid);

                    CleanCustomClipboardFormats();

                    if (Program.Settings.ClipboardContentFormats != null && Program.Settings.ClipboardContentFormats.Count > 0)
                    {
                        tssCopy6.Visible = true;

                        foreach (ClipboardFormat cf in Program.Settings.ClipboardContentFormats)
                        {
                            ToolStripMenuItem tsmiClipboardFormat = new ToolStripMenuItem(cf.Description);
                            tsmiClipboardFormat.Tag = cf;
                            tsmiClipboardFormat.Click += tsmiClipboardFormat_Click;
                            tsmiCopy.DropDownItems.Add(tsmiClipboardFormat);
                        }
                    }

                    tsmiUploadSelectedFile.Visible = !SystemOptions.DisableUpload && uim.SelectedItem.IsFileExist;
                    tsmiDownloadSelectedURL.Visible = uim.SelectedItem.IsFileURL;
                    tsmiEditSelectedFile.Visible = uim.SelectedItem.IsImageFile;
                    tsmiBeautifyImage.Visible = uim.SelectedItem.IsImageFile;
                    tsmiAddImageEffects.Visible = uim.SelectedItem.IsImageFile;
                    tsmiPinSelectedFile.Visible = uim.SelectedItem.IsImageFile;
                    UpdateActionsMenu(uim.SelectedItem.Info.FilePath);
                    tsmiDeleteSelectedItem.Visible = true;
                    tsmiDeleteSelectedFile.Visible = uim.SelectedItem.IsFileExist;
                    tsmiShortenSelectedURL.Visible = !SystemOptions.DisableUpload && uim.SelectedItem.IsURLExist;
                    tsmiShareSelectedURL.Visible = !SystemOptions.DisableUpload && uim.SelectedItem.IsURLExist;
                    tsmiAnalyzeImage.Visible = uim.SelectedItem.IsImageFile;
                    tsmiGoogleLens.Visible = uim.SelectedItem.IsURLExist;
                    tsmiBingVisualSearch.Visible = uim.SelectedItem.IsURLExist;
                    tsmiShowQRCode.Visible = uim.SelectedItem.IsURLExist;
                    tsmiOCRImage.Visible = uim.SelectedItem.IsImageFile;
                    tsmiCombineImages.Visible = uim.SelectedItems.Count(x => x.IsImageFile) > 1;
                    tsmiShowResponse.Visible = !string.IsNullOrEmpty(uim.SelectedItem.Info.Result.Response);
                }

                if (Program.Settings.TaskViewMode == TaskViewMode.ListView)
                {
                    if (!scMain.Panel2Collapsed)
                    {
                        if (uim.SelectedItem.IsImageFile)
                        {
                            pbPreview.LoadImageFromFileAsync(uim.SelectedItem.Info.FilePath);
                        }
                        else if (uim.SelectedItem.IsImageURL)
                        {
                            pbPreview.LoadImageFromURLAsync(uim.SelectedItem.Info.Result.URL);
                        }
                    }
                }
            }

            tsmiClearList.Visible = tssUploadInfo1.Visible = lvUploads.Items.Count > 0;

            cmsTaskInfo.ResumeLayout();

            Refresh();
        }

        private void UpdateTaskViewMode()
        {
            if (inlineSettingsKind != InlineSettingsKind.None && inlineSettingsHost != null && inlineSettingsHost.Visible)
            {
                scMain.Visible = false;
                ucTaskThumbnailView.Visible = false;
                pHotkeys.Visible = false;
                inlineSettingsHost.BringToFront();
                return;
            }

            if (noteLogPageActive && noteLogViewer != null && !noteLogViewer.IsDisposed)
            {
                scMain.Visible = false;
                ucTaskThumbnailView.Visible = false;
                pHotkeys.Visible = false;
                noteLogViewer.BringToFront();
                return;
            }

            if (screenshotFolderPageActive && screenshotFolderViewer != null && !screenshotFolderViewer.IsDisposed)
            {
                scMain.Visible = false;
                ucTaskThumbnailView.Visible = false;
                pHotkeys.Visible = false;
                screenshotFolderViewer.BringToFront();
                return;
            }

            if (temporaryFilePageActive && temporaryFileViewer != null && !temporaryFileViewer.IsDisposed)
            {
                scMain.Visible = false;
                ucTaskThumbnailView.Visible = false;
                pHotkeys.Visible = false;
                temporaryFileViewer.BringToFront();
                return;
            }

            if (Program.Settings.TaskViewMode == TaskViewMode.ListView)
            {
                tsmiSwitchTaskViewMode.Text = Resources.SwitchToThumbnailView;
                tsmiSwitchTaskViewMode.Image = Resources.application_icon_large;
                scMain.Visible = true;
                ucTaskThumbnailView.Visible = false;
                scMain.Focus();
            }
            else
            {
                tsmiSwitchTaskViewMode.Text = Resources.SwitchToListView;
                tsmiSwitchTaskViewMode.Image = Resources.application_list;
                ucTaskThumbnailView.Visible = true;
                scMain.Visible = false;
                ucTaskThumbnailView.Focus();
            }
        }

        public void UpdateTheme()
        {
            if (Program.Settings.Themes == null || Program.Settings.Themes.Count == 0)
            {
                Program.Settings.Themes = ShareXTheme.GetDefaultThemes();
                Program.Settings.SelectedTheme = 0;
            }

            if (!Program.Settings.Themes.IsValidIndex(Program.Settings.SelectedTheme))
            {
                Program.Settings.SelectedTheme = 0;
            }

            ShareXResources.Theme = Program.Settings.Themes[Program.Settings.SelectedTheme];

            if (IsHandleCreated)
            {
                NativeMethods.UseImmersiveDarkMode(Handle, ShareXResources.IsDarkTheme);
            }

#pragma warning disable WFO5001
            if (ShareXResources.IsDarkTheme)
            {
                Application.SetColorMode(SystemColorMode.Dark);
            }
            else
            {
                Application.SetColorMode(SystemColorMode.Classic);
            }
#pragma warning restore WFO5001

            BackColor = ShareXResources.Theme.BackgroundColor;
            tsMain.Font = ShareXResources.Theme.MenuFont;
            tsMain.Renderer = new ToolStripDarkRenderer();
            tsMain.DrawCustomBorder = false;
            CompactContextMenuStyle.Apply(cmsTray);
            ShareXResources.ApplyCustomThemeToContextMenuStrip(cmsTaskInfo);
            ttMain.BackColor = ShareXResources.Theme.BackgroundColor;
            ttMain.ForeColor = ShareXResources.Theme.TextColor;
            lvUploads.BackColor = ShareXResources.Theme.BackgroundColor;
            lvUploads.ForeColor = ShareXResources.Theme.TextColor;
            scMain.SplitterColor = ShareXResources.Theme.BackgroundColor;
            scMain.SplitterLineColor = ShareXResources.Theme.BorderColor;
            ShareXResources.ApplyCustomThemeToControl(dgvHotkeys);
            dgvHotkeys.BackgroundColor = ShareXResources.Theme.BackgroundColor;

            tsmiShowQRCode.Image = TaskHelpers.FindMenuIcon(HotkeyType.QRCode);

            tsmiOCRImage.Image = TaskHelpers.FindMenuIcon(HotkeyType.OCR);

            tsmiShortenSelectedURL.Image = TaskHelpers.FindMenuIcon(HotkeyType.ShortenURL);

            pbPreview.UpdateTheme();
            pbPreview.UpdateCheckers(true);
            ucTaskThumbnailView.UpdateTheme();
            UpdateInlineSettingsTheme();
            if (mainAppearanceReady) ConfigureMainMenuAppearance();
        }

        private void CleanCustomClipboardFormats()
        {
            tssCopy6.Visible = false;

            int tssCopy6Index = tsmiCopy.DropDownItems.IndexOf(tssCopy6);

            while (tssCopy6Index < tsmiCopy.DropDownItems.Count - 1)
            {
                using (ToolStripItem tsi = tsmiCopy.DropDownItems[tsmiCopy.DropDownItems.Count - 1])
                {
                    tsmiCopy.DropDownItems.Remove(tsi);
                }
            }
        }

        private void UpdateActionsMenu(string filePath)
        {
            tsmiRunAction.DropDownItems.Clear();

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                IEnumerable<ExternalProgram> actions = Program.DefaultTaskSettings.ExternalPrograms.
                    Where(x => !string.IsNullOrEmpty(x.Name) && x.CheckExtension(filePath));

                if (actions.Count() > 0)
                {
                    tsmiRunAction.Visible = true;

                    foreach (ExternalProgram action in actions)
                    {
                        string name = action.Name.Truncate(50, "...");
                        ToolStripMenuItem tsmi = new ToolStripMenuItem(name);

                        try
                        {
                            string actionFilePath = action.GetFullPath();
                            tsmi.Image = actionsMenuIconCache.GetFileIconAsImage(actionFilePath);
                        }
                        catch
                        {
                        }

                        tsmi.Click += async (sender, e) => await action.RunAsync(filePath);
                        tsmiRunAction.DropDownItems.Add(tsmi);
                    }
                }
            }
        }

        private void AfterApplicationSettingsJobs()
        {
            HotkeyRepeatLimit = Program.Settings.HotkeyRepeatLimit;

            HelpersOptions.CurrentProxy = Program.Settings.ProxySettings;
            HelpersOptions.URLEncodeIgnoreEmoji = Program.Settings.URLEncodeIgnoreEmoji;
            HelpersOptions.DefaultCopyImageFillBackground = Program.Settings.DefaultClipboardCopyImageFillBackground;
            HelpersOptions.UseAlternativeClipboardCopyImage = Program.Settings.UseAlternativeClipboardCopyImage;
            HelpersOptions.UseAlternativeClipboardGetImage = Program.Settings.UseAlternativeClipboardGetImage;
            HelpersOptions.RotateImageByExifOrientationData = Program.Settings.RotateImageByExifOrientationData;
            HelpersOptions.BrowserPath = Program.Settings.BrowserPath;
            HelpersOptions.RecentColors = Program.Settings.RecentColors;
            HelpersOptions.DevMode = Program.Settings.DevMode;
            Program.UpdateHelpersSpecialFolders();

            TaskManager.RecentManager.MaxCount = Program.Settings.RecentTasksMaxCount;

            UpdateTheme();
            Refresh();

            if (ShareXResources.UseWhiteIcon != Program.Settings.UseWhiteShareXIcon)
            {
                ShareXResources.UseWhiteIcon = Program.Settings.UseWhiteShareXIcon;

                Icon = ShareXResources.Icon;
                niTray.Icon = ShareXResources.Icon;
            }

            Text = Program.Title;
            niTray.Text = "小蟒";

#if RELEASE
            ConfigureAutoUpdate();
#else
            if (UpdateChecker.ForceUpdate)
            {
                ConfigureAutoUpdate();
            }
#endif

            UpdateTaskViewMode();
            UpdateMainWindowLayout();
            UpdateInfoManager();
        }

        private void ConfigureAutoUpdate()
        {
            Program.UpdateManager.AllowAutoUpdate = !SystemOptions.DisableUpdateCheck && Program.Settings.AutoCheckUpdate;
            Program.UpdateManager.UpdateChannel = Program.Settings.UpdateChannel;
            Program.UpdateManager.ConfigureAutoUpdate();
        }

        private void AfterTaskSettingsJobs()
        {
            // Capture reads the existing configuration directly; removed menu controls have no state to refresh.
        }

        public void UpdateCheckStates()
        {
            // The configuration remains authoritative after removing the capture/destination menus.
        }

        public static void SetTextFileDestinationChecked(TextDestination textDestination, FileDestination textFileDestination, params ToolStripDropDownItem[] lists)
        {
            if (textDestination == TextDestination.FileUploader)
            {
                SetEnumChecked(textFileDestination, lists);
            }
            else
            {
                Uncheck(lists);
            }
        }

        public static void SetImageFileDestinationChecked(ImageDestination imageDestination, FileDestination imageFileDestination, params ToolStripDropDownItem[] lists)
        {
            if (imageDestination == ImageDestination.FileUploader)
            {
                SetEnumChecked(imageFileDestination, lists);
            }
            else
            {
                Uncheck(lists);
            }
        }

        public void UpdateUploaderMenuNames()
        {
            // Retain the callback used by task helpers; the removed uploader menus have no labels to refresh.
        }

        private WorkerTask[] GetSelectedTasks()
        {
            if (lvUploads.SelectedItems.Count > 0)
            {
                return lvUploads.SelectedItems.Cast<ListViewItem>().Select(x => x.Tag as WorkerTask).Where(x => x != null).ToArray();
            }

            return null;
        }

        private void RemoveTasks(WorkerTask[] tasks)
        {
            if (tasks != null)
            {
                foreach (WorkerTask task in tasks.Where(x => x != null && !x.IsWorking))
                {
                    TaskManager.Remove(task);
                }

                UpdateInfoManager();
            }
        }

        private void RemoveSelectedItems()
        {
            IEnumerable<WorkerTask> tasks = null;

            if (Program.Settings.TaskViewMode == TaskViewMode.ListView)
            {
                tasks = lvUploads.SelectedItems.Cast<ListViewItem>().Select(x => x.Tag as WorkerTask);
            }
            else if (Program.Settings.TaskViewMode == TaskViewMode.ThumbnailView)
            {
                tasks = ucTaskThumbnailView.SelectedPanels.Select(x => x.Task);
            }

            RemoveTasks(tasks.ToArray());
        }

        private void RemoveAllItems()
        {
            RemoveTasks(lvUploads.Items.Cast<ListViewItem>().Select(x => x.Tag as WorkerTask).ToArray());
        }

        private void UpdateMainWindowLayout()
        {
            tsMain.Visible = Program.Settings.ShowMenu;
            pToolbars.Visible = Program.Settings.ShowMenu;

            ucTaskThumbnailView.TitleVisible = Program.Settings.ShowThumbnailTitle;
            ucTaskThumbnailView.TitleLocation = Program.Settings.ThumbnailTitleLocation;
            ucTaskThumbnailView.ThumbnailSize = Program.Settings.ThumbnailSize;
            ucTaskThumbnailView.ClickAction = Program.Settings.ThumbnailClickAction;

            lvUploads.HeaderStyle = Program.Settings.ShowColumns ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.None;

            Refresh();
        }

        public void UpdateToggleHotkeyButton()
        {
            if (Program.Settings.DisableHotkeys)
            {
                tsmiTrayToggleHotkeys.Text = Resources.MainForm_UpdateToggleHotkeyButton_Enable_hotkeys;
                tsmiTrayToggleHotkeys.Image = Resources.keyboard__plus;
            }
            else
            {
                tsmiTrayToggleHotkeys.Text = Resources.MainForm_UpdateToggleHotkeyButton_Disable_hotkeys;
                tsmiTrayToggleHotkeys.Image = Resources.keyboard__minus;
            }
        }

        private void RunPuushTasks()
        {
            if (Program.PuushMode && Program.Settings.IsFirstTimeRun)
            {
                using (PuushLoginForm puushLoginForm = new PuushLoginForm())
                {
                    if (puushLoginForm.ShowDialog() == DialogResult.OK)
                    {
                        Program.DefaultTaskSettings.ImageDestination = ImageDestination.FileUploader;
                        Program.DefaultTaskSettings.ImageFileDestination = FileDestination.Puush;
                        Program.DefaultTaskSettings.TextDestination = TextDestination.FileUploader;
                        Program.DefaultTaskSettings.TextFileDestination = FileDestination.Puush;
                        Program.DefaultTaskSettings.FileDestination = FileDestination.Puush;

                        SettingManager.WaitUploadersConfig();

                        if (Program.UploadersConfig != null)
                        {
                            Program.UploadersConfig.PuushAPIKey = puushLoginForm.APIKey;
                        }
                    }
                }
            }
        }

        public void ForceClose()
        {
            if (ScreenRecordManager.IsRecording)
            {
                if (MessageBox.Show(VisibleBranding.Replace(Resources.ShareXCannotBeClosedWhileScreenRecordingIsActive), Program.DisplayName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    ScreenRecordManager.AbortRecording();
                }
            }
            else
            {
                forceClose = true;
                Close();
            }
        }

        #region Form events

        protected override void SetVisibleCore(bool value)
        {
            if (value && !IsHandleCreated && (Program.SilentRun || Program.Settings.SilentRun) && Program.Settings.ShowTray)
            {
                CreateHandle();
                value = false;
            }

            base.SetVisibleCore(value);
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            AfterShownJobs();
            PersonalDataManager.NotifyDeferredInput();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            Refresh();
        }

        private void MainForm_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                Rectangle screenBounds = CaptureHelpers.GetScreenBounds();
                if (!screenBounds.IntersectsWith(Bounds))
                {
                    Rectangle activeScreen = Screen.FromPoint(Cursor.Position).WorkingArea;
                    Location = GetCenteredLocation(activeScreen, Size);
                }
                else
                {
                    Rectangle workingArea = Screen.FromRectangle(Bounds).WorkingArea;
                    Location = ClampLocationToScreen(workingArea, Size, Location);
                }

                ShowFirstMainMenuPage();
            }
        }

        private void ShowFirstMainMenuPage()
        {
            if (!firstMainMenuPagePending || !Visible || !IsReady || Program.HotkeyManager == null) return;

            firstMainMenuPagePending = false;
            OpenInlineSettings(InlineSettingsKind.Hotkeys);
        }

        private void MainForm_LocationChanged(object sender, EventArgs e)
        {
            if (IsReady && WindowState == FormWindowState.Normal)
            {
                Program.Settings.MainFormPosition = Location;
            }
        }

        private void MainForm_SizeChanged(object sender, EventArgs e)
        {
            if (IsReady && WindowState == FormWindowState.Normal)
            {
                Program.Settings.MainFormSize = Size;
            }
        }

        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (PersonalDataManager.Busy && !PersonalDataManager.RestartReady) { e.Cancel = true; return; }
            bool actuallyClosing = e.CloseReason != CloseReason.UserClosing || !Program.Settings.ShowTray || forceClose;
            if (actuallyClosing && !exitPreparationComplete)
            {
                // WinForms requires cancellation before the first await; close again only after durable saves.
                e.Cancel = true;
                if (await PrepareApplicationExitAsync())
                {
                    Close();
                    if (!IsDisposed && !Disposing) CancelApplicationExitPreparation();
                }
                return;
            }
            if (!exitPreparationComplete && inlineSettingsKind != InlineSettingsKind.None)
            {
                ApplyInlineSettingsChanges(false);
            }

            if (e.CloseReason == CloseReason.UserClosing && Program.Settings.ShowTray && !forceClose)
            {
                e.Cancel = true;
                Hide();
                SettingManager.SaveAllSettingsAsync();

                if (Program.Settings.FirstTimeMinimizeToTray)
                {
                    TaskHelpers.ShowNotificationTip(Resources.ShareXIsMinimizedToTheSystemTray, Program.DisplayName, 5000);
                    Program.Settings.FirstTimeMinimizeToTray = false;
                }
            }
        }

        private void CancelPreparedScratchClose()
        {
            foreach (ScratchEditorForm editor in new[] { scratchTextWindow, scratchMarkdownWindow })
            {
                if (editor != null && !editor.IsDisposed) editor.CancelPreparedClose();
            }
            todoWidget?.SetPendingWritesPaused(false);
        }

        internal async Task<bool> PrepareApplicationExitAsync()
        {
            if (exitPreparationComplete) return true;
            if (exitPreparationPending || (PersonalDataManager.Busy && !PersonalDataManager.RestartReady)) return false;
            exitPreparationPending = true;
            exitOldIgnoreHotkeys = Program.HotkeyManager?.IgnoreHotkeys ?? false;
            exitOldWidgetEnabled = todoWidget?.Enabled ?? true;
            exitOldSettingsEnabled = inlineSettingsForm?.Enabled ?? true;
            exitStateCaptured = true;
            try
            {
                if (Program.HotkeyManager != null) Program.HotkeyManager.IgnoreHotkeys = true;
                if (todoWidget != null && !todoWidget.IsDisposed) todoWidget.Enabled = false;
                if (inlineSettingsForm != null && !inlineSettingsForm.IsDisposed) inlineSettingsForm.Enabled = false;
                if (!await PreparePendingWritesAsync(false)) return false;
                foreach (Form tool in new Form[] { textCompareWindow, collageWindow })
                {
                    if (tool == null || tool.IsDisposed) continue;
                    tool.Close();
                    if (!tool.IsDisposed) return false;
                }
                exitPreparationComplete = true;
                forceClose = true;
                return true;
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Application exit preparation failed.");
                MessageBox.Show(this, "保存未完成，退出已取消，编辑内容已保留。\n" + error.Message,
                    Program.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            finally
            {
                exitPreparationPending = false;
                if (!exitPreparationComplete)
                {
                    CancelApplicationExitPreparation();
                }
            }
        }

        internal void CancelApplicationExitPreparation()
        {
            exitPreparationComplete = false;
            forceClose = false;
            CancelPreparedScratchClose();
            if (!exitStateCaptured) return;
            if (Program.HotkeyManager != null) Program.HotkeyManager.IgnoreHotkeys = exitOldIgnoreHotkeys;
            if (todoWidget != null && !todoWidget.IsDisposed) todoWidget.Enabled = exitOldWidgetEnabled;
            if (inlineSettingsForm != null && !inlineSettingsForm.IsDisposed) inlineSettingsForm.Enabled = exitOldSettingsEnabled;
            exitStateCaptured = false;
        }

        internal Task<bool> PreparePersonalDataEditorsAsync() => PreparePendingWritesAsync(true);

        private async Task<bool> PreparePendingWritesAsync(bool closeEditors)
        {
            bool prepared = false;
            try
            {
                if (inlineSettingsKind != InlineSettingsKind.None) ApplyInlineSettingsChanges(false);
                todoWidget?.SetPendingWritesPaused(true);
                foreach (ScratchEditorForm editor in new[] { scratchTextWindow, scratchMarkdownWindow })
                {
                    if (editor != null && !editor.IsDisposed && !await editor.PrepareCloseFromOwnerAsync()) return false;
                }
                while (true)
                {
                    try
                    {
                        todoWidget?.PreparePendingNoteWrites();
                        if (todoWidget != null && !todoWidget.IsDisposed) await todoWidget.FlushNoteLogsAsync();
                        await SettingManager.FlushHotkeysConfigAsync();
                        await SettingManager.FlushApplicationConfigAsync();
                        if (closeEditors)
                        {
                            foreach (ScratchEditorForm editor in new[] { scratchTextWindow, scratchMarkdownWindow })
                            {
                                if (editor == null || editor.IsDisposed) continue;
                                editor.Close();
                                if (!editor.IsDisposed) return false;
                            }
                        }
                        prepared = true;
                        return true;
                    }
                    catch (Exception error)
                    {
                        DebugHelper.WriteException(error, "Pending saves could not be completed.");
                        TaskDialogButton retry = new TaskDialogButton("重试保存");
                        TaskDialogButton back = new TaskDialogButton("返回");
                        TaskDialogButton choice = TaskDialog.ShowDialog(this, new TaskDialogPage
                        {
                            Caption = Program.DisplayName,
                            Heading = "保存尚未完成，操作已暂停",
                            Text = "内容已保留。请重试保存，或返回继续使用。\n" + error.Message,
                            Icon = TaskDialogIcon.Warning,
                            AllowCancel = true,
                            DefaultButton = back,
                            Buttons = { retry, back }
                        });
                        if (choice != retry) return false;
                    }
                }
            }
            finally
            {
                if (!prepared) CancelPreparedScratchClose();
                else if (closeEditors) todoWidget?.SetPendingWritesPaused(false);
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            inputMethodSwitchController?.Dispose();
            voiceTyping?.Dispose();
            trayMenuOutsideClickTimer.Stop();
            trayMenuOutsideClickTimer.Dispose();
            StopTrayMenuOutsideClickHook();
            clipboardTextMonitor?.Dispose();
            clipboardTextMonitor = null;
            dailyNewsScheduler?.Dispose();
            domesticNewsWindow?.Close();
            internationalNewsWindow?.Close();
            todoWidget?.Close();
            noteLogViewer?.Dispose();
            screenshotFolderViewer?.Dispose();
            temporaryFileViewer?.Dispose();
            if (scratchMarkdownWindow != null && !scratchMarkdownWindow.IsDisposed) scratchMarkdownWindow.Close();
            if (scratchTextWindow != null && !scratchTextWindow.IsDisposed) scratchTextWindow.Close();
            CloseEmbeddedForm(inlineSettingsForm);
            CloseEmbeddedForm(suspendedHotkeySettingsForm);
            if (cachedApplicationSettingsForm != inlineSettingsForm) CloseEmbeddedForm(cachedApplicationSettingsForm);
            if (cachedInputMethodForm != inlineSettingsForm) CloseEmbeddedForm(cachedInputMethodForm);
            if (cachedHotkeySettingsForm != inlineSettingsForm && cachedHotkeySettingsForm != suspendedHotkeySettingsForm)
                CloseEmbeddedForm(cachedHotkeySettingsForm);
            windowPinManager?.Dispose();
            CloseManagedEverything();
            TaskManager.StopAllTasks();
        }

        internal void OpenDailyNews(DailyNewsRegion region)
        {
            DailyNewsWindow window = region == DailyNewsRegion.Domestic ? domesticNewsWindow : internationalNewsWindow;
            if (window == null || window.IsDisposed)
            {
                window = new DailyNewsWindow(region, dailyNewsManager);
                if (region == DailyNewsRegion.Domestic) domesticNewsWindow = window;
                else internationalNewsWindow = window;
                window.Show(this);
            }
            else
            {
                if (!window.Visible) window.Show(this);
                if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
                window.Activate();
            }
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false) ||
                e.Data.GetDataPresent(DataFormats.Bitmap, false) ||
                e.Data.GetDataPresent(DataFormats.Text, false))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            UploadManager.DragDropUpload(e.Data);
        }

        private void TtMain_Draw(object sender, DrawToolTipEventArgs e)
        {
            e.DrawBackground();
            e.DrawBorder();
            e.DrawText();
        }

        private void dgvHotkeys_MouseUp(object sender, MouseEventArgs e)
        {
            if (Program.Settings.TaskViewMode == TaskViewMode.ListView)
            {
                if (e.Button == MouseButtons.Left)
                {
                    lvUploads.Focus();
                }
                else if (e.Button == MouseButtons.Right)
                {
                    UpdateInfoManager();
                    cmsTaskInfo.Show((Control)sender, e.X + 1, e.Y + 1);
                }
            }
            else if (Program.Settings.TaskViewMode == TaskViewMode.ThumbnailView)
            {
                if (e.Button == MouseButtons.Right)
                {
                    UcTaskView_ContextMenuRequested(sender, e);
                }
            }
        }

        private void dgvHotkeys_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                tsbHotkeySettings_Click(sender, e);
            }
        }

        private async void lvUploads_SelectedIndexChanged(object sender, EventArgs e)
        {
            lvUploads.SelectedIndexChanged -= lvUploads_SelectedIndexChanged;
            await Task.Delay(1);
            lvUploads.SelectedIndexChanged += lvUploads_SelectedIndexChanged;
            UpdateInfoManager();
        }

        private void lvUploads_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                UpdateInfoManager();
                cmsTaskInfo.Show(lvUploads, e.X + 1, e.Y + 1);
            }
        }

        private void lvUploads_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                uim.TryOpen();
            }
        }

        private void scMain_SplitterMoved(object sender, SplitterEventArgs e)
        {
            Program.Settings.PreviewSplitterDistance = scMain.SplitterDistance;
        }

        private void lvUploads_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyData)
            {
                default:
                    return;
                case Keys.Enter:
                    uim.TryOpen();
                    break;
                case Keys.Control | Keys.Enter:
                    uim.OpenFile();
                    break;
                case Keys.Shift | Keys.Enter:
                    uim.OpenFolder();
                    break;
                case Keys.Control | Keys.C:
                    uim.TryCopy();
                    break;
                case Keys.Shift | Keys.C:
                    uim.CopyFile();
                    break;
                case Keys.Alt | Keys.C:
                    uim.CopyImage();
                    break;
                case Keys.Control | Keys.Shift | Keys.C:
                    uim.CopyFilePath();
                    break;
                case Keys.Control | Keys.X:
                    uim.TryCopy();
                    RemoveSelectedItems();
                    break;
                case Keys.Control | Keys.V:
                    UploadManager.ClipboardUploadMainWindow();
                    break;
                case Keys.Control | Keys.U:
                    uim.Upload();
                    break;
                case Keys.Control | Keys.D:
                    uim.Download();
                    break;
                case Keys.Control | Keys.E:
                    uim.EditImage();
                    break;
                case Keys.Control | Keys.P:
                    uim.PinToScreen();
                    break;
                case Keys.Delete:
                    RemoveSelectedItems();
                    break;
                case Keys.Shift | Keys.Delete:
                    uim.DeleteFiles();
                    RemoveSelectedItems();
                    break;
                case Keys.Apps:
                    if (lvUploads.SelectedItems.Count > 0)
                    {
                        UpdateInfoManager();
                        Rectangle rect = lvUploads.GetItemRect(lvUploads.SelectedIndex);
                        cmsTaskInfo.Show(lvUploads, new Point(rect.X, rect.Bottom));
                    }
                    break;
            }

            e.SuppressKeyPress = true;
        }

        private void pbPreview_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && lvUploads.SelectedIndices.Count > 0)
            {
                string[] files = lvUploads.Items.Cast<ListViewItem>().Select(x => ((WorkerTask)x.Tag).Info?.FilePath).ToArray();
                int index = lvUploads.SelectedIndices[0];
                ImageViewer.ShowImage(files, index);
            }
        }

        private void ucTaskThumbnailView_SelectedPanelChanged(object sender, EventArgs e)
        {
            UpdateInfoManager();
        }

        private void UcTaskView_ContextMenuRequested(object sender, MouseEventArgs e)
        {
            cmsTaskInfo.Show(sender as Control, e.X + 1, e.Y + 1);
        }

        private void cmsTaskInfo_Closing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.Keyboard)
            {
                e.Cancel = !(NativeMethods.GetKeyState((int)Keys.Apps) < 0 || NativeMethods.GetKeyState((int)Keys.F10) < 0 || NativeMethods.GetKeyState((int)Keys.Escape) < 0);
            }
        }

        private void cmsTaskInfo_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyData == Keys.Apps)
            {
                cmsTaskInfo.Close();
            }
        }

        private void lvUploads_ColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e)
        {
            if (IsReady)
            {
                Program.Settings.TaskListViewColumnWidths = new List<int>();

                for (int i = 0; i < lvUploads.Columns.Count; i++)
                {
                    Program.Settings.TaskListViewColumnWidths.Add(lvUploads.Columns[i].Width);
                }
            }
        }

        private void lvUploads_ItemDrag(object sender, ItemDragEventArgs e)
        {
            TaskInfo[] taskInfos = GetSelectedTasks().Select(x => x.Info).Where(x => x != null).ToArray();

            if (taskInfos.Length > 0)
            {
                IDataObject dataObject = null;

                if (ModifierKeys.HasFlag(Keys.Control))
                {
                    string[] urls = taskInfos.Select(x => x.ToString()).Where(x => !string.IsNullOrEmpty(x)).ToArray();

                    if (urls.Length > 0)
                    {
                        dataObject = new DataObject(DataFormats.Text, string.Join(Environment.NewLine, urls));
                    }
                }
                else
                {
                    string[] files = taskInfos.Select(x => x.FilePath).Where(x => !string.IsNullOrEmpty(x) && File.Exists(x)).ToArray();

                    if (files.Length > 0)
                    {
                        dataObject = new DataObject(DataFormats.FileDrop, files);
                    }
                }

                if (dataObject != null)
                {
                    AllowDrop = false;
                    lvUploads.DoDragDrop(dataObject, DragDropEffects.Copy | DragDropEffects.Move);
                    AllowDrop = true;
                }
            }
        }

        #region Menu events

        private void tsbApplicationSettings_Click(object sender, EventArgs e)
        {
            OpenInlineSettings(InlineSettingsKind.Application);
        }

        private void tsbHotkeySettings_Click(object sender, EventArgs e)
        {
            if (Program.HotkeyManager != null)
            {
                OpenInlineSettings(InlineSettingsKind.Hotkeys);
            }
        }

        private void tsbScreenshotsFolder_Click(object sender, EventArgs e)
        {
            OpenScreenshotFolderPage();
        }

        private void tsbAbout_Click(object sender, EventArgs e)
        {
            OpenInlineSettings(InlineSettingsKind.About);
        }

        #endregion Menu events

        #region Tray events

        private async void niTray_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // A single left click is intentionally inert. NotifyIcon raises the dedicated
                // MouseDoubleClick event when the user completes a double click.
                return;
            }
            else if (e.Button == MouseButtons.Middle)
            {
                await TaskHelpers.ExecuteJob(Program.Settings.TrayMiddleClickAction);
            }
            else if (e.Button == MouseButtons.Right)
            {
                ShowTrayMenuAtPreferredPosition();
            }
        }

        private void niTray_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainMenu("tray double click");
            }
        }

        internal void ShowMainMenu(string source)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ShowMainMenu(source)));
                return;
            }

            bool nativeVisibleBefore = IsHandleCreated && NativeMethods.IsWindowVisible(Handle);
            DebugHelper.WriteLine($"Main menu activation requested ({source}). Managed visible: {Visible}; native visible: {nativeVisibleBefore}; state: {WindowState}");

            this.ForceActivate();

            // A launcher using STARTUPINFO/SW_HIDE can leave WinForms thinking the form is
            // visible while its HWND is hidden. Show it explicitly in that inconsistent state.
            if (IsHandleCreated && !NativeMethods.IsWindowVisible(Handle))
            {
                NativeMethods.ShowWindow(Handle, (int)WindowShowStyle.Show);
            }

            if (IsHandleCreated && NativeMethods.IsWindowVisible(Handle))
            {
                ShowFirstMainMenuPage();
                Activate();
                BringToFront();
                NativeMethods.SetForegroundWindow(Handle);
            }

            DebugHelper.WriteLine($"Main menu activation finished ({source}). Managed visible: {Visible}; native visible: {IsHandleCreated && NativeMethods.IsWindowVisible(Handle)}; state: {WindowState}");
        }

        internal void OpenUnifiedSearchResult(SearchHit hit)
        {
            if (todoWidget == null || todoWidget.IsDisposed) todoWidget = new TodoWidgetForm(noteLogStore);
            todoWidget.Show(); todoWidget.LocateSearchResult(hit);
        }

        internal void OpenInputMethod()
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OpenInputMethod));
                return;
            }

            ShowMainMenu("输入法安装按钮");
            OpenInlineSettings(InlineSettingsKind.InputMethod);
        }

        private void ShowTrayMenuAtPreferredPosition()
        {
            Point cursorPosition = Cursor.Position;
            Rectangle workingArea = Screen.FromPoint(cursorPosition).WorkingArea;
            Size menuSize = cmsTray.GetPreferredSize(Size.Empty);

            // Prefer the area above and to the right of the tray icon. If it would be clipped,
            // keep the whole menu visible by moving it only as far as necessary.
            int x = cursorPosition.X + 6;
            int y = cursorPosition.Y - menuSize.Height - 6;

            if (x + menuSize.Width > workingArea.Right)
            {
                x = workingArea.Right - menuSize.Width - 4;
            }

            if (x < workingArea.Left)
            {
                x = workingArea.Left + 4;
            }

            if (y < workingArea.Top)
            {
                y = Math.Min(cursorPosition.Y + 6, workingArea.Bottom - menuSize.Height - 4);
            }

            cmsTray.Show(this, PointToClient(new Point(x, y)));
        }

        private void tsmiTrayToggleHotkeys_Click(object sender, EventArgs e)
        {
            TaskHelpers.ToggleHotkeys();
        }

        internal async void AddCaptureToTodo(Bitmap capture, string language, float scale)
        {
            using (capture)
            {
                try
                {
                    string text = await OCRHelper.OCR(capture, language, scale, false);
                    if (IsDisposed || Disposing) return;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        MessageBox.Show("截图中未识别到文字，请重新框选清晰的文字区域。", "添加到待办", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (todoWidget == null || todoWidget.IsDisposed) todoWidget = new TodoWidgetForm(noteLogStore);
                    todoWidget.AddOcrText(text);
                }
                catch (Exception ex)
                {
                    DebugHelper.WriteException(ex);
                    if (!IsDisposed && !Disposing)
                        MessageBox.Show("识别失败，未添加待办：" + ex.Message, "添加到待办", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        internal void OpenRegistrationPage(string section)
        {
            OpenInlineSettings(InlineSettingsKind.Application);
            cachedApplicationSettingsForm?.SelectRegistrationPage(section);
        }

        private void OpenInlineSettings(InlineSettingsKind kind)
        {
            firstMainMenuPagePending = false;
            if (inlineSettingsKind == kind)
            {
                this.ForceActivate();
                return;
            }

            if (kind == InlineSettingsKind.Hotkeys && inlineSettingsKind == InlineSettingsKind.HotkeyTask)
            {
                ReturnToHotkeySettings();
                return;
            }

            try
            {
                if (!CloseInlineSettings()) return;
                Form form;
                switch (kind)
                {
                    case InlineSettingsKind.Application:
                        if (cachedApplicationSettingsForm == null || cachedApplicationSettingsForm.IsDisposed)
                            cachedApplicationSettingsForm = new ApplicationSettingsForm();
                        form = cachedApplicationSettingsForm;
                        break;
                    case InlineSettingsKind.DefaultTask:
                        form = new TaskSettingsForm(Program.DefaultTaskSettings, true);
                        break;
                    case InlineSettingsKind.Hotkeys:
                        if (cachedHotkeySettingsForm == null || cachedHotkeySettingsForm.IsDisposed)
                            cachedHotkeySettingsForm = new HotkeySettingsForm(Program.HotkeyManager);
                        else cachedHotkeySettingsForm.RefreshForActivation();
                        form = cachedHotkeySettingsForm;
                        break;
                    case InlineSettingsKind.About:
                        form = new AboutPageForm();
                        break;
                    case InlineSettingsKind.InputMethod:
                        if (cachedInputMethodForm == null || cachedInputMethodForm.IsDisposed)
                            cachedInputMethodForm = new InputMethodPageForm();
                        form = cachedInputMethodForm;
                        break;
                    default:
                        return;
                }

                ShowInlineSettingsForm(form, kind);
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Failed to open inline settings.");
                CloseInlineSettings();
                ShowTaskListPage();
                MessageBox.Show("打开设置页面失败：" + ex.Message, "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CreateInlineSettingsHost()
        {
            if (inlineSettingsHost != null) return;

            inlineSettingsHost = new Panel { Dock = DockStyle.Fill, Visible = false };
            inlineSettingsViewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            inlineSettingsViewport.Resize += (sender, e) => LayoutInlineSettingsForm();
            inlineSettingsHost.Controls.Add(inlineSettingsViewport);
            pMain.Controls.Add(inlineSettingsHost);
            UpdateInlineSettingsTheme();
        }

        private void UpdateInlineSettingsTheme()
        {
            if (inlineSettingsHost == null) return;

            Color background = ShareXResources.Theme.BackgroundColor;
            inlineSettingsHost.BackColor = background;
            inlineSettingsViewport.BackColor = background;
        }

        private void ShowInlineSettingsForm(Form form, InlineSettingsKind kind)
        {
            CreateInlineSettingsHost();
            inlineSettingsViewport.AutoScroll = kind is not (InlineSettingsKind.About or InlineSettingsKind.InputMethod or InlineSettingsKind.Application);
            // Responsive application pages own their vertical scrolling. The old
            // standalone minimum width must not enlarge the embedded viewport.
            if (kind == InlineSettingsKind.Application) form.MinimumSize = Size.Empty;
            noteLogPageActive = false;
            if (noteLogViewer != null && !noteLogViewer.IsDisposed) noteLogViewer.Visible = false;
            screenshotFolderPageActive = false;
            if (screenshotFolderViewer != null && !screenshotFolderViewer.IsDisposed) screenshotFolderViewer.Visible = false;
            temporaryFilePageActive = false;
            if (temporaryFileViewer != null && !temporaryFileViewer.IsDisposed) temporaryFileViewer.Visible = false;
            scMain.Visible = false;
            ucTaskThumbnailView.Visible = false;
            pHotkeys.Visible = false;

            bool firstShow = form.Parent != inlineSettingsViewport;
            if (firstShow)
            {
                form.TopLevel = false;
                form.FormBorderStyle = FormBorderStyle.None;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                inlineSettingsViewport.Controls.Add(form);
            }
            inlineSettingsForm = form;
            inlineSettingsKind = kind;
            inlineSettingsHost.Visible = true;
            inlineSettingsHost.BringToFront();
            this.ForceActivate();
            if (firstShow) EnsureInlineSettingsSpace(form);
            LayoutInlineSettingsForm();
            form.Show();
            if (firstShow) MainMenuAppearance.Apply(form);
            if (kind == InlineSettingsKind.Application)
                inlineSettingsViewport.AutoScrollPosition = applicationSettingsScrollPosition;
            tsMain.Invalidate();
        }

        private void EnsureInlineSettingsSpace(Form form)
        {
            if (WindowState != FormWindowState.Normal) return;

            int neededWidth = Math.Max(form.Width, form.MinimumSize.Width) - inlineSettingsViewport.ClientSize.Width;
            int neededHeight = Math.Max(form.Height, form.MinimumSize.Height) - inlineSettingsViewport.ClientSize.Height;
            if (neededWidth <= 0 && neededHeight <= 0) return;

            Rectangle workArea = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(workArea.Width, Width + Math.Max(0, neededWidth)),
                Math.Min(workArea.Height, Height + Math.Max(0, neededHeight)));
            Location = new Point(Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width)),
                Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height)));
        }

        private void LayoutInlineSettingsForm()
        {
            if (inlineSettingsForm == null || inlineSettingsForm.IsDisposed || inlineSettingsViewport == null) return;

            inlineSettingsForm.Location = Point.Empty;
            inlineSettingsForm.Size = new Size(
                Math.Max(inlineSettingsViewport.ClientSize.Width, inlineSettingsForm.MinimumSize.Width),
                Math.Max(inlineSettingsViewport.ClientSize.Height, inlineSettingsForm.MinimumSize.Height));
        }

        private void ReturnToHotkeySettings()
        {
            if (inlineSettingsKind != InlineSettingsKind.HotkeyTask || suspendedHotkeySettingsForm == null) return;

            inlineSettingsForm?.ValidateChildren();
            CloseEmbeddedForm(inlineSettingsForm);
            inlineSettingsForm = suspendedHotkeySettingsForm;
            suspendedHotkeySettingsForm = null;
            inlineSettingsKind = InlineSettingsKind.Hotkeys;
            hotkeyTaskUpdated?.Invoke();
            hotkeyTaskUpdated = null;
            LayoutInlineSettingsForm();
            inlineSettingsForm.Show();
            MainMenuAppearance.Apply(inlineSettingsForm);
            tsMain.Invalidate();
            SettingManager.SaveHotkeysConfigAsync();
            this.ForceActivate();
        }

        private void ApplyInlineSettingsChanges(bool saveSettings = true)
        {
            (inlineSettingsForm as HotkeySettingsForm)?.FinishEditing();
            inlineSettingsForm?.ValidateChildren();
            switch (inlineSettingsKind)
            {
                case InlineSettingsKind.Application:
                    if (inlineSettingsForm is ApplicationSettingsForm settings && !settings.HasPendingChanges) break;
                    AfterApplicationSettingsJobs();
                    AfterTaskSettingsJobs();
                    UpdateWorkflowsMenu();
                    if (saveSettings) SettingManager.SaveApplicationConfigAsync();
                    (inlineSettingsForm as ApplicationSettingsForm)?.AcceptChanges();
                    break;
                case InlineSettingsKind.DefaultTask:
                    AfterTaskSettingsJobs();
                    if (saveSettings) SettingManager.SaveApplicationConfigAsync();
                    break;
                case InlineSettingsKind.HotkeyTask:
                    hotkeyTaskUpdated?.Invoke();
                    goto case InlineSettingsKind.Hotkeys;
                case InlineSettingsKind.Hotkeys:
                    UpdateWorkflowsMenu();
                    if (saveSettings) SettingManager.SaveHotkeysConfigAsync();
                    break;
            }
        }

        private bool CloseInlineSettings()
        {
            if (inlineSettingsKind == InlineSettingsKind.None) return true;

            ApplyInlineSettingsChanges();
            if (inlineSettingsKind == InlineSettingsKind.Application)
                applicationSettingsScrollPosition = new Point(-inlineSettingsViewport.AutoScrollPosition.X, -inlineSettingsViewport.AutoScrollPosition.Y);
            inlineSettingsKind = InlineSettingsKind.None;
            Form currentForm = inlineSettingsForm;
            inlineSettingsForm = null;
            if (currentForm == cachedApplicationSettingsForm || currentForm == cachedInputMethodForm || currentForm == cachedHotkeySettingsForm) currentForm.Hide();
            else CloseEmbeddedForm(currentForm);
            if (suspendedHotkeySettingsForm == cachedHotkeySettingsForm) suspendedHotkeySettingsForm?.Hide();
            else CloseEmbeddedForm(suspendedHotkeySettingsForm);
            suspendedHotkeySettingsForm = null;
            hotkeyTaskUpdated = null;
            if (inlineSettingsHost != null) inlineSettingsHost.Visible = false;
            return true;
        }

        internal void OpenScratchMarkdown(string path = null)
            => OpenScratchEditor(true, path);

        internal void OpenScratchText(string path = null)
            => OpenScratchEditor(false, path);

        private async void OpenScratchEditor(bool markdown, string path)
        {
            try
            {
                await OpenScratchEditorCoreAsync(markdown, path);
            }
            catch (Exception error)
            {
                DebugHelper.WriteLine($"Scratch editor open failed: mode={(markdown ? "MD" : "TXT")}, error={error.GetType().Name}, code={error.HResult}.");
                MessageBox.Show("编辑器未能打开，请重试。若仍无法打开，请重启小蟒；已打开的编辑内容会保留。",
                    "小蟒", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task OpenScratchEditorCoreAsync(bool markdown, string path)
        {
            if (exitPreparationPending || exitPreparationComplete || PersonalDataManager.Busy) return;
            ScratchEditorForm current = markdown ? scratchMarkdownWindow : scratchTextWindow;
            if (current == null || current.IsDisposed)
            {
                Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
                Size savedSize = markdown ? Program.Settings.ScratchMarkdownWindowSize : Program.Settings.ScratchTextWindowSize;
                Size windowSize = new Size(
                    Math.Clamp(savedSize.Width, 650, Math.Max(650, workingArea.Width)),
                    Math.Clamp(savedSize.Height, 400, Math.Max(400, workingArea.Height)));
                ScratchEditorForm window = new ScratchEditorForm(markdown)
                {
                    StartPosition = FormStartPosition.CenterScreen,
                    Size = windowSize
                };
                void RememberWindowSize()
                {
                    Size size = window.WindowState == FormWindowState.Maximized ? window.RestoreBounds.Size : window.Size;
                    Size remembered = markdown ? Program.Settings.ScratchMarkdownWindowSize : Program.Settings.ScratchTextWindowSize;
                    if (size.Width < 650 || size.Height < 400 || size == remembered) return;
                    if (markdown) Program.Settings.ScratchMarkdownWindowSize = size;
                    else Program.Settings.ScratchTextWindowSize = size;
                    SettingManager.SaveApplicationConfigAsync();
                }
                window.ResizeEnd += (sender, args) => RememberWindowSize();
                window.FormClosed += (sender, args) =>
                {
                    RememberWindowSize();
                    if (ReferenceEquals(scratchMarkdownWindow, window)) scratchMarkdownWindow = null;
                    if (ReferenceEquals(scratchTextWindow, window)) scratchTextWindow = null;
                };
                if (markdown) scratchMarkdownWindow = window;
                else scratchTextWindow = window;
                window.ShowForEditing();
                if (path != null && !await window.LoadDocumentAsync(path)) window.Close();
                return;
            }

            current.ShowForEditing();
            if (path != null && !await current.LoadDocumentAsync(path)) return;
            if (path == null && !await current.NewDocumentAsync()) return;
            current.ShowForEditing();
        }

        internal void OpenVideoToGif()
        {
            TaskSettings settings = TaskSettings.GetDefaultTaskSettings();
            if (!TaskHelpers.CheckFFmpeg(settings)) return;
            VideoConverterOptions options = new VideoConverterOptions { VideoCodec = ConverterVideoCodecs.gif };
            string screenshotsFolder = TaskHelpers.GetScreenshotsFolder(settings);
            string initialDirectory = Directory.Exists(screenshotsFolder) ? screenshotsFolder : Program.ScreenshotsParentFolder;
            VideoConverterForm converter = new VideoConverterForm(settings.CaptureSettings.FFmpegOptions.FFmpegPath,
                options, initialDirectory, true);
            converter.Show();
        }

        internal void OpenImageCollage(IEnumerable<string> images = null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            DebugHelper.WriteLine("Collage activation requested.");
            try
            {
            if (collageWindow == null || collageWindow.IsDisposed)
            {
                collageWindow = new ImageCollageForm(images);
                if (Program.Settings.ImageCollageWindowSize == new Size(1200, 820))
                    Program.Settings.ImageCollageWindowSize = new Size(1300, 900);
                RememberUtilitySize(collageWindow, Program.Settings.ImageCollageWindowSize,
                    size => Program.Settings.ImageCollageWindowSize = size);
                collageWindow.Show();
            }
            else if (images != null) collageWindow.AddFiles(images);
            DebugHelper.WriteLine($"Collage before activation: managed={collageWindow.Visible}; native={collageWindow.IsHandleCreated && NativeMethods.IsWindowVisible(collageWindow.Handle)}; state={collageWindow.WindowState}");
            collageWindow.ShowForUser();
            DebugHelper.WriteLine($"Collage activation finished: native={NativeMethods.IsWindowVisible(collageWindow.Handle)}; elapsedMs={timer.ElapsedMilliseconds}");
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Collage activation failed.");
                if (collageWindow != null && !collageWindow.IsDisposed && !collageWindow.Visible)
                {
                    collageWindow.Dispose();
                    collageWindow = null;
                }
                MessageBox.Show("拼图窗口打开失败，请重试。错误详情已记录到日志。\n" + error.Message,
                    Program.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal void OpenTextCompare()
        {
            if (textCompareWindow == null || textCompareWindow.IsDisposed)
            {
                textCompareWindow = new TextCompareForm();
                RememberUtilitySize(textCompareWindow, Program.Settings.TextCompareWindowSize,
                    size => Program.Settings.TextCompareWindowSize = size);
                textCompareWindow.Show();
            }
            if (textCompareWindow.WindowState == FormWindowState.Minimized) textCompareWindow.WindowState = FormWindowState.Normal;
            textCompareWindow.Activate();
        }

        private void RememberUtilitySize(Form window, Size saved, Action<Size> remember)
        {
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            window.Size = new Size(Math.Min(work.Width, Math.Max(window.MinimumSize.Width, saved.Width)),
                Math.Min(work.Height, Math.Max(window.MinimumSize.Height, saved.Height)));
            window.ResizeEnd += (_, _) =>
            {
                Size size = window.WindowState == FormWindowState.Normal ? window.Size : window.RestoreBounds.Size;
                remember(size); SettingManager.SaveApplicationConfigAsync();
            };
        }

        internal void OpenWindowsCalculator()
        {
            try
            {
                Process.Start(new ProcessStartInfo("calc.exe") { UseShellExecute = true });
            }
            catch (Exception error)
            {
                MessageBox.Show("无法打开 Windows 计算器：" + error.Message, "计算器",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal void OpenWindowsNotepad(string path = null)
        {
            try
            {
                bool created = path == null;
                path ??= TemporaryFileStorage.CreateEmptyTextFile();
                if (!File.Exists(path)) throw new FileNotFoundException("临时文本文件不存在。", path);
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
                if (created)
                    TaskHelpers.ShowNotificationTip("文件已创建到临时文件夹，编辑后请在记事本中保存。", "空白.txt", 5000);
            }
            catch (Exception error)
            {
                MessageBox.Show("无法打开 Windows 记事本：" + error.Message, "空白.txt",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal void OpenSevenZip()
        {
            string fileManager = Path.Combine(AppContext.BaseDirectory, "ThirdParty", "SevenZip", "7zFM.exe");
            if (!File.Exists(fileManager))
            {
                MessageBox.Show("未找到随包附带的 7-Zip 文件管理器，请检查程序文件是否完整。", "7-Zip",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(fileManager)
                {
                    WorkingDirectory = Path.GetDirectoryName(fileManager),
                    UseShellExecute = false
                });
            }
            catch (Exception error)
            {
                MessageBox.Show("无法打开 7-Zip：" + error.Message, "7-Zip",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void CloseEmbeddedForm(Form form)
        {
            if (form == null || form.IsDisposed) return;
            form.Close();
            form.Dispose();
        }

        private void OpenNoteLogs(NoteLogKind kind)
        {
            try
            {
                if (!CloseInlineSettings()) return;
                screenshotFolderPageActive = false;
                if (screenshotFolderViewer != null && !screenshotFolderViewer.IsDisposed) screenshotFolderViewer.Visible = false;
                temporaryFilePageActive = false;
                if (temporaryFileViewer != null && !temporaryFileViewer.IsDisposed) temporaryFileViewer.Visible = false;
                if (noteLogViewer == null || noteLogViewer.IsDisposed)
                {
                    noteLogViewer = new NoteLogViewerControl(noteLogStore, kind) { Visible = false };
                    pMain.Controls.Add(noteLogViewer);
                }

                noteLogPageActive = true;
                scMain.Visible = false;
                ucTaskThumbnailView.Visible = false;
                pHotkeys.Visible = false;
                noteLogViewer.Visible = true;
                noteLogViewer.BringToFront();
                pMain.PerformLayout();
                noteLogViewer.SelectCategory(kind);
                MainMenuAppearance.Apply(noteLogViewer);
                tsMain.Invalidate();
                this.ForceActivate();
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Failed to open note logs.");
                ShowTaskListPage();
                MessageBox.Show("打开便签日志失败，请稍后重试。错误信息：" + ex.Message,
                    "便签文件夹", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private NoteLogKind GetPreferredNoteLogKind()
        {
            return noteLogViewer != null && !noteLogViewer.IsDisposed
                ? noteLogViewer.SelectedKind
                : NoteLogKind.Todo;
        }

        private void OpenScreenshotFolderPage()
        {
            try
            {
                if (!CloseInlineSettings()) return;
                noteLogPageActive = false;
                if (noteLogViewer != null && !noteLogViewer.IsDisposed) noteLogViewer.Visible = false;
                temporaryFilePageActive = false;
                if (temporaryFileViewer != null && !temporaryFileViewer.IsDisposed) temporaryFileViewer.Visible = false;
                if (screenshotFolderViewer == null || screenshotFolderViewer.IsDisposed)
                {
                    screenshotFolderViewer = new ScreenshotFolderViewerControl
                    {
                        Visible = false,
                        VideoFFmpegPath = TaskSettings.GetDefaultTaskSettings().CaptureSettings.FFmpegOptions.FFmpegPath
                    };
                    screenshotFolderViewer.CombineImagesRequested += paths => OpenImageCollage(paths);
                    pMain.Controls.Add(screenshotFolderViewer);
                }

                screenshotFolderPageActive = true;
                scMain.Visible = false;
                ucTaskThumbnailView.Visible = false;
                pHotkeys.Visible = false;
                screenshotFolderViewer.Visible = true;
                screenshotFolderViewer.BringToFront();
                pMain.PerformLayout();
                screenshotFolderViewer.RefreshFiles(Program.ScreenshotsParentFolder);
                MainMenuAppearance.Apply(screenshotFolderViewer);
                tsMain.Invalidate();
                this.ForceActivate();
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Failed to open screenshot folder page.");
                ShowTaskListPage();
                MessageBox.Show("打开截图文件夹失败：" + ex.Message,
                    "截图文件夹", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenTemporaryFilePage()
        {
            try
            {
                if (!CloseInlineSettings()) return;
                noteLogPageActive = false;
                screenshotFolderPageActive = false;
                if (noteLogViewer != null && !noteLogViewer.IsDisposed) noteLogViewer.Visible = false;
                if (screenshotFolderViewer != null && !screenshotFolderViewer.IsDisposed) screenshotFolderViewer.Visible = false;
                if (temporaryFileViewer == null || temporaryFileViewer.IsDisposed)
                {
                    temporaryFileViewer = new TemporaryFileViewerControl(path =>
                    {
                        if (Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)) OpenScratchMarkdown(path);
                        else if (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)) OpenScratchText(path);
                        else OpenWindowsNotepad(path);
                    }) { Visible = false };
                    pMain.Controls.Add(temporaryFileViewer);
                }

                temporaryFilePageActive = true;
                scMain.Visible = false;
                ucTaskThumbnailView.Visible = false;
                pHotkeys.Visible = false;
                temporaryFileViewer.Visible = true;
                temporaryFileViewer.BringToFront();
                pMain.PerformLayout();
                temporaryFileViewer.RefreshFiles();
                MainMenuAppearance.Apply(temporaryFileViewer);
                tsMain.Invalidate();
                this.ForceActivate();
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Failed to open temporary files page.");
                ShowTaskListPage();
                MessageBox.Show("打开临时文件夹失败：" + error.Message,
                    "临时文件夹", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowTaskListPage()
        {
            if (!CloseInlineSettings()) return;
            noteLogPageActive = false;
            screenshotFolderPageActive = false;
            temporaryFilePageActive = false;
            if (noteLogViewer != null && !noteLogViewer.IsDisposed)
            {
                noteLogViewer.Visible = false;
            }
            if (screenshotFolderViewer != null && !screenshotFolderViewer.IsDisposed)
            {
                screenshotFolderViewer.Visible = false;
            }
            if (temporaryFileViewer != null && !temporaryFileViewer.IsDisposed)
            {
                temporaryFileViewer.Visible = false;
            }
            pHotkeys.Visible = false;
            UpdateTaskViewMode();
        }

        private static string GetEverythingExecutablePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "ThirdParty", "Everything", "everything.exe");
        }

        internal void OpenDeskPins()
        {
            windowPinManager?.BeginSelection();
        }

        private static string GetDataOrganizerPath()
        {
            string preferredPath = Path.Combine(AppContext.BaseDirectory, "数据整理小工具", "_V1.0.html");
            if (File.Exists(preferredPath)) return preferredPath;

            return Path.Combine(AppContext.BaseDirectory, "数据整理小工具_V1.0.html");
        }

        internal void OpenDataOrganizer()
        {
            string htmlPath = GetDataOrganizerPath();

            if (!File.Exists(htmlPath))
            {
                MessageBox.Show("未找到数据整理工具 HTML 文件，请检查程序包是否完整。", "数据整理",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = htmlPath,
                    WorkingDirectory = Path.GetDirectoryName(htmlPath),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex);
                MessageBox.Show("无法打开数据整理工具：" + ex.Message, "数据整理",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal void OpenEverything()
        {
            string executablePath = GetEverythingExecutablePath();

            if (!File.Exists(executablePath))
            {
                MessageBox.Show("未找到 Everything 便携组件，请重新部署完整程序包。", "everything",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                bool alreadyRunning = Process.GetProcessesByName("Everything").Any(process =>
                {
                    try { return !process.HasExited; }
                    catch { return false; }
                });
                Process started = Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    UseShellExecute = true
                });
                if (!alreadyRunning && started != null)
                {
                    everythingProcess?.Dispose();
                    everythingProcess = started;
                    everythingStartedByThisApp = true;
                }
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex);
                MessageBox.Show("无法启动 Everything：" + ex.Message, "everything",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CloseManagedEverything()
        {
            if (!everythingStartedByThisApp || everythingProcess == null) return;
            try
            {
                if (everythingProcess.HasExited) return;
                everythingProcess.CloseMainWindow();
                if (!everythingProcess.WaitForExit(1500))
                {
                    everythingProcess.Kill(true);
                    everythingProcess.WaitForExit(1000);
                }
            }
            catch (Exception error)
            {
                // Everything is configured to run elevated. A non-elevated parent can only
                // make a best-effort shutdown request, and must never kill a pre-existing instance.
                DebugHelper.WriteException(error, "无法关闭由小蟒同学启动的 Everything。");
            }
            finally
            {
                everythingProcess.Dispose();
                everythingProcess = null;
                everythingStartedByThisApp = false;
            }
        }

        internal void OpenPresentationCanvas()
        {
            if (presentationCanvasWindow == null)
            {
                presentationCanvasWindow = new PresentationCanvasWindow();
                presentationCanvasWindow.Closed += (sender, e) => presentationCanvasWindow = null;
                presentationCanvasWindow.Show();
            }
            else
            {
                presentationCanvasWindow.Activate();
            }
        }

        private void tsmiTrayExit_MouseDown(object sender, MouseEventArgs e)
        {
            trayMenuSaveSettings = false;
        }

        private void cmsTray_Opened(object sender, EventArgs e)
        {
            RefreshTrayMenuScreenBounds();
            trayMenuOutsideClickPending = false;
            StartTrayMenuOutsideClickHook();
            trayMenuOutsideClickTimer.Start();

        }

        private void StartTrayMenuOutsideClickHook()
        {
            if (trayMenuMouseHookHandle != IntPtr.Zero)
            {
                return;
            }

            trayMenuMouseHookProc ??= TrayMenuMouseHookProc;

            using Process currentProcess = Process.GetCurrentProcess();
            using ProcessModule currentModule = currentProcess.MainModule;
            IntPtr moduleHandle = NativeMethods.GetModuleHandle(currentModule.ModuleName);
            trayMenuMouseHookHandle = NativeMethods.SetWindowsHookEx(
                NativeConstants.WH_MOUSE_LL, trayMenuMouseHookProc, moduleHandle, 0);

            if (trayMenuMouseHookHandle == IntPtr.Zero)
            {
                DebugHelper.WriteLine("Unable to install tray menu outside-click hook.");
            }
            else
            {
                DebugHelper.WriteLine("Tray menu outside-click hook installed.");
            }
        }

        private IntPtr TrayMenuMouseHookProc(int code, IntPtr message, IntPtr dataPointer)
        {
            // Keep the low-level callback independent from WinForms controls. It can run
            // while another window owns input; the UI timer performs the actual close.
            if (code >= 0 && IsMouseButtonDown(message.ToInt32()))
            {
                LowLevelMouseHookData data = Marshal.PtrToStructure<LowLevelMouseHookData>(dataPointer);
                Rectangle[] visibleMenuBounds = trayMenuScreenBounds;
                if (!visibleMenuBounds.Any(bounds => bounds.Contains(data.Point.X, data.Point.Y)))
                {
                    trayMenuOutsideClickPending = true;
                }
            }

            return NativeMethods.CallNextHookEx(trayMenuMouseHookHandle, code, message, dataPointer);
        }

        private static bool IsMouseButtonDown(int message)
        {
            return message == (int)WindowsMessages.LBUTTONDOWN ||
                message == (int)WindowsMessages.RBUTTONDOWN ||
                message == (int)WindowsMessages.MBUTTONDOWN ||
                message == (int)WindowsMessages.XBUTTONDOWN;
        }

        private void cmsTray_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            trayMenuOutsideClickTimer.Stop();
            trayMenuOutsideClickPending = false;
            StopTrayMenuOutsideClickHook();

            if (trayMenuSaveSettings)
            {
                SettingManager.SaveAllSettingsAsync();
            }

            trayMenuSaveSettings = true;
        }

        private void StopTrayMenuOutsideClickHook()
        {
            if (trayMenuMouseHookHandle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.UnhookWindowsHookEx(trayMenuMouseHookHandle);
            trayMenuMouseHookHandle = IntPtr.Zero;
        }

        private void TrayMenuOutsideClickTimer_Tick(object sender, EventArgs e)
        {
            if (!trayMenuOutsideClickPending)
            {
                RefreshTrayMenuScreenBounds();
                return;
            }

            trayMenuOutsideClickPending = false;
            RefreshTrayMenuScreenBounds();

            if (cmsTray.Visible)
            {
                cmsTray.Close(ToolStripDropDownCloseReason.AppClicked);
            }
        }

        private void RefreshTrayMenuScreenBounds()
        {
            List<Rectangle> bounds = new List<Rectangle>();

            void AddVisibleDropDown(ToolStripDropDown dropDown)
            {
                if (!dropDown.Visible)
                {
                    return;
                }

                bounds.Add(dropDown.RectangleToScreen(dropDown.ClientRectangle));

                foreach (ToolStripDropDownItem item in dropDown.Items.OfType<ToolStripDropDownItem>())
                {
                    if (item.DropDown.Visible)
                    {
                        AddVisibleDropDown(item.DropDown);
                    }
                }
            }

            AddVisibleDropDown(cmsTray);
            trayMenuScreenBounds = bounds.ToArray();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LowLevelMouseHookData
        {
            public POINT Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        private void tsmiTrayExit_Click(object sender, EventArgs e)
        {
            ForceClose();
        }

        #endregion Tray events

        #region UploadInfoMenu events

        private void tsmiShowErrors_Click(object sender, EventArgs e)
        {
            uim.ShowErrors();
        }

        private void tsmiStopUpload_Click(object sender, EventArgs e)
        {
            uim.StopUpload();
        }

        private void tsmiOpenURL_Click(object sender, EventArgs e)
        {
            uim.OpenURL();
        }

        private void tsmiOpenShortenedURL_Click(object sender, EventArgs e)
        {
            uim.OpenShortenedURL();
        }

        private void tsmiOpenThumbnailURL_Click(object sender, EventArgs e)
        {
            uim.OpenThumbnailURL();
        }

        private void tsmiOpenDeletionURL_Click(object sender, EventArgs e)
        {
            uim.OpenDeletionURL();
        }

        private void tsmiOpenFile_Click(object sender, EventArgs e)
        {
            uim.OpenFile();
        }

        private void tsmiOpenThumbnailFile_Click(object sender, EventArgs e)
        {
            uim.OpenThumbnailFile();
        }

        private void tsmiOpenFolder_Click(object sender, EventArgs e)
        {
            uim.OpenFolder();
        }

        private void tsmiCopyURL_Click(object sender, EventArgs e)
        {
            uim.CopyURL();
        }

        private void tsmiCopyShortenedURL_Click(object sender, EventArgs e)
        {
            uim.CopyShortenedURL();
        }

        private void tsmiCopyThumbnailURL_Click(object sender, EventArgs e)
        {
            uim.CopyThumbnailURL();
        }

        private void tsmiCopyDeletionURL_Click(object sender, EventArgs e)
        {
            uim.CopyDeletionURL();
        }

        private void tsmiCopyFile_Click(object sender, EventArgs e)
        {
            uim.CopyFile();
        }

        private void tsmiCopyImage_Click(object sender, EventArgs e)
        {
            uim.CopyImage();
        }

        private void tsmiCopyImageDimensions_Click(object sender, EventArgs e)
        {
            uim.CopyImageDimensions();
        }

        private void tsmiCopyText_Click(object sender, EventArgs e)
        {
            uim.CopyText();
        }

        private void tsmiCopyThumbnailFile_Click(object sender, EventArgs e)
        {
            uim.CopyThumbnailFile();
        }

        private void tsmiCopyThumbnailImage_Click(object sender, EventArgs e)
        {
            uim.CopyThumbnailImage();
        }

        private void tsmiCopyHTMLLink_Click(object sender, EventArgs e)
        {
            uim.CopyHTMLLink();
        }

        private void tsmiCopyHTMLImage_Click(object sender, EventArgs e)
        {
            uim.CopyHTMLImage();
        }

        private void tsmiCopyHTMLLinkedImage_Click(object sender, EventArgs e)
        {
            uim.CopyHTMLLinkedImage();
        }

        private void tsmiCopyForumLink_Click(object sender, EventArgs e)
        {
            uim.CopyForumLink();
        }

        private void tsmiCopyForumImage_Click(object sender, EventArgs e)
        {
            uim.CopyForumImage();
        }

        private void tsmiCopyForumLinkedImage_Click(object sender, EventArgs e)
        {
            uim.CopyForumLinkedImage();
        }

        private void tsmiCopyMarkdownLink_Click(object sender, EventArgs e)
        {
            uim.CopyMarkdownLink();
        }

        private void tsmiCopyMarkdownImage_Click(object sender, EventArgs e)
        {
            uim.CopyMarkdownImage();
        }

        private void tsmiCopyMarkdownLinkedImage_Click(object sender, EventArgs e)
        {
            uim.CopyMarkdownLinkedImage();
        }

        private void tsmiCopyFilePath_Click(object sender, EventArgs e)
        {
            uim.CopyFilePath();
        }

        private void tsmiCopyFileName_Click(object sender, EventArgs e)
        {
            uim.CopyFileName();
        }

        private void tsmiCopyFileNameWithExtension_Click(object sender, EventArgs e)
        {
            uim.CopyFileNameWithExtension();
        }

        private void tsmiCopyFolder_Click(object sender, EventArgs e)
        {
            uim.CopyFolder();
        }

        private void tsmiClipboardFormat_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem tsmiClipboardFormat = sender as ToolStripMenuItem;
            ClipboardFormat cf = tsmiClipboardFormat.Tag as ClipboardFormat;
            uim.CopyCustomFormat(cf.Format);
        }

        private void tsmiUploadSelectedFile_Click(object sender, EventArgs e)
        {
            uim.Upload();
        }

        private void tsmiDownloadSelectedURL_Click(object sender, EventArgs e)
        {
            uim.Download();
        }

        private void tsmiDeleteSelectedItem_Click(object sender, EventArgs e)
        {
            RemoveSelectedItems();
        }

        private void tsmiDeleteSelectedFile_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Resources.MainForm_tsmiDeleteSelectedFile_Click_Do_you_really_want_to_delete_this_file_,
                "ShareX - " + Resources.MainForm_tsmiDeleteSelectedFile_Click_File_delete_confirmation, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                uim.DeleteFiles();
                RemoveSelectedItems();
            }
        }

        private void tsmiEditSelectedFile_Click(object sender, EventArgs e)
        {
            uim.EditImage();
        }

        private void tsmiBeautifyImage_Click(object sender, EventArgs e)
        {
            uim.BeautifyImage();
        }

        private void tsmiAddImageEffects_Click(object sender, EventArgs e)
        {
            uim.AddImageEffects();
        }

        private void tsmiPinSelectedFile_Click(object sender, EventArgs e)
        {
            uim.PinToScreen();
        }

        private void tsmiGoogleLens_Click(object sender, EventArgs e)
        {
            uim.SearchImageUsingGoogleLens();
        }

        private void tsmiBingVisualSearch_Click(object sender, EventArgs e)
        {
            uim.SearchImageUsingBing();
        }

        private void tsmiAnalyzeImage_Click(object sender, EventArgs e)
        {
            uim.AnalyzeImage();
        }

        private void tsmiShowQRCode_Click(object sender, EventArgs e)
        {
            uim.ShowQRCode();
        }

        private async void tsmiOCRImage_Click(object sender, EventArgs e)
        {
            await uim.OCRImage();
        }

        private void tsmiCombineImages_Click(object sender, EventArgs e)
        {
            uim.CombineImages();
        }

        private void tsmiCombineImagesHorizontally_Click(object sender, EventArgs e)
        {
            uim.CombineImages(Orientation.Horizontal);
        }

        private void tsmiCombineImagesVertically_Click(object sender, EventArgs e)
        {
            uim.CombineImages(Orientation.Vertical);
        }

        private void tsmiShowResponse_Click(object sender, EventArgs e)
        {
            uim.ShowResponse();
        }

        private void tsmiClearList_Click(object sender, EventArgs e)
        {
            RemoveAllItems();
            TaskManager.RecentManager.Clear();
        }

        private void TsmiSwitchTaskViewMode_Click(object sender, EventArgs e)
        {
            tsMain.SendToBack();

            if (Program.Settings.TaskViewMode == TaskViewMode.ListView)
            {
                Program.Settings.TaskViewMode = TaskViewMode.ThumbnailView;
                ucTaskThumbnailView.UpdateAllThumbnails();
            }
            else
            {
                Program.Settings.TaskViewMode = TaskViewMode.ListView;
            }

            UpdateTaskViewMode();
            UpdateMainWindowLayout();
            UpdateInfoManager();
        }

        #endregion UploadInfoMenu events

        #endregion Form events
    }
}

