using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed record CollageSettings(bool Horizontal, bool Normalize, int Gap, Color Background);

    internal static class ImageCollageRenderer
    {
        internal sealed class SourceChangedException : IOException
        {
            internal SourceChangedException() : base("原图已变化，请检查更新后的预览，再复制或保存。") { }
        }
        internal sealed record RenderResult(Bitmap Image, Size FullSize, string[] Versions);
        internal static Bitmap OpenOrientedImage(Stream stream)
        {
            Bitmap image = new Bitmap(stream);
            try { ImageHelpers.RotateImageByExifOrientationData(image); return image; }
            catch { image.Dispose(); throw; }
        }
        internal static string ValidateImage(string path)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                if (stream.Length > 100 * 1024 * 1024) return "文件超过 100 MB";
                using Bitmap image = OpenOrientedImage(stream);
                if ((long)image.Width * image.Height > MaximumPixels) return "图片超过 4000 万像素";
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OutOfMemoryException || ex is System.Runtime.InteropServices.ExternalException)
            { return "不是可读取的图片或文件已损坏"; }
            catch (IOException) { return "文件不存在或正在被占用"; }
            catch (UnauthorizedAccessException) { return "无权读取文件"; }
        }
        internal const int MaximumImages = 50;
        internal const long MaximumPixels = 40_000_000;
        internal sealed record Layout(Size[] Images, Size Output);

        private sealed class NormalizedImageTooLongException : ArgumentException
        {
            internal (int Index, long Length)[] Images { get; }

            internal NormalizedImageTooLongException((int Index, long Length)[] images)
                : base("统一尺寸后图片过长。")
            {
                Images = images;
            }
        }

        internal static Layout Measure(IReadOnlyList<Size> sizes, CollageSettings settings)
        {
            if (sizes.Count < 2 || sizes.Count > MaximumImages)
                throw new ArgumentException("请选择 2～50 张图片。允许输出最多 4000 万像素。 ");
            if (settings.Gap < 0 || settings.Gap > 200) throw new ArgumentException("间距须为 0～200 px。");
            if (sizes.Any(s => s.Width <= 0 || s.Height <= 0 || (long)s.Width * s.Height > MaximumPixels))
                throw new ArgumentException("单张图片不得超过 4000 万像素。");
            int cross = sizes.Max(s => settings.Horizontal ? s.Height : s.Width);
            if (cross > 32767) throw new ArgumentException("拼图单边不得超过 32767 px。");
            var scaled = new Size[sizes.Count];
            var oversized = new List<(int Index, long Length)>();
            for (int index = 0; index < sizes.Count; index++)
            {
                Size s = sizes[index];
                if (!settings.Normalize)
                {
                    scaled[index] = s;
                    continue;
                }
                double scaledLength = settings.Horizontal ? (double)s.Width * cross / s.Height : (double)s.Height * cross / s.Width;
                if (scaledLength > 32767)
                {
                    oversized.Add((index, (long)Math.Ceiling(scaledLength)));
                    continue;
                }
                int length = Math.Max(1, (int)Math.Round(scaledLength));
                scaled[index] = settings.Horizontal ? new Size(length, cross) : new Size(cross, length);
            }
            if (oversized.Count > 0) throw new NormalizedImageTooLongException(oversized.ToArray());
            long along = scaled.Sum(s => (long)(settings.Horizontal ? s.Width : s.Height)) + (long)settings.Gap * (sizes.Count - 1);
            if (along > 32767 || cross > 32767 || along * cross > MaximumPixels)
                throw new ArgumentException("拼图过大：输出最多 4000 万像素，单边最多 32767 px。请减少图片或关闭统一尺寸。");
            return new Layout(scaled, settings.Horizontal ? new Size((int)along, cross) : new Size(cross, (int)along));
        }

        internal static (Bitmap Image, Size FullSize) Render(string[] paths, CollageSettings settings, bool preview, CancellationToken token)
        {
            RenderResult result = RenderWithVersions(paths, settings, preview, token, null);
            return (result.Image, result.FullSize);
        }

        internal static RenderResult RenderWithVersions(string[] paths, CollageSettings settings, bool preview,
            CancellationToken token, string[] expectedVersions)
        {
            if (paths.Length < 2 || paths.Length > MaximumImages) throw new ArgumentException("请添加 2～50 张图片。");
            if (expectedVersions != null && expectedVersions.Length != paths.Length) throw new SourceChangedException();
            var streams = new List<FileStream>();
            try
            {
            var versions = new List<string>();
            var sizes = new List<Size>();
            for (int index = 0; index < paths.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                // Keep each input read-locked from hashing through rendering, preventing a mid-export rewrite.
                FileStream stream = new FileStream(paths[index], FileMode.Open, FileAccess.Read, FileShare.Read);
                streams.Add(stream);
                if (stream.Length > 100 * 1024 * 1024) throw new ArgumentException("单个文件超过 100 MB：" + Path.GetFileName(paths[index]));
                string hash = Convert.ToHexString(SHA256.HashData(stream));
                versions.Add(hash);
                if (expectedVersions != null && expectedVersions[index] != hash) throw new SourceChangedException();
                stream.Position = 0;
                using Image source = OpenOrientedImage(stream);
                sizes.Add(source.Size);
            }
            Layout layout;
            try
            {
                layout = Measure(sizes, settings);
            }
            catch (NormalizedImageTooLongException ex)
            {
                string details = string.Join("；", ex.Images.Select(image =>
                    $"{Path.GetFileName(paths[image.Index])}（预计 {image.Length:N0} px）"));
                throw new ArgumentException($"统一尺寸后以下图片长度超过 32767 px：{details}。请关闭统一尺寸或移除这些图片。");
            }
            double factor = preview ? Math.Min(1d, 1400d / Math.Max(layout.Output.Width, layout.Output.Height)) : 1;
            // Round cumulative coordinates on one canvas, not the dimensions of each image.
            // Otherwise many subpixel-height images artificially enlarge the preview.
            Size canvas = new Size(Math.Max(1, (int)Math.Round(layout.Output.Width * factor)),
                Math.Max(1, (int)Math.Round(layout.Output.Height * factor)));
            Bitmap result = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppArgb);
            try
            {
                using Graphics g = Graphics.FromImage(result);
                g.Clear(settings.Background);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using ImageAttributes attributes = new ImageAttributes();
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                int along = 0;
                for (int i = 0; i < paths.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    streams[i].Position = 0;
                    using Image source = OpenOrientedImage(streams[i]);
                    if (source.Size != sizes[i]) throw new IOException("图片在处理过程中发生变化，请重新预览。");
                    Size size = layout.Images[i];
                    int x = settings.Horizontal ? along : (layout.Output.Width - size.Width) / 2;
                    int y = settings.Horizontal ? (layout.Output.Height - size.Height) / 2 : along;
                    Rectangle destination = Rectangle.FromLTRB((int)Math.Round(x * factor), (int)Math.Round(y * factor),
                        (int)Math.Round((x + size.Width) * factor), (int)Math.Round((y + size.Height) * factor));
                    if (destination.Width > 0 && destination.Height > 0)
                        g.DrawImage(source, destination, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                    along += (settings.Horizontal ? size.Width : size.Height) + settings.Gap;
                }
                token.ThrowIfCancellationRequested();
                return new RenderResult(result, layout.Output, versions.ToArray());
            }
            catch { result.Dispose(); throw; }
            }
            finally { foreach (FileStream stream in streams) stream.Dispose(); }
        }
    }

    internal sealed partial class ImageCollageForm : Form
    {
        private sealed class Item
        {
            internal string Path { get; }
            internal Bitmap Thumbnail { get; set; }
            internal Size ImageSize { get; set; }
            internal Item(string path) { Path = path; }
            public override string ToString() => System.IO.Path.GetFileName(Path);
        }
        private readonly ListBox files = new ListBox { Dock = DockStyle.Fill, AllowDrop = true, SelectionMode = SelectionMode.One, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
        private readonly PictureBox preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(32, 32, 32) };
        private readonly ComboBox direction = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 88, BackColor = Color.FromArgb(58, 58, 58), ForeColor = Color.WhiteSmoke };
        private readonly CheckBox normalize = new CheckBox { Text = "统一宽度", Checked = true, AutoSize = true, Margin = new Padding(8, 7, 6, 0) };
        private readonly NumericUpDown gap = new NumericUpDown { Minimum = 0, Maximum = 200, Width = 60 };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 34, TextAlign = ContentAlignment.MiddleLeft, Text = "拖入图片开始拼图（2～50 张）" };
        private readonly Button copy, save;
        private readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 220 };
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private Color background = Color.White;
        private int revision, dragIndex = -1;
        private bool working, exporting, pending, closing, resourcesDisposed;
        private string[] previewVersions;
        private string inputNotice = "";

        internal ImageCollageForm(IEnumerable<string> initialFiles = null)
        {
            Text = "小蟒同学 · 拼图"; Icon = ShareXResources.Icon;
            Font = new Font("Microsoft YaHei UI", 9f); BackColor = CanvasColor; ForeColor = Color.FromArgb(231, 237, 242);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; MinimumSize = new Size(760, 480); Size = new Size(1100, 760);
            StartPosition = FormStartPosition.CenterScreen; AllowDrop = true;
            HandleCreated += (_, _) => NativeMethods.UseImmersiveDarkMode(Handle, true);
            direction.Items.AddRange(new object[] { "上下", "左右" }); direction.SelectedIndex = 0;
            copy = ToolButton("复制", async (_, _) => await Export(false)); save = ToolButton("保存 PNG", async (_, _) => await Export(true));
            BuildLayout();
            direction.SelectedIndexChanged += (_, _) => { normalize.Text = direction.SelectedIndex == 0 ? "统一宽度" : "统一高度"; UpdateDirection(); Changed(); };
            normalize.CheckedChanged += (_, _) => Changed(); gap.ValueChanged += (_, _) => Changed();
            debounce.Tick += async (_, _) => { debounce.Stop(); await RefreshPreview(); };
            DragEnter += FileDragEnter; DragDrop += FileDragDrop;
            files.DragEnter += (s, e) => { if (e.Data.GetDataPresent(typeof(Item))) e.Effect = DragDropEffects.Move; else FileDragEnter(s, e); };
            files.DragDrop += (s, e) =>
            {
                if (e.Data.GetData(typeof(Item)) is Item item && files.Items.Contains(item))
                {
                    int target = files.IndexFromPoint(files.PointToClient(new Point(e.X, e.Y)));
                    if (target < 0) target = files.Items.Count - 1;
                    files.Items.Remove(item); files.Items.Insert(Math.Min(target, files.Items.Count), item); files.SelectedItem = item; Changed();
                }
                else FileDragDrop(s, e);
            };
            files.MouseDown += (_, e) => { dragIndex = files.IndexFromPoint(e.Location); dragOrigin = e.Location; };
            files.PreviewKeyDown += (_, e) => {
                if (e.KeyData == Keys.Delete || e.KeyData == (Keys.Alt | Keys.Up) || e.KeyData == (Keys.Alt | Keys.Down)) e.IsInputKey = true;
            };
            files.KeyDown += (_, e) => {
                if (exporting || working) return;
                if (e.KeyData == Keys.Delete) { e.SuppressKeyPress = true; RemoveSelected(); }
                else if (e.KeyData == (Keys.Alt | Keys.Up) || e.KeyData == (Keys.Alt | Keys.Down)) {
                    e.SuppressKeyPress = true; MoveSelected(e.KeyCode == Keys.Up ? -1 : 1);
                }
            };
            files.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left && dragIndex >= 0 && dragIndex < files.Items.Count && !new Rectangle(dragOrigin.X - SystemInformation.DragSize.Width / 2, dragOrigin.Y - SystemInformation.DragSize.Height / 2, SystemInformation.DragSize.Width, SystemInformation.DragSize.Height).Contains(e.Location)) { Item item = (Item)files.Items[dragIndex]; dragIndex = -1; files.DoDragDrop(item, DragDropEffects.Move); } };
            FormClosing += (_, e) => { if (exporting) { e.Cancel = true; status.Text = "正在输出图片，请等待完成。"; } };
            FormClosed += (_, _) => { closing = true; cancellation.Cancel(); debounce.Stop(); };
            if (initialFiles != null) AddFiles(initialFiles);
            copy.Enabled = save.Enabled = false;
        }

        internal void ShowForUser()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            // SW_HIDE supplied by a launcher can disagree with WinForms.Visible.
            if (!NativeMethods.IsWindowVisible(Handle))
                NativeMethods.ShowWindow(Handle, (int)WindowShowStyle.Show);
            Activate();
            BringToFront();
            NativeMethods.SetForegroundWindow(Handle);
        }

        private Button ToolButton(string text, EventHandler click)
        {
            Button button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(60, 34), Padding = new Padding(8, 3, 8, 3), Margin = new Padding(3), FlatStyle = FlatStyle.Flat, BackColor = SurfaceColor, ForeColor = ForeColor, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(65, 82, 94); button.FlatAppearance.MouseOverBackColor = Color.FromArgb(53, 70, 82); button.Click += click; return button;
        }
        internal void AddFiles(IEnumerable<string> paths)
        {
            var rejected = new List<string>();
            foreach (string path in paths)
            {
                if (files.Items.Count >= ImageCollageRenderer.MaximumImages) { rejected.Add("最多添加 50 张图片"); break; }
                if (!File.Exists(path) || !new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }
                    .Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                { rejected.Add(System.IO.Path.GetFileName(path) + "（不支持的图片或文件不存在）"); continue; }
                files.Items.Add(new Item(System.IO.Path.GetFullPath(path)));
            }
            inputNotice = rejected.Count == 0 ? "" : "已跳过：" + string.Join("；", rejected) + "。 ";
            if (files.SelectedIndex < 0 && files.Items.Count > 0) files.SelectedIndex = 0;
            Changed();
        }
        private void PickFiles() { using OpenFileDialog d = new OpenFileDialog { Multiselect = true, Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", InitialDirectory = Program.ScreenshotsParentFolder }; if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames); }
        private void FileDragEnter(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
        private void FileDragDrop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths); }
        private void MoveSelected(int delta) { int old = files.SelectedIndex, next = old + delta; if (old < 0 || next < 0 || next >= files.Items.Count) return; object item = files.Items[old]; files.Items.RemoveAt(old); files.Items.Insert(next, item); files.SelectedIndex = next; Changed(); }
        private CollageSettings Settings() => new CollageSettings(direction.SelectedIndex == 1, normalize.Checked, (int)gap.Value, background);
        private string[] Paths() => files.Items.Cast<Item>().Select(i => i.Path).ToArray();
        private void Changed()
        {
            UpdateListState();
            revision++; pending = true; copy.Enabled = save.Enabled = false;
            previewVersions = null;
            Image old = preview.Image; preview.Image = null; old?.Dispose();
            cancellation.Cancel(); debounce.Stop(); debounce.Start();
        }
        private async Task RefreshPreview()
        {
            if (closing || working || exporting) return;
            pending = false;
            if (files.Items.Count == 0) { status.Text = inputNotice + "请添加 2～50 张图片，可拖动列表调整顺序。"; return; }
            int version = revision; string[] paths = Paths(); CollageSettings settings = Settings();
            cancellation.Dispose(); cancellation = new CancellationTokenSource(); CancellationToken token = cancellation.Token;
            working = true; status.Text = "正在生成预览…";
            try
            {
                var invalid = await ValidateAndLoadThumbnails(paths, version, token);
                if (closing || IsDisposed || version != revision) return;
                if (invalid.Length > 0)
                {
                    foreach (Item item in files.Items.Cast<Item>().ToArray())
                        if (invalid.Any(bad => string.Equals(bad.Path, item.Path, StringComparison.OrdinalIgnoreCase))) { item.Thumbnail?.Dispose(); files.Items.Remove(item); }
                    inputNotice += "已跳过：" + string.Join("；", invalid.Select(bad => System.IO.Path.GetFileName(bad.Path) + "（" + bad.Error + "）")) + "。 ";
                    Changed(); return;
                }
                if (paths.Length < 2) { status.Text = inputNotice + "已添加 1 张图片，请再添加至少 1 张。"; return; }
                var result = await Task.Run(() => ImageCollageRenderer.RenderWithVersions(paths, settings, true, token, null));
                if (closing || IsDisposed || version != revision) { result.Image.Dispose(); return; }
                Image old = preview.Image; preview.Image = result.Image; old?.Dispose();
                previewVersions = result.Versions;
                status.Text = inputNotice + $"{paths.Length} 张图片 · 输出 {result.FullSize.Width} × {result.FullSize.Height} px · 预览已缩放，复制和保存使用完整尺寸";
                copy.Enabled = save.Enabled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closing && version == revision) status.Text = "无法拼图：" + ex.Message; }
            finally { working = false; if (!closing && pending) debounce.Start(); }
        }
        private async Task Export(bool toFile)
        {
            if (working || exporting || preview.Image == null) return;
            string[] paths = Paths(); CollageSettings settings = Settings(); string destination = null;
            string[] expectedVersions = previewVersions;
            if (toFile)
            {
                using SaveFileDialog d = new SaveFileDialog { Filter = "PNG 图片|*.png", DefaultExt = "png", AddExtension = true, FileName = "拼图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", InitialDirectory = System.IO.Path.GetDirectoryName(paths[0]), OverwritePrompt = true };
                if (d.ShowDialog(this) != DialogResult.OK) return;
                destination = System.IO.Path.GetFullPath(d.FileName);
                if (paths.Contains(destination, StringComparer.OrdinalIgnoreCase)) { MessageBox.Show(this, "请选择新的文件名，避免覆盖参与拼图的原图。", "拼图"); return; }
            }
            exporting = true; copy.Enabled = save.Enabled = false; status.Text = "正在生成完整尺寸图片…";
            try
            {
                var result = await Task.Run(() => ImageCollageRenderer.RenderWithVersions(paths, settings, false, CancellationToken.None, expectedVersions));
                using (result.Image)
                {
                    if (destination == null)
                    {
                        if (!ClipboardHelpers.CopyImage(result.Image)) throw new IOException("剪贴板暂时被其他程序占用，请重试。");
                        status.Text = "已复制完整尺寸拼图。";
                    }
                    else
                    {
                        string target = destination;
                        await Task.Run(() =>
                        {
                            string staging = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            try { result.Image.Save(staging, ImageFormat.Png); File.Move(staging, target, true); }
                            finally { if (File.Exists(staging)) File.Delete(staging); }
                        });
                        status.Text = "已保存：" + destination;
                    }
                }
            }
            catch (ImageCollageRenderer.SourceChangedException ex) { inputNotice = ex.Message + " "; Changed(); status.Text = inputNotice; }
            catch (Exception ex) { status.Text = "输出失败：" + ex.Message; }
            finally { exporting = false; copy.Enabled = save.Enabled = preview.Image != null; if (pending) debounce.Start(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !resourcesDisposed) { resourcesDisposed = true; closing = true; cancellation.Cancel(); cancellation.Dispose(); debounce.Dispose(); Image old = preview.Image; preview.Image = null; old?.Dispose(); foreach (Item item in files.Items) item.Thumbnail?.Dispose(); preview.BackgroundImage?.Dispose(); uiTips.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
