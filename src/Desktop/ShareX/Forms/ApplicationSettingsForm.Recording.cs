using System;
using System.Windows.Forms;
using ShareX.ScreenCaptureLib;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private static void DesignRecordingOptions(SettingsDesignSurface design, Func<FFmpegOptions> getOptions, Button advanced)
        {
            var audio = new RecordingChoice { Name = "recordingAudioSource", DropDownStyle = ComboBoxStyle.DropDownList };
            audio.Items.AddRange(new object[] { FFmpegCaptureDevice.None, FFmpegCaptureDevice.SystemAndMicrophone });
            var codec = new RecordingChoice { Name = "recordingVideoCodec", DropDownStyle = ComboBoxStyle.DropDownList };
            codec.Items.AddRange(new object[] { "H.264 / x264", "H.265 / x265", "GIF" });
            var codecs = new[] { FFmpegVideoCodec.libx264, FFmpegVideoCodec.libx265, FFmpegVideoCodec.gif };
            bool refreshing = false;
            void RefreshChoices()
            {
                refreshing = true;
                try
                {
                    var options = getOptions();
                    audio.SelectedIndex = options.AudioSource == FFmpegCaptureDevice.None.Value ? 0 :
                        options.AudioSource == FFmpegCaptureDevice.SystemAndMicrophone.Value ? 1 : -1;
                    codec.SelectedIndex = Array.IndexOf(codecs, options.VideoCodec);
                    audio.Enabled = !options.IsAnimatedImage;
                    // Unsupported advanced choices remain intact; a blank selection does not rewrite them.
                }
                finally { refreshing = false; }
            }
            audio.SelectedIndexChanged += (_, _) =>
            {
                if (!refreshing && audio.SelectedItem is FFmpegCaptureDevice device) getOptions().AudioSource = device.Value;
            };
            codec.SelectedIndexChanged += (_, _) =>
            {
                if (!refreshing && codec.SelectedIndex >= 0)
                {
                    getOptions().VideoCodec = codecs[codec.SelectedIndex];
                    RefreshChoices();
                }
            };
            // The existing Click handler completes the modal dialog and assigns its possibly reset options first.
            advanced.Click += (_, _) => RefreshChoices();
            design.VisibleChanged += (_, _) => { if (design.Visible) RefreshChoices(); };
            design.Section("编码与声音");
            int ContentWidth(ComboBox choice)
            {
                choice.Font = design.Font;
                int longest = 0;
                foreach (var item in choice.Items)
                    longest = Math.Max(longest, TextRenderer.MeasureText(choice.GetItemText(item), choice.Font).Width);
                return (int)Math.Ceiling(longest * 96d / design.DeviceDpi) + 40;
            }
            void AddChoice(string title, ComboBox choice)
            {
                var row = design.Editor(title, choice, width: ContentWidth(choice));
                row.Layout += (_, _) =>
                {
                    var frame = choice.Parent?.Parent;
                    foreach (Control child in row.Controls)
                        if (child is Label label && frame != null && frame.Left > 0)
                        {
                            label.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
                            label.SetBounds(label.Left, frame.Top, label.Width, frame.Height);
                        }
                };
            }
            AddChoice("音频源", audio);
            AddChoice("视频编码器", codec);
            design.Action("高级选项", advanced, "更多参数…");
            RefreshChoices();
        }

        private sealed class RecordingChoice : ComboBox
        {
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (e is HandledMouseEventArgs handled) handled.Handled = true;
            }
        }
    }
}
