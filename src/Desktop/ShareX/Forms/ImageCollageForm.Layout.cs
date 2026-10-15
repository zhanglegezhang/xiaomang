using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed partial class ImageCollageForm
    {
        private static readonly Color CanvasColor = Color.FromArgb(28, 37, 44);
        private static readonly Color SurfaceColor = Color.FromArgb(38, 48, 57);
        private static readonly Color AccentColor = Color.FromArgb(255, 136, 62);
        private readonly Label listHeading = new Label { Text = "图片  0 张", Dock = DockStyle.Top, Height = 38, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
        private readonly ToolTip uiTips = new ToolTip();
        private Button vertical, horizontal, remove, moveUp, moveDown, backgroundButton;
        private Point dragOrigin;

        private void BuildLayout()
        {
            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 5, 8, 5), WrapContents = true, BackColor = SurfaceColor };
            Button add = ToolButton("＋ 添加图片", (_, _) => PickFiles());
            Accent(add); Accent(save);
            tools.Controls.Add(add);
            var orientation = Group();
            orientation.Controls.Add(Caption("拼接方向"));
            vertical = ToolButton("上下", (_, _) => direction.SelectedIndex = 0);
            horizontal = ToolButton("左右", (_, _) => direction.SelectedIndex = 1);
            orientation.Controls.Add(vertical); orientation.Controls.Add(horizontal);
            tools.Controls.Add(orientation);
            normalize.Margin = new Padding(10, 11, 8, 0);
            tools.Controls.Add(normalize);
            var spacing = Group();
            spacing.Controls.Add(Caption("间距"));
            gap.BackColor = CanvasColor; gap.ForeColor = ForeColor; gap.BorderStyle = BorderStyle.FixedSingle;
            gap.Margin = new Padding(4, 7, 2, 0); gap.Width = 62;
            spacing.Controls.Add(gap); spacing.Controls.Add(Caption("px"));
            tools.Controls.Add(spacing);
            backgroundButton = ToolButton("背景色  ■", (_, _) =>
            {
                using var dialog = new ColorDialog { Color = background };
                if (dialog.ShowDialog(this) == DialogResult.OK) { background = dialog.Color; backgroundButton.Invalidate(); Changed(); }
            });
            backgroundButton.Paint += (_, e) =>
            {
                int size = ScalePx(12);
                var rect = new Rectangle(backgroundButton.ClientSize.Width - ScalePx(22), (backgroundButton.ClientSize.Height - size) / 2, size, size);
                using var fill = new SolidBrush(background); e.Graphics.FillRectangle(fill, rect);
                e.Graphics.DrawRectangle(Pens.Gray, rect);
            };
            tools.Controls.Add(backgroundButton);
            var output = Group(); output.Margin = new Padding(12, 0, 0, 0);
            output.Controls.Add(copy); output.Controls.Add(save); tools.Controls.Add(output);
            tools.Layout += (_, _) =>
            {
                int used = tools.Controls.Cast<Control>().Where(c => c != output).Sum(c => c.Width + c.Margin.Horizontal);
                int left = Math.Max(ScalePx(12), tools.ClientSize.Width - tools.Padding.Horizontal - used - output.Width);
                if (output.Margin.Left != left) output.Margin = new Padding(left, 0, 0, 0);
            };

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 2, BackColor = Color.FromArgb(53, 68, 80), Size = new Size(1000, 600), SplitterDistance = 260, Panel1MinSize = 220, Panel2MinSize = 240 };
            split.Panel1.BackColor = CanvasColor; split.Panel2.BackColor = CanvasColor;
            files.BackColor = CanvasColor; files.ForeColor = ForeColor; files.BorderStyle = BorderStyle.None;
            files.ItemHeight = 86;
            files.DrawItem += DrawFile;
            files.SelectedIndexChanged += (_, _) => UpdateListState();
            var listFooter = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(7), WrapContents = true };
            remove = ToolButton("移除", (_, _) => RemoveSelected());
            moveUp = ToolButton("↑ 上移", (_, _) => MoveSelected(-1));
            moveDown = ToolButton("↓ 下移", (_, _) => MoveSelected(1));
            listFooter.Controls.AddRange(new Control[] { remove, moveUp, moveDown });
            var dropHint = new Label { Text = "拖入添加 · 拖动排序", AutoSize = true, Margin = new Padding(5, 8, 0, 5), ForeColor = Color.FromArgb(156, 174, 187) };
            listFooter.SetFlowBreak(moveDown, true); listFooter.Controls.Add(dropHint);
            split.Panel1.Controls.Add(files); split.Panel1.Controls.Add(listFooter); split.Panel1.Controls.Add(listHeading);
            preview.BackColor = CanvasColor;
            var pattern = new Bitmap(24, 24);
            using (Graphics g = Graphics.FromImage(pattern))
            {
                g.Clear(CanvasColor); using var brush = new SolidBrush(Color.FromArgb(33, 44, 53));
                g.FillRectangle(brush, 0, 0, 12, 12); g.FillRectangle(brush, 12, 12, 12, 12);
            }
            preview.BackgroundImage = pattern; preview.BackgroundImageLayout = ImageLayout.Tile;
            preview.Paint += (_, e) =>
            {
                if (preview.Image == null)
                    TextRenderer.DrawText(e.Graphics, working ? "正在生成预览…" : "添加图片，开始拼图", Font, preview.ClientRectangle,
                        Color.FromArgb(153, 173, 188), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            var canvas = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = CanvasColor };
            canvas.Controls.Add(preview); split.Panel2.Controls.Add(canvas);
            foreach (Control target in new Control[] { preview, canvas, dropHint })
            { target.AllowDrop = true; target.DragEnter += FileDragEnter; target.DragDrop += FileDragDrop; }
            status.BackColor = SurfaceColor; status.ForeColor = Color.FromArgb(188, 204, 215);
            status.Padding = new Padding(12, 0, 8, 0); status.AutoEllipsis = true;
            status.TextChanged += (_, _) => uiTips.SetToolTip(status, status.Text);
            uiTips.SetToolTip(copy, "复制完整尺寸拼图"); uiTips.SetToolTip(save, "保存完整尺寸 PNG，不改变原图");
            Controls.Add(split); Controls.Add(status); Controls.Add(tools);
            Shown += (_, _) =>
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(Math.Min(ScalePx(760), area.Width), Math.Min(ScalePx(480), area.Height));
                Size = new Size(Math.Min(ScalePx(1100), area.Width), Math.Min(ScalePx(760), area.Height));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                files.ItemHeight = ScalePx(86);
                split.SplitterDistance = Math.Min(ScalePx(280), split.Width - split.Panel2MinSize - split.SplitterWidth);
            };
            DpiChanged += (_, _) => { files.ItemHeight = ScalePx(86); MinimumSize = new Size(ScalePx(760), ScalePx(480)); files.Invalidate(); };
            UpdateDirection(); UpdateListState();
        }

        private FlowLayoutPanel Group() => new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        private Label Caption(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(8, 11, 4, 0) };
        private int ScalePx(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        private void Accent(Button button) { button.BackColor = AccentColor; button.ForeColor = Color.FromArgb(25, 33, 39); button.FlatAppearance.BorderColor = AccentColor; button.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 157, 94); }
        private void UpdateDirection()
        {
            foreach (Button button in new[] { vertical, horizontal })
            {
                bool selected = button == (direction.SelectedIndex == 0 ? vertical : horizontal);
                button.BackColor = selected ? Color.FromArgb(90, 58, 38) : SurfaceColor;
                button.ForeColor = selected ? AccentColor : ForeColor;
                button.FlatAppearance.BorderColor = selected ? AccentColor : Color.FromArgb(65, 82, 94);
                button.AccessibleDescription = selected ? "已选中" : "未选中";
            }
        }
        private void UpdateListState()
        {
            listHeading.Text = $"图片  {files.Items.Count} 张";
            if (remove == null) return;
            int index = files.SelectedIndex;
            remove.Enabled = index >= 0; moveUp.Enabled = index > 0; moveDown.Enabled = index >= 0 && index < files.Items.Count - 1;
            files.Invalidate();
        }
        private void RemoveSelected()
        {
            int index = files.SelectedIndex;
            if (index < 0) return;
            ((Item)files.Items[index]).Thumbnail?.Dispose(); files.Items.RemoveAt(index);
            if (files.Items.Count > 0) files.SelectedIndex = Math.Min(index, files.Items.Count - 1);
            Changed();
        }
        private void DrawFile(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= files.Items.Count) return;
            Item item = (Item)files.Items[e.Index]; bool selected = (e.State & DrawItemState.Selected) != 0;
            using var fill = new SolidBrush(selected ? SurfaceColor : CanvasColor); e.Graphics.FillRectangle(fill, e.Bounds);
            var bounds = Rectangle.Inflate(e.Bounds, -ScalePx(7), -ScalePx(4));
            if (selected) { using var pen = new Pen(AccentColor, Math.Max(1, ScalePx(1))); e.Graphics.DrawRectangle(pen, bounds); }
            TextRenderer.DrawText(e.Graphics, (e.Index + 1).ToString("00"), Font, new Rectangle(bounds.X + ScalePx(5), bounds.Y, ScalePx(25), bounds.Height), Color.FromArgb(172, 191, 204), TextFormatFlags.VerticalCenter);
            var imageBox = new Rectangle(bounds.X + ScalePx(31), bounds.Y + ScalePx(11), ScalePx(76), bounds.Height - ScalePx(22));
            if (item.Thumbnail != null)
            {
                double factor = Math.Min((double)imageBox.Width / item.Thumbnail.Width, (double)imageBox.Height / item.Thumbnail.Height);
                var size = new Size(Math.Max(1, (int)(item.Thumbnail.Width * factor)), Math.Max(1, (int)(item.Thumbnail.Height * factor)));
                e.Graphics.DrawImage(item.Thumbnail, new Rectangle(imageBox.X + (imageBox.Width - size.Width) / 2, imageBox.Y + (imageBox.Height - size.Height) / 2, size.Width, size.Height));
            }
            else { using var pen = new Pen(Color.SlateGray); e.Graphics.DrawRectangle(pen, imageBox); }
            int x = imageBox.Right + ScalePx(10), width = Math.Max(1, bounds.Right - x - ScalePx(5));
            TextRenderer.DrawText(e.Graphics, item.ToString(), Font, new Rectangle(x, bounds.Y + ScalePx(14), width, ScalePx(24)), ForeColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            string dimensions = item.ImageSize.IsEmpty ? "等待预览" : $"{item.ImageSize.Width} × {item.ImageSize.Height}";
            TextRenderer.DrawText(e.Graphics, dimensions, Font, new Rectangle(x, bounds.Y + ScalePx(42), width, ScalePx(22)), Color.FromArgb(156, 174, 187), TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        }

        private async Task<(string Path, string Error)[]> ValidateAndLoadThumbnails(string[] paths, int version, CancellationToken token)
        {
            var thumbnails = new List<(string Path, Bitmap Image, Size Size)>();
            try
            {
                var invalid = await Task.Run(() =>
                {
                    var errors = new List<(string Path, string Error)>();
                    foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            using var stream = File.OpenRead(path);
                            if (stream.Length > 100 * 1024 * 1024) { errors.Add((path, "文件超过 100 MB")); continue; }
                            using Bitmap source = ImageCollageRenderer.OpenOrientedImage(stream);
                            if ((long)source.Width * source.Height > ImageCollageRenderer.MaximumPixels) { errors.Add((path, "图片超过 4000 万像素")); continue; }
                            double factor = Math.Min(1d, Math.Min(152d / source.Width, 100d / source.Height));
                            var thumbnail = new Bitmap(Math.Max(1, (int)(source.Width * factor)), Math.Max(1, (int)(source.Height * factor)));
                            try { using Graphics g = Graphics.FromImage(thumbnail); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(source, new Rectangle(Point.Empty, thumbnail.Size)); }
                            catch { thumbnail.Dispose(); throw; }
                            thumbnails.Add((path, thumbnail, source.Size));
                        }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OutOfMemoryException || ex is System.Runtime.InteropServices.ExternalException)
                        { errors.Add((path, "图片无法读取、已损坏或正在被占用")); }
                    }
                    return errors.ToArray();
                }, token);
                if (!closing && !IsDisposed && version == revision)
                {
                    foreach (Item item in files.Items)
                    {
                        var found = thumbnails.FirstOrDefault(t => string.Equals(t.Path, item.Path, StringComparison.OrdinalIgnoreCase));
                        if (found.Image == null) continue;
                        item.Thumbnail?.Dispose(); item.Thumbnail = (Bitmap)found.Image.Clone(); item.ImageSize = found.Size;
                    }
                    files.Invalidate();
                }
                return invalid;
            }
            finally { foreach (var thumbnail in thumbnails) thumbnail.Image.Dispose(); }
        }
    }
}
