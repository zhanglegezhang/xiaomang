using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;
using ShareX.Properties;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private sealed class ToolsPageComponent : WidgetPageComponent
        {

            internal Panel newsPanel => this;
            internal readonly Label newsTitleLabel;
            internal readonly Button domesticNewsButton;
            internal readonly Button internationalNewsButton;
            internal readonly Button presentationCanvasButton;
            internal readonly Button everythingButton;
            internal readonly Button dataOrganizerButton;
            internal readonly Button scratchTextButton;
            internal readonly Button scratchMarkdownButton;
            internal readonly Button videoToGifButton;
            internal readonly Button calculatorButton;
            internal readonly Button sevenZipButton;
            internal readonly Button centralWeatherButton;
            internal readonly Button msnWeatherButton;
            internal readonly Button collageButton;
            internal readonly Button textCompareButton;
            internal readonly Button qingjianInputMethodButton;
            internal readonly Label newsStatusLabel;

            public void SetNewsUpdateStatus(string message, bool isError)
            {
                if (IsDisposed || !Owner.IsHandleCreated) return;
                if (Owner.InvokeRequired)
                {
                    Owner.BeginInvoke(new Action(() => SetNewsUpdateStatus(message, isError)));
                    return;
                }
                newsStatusLabel.Text = message ?? "";
                newsStatusLabel.ForeColor = isError ? Color.FromArgb(225, 125, 105) : Color.FromArgb(125, 190, 135);
                newsStatusLabel.Visible = !string.IsNullOrWhiteSpace(message);
                newsStatusLabel.Tag = isError;
                if (Owner.expanded && Owner.Settings.ActiveModule == "News") Owner.LayoutContent();
            }


            internal void SetOtherButtonIcon(Button button, OtherIcon kind)
            {
                Bitmap icon = Owner.CreateOtherButtonIcon(kind);
                otherButtonIcons.Add(icon);
                button.Image = icon;
                button.ImageAlign = ContentAlignment.MiddleLeft;
                button.TextAlign = ContentAlignment.MiddleCenter;
                button.TextImageRelation = TextImageRelation.Overlay;
            }
            internal readonly List<Bitmap> otherButtonIcons = new List<Bitmap>();

            internal readonly PictureBox newsTitleLogo;

            internal void LayoutNewsPanel()
            {
                int edge = Owner.D(8);
                int width = Math.Max(Owner.D(100), newsPanel.ClientSize.Width - edge * 2);
                Owner.LayoutPageTitle(newsTitleLogo, newsTitleLabel, width - Owner.D(26));
                int buttonGap = Owner.D(8);
                int rowGap = Owner.D(16);
                int buttonWidth = Math.Max(Owner.D(80), (width - buttonGap) / 2);
                int buttonHeight = Owner.D(31);
                int top = newsTitleLabel.Bottom + Owner.D(8);
                int right = edge + buttonWidth + buttonGap;
                int rightWidth = Math.Max(Owner.D(80), newsPanel.ClientSize.Width - edge - right);
                int RowTop(int row) => top + row * (buttonHeight + rowGap);
                Rectangle LeftCell(int row) => new Rectangle(edge, RowTop(row), buttonWidth, buttonHeight);
                Rectangle RightCell(int row) => new Rectangle(right, RowTop(row), rightWidth, buttonHeight);
                domesticNewsButton.Bounds = LeftCell(0);
                presentationCanvasButton.Bounds = RightCell(0);
                internationalNewsButton.Bounds = LeftCell(1);
                everythingButton.Bounds = RightCell(1);
                calculatorButton.Bounds = LeftCell(2);
                videoToGifButton.Bounds = RightCell(2);
                dataOrganizerButton.Bounds = LeftCell(3);
                scratchTextButton.Bounds = LeftCell(4);
                scratchMarkdownButton.Bounds = RightCell(4);
                textCompareButton.Bounds = LeftCell(5);
                qingjianInputMethodButton.Bounds = LeftCell(6);
                collageButton.Bounds = LeftCell(7);
                sevenZipButton.Bounds = RightCell(7);
                msnWeatherButton.Bounds = LeftCell(8);
                centralWeatherButton.Bounds = RightCell(8);
                newsStatusLabel.Bounds = new Rectangle(edge, msnWeatherButton.Bottom + Owner.D(8), width, Owner.D(28));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    foreach (Bitmap icon in otherButtonIcons) icon.Dispose();
                }
                base.Dispose(disposing);
            }

            internal ToolsPageComponent(TodoWidgetForm owner) : base(owner, "News")
            {
                newsTitleLabel = new Label
                {
                    Text = "常用工具",
                    Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
                    ForeColor = ShareXResources.Theme.TextColor,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Owner.ConfigureTitleDrag(newsTitleLabel);
                newsPanel.Controls.Add(newsTitleLabel);
                newsTitleLogo = Owner.CreateTitleLogo(newsPanel);
                domesticNewsButton = Owner.CreateOtherButton("国内资讯");
                domesticNewsButton.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
                domesticNewsButton.Click += (sender, e) => Program.MainForm?.OpenDailyNews(DailyNewsRegion.Domestic);
                newsPanel.Controls.Add(domesticNewsButton);
                internationalNewsButton = Owner.CreateOtherButton("国际资讯");
                internationalNewsButton.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
                internationalNewsButton.Click += (sender, e) => Program.MainForm?.OpenDailyNews(DailyNewsRegion.International);
                newsPanel.Controls.Add(internationalNewsButton);
                presentationCanvasButton = Owner.CreateOtherButton("画图");
                presentationCanvasButton.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
                presentationCanvasButton.Click += (sender, e) => Program.MainForm?.OpenPresentationCanvas();
                newsPanel.Controls.Add(presentationCanvasButton);
                everythingButton = Owner.CreateOtherButton("everything");
                everythingButton.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
                everythingButton.Click += (sender, e) => Program.MainForm?.OpenEverything();
                newsPanel.Controls.Add(everythingButton);
                dataOrganizerButton = Owner.CreateOtherButton("数据处理.html");
                dataOrganizerButton.Font = new Font(Font.FontFamily, 8f, FontStyle.Bold);
                dataOrganizerButton.Click += (sender, e) => Program.MainForm?.OpenDataOrganizer();
                newsPanel.Controls.Add(dataOrganizerButton);
                scratchTextButton = Owner.CreateOtherButton("空白.txt");
                scratchTextButton.Click += (sender, e) => Program.MainForm?.OpenScratchText();
                newsPanel.Controls.Add(scratchTextButton);
                scratchMarkdownButton = Owner.CreateOtherButton("空白.md");
                scratchMarkdownButton.Click += (sender, e) => Program.MainForm?.OpenScratchMarkdown();
                newsPanel.Controls.Add(scratchMarkdownButton);
                videoToGifButton = Owner.CreateOtherButton("视频转 GIF");
                videoToGifButton.Click += (sender, e) => Program.MainForm?.OpenVideoToGif();
                newsPanel.Controls.Add(videoToGifButton);
                calculatorButton = Owner.CreateOtherButton("计算器");
                calculatorButton.Click += (sender, e) => Program.MainForm?.OpenWindowsCalculator();
                newsPanel.Controls.Add(calculatorButton);
                sevenZipButton = Owner.CreateOtherButton("7-Zip");
                sevenZipButton.Click += (sender, e) => Program.MainForm?.OpenSevenZip();
                newsPanel.Controls.Add(sevenZipButton);
                centralWeatherButton = Owner.CreateOtherButton("中央气象台.html");
                centralWeatherButton.Click += (sender, e) => URLHelpers.OpenURL("https://www.nmc.cn/");
                newsPanel.Controls.Add(centralWeatherButton);
                msnWeatherButton = Owner.CreateOtherButton("MSN天气.html");
                msnWeatherButton.Click += (sender, e) => URLHelpers.OpenURL("https://www.msn.cn/zh-cn/weather/maps/precipitation");
                newsPanel.Controls.Add(msnWeatherButton);
                collageButton = Owner.CreateOtherButton("拼图");
                collageButton.Click += (sender, e) => Program.MainForm?.OpenImageCollage();
                newsPanel.Controls.Add(collageButton);
                textCompareButton = Owner.CreateOtherButton("文本对比");
                textCompareButton.Click += (sender, e) => Program.MainForm?.OpenTextCompare();
                newsPanel.Controls.Add(textCompareButton);
                qingjianInputMethodButton = Owner.CreateOtherButton("输入法安装");
                qingjianInputMethodButton.Click += (sender, e) => Program.MainForm?.OpenInputMethod();
                newsPanel.Controls.Add(qingjianInputMethodButton);
                foreach (Button button in new[] { scratchTextButton, scratchMarkdownButton, videoToGifButton,
                    calculatorButton, sevenZipButton, centralWeatherButton, msnWeatherButton, collageButton, textCompareButton,
                    qingjianInputMethodButton })
                {
                    button.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
                }
                centralWeatherButton.Font = new Font(Font.FontFamily, 7f, FontStyle.Bold);
                msnWeatherButton.Font = new Font(Font.FontFamily, 7f, FontStyle.Bold);
                SetOtherButtonIcon(domesticNewsButton, OtherIcon.DomesticNews);
                SetOtherButtonIcon(internationalNewsButton, OtherIcon.InternationalNews);
                SetOtherButtonIcon(presentationCanvasButton, OtherIcon.Canvas);
                SetOtherButtonIcon(everythingButton, OtherIcon.Search);
                SetOtherButtonIcon(dataOrganizerButton, OtherIcon.Data);
                SetOtherButtonIcon(scratchTextButton, OtherIcon.TextFile);
                SetOtherButtonIcon(scratchMarkdownButton, OtherIcon.MarkdownFile);
                SetOtherButtonIcon(videoToGifButton, OtherIcon.Gif);
                SetOtherButtonIcon(calculatorButton, OtherIcon.Calculator);
                SetOtherButtonIcon(sevenZipButton, OtherIcon.Archive);
                SetOtherButtonIcon(centralWeatherButton, OtherIcon.Forecast);
                SetOtherButtonIcon(msnWeatherButton, OtherIcon.Rain);
                SetOtherButtonIcon(collageButton, OtherIcon.Collage);
                SetOtherButtonIcon(textCompareButton, OtherIcon.Compare);
                SetOtherButtonIcon(qingjianInputMethodButton, OtherIcon.InputMethod);
                newsStatusLabel = new Label
                {
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.FromArgb(220, 155, 85),
                    Font = new Font(Font.FontFamily, 7.5f),
                    Visible = false
                };
                newsPanel.Controls.Add(newsStatusLabel);

            }

            internal override void ArrangePage() => LayoutNewsPanel();
            internal override void RefreshPage() {  }
        }
    }
}
