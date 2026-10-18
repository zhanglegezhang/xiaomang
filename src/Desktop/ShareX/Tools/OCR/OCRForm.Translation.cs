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
