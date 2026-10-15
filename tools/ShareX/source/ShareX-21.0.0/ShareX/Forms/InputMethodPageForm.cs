using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class QingjianInputMethodPageForm : Form
    {
        private readonly TableLayoutPanel content;
        private readonly Label installationStatus;
        private readonly Label operationStatus;
        private readonly Label stableVersion;
        private readonly Label previewVersion;
        private readonly Label versionCheckStatus;
        private readonly Button checkUpdates;
        private readonly Button releaseNotes;
        private readonly QingjianUpdateChecker updateChecker;
        private readonly CancellationTokenSource updateCancellation = new();
        private QingjianUpdateState updateState;
        private bool checkingUpdates;
        private readonly Button install;
        private readonly Button settings;
        private readonly Button uninstall;
        private readonly Button cancel;
        private readonly ProgressBar progress;
        private readonly TextBox installDirectory;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private CancellationTokenSource downloadCancellation;
        private bool localOperation;
        private bool watchingOperation;

        public QingjianInputMethodPageForm() : this(new QingjianUpdateChecker(
            Path.Combine(Program.PersonalFolder, "Cache", "Qingjian", "versions.json"))) { }

        internal QingjianInputMethodPageForm(QingjianUpdateChecker updateChecker)
        {
            this.updateChecker = updateChecker;
            updateState = updateChecker.ReadState();
            Text = "青简输入法";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            Font = MainMenuAppearance.BodyFont;
            Size = new Size(780, 550);
            MinimumSize = new Size(580, 440);
            content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(24),
                Margin = Padding.Empty
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(content);
            Add(CreateLabel("青简输入法", 16, true));
            Add(CreateLabel("小蟒可安装版本：青简 " + QingjianInputMethodManager.Version + " · 拼音 / 双拼 / 五笔\n安装后可在 Windows 应用中使用，按 Win + 空格切换到青简。"));
            installationStatus = CreateLabel("");
            installationStatus.Name = "installationStatus";
            Add(installationStatus);

            stableVersion = CreateLabel("");
            stableVersion.Name = "latestStableVersion";
            previewVersion = CreateLabel("");
            previewVersion.Name = "latestPreviewVersion";
            versionCheckStatus = CreateLabel("");
            versionCheckStatus.Name = "versionCheckStatus";
            Add(stableVersion);
            Add(previewVersion);
            FlowLayoutPanel updateActions = new()
            {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 12)
            };
            checkUpdates = CreateButton("检查更新", "checkInputMethodUpdates");
            releaseNotes = CreateButton("查看更新说明", "viewInputMethodReleaseNotes");
            updateActions.Controls.AddRange(new Control[] { checkUpdates, releaseNotes });
            Add(updateActions);
            Add(versionCheckStatus);
            checkUpdates.Click += async (sender, e) => await CheckUpdatesAsync(true);
            releaseNotes.Click += (sender, e) => RunAction(() => URLHelpers.OpenURL(LatestRelease().PageUrl));

            FlowLayoutPanel actions = new()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 4, 0, 10)
            };
            install = CreateButton("安装并启用", "installInputMethod");
            settings = CreateButton("输入法设置", "openInputMethodSettings");
            uninstall = CreateButton("卸载输入法", "uninstallInputMethod");
            actions.Controls.AddRange(new Control[] { install, settings, uninstall });
            Add(actions);
            install.Click += async (sender, e) => await InstallAsync();
            uninstall.Click += async (sender, e) => await UninstallAsync();
            settings.Click += (sender, e) => RunAction(QingjianInputMethodManager.OpenSettings);
            Add(CreateLabel("没有已校验的安装器时，需联网下载约 94 MB；安装由 Windows 请求管理员权限。\n安装器保存在程序文件夹内，随小蟒一起移动。\n请保持官方默认安装目录；卸载保留学习词库与设置，可能需重启才能完全清除组件。"));

            progress = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Visible = false, Margin = new Padding(0, 4, 0, 8) };
            Add(progress);
            operationStatus = CreateLabel("");
            operationStatus.Name = "operationStatus";
            Add(operationStatus);
            cancel = CreateButton("取消下载", "cancelInputMethodDownload");
            cancel.Visible = false;
            cancel.Click += (sender, e) => downloadCancellation?.Cancel();
            Add(cancel);

            AddPath("安装器下载目录", QingjianInputMethodManager.GetDownloadDirectory(), "downloadDirectory");
            installDirectory = AddPath("输入法安装目录", QingjianInputMethodManager.DefaultInstallDirectory, "installDirectory");
            AddPath("学习词库与设置", QingjianInputMethodManager.UserDataDirectory, "userDataDirectory");

            FlowLayoutPanel links = new() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
            AddLink(links, "项目与源码", QingjianInputMethodManager.SourceUrl);
            AddLink(links, "安装说明", QingjianInputMethodManager.InstallHelpUrl);
            AddLink(links, "开源许可", QingjianInputMethodManager.ProjectUrl + "/blob/v0.1.4/LICENSE");
            Add(links);
            Add(CreateLabel("青简为独立开源输入法，采用 GPL-3.0-or-later 许可。\n当前 Windows 版仍在测试，部分系统界面可能遮挡候选窗口。"));
            ClientSizeChanged += (sender, e) => UpdateWrapping();
            Shown += async (sender, e) =>
            {
                MainMenuAppearance.Apply(this);
                UpdateWrapping();
                RefreshInstallation();
                await CheckUpdatesAsync(false);
            };
            refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            refreshTimer.Tick += (sender, e) => RefreshInstallation();
            refreshTimer.Start();
            // Set the design DPI after all dynamically-created children exist; setting it
            // earlier lets WinForms finish autoscaling an empty form at the current DPI.
            AutoScaleDimensions = new SizeF(96, 96);
            PerformAutoScale();
            RefreshInstallation();
        }

        private void Add(Control control)
        {
            int row = content.RowCount++;
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(control, 0, row);
        }

        private static Label CreateLabel(string text, float size = 9, bool bold = false) => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            Margin = new Padding(0, 0, 0, 12)
        };

        private static Button CreateButton(string text, string name) => new()
        {
            Text = text,
            Name = name,
            Size = new Size(128, 34),
            Margin = new Padding(0, 0, 10, 0)
        };

        private TextBox AddPath(string title, string path, string name)
        {
            Add(CreateLabel(title));
            TableLayoutPanel row = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 16) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            TextBox box = new() { Name = name, Text = path, ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 10, 0) };
            Button open = CreateButton("打开", "open" + name);
            open.Dock = DockStyle.Fill;
            open.Margin = Padding.Empty;
            open.Click += (sender, e) => RunAction(() =>
            {
                if (!Directory.Exists(box.Text))
                {
                    operationStatus.Text = "此目录尚未生成，安装或首次使用后会自动创建。";
                    return;
                }
                FileHelpers.OpenFolder(box.Text);
            });
            row.Controls.Add(box, 0, 0);
            row.Controls.Add(open, 1, 0);
            Add(row);
            return box;
        }

        private void AddLink(FlowLayoutPanel parent, string text, string url)
        {
            LinkLabel link = new() { Text = text, AutoSize = true, Margin = new Padding(0, 0, 20, 0) };
            link.LinkClicked += (sender, e) => RunAction(() => URLHelpers.OpenURL(url));
            parent.Controls.Add(link);
        }

        private void UpdateWrapping()
        {
            int width = Math.Max(180, ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            content.ColumnStyles[0].SizeType = SizeType.Absolute;
            content.ColumnStyles[0].Width = width;
            foreach (Control control in content.Controls)
                if (control is Label label) label.MaximumSize = new Size(width, 0);
        }

        private void RefreshInstallation()
        {
            if (IsDisposed || Disposing) return;
            try
            {
                QingjianInstallation state = QingjianInputMethodManager.GetInstallation();
                installDirectory.Text = state.Directory;
                installationStatus.Text = !QingjianInputMethodManager.IsSupported ? "此电脑的系统架构暂不支持安装当前组件。" :
                    state.IsInstalled ? "已安装版本：青简" + (state.Version.Length == 0 ? "（版本未知）" : " " + state.Version) :
                    state.CanUninstall || state.CanOpenSettings || state.HasRegistration ? "检测到输入法组件，但文件或注册不完整，可修复安装。" : "尚未安装";
                RefreshVersions(state.IsInstalled ? state.Version : QingjianInputMethodManager.Version);
                bool busy = localOperation || QingjianInputMethodManager.IsBusy;
                install.Enabled = !busy && !state.IsInstalled && QingjianInputMethodManager.IsSupported;
                install.Text = state.CanUninstall || state.CanOpenSettings ? "修复安装" : "安装并启用";
                settings.Enabled = !busy && state.CanOpenSettings;
                uninstall.Enabled = !busy && state.CanUninstall;
                if (!localOperation && QingjianInputMethodManager.IsBusy)
                {
                    watchingOperation = true;
                    operationStatus.Text = "输入法操作正在进行，完成后状态自动刷新。";
                }
                else if (watchingOperation && !busy)
                {
                    watchingOperation = false;
                    operationStatus.Text = "输入法操作已结束，当前安装状态已刷新。";
                }
            }
            catch (Exception error)
            {
                installationStatus.Text = "读取输入法状态失败：" + error.Message;
                install.Enabled = settings.Enabled = uninstall.Enabled = false;
            }
        }

        private QingjianRelease LatestRelease()
        {
            var stable = updateState.Stable;
            var preview = updateState.Preview;
            return stable == null ? preview : preview != null && QingjianUpdateChecker.IsNewer(preview.Version, stable.Version) ? preview : stable;
        }

        private void RefreshVersions(string current)
        {
            string Describe(QingjianRelease release, bool preview)
            {
                string title = preview ? "最新测试版：" : "最新正式版：";
                if (release == null) return title + (updateState.CheckedUtc.HasValue ? "暂无可用 Windows 版本" : "尚未获取");
                bool newer = QingjianUpdateChecker.IsNewer(release.Version, current) &&
                    (!preview || updateState.Stable == null || QingjianUpdateChecker.IsNewer(release.Version, updateState.Stable.Version));
                return title + release.Version + " · " + release.PublishedUtc.ToLocalTime().ToString("yyyy-MM-dd") + " 发布" +
                    (newer ? (preview ? " · 有新测试版" : " · 有新正式版") : "");
            }
            stableVersion.Text = Describe(updateState.Stable, false);
            previewVersion.Text = Describe(updateState.Preview, true);
            releaseNotes.Enabled = LatestRelease() != null;
            checkUpdates.Enabled = !checkingUpdates;
            checkUpdates.Text = checkingUpdates ? "检查中…" : "检查更新";
            string stamp = updateState.CheckedUtc.HasValue
                ? "上次成功检查：" + updateState.CheckedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "尚无成功检查记录";
            versionCheckStatus.Text = checkingUpdates ? "正在查询青简官方版本信息…" :
                !string.IsNullOrEmpty(updateState.LastError) ? updateState.LastError + "\n" + stamp + "；以上为上次结果。" :
                stamp + "。自动检查每天最多一次，手动检查可随时刷新。";
            if (updateState.CacheWriteFailed && !checkingUpdates) versionCheckStatus.Text += "\n本次结果未能保存到缓存。";
        }

        private async Task CheckUpdatesAsync(bool force)
        {
            if (checkingUpdates || IsDisposed || Disposing) return;
            checkingUpdates = true;
            RefreshInstallation();
            try
            {
                var result = await updateChecker.CheckAsync(force, updateCancellation.Token);
                if (!IsDisposed && !Disposing) updateState = result;
            }
            catch (OperationCanceledException) when (updateCancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Input method version check failed.");
                if (!IsDisposed && !Disposing) updateState.LastError = "检查失败，请稍后重试。";
            }
            finally
            {
                checkingUpdates = false;
                if (!IsDisposed && !Disposing) RefreshInstallation();
            }
        }

        private async Task InstallAsync()
        {
            if (localOperation || QingjianInputMethodManager.IsBusy) return;
            localOperation = true;
            downloadCancellation = new CancellationTokenSource();
            progress.Value = 0;
            progress.Visible = cancel.Visible = true;
            cancel.Enabled = true;
            RefreshInstallation();
            var reporter = new Progress<QingjianProgress>(value =>
            {
                if (IsDisposed || Disposing || !localOperation) return;
                operationStatus.Text = value.Message;
                progress.Value = Math.Clamp(value.Percent, 0, 100);
                cancel.Visible = value.CanCancel;
            });
            try
            {
                string message = await QingjianInputMethodManager.InstallAsync(reporter, downloadCancellation.Token);
                if (!IsDisposed) operationStatus.Text = message;
            }
            catch (OperationCanceledException error)
            {
                if (!IsDisposed) operationStatus.Text = error.Message == "已取消管理员授权。" ? error.Message : "已取消下载或安装，可稍后重试。";
            }
            catch (Exception error) { ShowError(error); }
            finally
            {
                downloadCancellation.Dispose();
                downloadCancellation = null;
                localOperation = false;
                if (!IsDisposed) { progress.Visible = cancel.Visible = false; RefreshInstallation(); }
            }
        }

        private async Task UninstallAsync()
        {
            if (localOperation || QingjianInputMethodManager.IsBusy) return;
            localOperation = true;
            operationStatus.Text = "请在青简卸载窗口完成卸载；学习词库与设置会保留。";
            RefreshInstallation();
            try
            {
                string message = await QingjianInputMethodManager.UninstallAsync();
                if (!IsDisposed) operationStatus.Text = message;
            }
            catch (OperationCanceledException) { if (!IsDisposed) operationStatus.Text = "已取消管理员授权，输入法保留。"; }
            catch (Exception error) { ShowError(error); }
            finally { localOperation = false; if (!IsDisposed) RefreshInstallation(); }
        }

        private void RunAction(Action action)
        {
            try { action(); }
            catch (Exception error) { ShowError(error); }
        }

        private void ShowError(Exception error)
        {
            DebugHelper.WriteException(error, "Input method operation failed.");
            if (!IsDisposed && !Disposing) operationStatus.Text = "操作失败：" + error.Message;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer?.Dispose();
                downloadCancellation?.Cancel();
                if (!IsDisposed)
                {
                    updateCancellation.Cancel();
                    updateCancellation.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
