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
using ShareX.ScreenCaptureLib;
using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    public partial class OCRForm : Form
    {
        public OCROptions Options { get; set; }
        public string Result { get; private set; }

        private Bitmap bmpSource;
        private bool loaded;
        private bool busy;
        private readonly Point openingPoint = Cursor.Position;

        public OCRForm(Bitmap bmp, OCROptions options)
        {
            bmpSource = (Bitmap)bmp.Clone();
            Options = options;

            InitializeComponent();
            InitializeTranslationPanel();
            ShareXResources.ApplyTheme(this, true);
            ApplyOcrStyle();

            Options.ScaleFactor = 2f;
            Options.SingleLine = false;
            nudScaleFactor.SetValue((decimal)Options.ScaleFactor);
            cbSingleLine.Checked = Options.SingleLine;

            // Keep saved custom services intact; this simplified window always uses Baidu.
            cbServices.Items.Add(new ServiceLink("百度翻译", "https://fanyi.baidu.com/mtpe-individual/transText#/auto/zh/{0}"));
            cbServices.SelectedIndex = 0;

            txtResult.SupportSelectAll();
            UpdateControls();

            busy = true;
            translationStatus.Text = "正在准备识别…";
            UpdateControls();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelApiTranslation();
                ocrCancellation.Cancel();
                components?.Dispose();
                bmpSource?.Dispose();
                foreach (Control control in new Control[] { btnSelectRegion, lblScaleFactor, nudScaleFactor,
                    cbSingleLine, cbServices, cbEditServices, cbLanguages, btnOpenOCRHelp })
                    if (control.Parent == null) control.Dispose();
            }

            base.Dispose(disposing);
        }

        private void UpdateControls()
        {
            if (busy)
            {
                Cursor = Cursors.WaitCursor;
            }
            else
            {
                Cursor = Cursors.Default;
            }

            btnSelectRegion.Enabled = !busy;
            cbLanguages.Enabled = !busy && cbLanguages.Items.Count > 0;
            UpdateLanguageButtons();
            nudScaleFactor.Enabled = !busy;
            cbSingleLine.Enabled = !busy;
        }

        private async Task OCR(Bitmap bmp)
        {
            if (bmp != null && (mixedSelected || !string.IsNullOrEmpty(Options.Language)))
            {
                busy = true;
                Result = null;
                txtResult.Text = "";
                translationStatus.Text = "正在识别…";
                var timer = System.Diagnostics.Stopwatch.StartNew();
                UpdateControls();

                try
                {
                    using var snapshot = (Bitmap)bmp.Clone();
                    if(mixedSelected)
                    {
                        var recognized=await MixedOcrClient.RecognizeAsync(snapshot,Options.Language,ocrCancellation.Token);
                        Result=recognized.Text;
                        if(!IsDisposed)lblResult.Text=recognized.Notice;
                    }
                    else
                    {
                        Result = await OCRHelper.OCR(snapshot, Options.Language, Options.ScaleFactor, Options.SingleLine,false);
                        if(!IsDisposed)lblResult.Text="Windows OCR · "+(Options.Language.StartsWith("zh")?"中文":"英语");
                    }
                    DebugHelper.WriteLine($"[OCR] recognition-completed ms={timer.ElapsedMilliseconds}; chars={Result?.Length ?? 0}");
                    if (IsDisposed || Disposing) return;

                }
                catch (Exception e)
                {
                    DebugHelper.WriteException(e);
                    if (!IsDisposed && !Disposing) translationStatus.Text = "识别失败，请重新选择区域或切换语言重试。";
                }

                if (!IsDisposed)
                {
                    busy = false;
                    txtResult.Text = Result;
                    if (Result != null && string.IsNullOrWhiteSpace(Result)) translationStatus.Text = "未识别到文字，请重新选择区域或切换语言。";
                    txtResult.Focus();
                    txtResult.DeselectAll();
                    UpdateControls();
                    if (!string.IsNullOrWhiteSpace(Result) && IsBaiduTranslationSelected())
                    {
                        await ShowTranslationAsync();
                    }
                }
            }
        }

        private async void OCRForm_Shown(object sender, EventArgs e)
        {
            // Switching the existing dialog to a modeless window must retain its result.
            if (loaded) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            DebugHelper.WriteLine($"[OCR] window-shown bounds={Bounds}; visible={Visible}; state={WindowState}; foreground={NativeMethods.GetForegroundWindow()}; hwnd={Handle}");
            Rectangle saved = Program.Settings.OcrWindowBounds;
            Size fallbackSize = Program.Settings.OcrDefaultWindowSize;
            bool remembered = saved.Width > 0 && saved.Height > 0 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(saved));
            Rectangle area = remembered ? Screen.FromRectangle(saved).WorkingArea : Screen.FromPoint(openingPoint).WorkingArea;
            StartPosition = FormStartPosition.Manual;
            WindowState = FormWindowState.Normal;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new Size(Math.Min(saved.Width > 0 ? saved.Width : fallbackSize.Width > 0 ? fallbackSize.Width : Width, area.Width), Math.Min(saved.Height > 0 ? saved.Height : fallbackSize.Height > 0 ? fallbackSize.Height : Height, area.Height));
            Location = remembered
                ? new Point(Math.Clamp(saved.X, area.Left, area.Right - Width), Math.Clamp(saved.Y, area.Top, area.Bottom - Height))
                : new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || Disposing) return;
                this.ForceActivate();
                bool activated = NativeMethods.SetForegroundWindow(Handle);
                DebugHelper.WriteLine($"[OCR] window-activated bounds={Bounds}; visible={Visible}; state={WindowState}; foreground={NativeMethods.GetForegroundWindow()}; hwnd={Handle}; accepted={activated}; topmost={TopMost}");
            }));
            try
            {
                await Task.Yield();
                var available = await Task.Run(() => OCRHelper.AvailableLanguages);
                if (IsDisposed || Disposing) return;
                OCRLanguage[] languages = available
                    .Where(x => x.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                        x.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(x => x.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "中文" : "英语")
                    .OrderBy(x => x.Key == "中文" ? 0 : 1)
                    .Select(g => new OCRLanguage(g.Key, g.OrderBy(x =>
                        x.LanguageTag.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
                        x.LanguageTag.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? 0 : 1).First().LanguageTag))
                    .ToArray();

                if (languages.Length > 0)
                {
                    cbLanguages.Items.AddRange(languages);

                    cbLanguages.SelectedIndex = 0;
                    Options.Language = languages[0].LanguageTag;
                }
                else
                {
                    Options.Language = null;
                    cbLanguages.Enabled = false;
                    translationStatus.Text = "未安装中文或英语识别语言，请在 Windows 语言设置中安装对应文字识别组件。";
                }


                loaded = true;
                busy = false;
                UpdateControls();
                DebugHelper.WriteLine($"[OCR] languages-ready ms={timer.ElapsedMilliseconds}; count={languages.Length}");
                await OCR(bmpSource);
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex);
                if (!IsDisposed && !Disposing)
                {
                    busy = false;
                    UpdateControls();
                    translationStatus.Text = "识别初始化失败，请关闭窗口重试；详情已记录到日志。";
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || !Visible || Program.Settings == null) return;
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0 || bounds == Program.Settings.OcrWindowBounds) return;
            Program.Settings.OcrWindowBounds = bounds;
            SettingManager.SaveApplicationConfigAsync();
        }

        private async void btnSelectRegion_Click(object sender, EventArgs e)
        {
            FormWindowState previousState = WindowState;
            WindowState = FormWindowState.Minimized;
            await Task.Delay(250);
            Bitmap regionImage = RegionCaptureTasks.GetRegionImage();
            WindowState = previousState;

            if (regionImage != null)
            {
                bmpSource?.Dispose();
                bmpSource = regionImage;
                await Task.Delay(250);
                await OCR(bmpSource);
            }
        }

        private async void cbLanguages_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loaded)
            {
                Options.Language = ((OCRLanguage)cbLanguages.SelectedItem).LanguageTag;

                await OCR(bmpSource);
            }
        }

        private void btnOpenOCRHelp_Click(object sender, EventArgs e)
        {
            URLHelpers.OpenURL(Links.DocsOCR);
        }

        private async void nudScaleFactor_ValueChanged(object sender, EventArgs e)
        {
            if (loaded)
            {
                Options.ScaleFactor = (float)nudScaleFactor.Value;

                await OCR(bmpSource);
            }
        }

        private async void cbSingleLine_CheckedChanged(object sender, EventArgs e)
        {
            if (loaded)
            {
                Options.SingleLine = cbSingleLine.Checked;

                await OCR(bmpSource);
            }
        }

        private void cbServices_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loaded) Options.SelectedServiceLink = cbServices.SelectedIndex;
            InvalidateTranslation();
        }

        private async void btnOpenServiceLink_Click(object sender, EventArgs e)
        {
            await ShowTranslationAsync();
        }

        private void cbEditServices_Click(object sender, EventArgs e)
        {
            using (ServiceLinksForm form = new ServiceLinksForm(Options.ServiceLinks))
            {
                form.ShowDialog();

                cbServices.Items.Clear();

                if (Options.ServiceLinks.Count > 0)
                {
                    cbServices.Items.AddRange(Options.ServiceLinks.ToArray());
                    cbServices.SelectedIndex = 0;
                    Options.SelectedServiceLink = 0;
                }

                cbServices.Enabled = cbServices.Items.Count > 0;
            }
        }

        private void btnCopyAll_Click(object sender, EventArgs e)
        {
            string message = string.IsNullOrWhiteSpace(txtResult.Text) ? "暂无可复制的原文"
                : ClipboardHelpers.CopyText(txtResult.Text) ? "已复制原文" : "复制失败，请重试";
            translationStatus.Text = message;
            if (apiMode && apiFeedback != null) apiFeedback.Text = message;
        }

        private void txtResult_TextChanged(object sender, EventArgs e)
        {
            Result = txtResult.Text.Trim();
            btnOpenServiceLink.Enabled = btnCopyAll.Enabled = !string.IsNullOrEmpty(Result);
            InvalidateTranslation();
        }
    }
}
