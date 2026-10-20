using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        internal void SelectDailyNewsPage()
        {
            tttvMain.NavigateToTabPage(dailyNewsTab);
            EnsureSettingsPage(dailyNewsTab);
        }

        private TabPage dailyNewsTab;
        private CheckBox dailyNewsEnabled;
        private TextBox dailyNewsApiKey;
        private TextBox dailyNewsApiBase;
        private TextBox dailyNewsModel;
        private NumericUpDown dailyNewsMaxTotal;
        private NumericUpDown dailyNewsMaxPerSource;
        private NumericUpDown dailyNewsConcurrency;
        private NumericUpDown dailyNewsTimeout;
        private CheckBox dailyNewsAutoRun;
        private NumericUpDown dailyNewsAutoRunHour;
        private CheckBox dailyNewsProxyEnabled;
        private TextBox dailyNewsProxyUrl;
        private TextBox dailyNewsHotApi;
        private CheckBox dailyNewsDigest;
        private CheckBox dailyNewsTopicGroup;
        private Label dailyNewsStorageUsage;
        private Label dailyNewsCacheUsage;
        private int dailyNewsUsageVersion;
        private CancellationTokenSource dailyNewsUsageCancellation;

        private DailyNewsSettings DailyNewsSettings
        {
            get
            {
                Program.Settings.DailyNewsSettings ??= new DailyNewsSettings();
                return Program.Settings.DailyNewsSettings;
            }
        }

        private void InitializeDailyNewsSettingsTab()
        {
            dailyNewsTab = new TabPage(SettingsPageLayout.Caption("每日资讯", "Daily news")) { Name = "tpDailyNews", Padding = new Padding(12), AutoScroll = true };
            tcSettings.TabPages.Add(dailyNewsTab);
            Disposed += (sender, e) =>
            {
                var cancellation = dailyNewsUsageCancellation;
                dailyNewsUsageCancellation = null;
                cancellation?.Cancel();
                cancellation?.Dispose();
            };
            RegisterSettingsPage(dailyNewsTab, () =>
            {
                BuildDailyNewsSettingsControls();
                LoadDailyNewsSettingsControls();
            });
        }

        private void BuildDailyNewsSettingsControls()
        {

            TableLayoutPanel table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(6),
                BackColor = Color.Transparent
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.SuspendLayout();

            Label heading = new Label
            {
                Text = "每日资讯",
                Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(3, 4, 3, 12)
            };
            table.Controls.Add(heading, 0, table.RowCount);
            table.SetColumnSpan(heading, 2);
            table.RowCount++;

            dailyNewsEnabled = AddNewsCheckBox(table, "启用资讯功能", "启用国内资讯和国际资讯");
            dailyNewsApiKey = AddNewsTextBox(table, "DeepSeek API Key", true);
            Label keyHint = new Label
            {
                Text = "API Key 使用 Windows 当前用户加密保存，不写入日志。",
                AutoSize = true,
                ForeColor = Color.Gray,
                Margin = new Padding(3, 0, 3, 10)
            };
            table.Controls.Add(keyHint, 1, table.RowCount++);
            dailyNewsApiBase = AddNewsTextBox(table, "API 地址", false);
            dailyNewsModel = AddNewsTextBox(table, "模型", false);
            dailyNewsMaxTotal = AddNewsNumber(table, "每日最多资讯数", 1, 200);
            Label tokenHint = new Label
            {
                Text = "提示：每次手动更新都会重新调用 DeepSeek，并增加 Token 消耗。",
                AutoSize = true,
                ForeColor = Color.FromArgb(210, 165, 70),
                Margin = new Padding(3, 0, 3, 10)
            };
            table.Controls.Add(tokenHint, 1, table.RowCount++);
            dailyNewsMaxPerSource = AddNewsNumber(table, "每个来源最多条数", 1, 50);
            dailyNewsConcurrency = AddNewsNumber(table, "并发处理数", 1, 10);
            dailyNewsTimeout = AddNewsNumber(table, "网络超时（秒）", 5, 120);
            dailyNewsAutoRun = AddNewsCheckBox(table, "自动更新", "每天定时更新；错过时间会在下次启动时补更");
            dailyNewsAutoRunHour = AddNewsNumber(table, "自动更新时间（时）", 0, 23);
            dailyNewsProxyEnabled = AddNewsCheckBox(table, "国际资讯使用代理", "仅国际资讯源使用代理");
            dailyNewsProxyUrl = AddNewsTextBox(table, "代理地址", false);
            dailyNewsHotApi = AddNewsTextBox(table, "国内热榜接口", false);
            dailyNewsDigest = AddNewsCheckBox(table, "生成今日导读", "使用 AI 生成简短导读");
            dailyNewsTopicGroup = AddNewsCheckBox(table, "合并同一事件", "相同事件在页面中相邻显示");

            Label output = new Label
            {
                Text = Path.Combine(Program.PersonalFolder, "daily_news"),
                AutoSize = true,
                MaximumSize = new Size(600, 0),
                ForeColor = Color.Gray
            };
            AddNewsRow(table, "保存位置", output);

            dailyNewsStorageUsage = new Label { AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(3, 6, 3, 6) };
            dailyNewsCacheUsage = new Label { AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(3, 6, 3, 6) };
            AddNewsRow(table, "资讯文件占用", dailyNewsStorageUsage);
            AddNewsRow(table, "网页缓存占用", dailyNewsCacheUsage);

            FlowLayoutPanel maintenance = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0) };
            Button refreshUsage = new Button { Text = "刷新占用", AutoSize = true };
            Button clearNews = new Button { Text = "清理历史资讯", AutoSize = true };
            Button clearCache = new Button { Text = "清理网页缓存", AutoSize = true };
            maintenance.Controls.AddRange(new Control[] { refreshUsage, clearNews, clearCache });
            AddNewsRow(table, "存储管理", maintenance);
            refreshUsage.Click += (sender, e) => RefreshDailyNewsStorageUsage();
            clearNews.Click += (sender, e) =>
            {
                if (MessageBox.Show("确定清理全部已保存的国内和国际资讯文件吗？", "清理历史资讯",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try
                {
                    int count = DailyNewsStorageMaintenance.ClearSavedNews(Program.PersonalFolder);
                    MessageBox.Show($"已清理 {count} 个资讯文件。", "清理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshDailyNewsStorageUsage();
                }
                catch (Exception error)
                {
                    MessageBox.Show("清理失败：" + error.Message, "清理历史资讯", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            clearCache.Click += (sender, e) =>
            {
                if (MessageBox.Show("网页缓存将在下次启动小蟒同学时清理。继续吗？", "清理网页缓存",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                DailyNewsStorageMaintenance.ScheduleCacheCleanup(Program.PersonalFolder);
                MessageBox.Show("已安排在下次启动时清理资讯网页缓存。", "清理网页缓存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            dailyNewsEnabled.CheckedChanged += DailyNewsSettingChanged;
            dailyNewsApiKey.TextChanged += DailyNewsSettingChanged;
            dailyNewsApiBase.Leave += CommitDailyNewsEndpoint;
            dailyNewsModel.TextChanged += DailyNewsSettingChanged;
            dailyNewsMaxTotal.ValueChanged += DailyNewsSettingChanged;
            dailyNewsMaxPerSource.ValueChanged += DailyNewsSettingChanged;
            dailyNewsConcurrency.ValueChanged += DailyNewsSettingChanged;
            dailyNewsTimeout.ValueChanged += DailyNewsSettingChanged;
            dailyNewsAutoRun.CheckedChanged += DailyNewsSettingChanged;
            dailyNewsAutoRunHour.ValueChanged += DailyNewsSettingChanged;
            dailyNewsProxyEnabled.CheckedChanged += DailyNewsSettingChanged;
            dailyNewsProxyUrl.TextChanged += DailyNewsSettingChanged;
            dailyNewsHotApi.TextChanged += DailyNewsSettingChanged;
            dailyNewsDigest.CheckedChanged += DailyNewsSettingChanged;
            dailyNewsTopicGroup.CheckedChanged += DailyNewsSettingChanged;
            dailyNewsApiKey.Leave += (sender, e) => SettingManager.SaveApplicationConfigAsync();
            table.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f));
            dailyNewsTab.Controls.Add(table);
            bool arranging = false;
            int arrangedWidth = -1;
            int arrangedDpi = -1;
            void ArrangeNewsPage()
            {
                if (arranging || table.IsDisposed || designedSettingsPages.Contains(dailyNewsTab)) return;
                int width = Math.Max(1, dailyNewsTab.ClientSize.Width - dailyNewsTab.Padding.Horizontal);
                if (width == arrangedWidth && dailyNewsTab.DeviceDpi == arrangedDpi) return;
                arranging = true;
                table.SuspendLayout();
                try
                {
                    table.MaximumSize = new Size(width, 0);
                    int labelWidth = 0;
                    foreach (Control control in table.Controls)
                        if (table.GetColumn(control) == 0 && table.GetColumnSpan(control) == 1 && control is Label)
                            labelWidth = Math.Max(labelWidth, TextRenderer.MeasureText(control.Text, control.Font).Width + control.Margin.Horizontal);
                    labelWidth = Math.Max(1, (int)Math.Round(labelWidth * 0.8));
                    table.ColumnStyles[0].Width = labelWidth;
                    foreach (Control control in table.Controls)
                        if (table.GetColumn(control) == 0 && table.GetColumnSpan(control) == 1 && control is Label label)
                        {
                            label.MaximumSize = new Size(Math.Max(1, labelWidth - label.Margin.Horizontal), 0);
                            label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                        }
                    int fieldWidth = Math.Max(1, width - table.Padding.Horizontal - labelWidth);
                    foreach (Control control in table.Controls)
                    {
                        if (table.GetColumn(control) != 1) continue;
                        int available = Math.Max(1, fieldWidth - control.Margin.Horizontal);
                        if (control is Label || control is CheckBox || control is FlowLayoutPanel)
                        {
                            control.MaximumSize = new Size(available, 0);
                            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                        }
                        if (control is CheckBox check)
                        {
                            // Native checkbox AutoSize keeps a one-line preferred height even
                            // when the constrained width needs more than one line.
                            check.AutoSize = false;
                            int markSpace = (int)Math.Round(24 * dailyNewsTab.DeviceDpi / 96f);
                            int textHeight = TextRenderer.MeasureText(check.Text, check.Font,
                                new Size(Math.Max(1, available - markSpace), int.MaxValue),
                                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Height;
                            check.Size = new Size(available, Math.Max(check.Font.Height + markSpace / 4, textHeight + markSpace / 4));
                        }
                    }
                    arrangedWidth = width;
                    arrangedDpi = dailyNewsTab.DeviceDpi;
                }
                finally { table.ResumeLayout(true); arranging = false; }
            }
            dailyNewsTab.SizeChanged += (sender, e) => ArrangeNewsPage();
            table.Layout += (sender, e) => ArrangeNewsPage();
            table.FontChanged += (sender, e) => { arrangedWidth = -1; ArrangeNewsPage(); table.PerformLayout(); };
            ArrangeNewsPage();
            table.ResumeLayout(true);
        }

        private void LoadDailyNewsSettingsControls()
        {
            DailyNewsSettings settings = DailyNewsSettings;
            settings.Normalize();
            dailyNewsEnabled.Checked = settings.Enabled;
            dailyNewsApiKey.Text = settings.GetApiKey();
            dailyNewsApiBase.Text = settings.ApiBase;
            dailyNewsModel.Text = settings.Model;
            dailyNewsMaxTotal.Value = settings.MaxTotal;
            dailyNewsMaxPerSource.Value = settings.MaxPerSource;
            dailyNewsConcurrency.Value = settings.Concurrency;
            dailyNewsTimeout.Value = settings.TimeoutSeconds;
            dailyNewsAutoRun.Checked = settings.AutoRun;
            dailyNewsAutoRunHour.Value = settings.AutoRunHour;
            dailyNewsProxyEnabled.Checked = settings.ProxyEnabled;
            dailyNewsProxyUrl.Text = settings.ProxyUrl;
            dailyNewsHotApi.Text = settings.DailyHotApi;
            dailyNewsDigest.Checked = settings.EnableDigest;
            dailyNewsTopicGroup.Checked = settings.EnableTopicGroup;
            RefreshDailyNewsStorageUsage();
        }

        private async void RefreshDailyNewsStorageUsage()
        {
            if (dailyNewsStorageUsage == null || dailyNewsCacheUsage == null) return;
            int version = ++dailyNewsUsageVersion;
            dailyNewsUsageCancellation?.Cancel();
            dailyNewsUsageCancellation?.Dispose();
            dailyNewsUsageCancellation = new CancellationTokenSource();
            CancellationToken token = dailyNewsUsageCancellation.Token;
            string personal = Program.PersonalFolder;
            dailyNewsStorageUsage.Text = dailyNewsCacheUsage.Text = "统计中…";
            try
            {
                var usage = await Task.Run(() => (
                    News: DailyNewsStorageMaintenance.GetFolderSize(DailyNewsStorageMaintenance.NewsFolder(personal), token),
                    Cache: DailyNewsStorageMaintenance.GetFolderSize(DailyNewsStorageMaintenance.CacheFolder(personal), token)), token);
                if (IsDisposed || Disposing || version != dailyNewsUsageVersion || dailyNewsStorageUsage.IsDisposed) return;
                dailyNewsStorageUsage.Text = DailyNewsStorageMaintenance.FormatSize(usage.News);
                dailyNewsCacheUsage.Text = DailyNewsStorageMaintenance.FormatSize(usage.Cache);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ShareX.HelpersLib.DebugHelper.WriteException(ex, "Failed to read news storage usage.");
                if (!IsDisposed && !Disposing && version == dailyNewsUsageVersion && !dailyNewsStorageUsage.IsDisposed)
                    dailyNewsStorageUsage.Text = dailyNewsCacheUsage.Text = "统计失败，请刷新重试";
            }
        }

        private void DailyNewsSettingChanged(object sender, EventArgs e)
        {
            if (!ready) return;
            DailyNewsSettings settings = DailyNewsSettings;
            settings.Enabled = dailyNewsEnabled.Checked;
            settings.SetApiKey(dailyNewsApiKey.Text);
            settings.Model = dailyNewsModel.Text;
            settings.MaxTotal = (int)dailyNewsMaxTotal.Value;
            settings.MaxPerSource = (int)dailyNewsMaxPerSource.Value;
            settings.Concurrency = (int)dailyNewsConcurrency.Value;
            settings.TimeoutSeconds = (int)dailyNewsTimeout.Value;
            settings.AutoRun = dailyNewsAutoRun.Checked;
            settings.AutoRunHour = (int)dailyNewsAutoRunHour.Value;
            settings.ProxyEnabled = dailyNewsProxyEnabled.Checked;
            settings.ProxyUrl = dailyNewsProxyUrl.Text;
            settings.DailyHotApi = dailyNewsHotApi.Text;
            settings.EnableDigest = dailyNewsDigest.Checked;
            settings.EnableTopicGroup = dailyNewsTopicGroup.Checked;
            settings.Normalize();
        }

        private void CommitDailyNewsEndpoint(object sender, EventArgs e)
        {
            if (!ready || dailyNewsApiBase.Text.Trim().TrimEnd('/') == DailyNewsSettings.ApiBase) return;
            string value = dailyNewsApiBase.Text.Trim().TrimEnd('/');
            try
            {
                string recipient = DailyNewsEndpoint.Recipient(value);
                string previous = null;
                try { previous = DailyNewsEndpoint.Recipient(DailyNewsSettings.ApiBase); } catch (ArgumentException) { }
                if (!string.Equals(previous, recipient, StringComparison.OrdinalIgnoreCase) &&
                    MessageBox.Show(this, "资讯请求将把 API 密钥发送给：\n" + recipient + "\n确认使用此服务地址？", "确认资讯 API 接收方", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    dailyNewsApiBase.Text = DailyNewsSettings.ApiBase;
                    return;
                }
                DailyNewsSettings.ApiBase = value;
            }
            catch (ArgumentException error)
            {
                MessageBox.Show(this, error.Message, "资讯 API 地址", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dailyNewsApiBase.Text = DailyNewsSettings.ApiBase;
            }
        }

        private static TextBox AddNewsTextBox(TableLayoutPanel table, string label, bool password)
        {
            TextBox value = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = password, Margin = new Padding(3, 4, 3, 7) };
            AddNewsRow(table, label, value);
            return value;
        }

        private static NumericUpDown AddNewsNumber(TableLayoutPanel table, string label, int minimum, int maximum)
        {
            NumericUpDown value = new NumericUpDown
            {
                Minimum = minimum,
                Maximum = maximum,
                Width = 120,
                Margin = new Padding(3, 4, 3, 7)
            };
            AddNewsRow(table, label, value);
            return value;
        }

        private static CheckBox AddNewsCheckBox(TableLayoutPanel table, string label, string text)
        {
            CheckBox value = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 5, 3, 8) };
            AddNewsRow(table, label, value);
            return value;
        }

        private static void AddNewsRow(TableLayoutPanel table, string label, Control value)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 7, 3, 3)
            }, 0, row);
            table.Controls.Add(value, 1, row);
        }
    }
}
