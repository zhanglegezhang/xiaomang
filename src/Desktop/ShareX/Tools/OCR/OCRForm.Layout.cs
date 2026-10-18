using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    public partial class OCRForm
    {
        private readonly Button chineseButton = new Button { Name = "ocrChinese" };
        private readonly Button englishButton = new Button { Name = "ocrEnglish" };
        private readonly Button mixedButton = new Button { Name = "ocrMixed", Text="中英" };
        private readonly Button mixedDownload = new Button { Name = "ocrMixedDownload", Text="下载中英组件" };
        private bool mixedSelected = true;
        private readonly System.Threading.CancellationTokenSource ocrCancellation = new();

        private void UpdateLanguageButtons()
        {
            mixedButton.Enabled=!busy;
            mixedButton.BackColor=mixedSelected?Color.FromArgb(255,135,62):Color.FromArgb(38,48,57);
            mixedButton.ForeColor=mixedSelected?Color.FromArgb(27,36,43):Color.FromArgb(229,236,242);
            mixedDownload.Visible=!MixedOcrComponent.IsPresent;
            mixedDownload.Enabled=!busy;
            foreach (var pair in new[] { (chineseButton, "zh"), (englishButton, "en") })
            {
                int index = -1;
                for (int i = 0; i < cbLanguages.Items.Count; i++)
                    if (((OCRLanguage)cbLanguages.Items[i]).LanguageTag.StartsWith(pair.Item2, System.StringComparison.OrdinalIgnoreCase)) { index = i; break; }
                pair.Item1.Enabled = !busy && index >= 0;
                bool selected = !mixedSelected && cbLanguages.SelectedIndex == index && index >= 0;
                pair.Item1.BackColor = selected ? Color.FromArgb(255, 135, 62) : Color.FromArgb(38, 48, 57);
                pair.Item1.ForeColor = selected ? Color.FromArgb(27, 36, 43) : Color.FromArgb(229, 236, 242);
                pair.Item1.AccessibleDescription = index < 0 ? "未安装此识别语言" : selected ? "已选中" : "切换识别语言";
                pair.Item1.Tag = index;
            }
        }
        private void InitializeTranslationPanel()
        {
            SuspendLayout();
            Controls.Clear();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            Font = new Font("Microsoft YaHei UI", 10F);
            Text = "小蟒 · 文字识别（OCR）";
            ClientSize = new Size(1180, 740);
            MinimumSize = new Size(760, 520);
            SizeGripStyle = SizeGripStyle.Show;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 12), ColumnCount = 1, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var toolbar = new FlowLayoutPanel { Name = "ocrToolbar", Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 0, 0, 2) };
            Panel Group(params Control[] controls)
            {
                var group = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = false, Margin = new Padding(0, 0, 12, 0) };
                foreach (Control control in controls)
                {
                    control.Dock = DockStyle.None;
                    control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    control.Margin = new Padding(0, 0, 6, 0);
                    group.Controls.Add(control);
                }
                return group;
            }
            void LabelStyle(Label label, string text, int width = 65)
            {
                label.Text = text; label.AutoSize = false; label.Size = new Size(width, 32);
                label.TextAlign = ContentAlignment.MiddleLeft;
            }
            void ButtonSize(Button button, string text, int width)
            {
                button.Text = text; button.AutoSize = false; button.Size = new Size(width, 36);
            }
            ButtonSize(btnSelectRegion, "选择区域", 112);
            ButtonSize(btnOpenServiceLink, "翻译并显示", 114);
            ButtonSize(btnCopyAll, "复制原文", 108);
            ButtonSize(btnOpenOCRHelp, "?", 32); btnOpenOCRHelp.Image = null;
            ButtonSize(cbEditServices, "⋯", 32); cbEditServices.Image = null;
            btnOpenOCRHelp.AccessibleName = "识别帮助";
            cbEditServices.AccessibleName = "翻译服务设置";
            LabelStyle(lblLanguage, "识别语言");
            LabelStyle(lblScaleFactor, "缩放", 38);
            LabelStyle(lblService, "翻译服务");
            cbLanguages.Width = 155; cbServices.Width = 135;
            nudScaleFactor.Width = 64;
            cbSingleLine.Text = "单行"; cbSingleLine.AutoSize = true;
            ButtonSize(chineseButton, "中文", 60);
            ButtonSize(englishButton, "En", 54);
            ButtonSize(mixedButton,"中英",60);ButtonSize(mixedDownload,"下载中英组件",125);
            chineseButton.Click += async (sender, e) => { mixedSelected=false; if (chineseButton.Tag is int index && index >= 0) {if(cbLanguages.SelectedIndex==index)await OCR(bmpSource);else cbLanguages.SelectedIndex=index;} };
            englishButton.Click += async (sender, e) => { mixedSelected=false; if (englishButton.Tag is int index && index >= 0) {if(cbLanguages.SelectedIndex==index)await OCR(bmpSource);else cbLanguages.SelectedIndex=index;} };
            mixedButton.Click += async(sender,e)=>{mixedSelected=true;await OCR(bmpSource);};
            mixedDownload.Click += async(sender,e)=>
            {
                busy=true;UpdateControls();
                try {await MixedOcrComponent.DownloadAsync(new System.Progress<int>(value=>{if(!IsDisposed)lblResult.Text=$"下载中英组件 {value}%";}),ocrCancellation.Token);}
                catch(System.OperationCanceledException){}
                catch(System.Exception error){if(!IsDisposed)MessageBox.Show(this,error.Message,"中英识别组件");}
                finally{if(!IsDisposed){busy=false;UpdateControls();}}
                if(!IsDisposed && MixedOcrComponent.IsPresent){mixedSelected=true;await OCR(bmpSource);}
            };
            toolbar.Controls.Add(Group(lblLanguage,mixedButton,chineseButton, englishButton,mixedDownload));
            toolbar.Controls.Add(Group(btnOpenServiceLink, btnCopyAll));
            var split = new SplitContainer { Name = "ocrContentSplit", Dock = DockStyle.Fill,
                Size = new Size(1148, 620), Orientation = Orientation.Vertical,
                SplitterWidth = 8, SplitterDistance = 304, Panel1MinSize = 220, Panel2MinSize = 300,
                Margin = Padding.Empty };
            TableLayoutPanel ContentPanel()
            {
                var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                return panel;
            }
            var left = ContentPanel();
            var right = ContentPanel();
            var originalHeader = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            lblResult.Text = "识别原文"; lblResult.AutoSize = false;
            lblResult.Dock = DockStyle.Fill; lblResult.TextAlign = ContentAlignment.MiddleLeft;
            originalHeader.Controls.Add(lblResult);
            txtResult.Dock = DockStyle.Fill; txtResult.Margin = Padding.Empty;
            txtResult.Font = new Font("Microsoft YaHei UI", 11F);
            txtResult.BorderStyle = BorderStyle.FixedSingle;
            translationStatus = new Label { Text = "翻译网页 · 等待识别原文", AutoEllipsis = true,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
            var translationHeader = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var openExternal = new LinkLabel { Text = "在浏览器中打开 ↗", AutoSize = false, Width = 155,
                Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight, TabStop = true };
            openExternal.LinkClicked += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(Result) && cbServices.SelectedItem is ServiceLink service)
                    service.OpenLink(Result);
            };
            translationHeader.Controls.Add(translationStatus); translationHeader.Controls.Add(openExternal);
            translationHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            left.Controls.Add(originalHeader, 0, 0); left.Controls.Add(txtResult, 0, 1);
            right.Controls.Add(translationHeader, 0, 0); right.Controls.Add(translationHost, 0, 1);
            split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(right);
            root.Controls.Add(toolbar, 0, 0); root.Controls.Add(split, 0, 1);
            Controls.Add(root);
            ResumeLayout(true);
        }

        private void ApplyOcrStyle()
        {
            Font = new Font("Microsoft YaHei UI", 10F);
            Color background = Color.FromArgb(27, 36, 43);
            Color surface = Color.FromArgb(38, 48, 57);
            Color foreground = Color.FromArgb(229, 236, 242);
            void Style(Control parent)
            {
                parent.Font = Font;
                parent.BackColor = parent is TextBox || parent is ComboBox || parent is NumericUpDown || parent is Button ? surface : background;
                parent.ForeColor = foreground;
                if (parent is Button button)
                {
                    button.UseVisualStyleBackColor = false;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Color.FromArgb(62, 78, 90);
                }
                if (parent is LinkLabel link) link.LinkColor = link.ActiveLinkColor = Color.FromArgb(112, 183, 240);
                if (parent is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
                foreach (Control child in parent.Controls) Style(child);
            }
            Style(this);
            txtResult.Font = new Font("Microsoft YaHei UI", 11F);
            foreach (Button button in new[] { btnSelectRegion, btnOpenServiceLink, btnCopyAll })
            {
                button.BackColor = Color.FromArgb(255, 135, 62);
                button.ForeColor = Color.FromArgb(27, 36, 43);
                button.FlatAppearance.BorderSize = 0;
            }
            foreach (Button button in new[] { btnOpenServiceLink, btnCopyAll, chineseButton, englishButton, mixedButton, mixedDownload })
            {
                void RoundButton()
                {
                    float d = 8f * button.DeviceDpi / 96f;
                    using var path = new System.Drawing.Drawing2D.GraphicsPath();
                    float w = button.Width, h = button.Height;
                    path.AddArc(0, 0, d, d, 180, 90);
                    path.AddArc(w - d, 0, d, d, 270, 90);
                    path.AddArc(w - d, h - d, d, d, 0, 90);
                    path.AddArc(0, h - d, d, d, 90, 90);
                    path.CloseFigure();
                    var old = button.Region;
                    button.Region = new Region(path);
                    old?.Dispose();
                }
                button.SizeChanged += (sender, e) => RoundButton();
                RoundButton();
            }
            translationHost.BackColor = surface;
        }
    }
}
