using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private TabPage voiceTranscriptionTab;
        private Label voiceModelStatus;
        private Label voiceModelLocation;
        private ProgressBar voiceModelProgress;
        private Button voiceModelInstallButton;
        private Button voiceModelCancelButton;
        private Button voiceModelRemoveButton;
        private CancellationTokenSource voiceModelDownloadCancellation;
        private bool voiceModelRemoving;

        private void InitializeVoiceTranscriptionSettingsTab()
        {
            voiceTranscriptionTab = new TabPage("语音转写") { Name = "tpVoiceTranscription", Padding = new Padding(12), AutoScroll = true };
            tcSettings.TabPages.Add(voiceTranscriptionTab);
            RegisterSettingsPage(voiceTranscriptionTab, () =>
            {
                BuildVoiceTranscriptionSettingsControls();
                LoadVoiceTranscriptionSettingsControls();
            });
        }

        private void BuildVoiceTranscriptionSettingsControls()
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
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.SuspendLayout();

            Label heading = new Label
            {
                Text = "本地语音识别",
                Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(3, 4, 3, 12)
            };
            table.Controls.Add(heading, 0, table.RowCount);
            table.SetColumnSpan(heading, 2);
            table.RowCount++;

            Label description = new Label
            {
                Text = "闪念胶囊转写和 Ctrl + Alt 语音打字共用 SenseVoice-Small 模型。录音在本机离线识别；模型未安装或加载失败时，使用 Windows 语音识别作为后备。",
                AutoSize = true,
                MaximumSize = new Size(650, 0),
                ForeColor = Color.Gray,
                Margin = new Padding(3, 0, 3, 12)
            };
            table.Controls.Add(description, 0, table.RowCount);
            table.SetColumnSpan(description, 2);
            table.RowCount++;

            voiceModelStatus = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 8) };
            AddVoiceModelRow(table, "模型状态", voiceModelStatus);

            voiceModelLocation = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(650, 0),
                ForeColor = Color.Gray,
                Margin = new Padding(3, 6, 3, 8)
            };
            AddVoiceModelRow(table, "保存位置", voiceModelLocation);

            voiceModelProgress = new ProgressBar
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = 20,
                Minimum = 0,
                Maximum = 100,
                Visible = false,
                Margin = new Padding(3, 6, 3, 8)
            };
            AddVoiceModelRow(table, "下载进度", voiceModelProgress);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0)
            };
            voiceModelInstallButton = new Button { Text = "安装离线模型", AutoSize = true };
            voiceModelCancelButton = new Button { Text = "取消下载", AutoSize = true, Visible = false };
            Button openFolder = new Button { Text = "打开模型文件夹", AutoSize = true };
            voiceModelRemoveButton = new Button { Text = "删除模型", AutoSize = true };
            actions.Controls.AddRange(new Control[]
            {
                voiceModelInstallButton, voiceModelCancelButton, openFolder, voiceModelRemoveButton
            });
            AddVoiceModelRow(table, "模型管理", actions);

            Label detail = new Label
            {
                Text = "模型约 239MB，首次安装需要联网；安装完成后转写无需联网。识别语言为自动，启用标点和数字格式化。",
                AutoSize = true,
                MaximumSize = new Size(650, 0),
                ForeColor = Color.FromArgb(210, 165, 70),
                Margin = new Padding(3, 4, 3, 8)
            };
            table.Controls.Add(detail, 1, table.RowCount++);
            table.SetColumn(detail, 0);
            table.SetColumnSpan(detail, 2);

            Label license = new Label
            {
                Text = "模型来源：SenseVoice / sherpa-onnx。安装时会同时保存模型许可证和来源信息。",
                AutoSize = true,
                MaximumSize = new Size(650, 0),
                ForeColor = Color.Gray,
                Margin = new Padding(3, 4, 3, 8)
            };
            table.Controls.Add(license, 1, table.RowCount++);
            table.SetColumn(license, 0);
            table.SetColumnSpan(license, 2);

            Label inputLocation = new Label
            {
                Text = "系统语音输入组件：到主菜单“输入法 → 小蟒语音输入”查看注册状态。此页管理语音识别模型。",
                AutoSize = true, MaximumSize = new Size(650, 0), ForeColor = Color.Gray, Margin = new Padding(3, 12, 3, 8)
            };
            table.Controls.Add(inputLocation, 0, table.RowCount++); table.SetColumnSpan(inputLocation, 2);

            voiceModelInstallButton.Click += async (sender, e) => await InstallVoiceModelAsync();
            voiceModelCancelButton.Click += (sender, e) => voiceModelDownloadCancellation?.Cancel();
            openFolder.Click += (sender, e) =>
            {
                string folder = SenseVoiceModelManager.GetModelFiles(Program.PersonalFolder).Folder;
                Directory.CreateDirectory(folder);
                FileHelpers.OpenFolder(folder);
            };
            voiceModelRemoveButton.Click += async (sender, e) => await RemoveVoiceModelAsync();
            FormClosed += (sender, e) => voiceModelDownloadCancellation?.Cancel();
            table.ResumeLayout(false);
            table.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f));
            voiceTranscriptionTab.Controls.Add(table);
            bool arranging = false;
            void ArrangeVoiceModelPage()
            {
                if (arranging || table.IsDisposed || designedSettingsPages.Contains(voiceTranscriptionTab)) return;
                arranging = true;
                try
                {
                    int S(int value) => MainMenuAppearance.Scale(voiceTranscriptionTab, value);
                    int width = Math.Max(1, voiceTranscriptionTab.ClientSize.Width - voiceTranscriptionTab.Padding.Horizontal);
                    // Constrain preferred widths to the viewport before AutoSize measures long paths.
                    table.MaximumSize = new Size(width, 0);
                    int labelWidth = S(72);
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
                    int fullWidth = Math.Max(1, width - table.Padding.Horizontal);
                    int fieldWidth = Math.Max(1, fullWidth - labelWidth);
                    foreach (Label label in new[] { description, voiceModelStatus, voiceModelLocation, detail, license, inputLocation })
                    {
                        int available = table.GetColumnSpan(label) == 2 ? fullWidth : fieldWidth;
                        label.MaximumSize = new Size(Math.Max(1, available - label.Margin.Horizontal), 0);
                        label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    }
                    actions.MaximumSize = new Size(fieldWidth, 0);
                    actions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                }
                finally { arranging = false; }
            }
            voiceTranscriptionTab.SizeChanged += (sender, e) => ArrangeVoiceModelPage();
            table.Layout += (sender, e) => ArrangeVoiceModelPage();
            ArrangeVoiceModelPage();
        }

        private void LoadVoiceTranscriptionSettingsControls()
        {
            RefreshVoiceModelStatus();
        }

        private async Task InstallVoiceModelAsync()
        {
            if (voiceModelDownloadCancellation != null) return;
            voiceModelDownloadCancellation = new CancellationTokenSource();
            SetVoiceModelDownloadState(true);
            Progress<SenseVoiceDownloadProgress> progress = new Progress<SenseVoiceDownloadProgress>(value =>
            {
                if (IsDisposed || voiceModelStatus == null || voiceModelStatus.IsDisposed) return;
                voiceModelStatus.Text = value.Stage + FormatDownloadBytes(value.BytesReceived, value.TotalBytes);
                voiceModelProgress.Value = Math.Clamp(value.Percent, voiceModelProgress.Minimum, voiceModelProgress.Maximum);
            });

            try
            {
                await SenseVoiceModelManager.InstallAsync(Program.PersonalFolder, progress,
                    voiceModelDownloadCancellation.Token);
                MessageBox.Show("SenseVoice 离线语音模型安装完成。", "语音转写",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) MessageBox.Show("已取消模型下载。", "语音转写",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e)
            {
                if (!IsDisposed) MessageBox.Show("模型安装失败：" + e.Message, "语音转写",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                voiceModelDownloadCancellation?.Dispose();
                voiceModelDownloadCancellation = null;
                if (!IsDisposed)
                {
                    SetVoiceModelDownloadState(false);
                    RefreshVoiceModelStatus();
                }
            }
        }

        private async Task RemoveVoiceModelAsync()
        {
            if (voiceModelRemoving || voiceModelDownloadCancellation != null || !SenseVoiceModelManager.HasLocalFiles(Program.PersonalFolder)) return;
            if (MessageBox.Show("确定删除 SenseVoice 离线语音模型及未完成的下载文件吗？删除后将改用 Windows 语音识别。",
                "删除语音模型", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                voiceModelRemoving = true;
                voiceModelInstallButton.Enabled = voiceModelRemoveButton.Enabled = false;
                voiceModelStatus.Text = "正在等待转写结束并删除模型…";
                string personalFolder = Program.PersonalFolder;
                await SenseVoiceModelManager.RemoveAsync(personalFolder);
                if (!IsDisposed) RefreshVoiceModelStatus();
            }
            catch (Exception e)
            {
                if (!IsDisposed) MessageBox.Show("删除失败：" + e.Message, "删除语音模型", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                voiceModelRemoving = false;
                if (!IsDisposed) SetVoiceModelDownloadState(false);
            }
        }

        private void SetVoiceModelDownloadState(bool downloading)
        {
            voiceModelInstallButton.Enabled = !downloading;
            voiceModelRemoveButton.Enabled = !downloading && SenseVoiceModelManager.HasLocalFiles(Program.PersonalFolder);
            voiceModelCancelButton.Visible = downloading;
            voiceModelProgress.Visible = downloading;
            if(voiceProgressLayoutRow!=null)voiceDesignSurface?.SetRowVisible(voiceProgressLayoutRow,downloading);
            if (downloading) voiceModelProgress.Value = 0;
        }

        private void RefreshVoiceModelStatus()
        {
            bool installed = SenseVoiceModelManager.IsInstalled(Program.PersonalFolder);
            voiceState?.SetInstalled(installed);
            long size = SenseVoiceModelManager.GetInstalledSize(Program.PersonalFolder);
            voiceModelStatus.Text = installed ? $"已安装 {SenseVoiceModelManager.DisplayName}（{FormatFileSize(size)}）" : "未安装，将使用 Windows 语音识别";
            if (!installed && size > 0) voiceModelStatus.Text += $"；存在不完整模型文件（{FormatFileSize(size)}），可删除后重新安装";
            voiceModelStatus.ForeColor = installed ? Color.FromArgb(88, 190, 115) : Color.FromArgb(210, 165, 70);
            voiceModelLocation.Text = SenseVoiceModelManager.GetModelFiles(Program.PersonalFolder).Folder;
            voiceModelInstallButton.Text = installed ? "重新安装模型" : "安装离线模型";
            voiceModelRemoveButton.Enabled = voiceModelDownloadCancellation == null && SenseVoiceModelManager.HasLocalFiles(Program.PersonalFolder);
        }

        private static string FormatDownloadBytes(long received, long? total)
        {
            if (received <= 0) return "";
            return total > 0 ? $"（{FormatFileSize(received)} / {FormatFileSize(total.Value)}）" :
                $"（{FormatFileSize(received)}）";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / (1024d * 1024d * 1024d)).ToString("0.0") + "GB";
            if (bytes >= 1024L * 1024L) return (bytes / (1024d * 1024d)).ToString("0.0") + "MB";
            if (bytes >= 1024L) return (bytes / 1024d).ToString("0.0") + "KB";
            return bytes + "B";
        }

        private static void AddVoiceModelRow(TableLayoutPanel table, string label, Control value)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Margin = new Padding(3, 7, 3, 3)
            }, 0, row);
            table.Controls.Add(value, 1, row);
        }
    }
}
