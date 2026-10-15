using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    public partial class OCRForm
    {
        private Panel translationHost;
        private Label translationStatus;
        private WebView2 translationBrowser;
        private Task initializeTranslationBrowser;
        private int translationRevision;
        private int navigatingRevision;

        private void InitializeTranslationPanel()
        {
            SuspendLayout();
            int margin = Math.Max(12, ClientSize.Width - txtResult.Right);
            int rightLeft = txtResult.Left;
            int headerHeight = btnCopyAll.Height + margin / 2;
            Size originalSize = ClientSize;
            ClientSize = new Size((int)(originalSize.Width * 1.3), (int)(originalSize.Height * 1.55));
            MinimumSize = Size;

            var layout = new TableLayoutPanel
            {
                Location = new Point(rightLeft, lblResult.Top),
                Size = new Size(ClientSize.Width - rightLeft - margin, ClientSize.Height - lblResult.Top - margin),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ColumnCount = 1,
                RowCount = 4,
                Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, headerHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, headerHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 76));

            var originalHeader = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            lblResult.Text = "识别原文";
            lblResult.AutoSize = true;
            lblResult.Location = new Point(0, margin / 2);
            btnCopyAll.Text = "复制原文";
            btnCopyAll.Dock = DockStyle.Right;
            originalHeader.Controls.Add(lblResult);
            originalHeader.Controls.Add(btnCopyAll);
            txtResult.Dock = DockStyle.Fill;
            txtResult.Margin = Padding.Empty;
            translationStatus = new Label
            {
                Text = "翻译结果 · 选择百度翻译后，识别完成会自动显示",
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            };
            translationHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BorderStyle = BorderStyle.FixedSingle };
            layout.Controls.Add(originalHeader, 0, 0);
            layout.Controls.Add(txtResult, 0, 1);
            layout.Controls.Add(translationStatus, 0, 2);
            layout.Controls.Add(translationHost, 0, 3);
            Controls.Add(layout);
            btnOpenServiceLink.Text = "翻译并显示";

            var openExternal = new LinkLabel
            {
                Text = "在浏览器中打开",
                AutoSize = true,
                Location = new Point(btnOpenServiceLink.Left, btnOpenServiceLink.Bottom + margin),
                TabStop = true
            };
            openExternal.LinkClicked += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(Result) && cbServices.SelectedItem is ServiceLink service)
                    service.OpenLink(Result);
            };
            Controls.Add(openExternal);
            ResumeLayout(true);
        }

        private bool IsBaiduTranslationSelected()
        {
            return cbServices.SelectedItem is ServiceLink service &&
                Uri.TryCreate(service.URL, UriKind.Absolute, out Uri uri) &&
                uri.Host.Equals("fanyi.baidu.com", StringComparison.OrdinalIgnoreCase);
        }

        private void InvalidateTranslation()
        {
            translationRevision++;
            if (translationStatus == null) return;
            translationStatus.Text = string.IsNullOrWhiteSpace(Result)
                ? "翻译结果 · 等待识别原文"
                : "翻译结果 · 点击“翻译并显示”更新译文";
            // Do not show a previous source's translation as the current result.
            if (translationBrowser != null) translationBrowser.Visible = false;
        }

        private async Task InitializeTranslationBrowserAsync()
        {
            translationBrowser = new WebView2 { Dock = DockStyle.Fill, Visible = false };
            translationHost.Controls.Add(translationBrowser);
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(Program.PersonalFolder, "TranslationWebView"));
            if (IsDisposed || Disposing) return;
            await translationBrowser.EnsureCoreWebView2Async(environment);
            if (IsDisposed || Disposing) return;
            translationBrowser.CoreWebView2.NavigationCompleted += (sender, e) =>
            {
                if (navigatingRevision != translationRevision || IsDisposed) return;
                translationStatus.Text = e.IsSuccess
                    ? "翻译结果 · 可在下方网页切换目标语言、复制译文"
                    : "翻译页面加载失败 · 请点击“翻译并显示”重试，或在浏览器中打开";
            };
            translationBrowser.CoreWebView2.NewWindowRequested += (sender, e) =>
            {
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                    URLHelpers.OpenURL(uri.AbsoluteUri);
            };
        }

        private async Task ShowTranslationAsync()
        {
            if (string.IsNullOrWhiteSpace(Result) || cbServices.SelectedItem is not ServiceLink service) return;
            string url = service.GenerateLink(Result);
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            {
                translationStatus.Text = "服务链接无效，请检查网址设置。";
                return;
            }
            int revision = ++translationRevision;
            translationStatus.Text = "正在加载翻译页面…";
            try
            {
                initializeTranslationBrowser ??= InitializeTranslationBrowserAsync();
                await initializeTranslationBrowser;
                if (IsDisposed || Disposing || revision != translationRevision) return;
                navigatingRevision = revision;
                translationBrowser.Visible = true;
                translationBrowser.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing || revision != translationRevision) return;
                translationStatus.Text = "无法打开内嵌翻译页面，可重试或在浏览器中打开。";
                DebugHelper.WriteException(ex);
                translationBrowser?.Dispose();
                translationBrowser = null;
                initializeTranslationBrowser = null;
            }
        }
    }
}
