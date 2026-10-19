using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    public partial class OCRForm
    {
        private readonly Button chineseButton = new Button { Name = "ocrChinese" };

        private readonly Button copyTranslationButton = new Button { Name = "ocrCopyTranslation", Text = "复制译文" };
        private readonly Button registrationButton = new Button { Name = "ocrRegistration", Text = "前往注册页" };
        private bool mixedSelected = true;
        private readonly System.Threading.CancellationTokenSource ocrCancellation = new();

        private void UpdateLanguageButtons()
        {
            registrationButton.Enabled = !busy;
            registrationButton.Visible = !registrationValid;
            chineseButton.Enabled = !busy;
            chineseButton.Text = mixedSelected ? "中+英" : Options.Language?.StartsWith("zh") == true ? "中" : "EN";
            chineseButton.BackColor = mixedSelected ? Color.FromArgb(38,48,57) : Color.FromArgb(255,135,62);
            chineseButton.ForeColor = mixedSelected ? Color.FromArgb(229,236,242) : Color.FromArgb(27,36,43);
            chineseButton.AccessibleDescription = "点击按中+英、中、EN循环切换识别";
        }
        private bool registrationValid, checkingRegistration;
        private async System.Threading.Tasks.Task RefreshRegistrationAsync()
        {
            if (checkingRegistration || IsDisposed || Disposing) return;
            checkingRegistration = true;
            try { registrationValid = await System.Threading.Tasks.Task.Run(() => MixedOcrComponent.IsValidAsync(ocrCancellation.Token)); }
            catch (System.OperationCanceledException) { return; }
            catch { registrationValid = false; }
            finally { checkingRegistration = false; }
            if (!IsDisposed && !Disposing) UpdateLanguageButtons();
        }
        private async System.Threading.Tasks.Task ToggleLanguageAsync()
        {
            if (busy) return;
            if (!mixedSelected && Options.Language?.StartsWith("en") == true) { mixedSelected = true; UpdateLanguageButtons(); await OCR(bmpSource); return; }
            string target = !mixedSelected && Options.Language?.StartsWith("zh") == true ? "en" : "zh";
            int index = -1;
            for(int i=0;i<cbLanguages.Items.Count;i++)
                if(((OCRLanguage)cbLanguages.Items[i]).LanguageTag.StartsWith(target,System.StringComparison.OrdinalIgnoreCase)){index=i;break;}
            if(index < 0){
                // Missing single-language modes are skipped, never trapping the cycle.
                if (!mixedSelected) { mixedSelected = true; UpdateLanguageButtons(); await OCR(bmpSource); return; }
                for(int i=0;i<cbLanguages.Items.Count;i++)
                    if(((OCRLanguage)cbLanguages.Items[i]).LanguageTag.StartsWith("en",System.StringComparison.OrdinalIgnoreCase)){index=i;break;}
                if(index < 0){ mixedSelected = true; UpdateLanguageButtons(); return; }
            }
            mixedSelected = false;
            if(cbLanguages.SelectedIndex == index) await OCR(bmpSource); else cbLanguages.SelectedIndex = index;
        }
        private void InitializeTranslationPanel()
        {
            Activated += async (_, _) => await RefreshRegistrationAsync();
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
            ButtonSize(btnOpenServiceLink, "翻译并显示", 140);
            ButtonSize(btnCopyAll, "复制原文", 130);
            void SetActionIcon(Button button, bool copy)
            {
                bool hovered = false, pressed = false;
                button.Cursor = Cursors.Hand;
                button.MouseEnter += (_, _) => { hovered = true; button.Invalidate(); };
                button.MouseLeave += (_, _) => { hovered = pressed = false; button.Invalidate(); };
                button.MouseDown += (_, e) => { if(e.Button == MouseButtons.Left) pressed = true; button.Invalidate(); };
                button.MouseUp += (_, _) => { pressed = false; button.Invalidate(); };
                button.KeyDown += (_, e) => { if(e.KeyCode == Keys.Space) { pressed = true; button.Invalidate(); } };
                button.KeyUp += (_, _) => { pressed = false; button.Invalidate(); };
                button.LostFocus += (_, _) => { pressed = false; button.Invalidate(); };
                button.EnabledChanged += (_, _) => button.Invalidate();
                void Render()
                {
                    int size = (int)(20 * button.DeviceDpi / 96f);
                    var icon = new Bitmap(size, size);
                    using (var g = Graphics.FromImage(icon))
                    using (var pen = new Pen(copy ? Color.FromArgb(255,135,62) : Color.FromArgb(27, 36, 43), 1.6f))
                    {
                        g.ScaleTransform(size / 20f, size / 20f);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        if (copy) { g.DrawRectangle(pen, 7, 6, 10, 12); g.DrawLines(pen, new[] { new Point(4,14), new Point(2,14), new Point(2,2), new Point(12,2) }); }
                        else
                        {
                            using var font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold, GraphicsUnit.Pixel);
                            using var brush = new SolidBrush(pen.Color);
                            g.DrawString("文", font, brush, 0, 0); g.DrawString("A", font, brush, 11, 9);
                            g.DrawLines(pen, new[] { new Point(11,3), new Point(17,3), new Point(17,7) });
                        }
                    }
                    var old = button.Image; button.Image = icon; old?.Dispose();
                }
                button.ImageAlign = ContentAlignment.MiddleCenter;
                button.TextAlign = ContentAlignment.MiddleCenter;
                button.TextImageRelation = TextImageRelation.ImageBeforeText;
                button.Paint += (_, e) =>
                {
                    // WinForms reserves separate image/text cells; center the actual group.
                    using var background = new SolidBrush(!button.Enabled ? Color.FromArgb(48,55,61)
                        : pressed ? (copy ? Color.FromArgb(62,55,48) : Color.FromArgb(222,104,35))
                        : hovered ? (copy ? Color.FromArgb(53,61,68) : Color.FromArgb(255,159,95)) : button.BackColor);
                    e.Graphics.FillRectangle(background, 1, 1, button.Width - 2, button.Height - 2);
                    if (button.Image == null) return;
                    var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                    var textSize = TextRenderer.MeasureText(e.Graphics, button.Text, button.Font, Size.Empty, flags);
                    int gap = 4 * button.DeviceDpi / 96;
                    int x = (button.Width - button.Image.Width - gap - textSize.Width) / 2;
                    e.Graphics.DrawImageUnscaled(button.Image, x, (button.Height - button.Image.Height) / 2);
                    TextRenderer.DrawText(e.Graphics, button.Text, button.Font,
                        new Point(x + button.Image.Width + gap, (button.Height - textSize.Height) / 2),
                        button.Enabled ? button.ForeColor : SystemColors.GrayText, flags);
                };
                button.DpiChangedAfterParent += (_, _) => Render();
                button.Disposed += (_, _) => button.Image?.Dispose();
                Render();
            }
            SetActionIcon(btnOpenServiceLink, false);
            SetActionIcon(btnCopyAll, true);
            ButtonSize(copyTranslationButton, "复制译文", 130);
            SetActionIcon(copyTranslationButton, true);
            copyTranslationButton.Click += async (_, _) => await CopyTranslationAsync();
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
            ButtonSize(chineseButton, "中+英", 78);

            ButtonSize(registrationButton,"前往注册页",125);
            chineseButton.Click += async (sender, e) => await ToggleLanguageAsync();

            registrationButton.Click += (sender, e) =>
            {
                try
                {
                    Program.MainForm.ShowMainMenu("OCR registration");
                    Program.MainForm.OpenRegistrationPage("ocr");
                }
                catch (System.Exception error)
                {
                    ShareX.HelpersLib.DebugHelper.WriteException(error);
                    if (!IsDisposed) MessageBox.Show(this, "无法打开注册页：" + error.Message, "OCR 识别组件");
                }
            };
            toolbar.Controls.Add(Group(lblLanguage,chineseButton,registrationButton));
            toolbar.Controls.Add(Group(btnOpenServiceLink, btnCopyAll, copyTranslationButton));
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
            txtResult.BorderStyle = BorderStyle.None;
            translationStatus = new Label { Text = "翻译网页 · 等待识别原文", AutoEllipsis = true,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
            var translationHeader = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, AutoSize = true, MinimumSize = new Size(0,30) };
            openExternal = new LinkLabel { Text = "在浏览器中打开 ↗", AutoSize = false, Width = 155,
                Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight, TabStop = true };
            openExternal.LinkClicked += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(Result) && cbServices.SelectedItem is ServiceLink service)
                    service.OpenLink(Result);
            };
            translationHeader.Controls.Add(translationStatus); translationHeader.Controls.Add(openExternal);
            apiHeader = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Visible = false, Margin = Padding.Empty };
            translationHeader.Controls.Add(apiHeader);
            translationHeader.SizeChanged += (_, _) => left.RowStyles[0].Height = translationHeader.Height;
            right.RowStyles[0] = new RowStyle(SizeType.AutoSize);
            translationHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var originalTitle = new Label { Text = "原文", Dock = DockStyle.Left, Width = 46, TextAlign = ContentAlignment.MiddleLeft };
            originalHeader.Controls.Add(originalTitle);
            var originalBody = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(12) };
            originalBody.Controls.Add(txtResult);
            originalBody.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(62,78,90)); e.Graphics.DrawRectangle(pen,0,0,originalBody.Width-1,originalBody.Height-1); };
            left.Controls.Add(originalHeader, 0, 0); left.Controls.Add(originalBody, 0, 1);
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
            txtResult.BorderStyle = BorderStyle.None;
            txtResult.Parent.BackColor = surface;
            lblResult.ForeColor = Color.FromArgb(146,163,175);
            txtResult.Font = new Font("Microsoft YaHei UI", 11F);
            foreach (Button button in new[] { btnSelectRegion, btnOpenServiceLink, btnCopyAll, copyTranslationButton })
            {
                button.BackColor = Color.FromArgb(255, 135, 62);
                button.ForeColor = Color.FromArgb(27, 36, 43);
                button.FlatAppearance.BorderSize = 0;
            }
            foreach (Button button in new[] { btnCopyAll, copyTranslationButton })
            {
                button.BackColor = surface;
                button.ForeColor = Color.FromArgb(255,135,62);
                button.FlatAppearance.BorderSize = 1;
            }
            foreach (Button button in new[] { btnOpenServiceLink, btnCopyAll, chineseButton, copyTranslationButton, registrationButton })
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
