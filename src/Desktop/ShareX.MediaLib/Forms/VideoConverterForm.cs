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
using ShareX.MediaLib.Properties;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX.MediaLib
{
    public partial class VideoConverterForm : Form
    {
        public string FFmpegFilePath { get; private set; }
        public VideoConverterOptions Options { get; private set; }

        private bool formClosing, formReady, encoding;
        private readonly bool simplifiedGifMode;
        private readonly string defaultInputDirectory;
        private Label lblFixedGifCodec;
        private FFmpegCLIManager ffmpeg;
        private string automaticOutputName;
        private volatile bool cancelEncoding;

        public VideoConverterForm(string ffmpegFilePath, VideoConverterOptions options)
            : this(ffmpegFilePath, options, null, false)
        {
        }

        public VideoConverterForm(string ffmpegFilePath, VideoConverterOptions options, string defaultInputDirectory, bool simplifiedGifMode)
        {
            FFmpegFilePath = ffmpegFilePath;
            Options = options;
            this.defaultInputDirectory = defaultInputDirectory;
            this.simplifiedGifMode = simplifiedGifMode;
            if (simplifiedGifMode) Options.VideoCodec = ConverterVideoCodecs.gif;

            InitializeComponent();
            ShareXResources.ApplyTheme(this, true);
            if (simplifiedGifMode) ConfigureSimplifiedGifLayout();

            UpdateOptions();

            txtInputFilePath.Text = Options.InputFilePath;
            txtOutputFolder.Text = Options.OutputFolderPath;
            txtOutputFileName.Text = Options.OutputFileName;
            cbVideoEncoder.Items.AddRange(Helpers.GetEnumDescriptions<ConverterVideoCodecs>());
            cbVideoEncoder.SelectedIndex = (int)Options.VideoCodec;
            cbVideoQualityUseBitrate.Checked = Options.VideoQualityUseBitrate;
            tbVideoQuality.SetValue(tbVideoQuality.Minimum + tbVideoQuality.Maximum - Options.VideoQuality);
            nudVideoQualityBitrate.SetValue(Options.VideoQualityBitrate);

            cbAutoOpenFolder.Checked = Options.AutoOpenFolder;

            cbUseCustomArguments.Checked = Options.UseCustomArguments;

            if (Options.UseCustomArguments)
            {
                txtArguments.Text = Options.CustomArguments;
            }

            formReady = true;
            UpdateOptions();
        }

        private void ConfigureSimplifiedGifLayout()
        {
            int outputRowHeight = txtOutputFileName.Top - txtOutputFolder.Top;

            lblOutputFolder.Visible = txtOutputFolder.Visible = btnOutputFolderBrowse.Visible = false;
            cbUseCustomArguments.Visible = txtArguments.Visible = false;
            cbVideoEncoder.Visible = false;
            lblVideoQuality.Visible = cbVideoQualityUseBitrate.Visible = tbVideoQuality.Visible = false;
            lblVideoQualityValue.Visible = lblVideoQualityLower.Visible = lblVideoQualityHigher.Visible = false;
            nudVideoQualityBitrate.Visible = lblVideoQualityBitrateHint.Visible = false;

            foreach (Control control in new Control[]
            {
                lblOutputFileName, txtOutputFileName, lblVideoEncoder, cbVideoEncoder,
                lblVideoQuality, cbVideoQualityUseBitrate, tbVideoQuality,
                lblVideoQualityValue, lblVideoQualityLower, lblVideoQualityHigher,
                nudVideoQualityBitrate, lblVideoQualityBitrateHint
            })
            {
                control.Top -= outputRowHeight;
            }

            lblFixedGifCodec = new Label
            {
                AutoSize = true,
                BackColor = System.Drawing.Color.Transparent,
                ForeColor = lblVideoEncoder.ForeColor,
                Font = cbVideoEncoder.Font,
                Left = cbVideoEncoder.Left,
                Top = lblVideoEncoder.Top,
                Text = "GIF"
            };
            Controls.Add(lblFixedGifCodec);

            int checkboxGap = Math.Max(12, (int)Math.Round(16 * DeviceDpi / 96f));
            cbAutoOpenFolder.Left = lblFixedGifCodec.Right + checkboxGap;
            cbAutoOpenFolder.Top = cbVideoEncoder.Top + (cbVideoEncoder.Height - cbAutoOpenFolder.Height) / 2;

            int buttonGap = Math.Max(8, (int)Math.Round(12 * DeviceDpi / 96f));
            int buttonTop = cbVideoEncoder.Bottom + buttonGap;
            btnEncode.Top = pbProgress.Top = buttonTop;
            btnEncode.Height /= 2;
            pbProgress.Height /= 2;
            SetCompactFont(btnEncode, btnEncode.Text);
            SetCompactFont(pbProgress, "100%");
            ReduceFontSize(btnEncode, 2f);
            ReduceFontSize(pbProgress, 1f);
            int progressRight = pbProgress.Right;
            int progressGap = pbProgress.Left - btnEncode.Right;
            btnEncode.Width /= 2;
            pbProgress.Left = btnEncode.Right + progressGap;
            pbProgress.Width = progressRight - pbProgress.Left;
            int bottomPadding = Math.Max(12, (int)Math.Round(16 * DeviceDpi / 96f));
            ClientSize = new System.Drawing.Size(ClientSize.Width, btnEncode.Bottom + bottomPadding);
        }

        private static void ReduceFontSize(Control control, float points)
        {
            System.Drawing.Font previous = control.Font;
            control.Font = new System.Drawing.Font(previous.FontFamily,
                Math.Max(4f, previous.SizeInPoints - points), previous.Style, System.Drawing.GraphicsUnit.Point);
            previous.Dispose();
        }

        private static void SetCompactFont(Control control, string sampleText)
        {
            System.Drawing.Font original = control.Font;
            for (float points = Math.Max(6f, original.SizeInPoints - 1f); points >= 6f; points -= 0.25f)
            {
                System.Drawing.Font candidate = new System.Drawing.Font(original.FontFamily, points, original.Style,
                    System.Drawing.GraphicsUnit.Point);
                if (TextRenderer.MeasureText(sampleText, candidate).Height <= control.Height - 2)
                {
                    control.Font = candidate;
                    return;
                }
                candidate.Dispose();
            }

            control.Font = new System.Drawing.Font(original.FontFamily, 6f, original.Style,
                System.Drawing.GraphicsUnit.Point);
        }

        public VideoConverterForm(string inputFilePath, string ffmpegFilePath, VideoConverterOptions options) : this(ffmpegFilePath, options)
        {
            txtInputFilePath.Text = inputFilePath;
            txtOutputFolder.Text = Path.GetDirectoryName(inputFilePath);
            txtOutputFileName.Text = Path.GetFileNameWithoutExtension(inputFilePath) + "-output";
        }

        private void UpdateOptions()
        {
            if (formReady)
            {
                Options.InputFilePath = txtInputFilePath.Text;
                Options.OutputFolderPath = simplifiedGifMode ? GetInputDirectory(Options.InputFilePath) : txtOutputFolder.Text;
                Options.OutputFileName = simplifiedGifMode && !string.IsNullOrWhiteSpace(txtOutputFileName.Text)
                    ? EnsureGifExtension(txtOutputFileName.Text) : txtOutputFileName.Text;
                Options.VideoCodec = simplifiedGifMode ? ConverterVideoCodecs.gif : (ConverterVideoCodecs)cbVideoEncoder.SelectedIndex;
                Options.VideoQualityUseBitrate = cbVideoQualityUseBitrate.Checked;
                Options.VideoQualityBitrate = (int)nudVideoQualityBitrate.Value;
                Options.UseCustomArguments = !simplifiedGifMode && cbUseCustomArguments.Checked;

                if (Options.UseCustomArguments)
                {
                    Options.CustomArguments = txtArguments.Text;
                }
            }

            switch (Options.VideoCodec)
            {
                case ConverterVideoCodecs.x264:
                case ConverterVideoCodecs.x265:
                case ConverterVideoCodecs.vp8:
                case ConverterVideoCodecs.vp9:
                case ConverterVideoCodecs.av1:
                case ConverterVideoCodecs.xvid:
                    cbVideoQualityUseBitrate.Visible = true;
                    tbVideoQuality.Visible = lblVideoQualityValue.Visible = lblVideoQualityLower.Visible = lblVideoQualityHigher.Visible = !Options.VideoQualityUseBitrate;
                    nudVideoQualityBitrate.Visible = lblVideoQualityBitrateHint.Visible = Options.VideoQualityUseBitrate;
                    break;
                case ConverterVideoCodecs.h264_nvenc:
                case ConverterVideoCodecs.hevc_nvenc:
                case ConverterVideoCodecs.h264_amf:
                case ConverterVideoCodecs.hevc_amf:
                case ConverterVideoCodecs.h264_qsv:
                case ConverterVideoCodecs.hevc_qsv:
                    cbVideoQualityUseBitrate.Visible = false;
                    tbVideoQuality.Visible = lblVideoQualityValue.Visible = lblVideoQualityLower.Visible = lblVideoQualityHigher.Visible = false;
                    nudVideoQualityBitrate.Visible = lblVideoQualityBitrateHint.Visible = true;
                    break;
                default:
                    cbVideoQualityUseBitrate.Visible = tbVideoQuality.Visible = lblVideoQualityValue.Visible = lblVideoQualityLower.Visible =
                        lblVideoQualityHigher.Visible = nudVideoQualityBitrate.Visible = lblVideoQualityBitrateHint.Visible = false;
                    break;
            }

            switch (Options.VideoCodec)
            {
                case ConverterVideoCodecs.x264:
                    tbVideoQuality.Minimum = FFmpegCLIManager.x264_min;
                    tbVideoQuality.Maximum = FFmpegCLIManager.x264_max;
                    break;
                case ConverterVideoCodecs.x265:
                    tbVideoQuality.Minimum = FFmpegCLIManager.x265_min;
                    tbVideoQuality.Maximum = FFmpegCLIManager.x265_max;
                    break;
                case ConverterVideoCodecs.vp8:
                    tbVideoQuality.Minimum = FFmpegCLIManager.vp8_min;
                    tbVideoQuality.Maximum = FFmpegCLIManager.vp8_max;
                    break;
                case ConverterVideoCodecs.vp9:
                    tbVideoQuality.Minimum = FFmpegCLIManager.vp9_min;
                    tbVideoQuality.Maximum = FFmpegCLIManager.vp9_max;
                    break;
                case ConverterVideoCodecs.av1:
                    tbVideoQuality.Minimum = FFmpegCLIManager.av1_min;
                    tbVideoQuality.Maximum = FFmpegCLIManager.av1_max;
                    break;
                case ConverterVideoCodecs.xvid:
                    tbVideoQuality.Minimum = FFmpegCLIManager.xvid_min;
                    tbVideoQuality.Maximum = FFmpegCLIManager.xvid_max;
                    break;
            }

            lblVideoQualityLower.Text = tbVideoQuality.Maximum + "   ← " + Resources.LowerQualitySize;
            lblVideoQualityHigher.Text = Resources.HigherQualitySize + " →   " + tbVideoQuality.Minimum;

            if (formReady)
            {
                Options.VideoQuality = tbVideoQuality.Minimum + tbVideoQuality.Maximum - tbVideoQuality.Value;
            }

            lblVideoQualityValue.Text = Options.VideoQuality.ToString();

            if (!Options.UseCustomArguments)
            {
                try
                {
                    txtArguments.Text = Options.GetFFmpegArgs();
                }
                catch
                {
                    txtArguments.Text = "";
                }
            }

            txtArguments.Enabled = Options.UseCustomArguments;

            btnEncode.Enabled = !string.IsNullOrEmpty(Options.InputFilePath) && !string.IsNullOrEmpty(Options.OutputFolderPath) &&
                !string.IsNullOrEmpty(Options.OutputFileName);
        }

        private static string GetInputDirectory(string filePath)
        {
            try
            {
                return string.IsNullOrWhiteSpace(filePath) ? null : Path.GetDirectoryName(filePath);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private void UpdateInputFilePathTextBox(string filePath)
        {
            txtInputFilePath.Text = filePath;

            if (string.IsNullOrEmpty(txtOutputFolder.Text))
            {
                txtOutputFolder.Text = Path.GetDirectoryName(filePath);
            }

            if (string.IsNullOrEmpty(txtOutputFileName.Text))
            {
                txtOutputFileName.Text = Path.GetFileNameWithoutExtension(filePath) + "-output";
            }
        }

        private bool StartEncoding()
        {
            if (simplifiedGifMode) return StartGifEncoding();
            bool result = false;

            if (File.Exists(FFmpegFilePath) && !string.IsNullOrEmpty(Options.InputFilePath) && File.Exists(Options.InputFilePath) &&
                !string.IsNullOrEmpty(Options.OutputFolderPath) && !string.IsNullOrEmpty(Options.OutputFileName))
            {
                using (ffmpeg = new FFmpegCLIManager(FFmpegFilePath))
                {
                    ffmpeg.ShowError = true;
                    ffmpeg.TrackEncodeProgress = true;
                    ffmpeg.EncodeProgressChanged += Manager_EncodeProgressChanged;

                    try
                    {
                        string outputFilePath = Options.OutputFilePath;
                        string args = Options.Arguments;
                        result = ffmpeg.Run(args);

                        if (Options.AutoOpenFolder && result && !ffmpeg.StopRequested)
                        {
                            FileHelpers.OpenFolderWithFile(outputFilePath);
                        }
                    }
                    catch (Exception e)
                    {
                        e.ShowError();
                    }
                }
            }

            return result;
        }

        private static string EnsureGifExtension(string name) =>
            name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? name : name + ".gif";

        private static string CommitGif(string staging, string desiredPath)
        {
            string folder = Path.GetDirectoryName(desiredPath);
            string stem = Path.GetFileNameWithoutExtension(desiredPath);
            for (int index = 0; ; index++)
            {
                string candidate = index == 0 ? desiredPath : Path.Combine(folder, $"{stem} ({index}).gif");
                try
                {
                    File.Move(staging, candidate, false);
                    return candidate;
                }
                catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
            }
        }

        private bool StartGifEncoding()
        {
            string staging = null;
            try
            {
                if (cancelEncoding || formClosing) return false;
                if (!File.Exists(FFmpegFilePath)) throw new FileNotFoundException("未找到视频编码工具。", FFmpegFilePath);
                if (!File.Exists(Options.InputFilePath)) throw new FileNotFoundException("输入视频不存在。", Options.InputFilePath);
                string name = EnsureGifExtension(Options.OutputFileName ?? "");
                if (string.IsNullOrWhiteSpace(Options.OutputFileName) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new ArgumentException("输出文件名无效，请勿填写路径或特殊符号。");
                string desired = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Options.InputFilePath)), name);
                staging = Path.Combine(Path.GetDirectoryName(desired), ".xiaomang-" + Guid.NewGuid().ToString("N") + ".gif");
                var job = new VideoConverterOptions
                {
                    InputFilePath = Options.InputFilePath,
                    OutputFolderPath = Path.GetDirectoryName(staging),
                    OutputFileName = Path.GetFileName(staging),
                    VideoCodec = ConverterVideoCodecs.gif
                };
                using (ffmpeg = new FFmpegCLIManager(FFmpegFilePath))
                {
                    ffmpeg.ShowError = false;
                    ffmpeg.TrackEncodeProgress = true;
                    ffmpeg.EncodeProgressChanged += Manager_EncodeProgressChanged;
                    if (cancelEncoding || formClosing) return false;
                    bool succeeded = ffmpeg.Run(job.Arguments);
                    if (ffmpeg.StopRequested || cancelEncoding || formClosing) return false;
                    if (!succeeded) throw new IOException("编码未完成，请检查输入视频是否损坏或格式是否受支持。");
                    if (!File.Exists(staging) || new FileInfo(staging).Length == 0) throw new IOException("未生成有效的 GIF 文件。");
                    string completed = CommitGif(staging, desired);
                    staging = null;
                    if (Options.AutoOpenFolder) FileHelpers.OpenFolderWithFile(completed);
                    return true;
                }
            }
            catch (Exception error)
            {
                if (!formClosing) this.InvokeSafe(() => MessageBox.Show(this, "转换失败：" + error.Message,
                    "视频转 GIF", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                return false;
            }
            finally
            {
                if (staging != null)
                {
                    try { File.Delete(staging); }
                    catch (Exception error) { DebugHelper.WriteException(error, "GIF temporary output cleanup failed."); }
                }
            }
        }

        private Task<bool> StartEncodingAsync()
        {
            return Task.Run(StartEncoding);
        }

        private void Manager_EncodeProgressChanged(float percentage)
        {
            if (!formClosing)
            {
                this.InvokeSafe(() => pbProgress.Value = (int)percentage);
            }
        }

        private void txtInputFilePath_TextChanged(object sender, EventArgs e)
        {
            if (simplifiedGifMode && formReady)
            {
                try
                {
                    string next = string.IsNullOrWhiteSpace(txtInputFilePath.Text) ? "" :
                        Path.GetFileNameWithoutExtension(txtInputFilePath.Text) + "-output";
                    if (string.IsNullOrWhiteSpace(txtOutputFileName.Text) || txtOutputFileName.Text == automaticOutputName)
                        txtOutputFileName.Text = next;
                    automaticOutputName = next;
                }
                catch (ArgumentException) { }
            }
            UpdateOptions();
        }

        private void btnInputFilePathBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                if (simplifiedGifMode && Directory.Exists(defaultInputDirectory))
                {
                    ofd.InitialDirectory = defaultInputDirectory;
                }

                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    string filePath = ofd.FileName;
                    UpdateInputFilePathTextBox(filePath);
                }
            }
        }

        private void txtOutputFolder_TextChanged(object sender, EventArgs e)
        {
            UpdateOptions();
        }

        private void btnOutputFolderBrowse_Click(object sender, EventArgs e)
        {
            FileHelpers.BrowseFolder(txtOutputFolder);
        }

        private void txtOutputFileName_TextChanged(object sender, EventArgs e)
        {
            UpdateOptions();
        }

        private void cbVideoEncoder_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateOptions();
        }

        private void cbVideoQualityUseBitrate_CheckedChanged(object sender, EventArgs e)
        {
            UpdateOptions();
        }

        private void tbVideoQuality_ValueChanged(object sender, EventArgs e)
        {
            UpdateOptions();
        }

        private void nudVideoQualityBitrate_ValueChanged(object sender, EventArgs e)
        {
            UpdateOptions();
        }

        private void cbAutoOpenFolder_CheckedChanged(object sender, EventArgs e)
        {
            Options.AutoOpenFolder = cbAutoOpenFolder.Checked;
        }

        private void cbUseCustomArguments_CheckedChanged(object sender, EventArgs e)
        {
            UpdateOptions();
        }

        private void txtArguments_TextChanged(object sender, EventArgs e)
        {
            UpdateOptions();
        }

        private async void btnEncode_Click(object sender, EventArgs e)
        {
            if (!encoding)
            {
                encoding = true;
                cancelEncoding = false;
                if (simplifiedGifMode)
                {
                    txtInputFilePath.Enabled = txtOutputFileName.Enabled = btnInputFilePathBrowse.Enabled = cbAutoOpenFolder.Enabled = false;
                    AllowDrop = false;
                }

                btnEncode.Text = Resources.StopEncoding;

                UpdateOptions();
                pbProgress.Value = 0;

                bool result = await StartEncodingAsync();

                if (!formClosing)
                {
                    if (result && ffmpeg != null && !ffmpeg.StopRequested)
                    {
                        pbProgress.Value = 100;
                    }
                    else
                    {
                        pbProgress.Value = 0;
                    }

                    btnEncode.Text = Resources.StartEncoding;
                    if (simplifiedGifMode)
                    {
                        txtInputFilePath.Enabled = txtOutputFileName.Enabled = btnInputFilePathBrowse.Enabled = cbAutoOpenFolder.Enabled = true;
                        AllowDrop = true;
                    }
                }

                encoding = false;
            }
            else
            {
                cancelEncoding = true;
                ffmpeg?.Close();
            }
        }

        private void VideoConverterForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void VideoConverterForm_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false) && e.Data.GetData(DataFormats.FileDrop, false) is string[] files && files.Length > 0)
            {
                string filePath = files[0];
                UpdateInputFilePathTextBox(filePath);
            }
        }

        private void VideoConverterForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            formClosing = true;
            cancelEncoding = true;

            if (ffmpeg != null)
            {
                ffmpeg.Close();
            }
        }
    }
}
