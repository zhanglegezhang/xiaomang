using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ShareX.HelpersLib;
using ShareX.Properties;
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class DailyNewsWindow : Form
    {
        private const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
        private readonly DailyNewsRegion region;
        private readonly DailyNewsManager manager;
        private readonly Button refreshButton;
        private readonly Label statusLabel;
        private readonly ProgressBar progressBar;
        private readonly Panel contentPanel;
        private WebView2 browser;
        private CancellationTokenSource updateCancellation;
        private bool browserReady;
        private string trustedHtmlDataUri;

        private DailyNewsSettings Settings
        {
            get
            {
                Program.Settings.DailyNewsSettings ??= new DailyNewsSettings();
                Program.Settings.DailyNewsSettings.Normalize();
                return Program.Settings.DailyNewsSettings;
            }
        }

        public DailyNewsWindow(DailyNewsRegion region, DailyNewsManager manager)
        {
            this.region = region;
            this.manager = manager;
            Text = "小蟒同学 - " + RegionName + "资讯";
            Icon = ShareXResources.Icon;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(800, 600);
            Settings.Normalize();
            Size = FitToCurrentScreen(region == DailyNewsRegion.Domestic ? Settings.DomesticWindowSize : Settings.InternationalWindowSize);
            BackColor = Color.FromArgb(38, 38, 38);
            Font = new Font("Microsoft YaHei UI", 8f);

            TableLayoutPanel toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 54,
                BackColor = Color.FromArgb(48, 48, 48),
                Padding = new Padding(8, 7, 8, 7),
                ColumnCount = 3,
                RowCount = 1
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            refreshButton = new Button
            {
                Text = "立即更新",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8f),
                BackColor = Color.FromArgb(64, 64, 64),
                ForeColor = Color.WhiteSmoke
            };
            refreshButton.FlatAppearance.BorderColor = Color.FromArgb(105, 105, 105);
            refreshButton.Click += async (sender, e) =>
            {
                if (updateCancellation != null)
                {
                    refreshButton.Enabled = false;
                    statusLabel.Text = "正在取消更新…";
                    updateCancellation.Cancel();
                    return;
                }
                await UpdateNewsAsync();
            };
            toolbar.Controls.Add(refreshButton, 0, 0);

            progressBar = new ProgressBar { Dock = DockStyle.Fill, Margin = new Padding(6, 5, 0, 5), Visible = false, Style = ProgressBarStyle.Continuous };
            toolbar.Controls.Add(progressBar, 2, 0);
            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
                Padding = new Padding(12, 0, 8, 0),
                ForeColor = Color.Gainsboro,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            toolbar.Controls.Add(statusLabel, 1, 0);

            contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 32, 32) };
            Controls.Add(contentPanel);
            Controls.Add(toolbar);
            ResizeEnd += DailyNewsWindow_ResizeEnd;
            FormClosed += (sender, e) => updateCancellation?.Cancel();
            Shown += async (sender, e) =>
            {
                EnsureVisibleOnCurrentScreen();
                await InitializeAsync();
            };
        }

        private string RegionName => region == DailyNewsRegion.Domestic ? "国内" : "国际";

        private async Task InitializeAsync()
        {
            try
            {
                if (browserReady || IsDisposed) return;
                string latest = manager.RefreshLatestHtmlFromJson(region);
                if (string.IsNullOrWhiteSpace(latest) || !File.Exists(latest))
                {
                    ShowEmptyState();
                    return;
                }
                ClearContent();
                browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(32, 32, 32) };
                contentPanel.Controls.Add(browser);
                string userDataFolder = Path.Combine(Program.PersonalFolder, "DailyNewsWebView");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                if (IsDisposed || Disposing) return;
                await browser.EnsureCoreWebView2Async(environment);
                if (IsDisposed || Disposing) return;
                browser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;
                browser.CoreWebView2.NavigationStarting += Browser_NavigationStarting;
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                browserReady = true;
                ShowLatestOrMessage(latest);
            }
            catch (Exception error) when (IsWebViewRuntimeError(error))
            {
                if (IsDisposed || Disposing) return;
                browser?.Dispose();
                browser = null;
                ShowWebView2MissingPanel();
            }
            catch (Exception error)
            {
                if (IsDisposed || Disposing) return;
                ShowFallbackMessage("资讯页面加载失败", GetSafeMessage(error));
            }
        }

        private void ShowLatestOrMessage(string latest)
        {
            if (!string.IsNullOrWhiteSpace(latest) && File.Exists(latest))
            {
                NavigateToFile(latest);
                DateTime? lastSuccess = manager.GetLastSuccess(region);
                statusLabel.Text = lastSuccess.HasValue ? "上次成功更新：" + lastSuccess.Value.ToString("yyyy-MM-dd HH:mm") : "已显示最近一次资讯";
                return;
            }

            ShowEmptyState();
        }

        private async Task UpdateNewsAsync()
        {
            if (updateCancellation != null) return;
            if (!manager.HasApiKey)
            {
                if (string.IsNullOrWhiteSpace(manager.GetLatestHtmlPath(region))) ShowEmptyState();
                else statusLabel.Text = "未配置 API Key，已保留历史资讯；请到“全局设置 → 每日资讯”配置后更新。";
                return;
            }

            updateCancellation = new CancellationTokenSource();
            refreshButton.Text = "取消更新";
            refreshButton.Enabled = true;
            progressBar.Visible = true;
            progressBar.Value = 0;
            statusLabel.ForeColor = Color.Gainsboro;
            if (manager.IsUpdateInProgress(region)) statusLabel.Text = "正在等待当前更新完成…";
            Progress<DailyNewsProgress> progress = new Progress<DailyNewsProgress>(value =>
            {
                progressBar.Value = Math.Clamp(value.Percent, 0, 100);
                statusLabel.Text = value.Message;
            });

            try
            {
                await manager.UpdateAsync(region, progress, updateCancellation.Token);
                if (!browserReady) await InitializeAsync();
                NavigateToFile(manager.GetTodayHtmlPath(region));
                statusLabel.Text = "更新完成：" + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (OperationCanceledException)
            {
                statusLabel.Text = "更新已取消";
            }
            catch (Exception error)
            {
                // Keep the last successfully generated page visible.
                statusLabel.ForeColor = Color.FromArgb(255, 170, 120);
                bool hasHistory = !string.IsNullOrWhiteSpace(manager.GetLatestHtmlPath(region));
                statusLabel.Text = (hasHistory ? "更新失败，已保留上次内容：" : "更新失败，暂无历史内容：") + GetSafeMessage(error);
                if (!hasHistory)
                {
                    ShowHtmlMessage("更新失败", GetSafeMessage(error));
                }
                DebugHelper.WriteException(error, "Daily news update failed");
            }
            finally
            {
                updateCancellation.Dispose();
                updateCancellation = null;
                refreshButton.Text = "立即更新";
                refreshButton.Enabled = true;
                progressBar.Visible = false;
            }
        }

        private void NavigateToFile(string path)
        {
            if (browserReady && File.Exists(path)) NavigateHtml(File.ReadAllText(path));
        }

        private void ShowHtmlMessage(string title, string message)
        {
            if (browserReady) NavigateHtml(DailyNewsHtmlRenderer.RenderMessage(title, message));
            else ShowFallbackMessage(title, message);
        }

        private void NavigateHtml(string html)
        {
            // Some WebView runtimes expose NavigateToString as a data navigation.
            // Permit only the exact HTML requested by this window, never arbitrary data links.
            trustedHtmlDataUri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
            browser.CoreWebView2.NavigateToString(html);
        }

        private void ShowFallbackMessage(string title, string message)
        {
            browserReady = false;
            trustedHtmlDataUri = null;
            ClearContent();
            contentPanel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = title + Environment.NewLine + Environment.NewLine + message,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(32, 32, 32),
                Font = new Font(Font.FontFamily, 12f)
            });
        }

        private void ClearContent()
        {
            while (contentPanel.Controls.Count > 0)
            {
                Control control = contentPanel.Controls[0];
                contentPanel.Controls.RemoveAt(0);
                control.Dispose();
            }
            browser = null;
            browserReady = false;
        }

        private void ShowEmptyState()
        {
            bool missingKey = !manager.HasApiKey;
            ShowFallbackMessage(missingKey ? "尚未配置资讯服务" : "还没有资讯",
                missingKey ? "请在“全局设置 → 每日资讯”配置 API Key，再点击“立即更新”。" :
                "点击上方“立即更新”生成今天的" + RegionName + "资讯；更新可能使用 API 额度。");
            var settings = new Button
            {
                Name = "openDailyNewsSettings", Text = "前往资讯设置", Dock = DockStyle.Bottom,
                Height = 40, FlatStyle = FlatStyle.Flat, ForeColor = Color.WhiteSmoke,
                BackColor = Color.FromArgb(48, 48, 48)
            };
            settings.Click += (_, _) => Program.MainForm?.OpenDailyNewsSettings();
            contentPanel.Controls.Add(settings);
            settings.BringToFront();
            statusLabel.Text = missingKey ? "请先配置资讯 API Key" : "等待首次更新";
        }

        private void ShowWebView2MissingPanel()
        {
            ClearContent();
            Button installButton = new Button
            {
                Text = "下载 WebView2 运行环境",
                AutoSize = true,
                Anchor = AnchorStyles.None,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.WhiteSmoke,
                BackColor = Color.FromArgb(65, 65, 65)
            };
            Label label = new Label
            {
                Text = "当前电脑缺少 Microsoft Edge WebView2 运行环境，安装后即可显示资讯页面。",
                AutoSize = true,
                ForeColor = Color.Gainsboro
            };
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(40),
                BackColor = Color.FromArgb(32, 32, 32)
            };
            installButton.Click += (sender, e) => URLHelpers.OpenURL(WebView2DownloadUrl);
            panel.Controls.Add(label);
            panel.Controls.Add(installButton);
            contentPanel.Controls.Add(panel);
            statusLabel.Text = "需要安装 WebView2 运行环境";
        }

        private void Browser_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                e.Handled = true;
                URLHelpers.OpenURL(uri.AbsoluteUri);
            }
        }

        private void Browser_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri == "about:blank") return;
            if (trustedHtmlDataUri != null && string.Equals(e.Uri, trustedHtmlDataUri, StringComparison.Ordinal))
            {
                trustedHtmlDataUri = null;
                return;
            }
            e.Cancel = true;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri)) return;
            if (DailyNewsFetcher.IsWebUrl(e.Uri)) { URLHelpers.OpenURL(uri.AbsoluteUri); return; }
            if (!uri.Scheme.Equals("xiaomangtongxue-news", StringComparison.OrdinalIgnoreCase)) return;

            e.Cancel = true;
            DailyNewsRegion target = uri.Host.Equals("international", StringComparison.OrdinalIgnoreCase)
                ? DailyNewsRegion.International : DailyNewsRegion.Domestic;
            if (target == region) return;
            BeginInvoke(() =>
            {
                Program.MainForm?.OpenDailyNews(target);
                Close();
            });
        }

        private void DailyNewsWindow_ResizeEnd(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Normal) return;
            if (region == DailyNewsRegion.Domestic) Settings.DomesticWindowSize = Size;
            else Settings.InternationalWindowSize = Size;
            SettingManager.SaveApplicationConfigAsync();
        }

        private static Size FitToCurrentScreen(Size requested)
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int width = Math.Clamp(requested.Width, 800, Math.Max(800, area.Width));
            int height = Math.Clamp(requested.Height, 600, Math.Max(600, area.Height));
            return new Size(width, height);
        }

        private void EnsureVisibleOnCurrentScreen()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Size fitted = new Size(
                Math.Clamp(Width, MinimumSize.Width, Math.Max(MinimumSize.Width, area.Width)),
                Math.Clamp(Height, MinimumSize.Height, Math.Max(MinimumSize.Height, area.Height)));
            Size = fitted;
            Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        }

        private static bool IsWebViewRuntimeError(Exception error)
        {
            return error is WebView2RuntimeNotFoundException ||
                error.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase) ||
                error.InnerException is WebView2RuntimeNotFoundException;
        }

        private static string GetSafeMessage(Exception error)
        {
            string message = error?.Message?.Trim();
            return string.IsNullOrWhiteSpace(message) ? "未知错误" : message.Length > 180 ? message.Substring(0, 180) : message;
        }
    }
}
