using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class AboutPageForm : Form
    {
        private const string LogoResourceName = "ShareX.Assets.XiaomangRobotLogo.png";
        private const string TrainingResourceName = "ShareX.Assets.XiaomangTraining.pptx";
        private const string FeatureOverviewResourceName = "ShareX.Assets.XiaomangFeatureOverview.pdf";
        private const string VersionLogResourceName = "ShareX.Assets.XiaomangVersionLog.txt";
        private const string MindMapResourceName = "ShareX.Assets.XiaomangFeatureMindMap.html";
        private readonly Bitmap logoImage;
        private readonly PictureBox logo;

        public AboutPageForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = ShareXResources.Theme.BackgroundColor;
            ForeColor = ShareXResources.Theme.TextColor;
            Font = new Font("Microsoft YaHei", 10f);
            MinimumSize = new Size(560, 330);
            AutoScroll = true;

            using (Stream stream = typeof(AboutPageForm).Assembly.GetManifestResourceStream(LogoResourceName)
                ?? throw new InvalidOperationException("找不到小蟒同学 Logo 资源。"))
            using (Image source = Image.FromStream(stream))
            {
                logoImage = new Bitmap(source);
            }

            logo = new PictureBox
            {
                Image = logoImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = Padding.Empty
            };

            Panel logoHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = BackColor };
            logoHost.Controls.Add(logo);
            logoHost.SizeChanged += (sender, e) =>
            {
                int size = Math.Max(1, Math.Min(logoHost.ClientSize.Width, logoHost.ClientSize.Height));
                logo.Bounds = new Rectangle(0, 0, size, size);
            };

            TableLayoutPanel details = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                Padding = new Padding(28, 0, 0, 0),
                Margin = Padding.Empty,
                BackColor = BackColor
            };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int row = 0; row < 6; row++)
                details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Label creditHeading = CreateHeadingLabel("鸣　　谢：");
            details.Controls.Add(creditHeading, 0, 0);
            LinkLabel credit = CreateCreditLabel(Links.GitHub);
            details.Controls.Add(credit, 1, 0);
            LinkLabel inputMethodCredit = CreateCreditLabel(QingjianInputMethodManager.ProjectUrl);
            inputMethodCredit.Margin = new Padding(0, MainMenuAppearance.Scale(this, 6), 0, 0);
            details.Controls.Add(inputMethodCredit, 1, 1);
            LinkLabel diaryCredit = CreateCreditLabel(LocalDiaryManager.ProjectUrl);
            diaryCredit.Margin = new Padding(0, MainMenuAppearance.Scale(this, 6), 0, 0);
            details.Controls.Add(diaryCredit, 1, 2);
            var mindMap=new LinkLabel {
                Name="productMindMapLink",Text="产品功能思维导图.html",AutoSize=true,Dock=DockStyle.Fill,
                LinkBehavior=LinkBehavior.NeverUnderline,LinkColor=Color.FromArgb(111,190,255),ActiveLinkColor=Color.FromArgb(165,216,255),
                VisitedLinkColor=Color.FromArgb(111,190,255),Tag=VisibleBranding.PreserveText,Margin=new Padding(0,MainMenuAppearance.Scale(this,8),0,0)
            };
            Label productHeading = CreateHeadingLabel("产品介绍：");
            productHeading.Margin = new Padding(0, mindMap.Margin.Top, productHeading.Margin.Right, 0);
            details.Controls.Add(productHeading, 0, 3);
            mindMap.LinkClicked += (_,_)=>OpenMindMap();details.Controls.Add(mindMap,1,3);
            Label versionLogHeading = CreateHeadingLabel("版本日志：");
            details.Controls.Add(versionLogHeading, 0, 4);
            LinkLabel versionLog = new LinkLabel
            {
                Name = "versionLogLink",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = "下载版本日志（TXT）",
                LinkArea = new LinkArea(0, "下载版本日志（TXT）".Length),
                LinkBehavior = LinkBehavior.NeverUnderline,
                LinkColor = Color.FromArgb(111, 190, 255),
                ActiveLinkColor = Color.FromArgb(165, 216, 255),
                VisitedLinkColor = Color.FromArgb(111, 190, 255),
                ForeColor = ForeColor,
                TextAlign = ContentAlignment.TopLeft,
                Margin = Padding.Empty,
                Tag = VisibleBranding.PreserveText
            };
            versionLog.LinkClicked += (sender, e) => SaveVersionLog();
            details.Controls.Add(versionLog, 1, 4);
            var updateHeading = CreateHeadingLabel("软件更新：");
            // Use a dedicated link handler; this is an in-app action, not a URL.
            var updateButton = new ProductUpdateButton { Text = "检查更新", Name = "checkProductUpdateButton", RefreshGlyph = true, BackColor = BackColor, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 0, 4) };
            updateButton.Click += (_, _) => ProductUpdateForm.Open();
            details.Controls.Add(updateHeading, 0, 5);
            details.Controls.Add(updateButton, 1, 5);
            int sectionSpacing = MainMenuAppearance.Scale(this, 22);
            int rowSpacing = MainMenuAppearance.Scale(this, 12);
            updateHeading.TextAlign = ContentAlignment.MiddleLeft;
            updateHeading.Margin = new Padding(0, rowSpacing, updateHeading.Margin.Right, 0);
            updateButton.Margin = new Padding(0, rowSpacing, 0, 0);
            versionLogHeading.Margin = new Padding(0, sectionSpacing, versionLogHeading.Margin.Right, 0);
            versionLog.Margin = new Padding(0, sectionSpacing, 0, 0);
            TableLayoutPanel content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(32, 28, 32, 28),
                BackColor = BackColor
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            content.Controls.Add(logoHost, 0, 0);
            content.Controls.Add(details, 1, 0);
            Controls.Add(content);
            content.Dock = DockStyle.None;
            void LayoutContent()
            {
                int inset = MainMenuAppearance.Scale(this, 24);
                int width = Math.Max(1, ClientSize.Width - inset * 2);
                int detailsWidth = Math.Max(1, (int)((width - content.Padding.Horizontal) * 0.58f));
                int minimumHeight = details.GetPreferredSize(new Size(detailsWidth, 0)).Height + content.Padding.Vertical;
                int height = Math.Max(minimumHeight, Math.Min(ClientSize.Height - inset * 2, MainMenuAppearance.Scale(this, 380)));
                content.Bounds = new Rectangle(inset + AutoScrollPosition.X, inset + AutoScrollPosition.Y, width, Math.Max(1, height));
                AutoScrollMinSize = new Size(0, height + inset * 2);
            }
            SizeChanged += (sender, e) => LayoutContent();
            LayoutContent();
            MainMenuAppearance.Apply(this);
        }

        private LinkLabel CreateCreditLabel(string url)
        {
            LinkLabel label = new LinkLabel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = url,
                LinkArea = new LinkArea(0, url.Length),
                LinkBehavior = LinkBehavior.NeverUnderline,
                LinkColor = Color.FromArgb(111, 190, 255),
                ActiveLinkColor = Color.FromArgb(165, 216, 255),
                VisitedLinkColor = Color.FromArgb(111, 190, 255),
                ForeColor = ForeColor,
                TextAlign = ContentAlignment.TopLeft,
                Margin = Padding.Empty,
                Tag = VisibleBranding.PreserveText
            };
            label.LinkClicked += (sender, e) => URLHelpers.OpenURL(url);
            return label;
        }

        private Label CreateInfoLabel(string text, Color color)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = text,
                ForeColor = color,
                TextAlign = ContentAlignment.TopLeft,
                Margin = Padding.Empty
            };
        }

        private Label CreateHeadingLabel(string text)
        {
            Label label = CreateInfoLabel(text, ForeColor);
            label.TextAlign = ContentAlignment.TopRight;
            label.Margin = new Padding(0, 0, MainMenuAppearance.Scale(this, 8), 0);
            return label;
        }

        private void SaveTrainingGuide()
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "下载小蟒同学使用培训",
                FileName = "小蟒同学使用培训_全新教程版.pptx",
                Filter = "PowerPoint 演示文稿 (*.pptx)|*.pptx",
                DefaultExt = "pptx",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                using Stream resource = typeof(AboutPageForm).Assembly.GetManifestResourceStream(TrainingResourceName)
                    ?? throw new InvalidOperationException("找不到内置的新手使用培训文件。");
                using FileStream destination = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write);
                resource.CopyTo(destination);
                MessageBox.Show(this, "培训文件已保存到所选位置。", "下载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存培训文件失败：" + ex.Message, "下载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveFeatureOverview()
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "保存产品介绍",
                FileName = "小蟒2.3_产品介绍.pdf",
                Filter = "PDF 文档 (*.pdf)|*.pdf",
                DefaultExt = "pdf",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            string temporaryPath = null;
            try
            {
                using Stream resource = typeof(AboutPageForm).Assembly.GetManifestResourceStream(FeatureOverviewResourceName)
                    ?? throw new InvalidOperationException("找不到内置的功能简介文件。");
                temporaryPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dialog.FileName)),
                    ".xiaomang-feature-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    resource.CopyTo(destination);
                    destination.Flush(true);
                }
                File.Move(temporaryPath, dialog.FileName, true);
                temporaryPath = null;
                MessageBox.Show(this, "功能简介已保存到所选位置。", "保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存功能简介失败：" + ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (temporaryPath != null)
                {
                    try { File.Delete(temporaryPath); }
                    catch (Exception ex) { DebugHelper.WriteException(ex, "清理功能简介临时文件失败。"); }
                }
            }
        }

        private void SaveVersionLog()
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "保存版本日志",
                FileName = "小蟒_版本日志.txt",
                Filter = "文本文件 (*.txt)|*.txt",
                DefaultExt = "txt",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                ExportVersionLog(dialog.FileName);
                MessageBox.Show(this, "版本日志已保存到所选位置。", "保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存版本日志失败：" + ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal static void ExportVersionLog(string fileName)
        {
            string temporaryPath = null;
            try
            {
                using Stream resource = typeof(AboutPageForm).Assembly.GetManifestResourceStream(VersionLogResourceName)
                    ?? throw new InvalidOperationException("找不到内置的版本日志文件。");
                string destinationPath = Path.GetFullPath(fileName);
                temporaryPath = Path.Combine(Path.GetDirectoryName(destinationPath),
                    ".xiaomang-version-log-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    resource.CopyTo(destination);
                    destination.Flush(true);
                }
                File.Move(temporaryPath, destinationPath, true);
                temporaryPath = null;
            }
            finally
            {
                if (temporaryPath != null)
                {
                    try { File.Delete(temporaryPath); }
                    catch (Exception ex) { DebugHelper.WriteException(ex, "清理版本日志临时文件失败。"); }
                }
            }
        }

        private void OpenMindMap()
        {
            try
            {
                string directory=Path.Combine(Program.PersonalFolder,"Cache","ProductGuide");
                Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,"产品功能思维导图.html");
                ExportMindMap(path);
                FileHelpers.OpenFile(path);
            }
            catch(Exception error){DebugHelper.WriteException(error);MessageBox.Show(this,"打开思维导图失败："+error.Message,"产品功能思维导图",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }

        internal static void ExportMindMap(string destination)
        {
            string temp=destination+".new-"+Guid.NewGuid().ToString("N");
            try
            {
                using Stream resource=typeof(AboutPageForm).Assembly.GetManifestResourceStream(MindMapResourceName)
                    ?? throw new IOException("找不到内置产品思维导图。");
                using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){resource.CopyTo(file);file.Flush(true);}
                File.Move(temp,destination,true);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                logo.Image = null;
                logoImage.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
