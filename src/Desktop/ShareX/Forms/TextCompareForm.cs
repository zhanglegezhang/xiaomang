using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Wpf = System.Windows;
using Media = System.Windows.Media;

namespace ShareX
{
    internal static class LocalTextDiff
    {
        internal const int MaximumCharacters = 1_000_000;
        internal const int MaximumLines = 20_000;
        // Compare long changed lines as a unit, avoiding a costly per-character edit graph.
        private sealed class BoundedCharacterChunker : IChunker
        {
            public IReadOnlyList<string> Chunk(string text) => text.Length <= 1024
                ? System.Globalization.StringInfo.GetTextElementEnumerator(text).AsElements()
                : new[] { text };
        }
        internal static SideBySideDiffModel Compare(string left, string right, bool ignoreWhitespace, bool ignoreCase)
        {
            left = left.Replace("\r\n", "\n").Replace('\r', '\n');
            right = right.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (string text in new[] { left, right })
            {
                if (text.Length > MaximumCharacters || text.Count(c => c == '\n') + 1 > MaximumLines)
                    throw new ArgumentException("对比上限为每侧 100 万字符、2 万行，请缩小内容范围。");
            }
            return new SideBySideDiffBuilder(new Differ(), new LineChunker(), new BoundedCharacterChunker())
                .BuildDiffModel(left, right, ignoreWhitespace, ignoreCase);
        }
        private static string[] AsElements(this System.Globalization.TextElementEnumerator iterator)
        {
            var elements = new List<string>(); while (iterator.MoveNext()) elements.Add(iterator.GetTextElement()); return elements.ToArray();
        }
    }

    internal sealed class TextCompareForm : Form
    {
        private sealed class DifferenceColorizer : DocumentColorizingTransformer
        {
            internal Dictionary<int, DiffPiece> Lines = new Dictionary<int, DiffPiece>();
            private static Media.Brush Brush(byte r, byte g, byte b) { var brush = new Media.SolidColorBrush(Media.Color.FromRgb(r, g, b)); brush.Freeze(); return brush; }
            private static readonly Media.Brush Deleted = Brush(252, 215, 220), Inserted = Brush(207, 239, 216), Modified = Brush(255, 233, 194);
            private static readonly Media.Brush DeletedWord = Brush(246, 174, 185), InsertedWord = Brush(155, 221, 179);
            protected override void ColorizeLine(DocumentLine line)
            {
                if (line.Length == 0 || !Lines.TryGetValue(line.LineNumber, out DiffPiece piece) || piece.Type == ChangeType.Unchanged) return;
                ChangeLinePart(line.Offset, line.EndOffset, e => e.TextRunProperties.SetBackgroundBrush(piece.Type == ChangeType.Deleted ? Deleted : piece.Type == ChangeType.Inserted ? Inserted : Modified));
                int offset = line.Offset;
                foreach (DiffPiece word in piece.SubPieces)
                {
                    int end = Math.Min(line.EndOffset, offset + (word.Text?.Length ?? 0));
                    if (end > offset && word.Type != ChangeType.Unchanged)
                        ChangeLinePart(offset, end, e => e.TextRunProperties.SetBackgroundBrush(word.Type == ChangeType.Deleted ? DeletedWord : InsertedWord));
                    offset = end;
                }
            }
        }
        private sealed class EmptyLineDifferenceRenderer : IBackgroundRenderer
        {
            private readonly DifferenceColorizer colors;
            internal EmptyLineDifferenceRenderer(DifferenceColorizer colors) { this.colors = colors; }
            public KnownLayer Layer => KnownLayer.Background;
            public void Draw(TextView view, Media.DrawingContext drawing)
            {
                if (!view.VisualLinesValid) return;
                foreach (VisualLine visual in view.VisualLines)
                {
                    DocumentLine line = visual.FirstDocumentLine;
                    if (line.Length != 0 || !colors.Lines.TryGetValue(line.LineNumber, out DiffPiece piece) ||
                        piece.Type == ChangeType.Unchanged) continue;
                    Media.Color color = piece.Type == ChangeType.Deleted ? Media.Color.FromRgb(252, 215, 220) :
                        piece.Type == ChangeType.Inserted ? Media.Color.FromRgb(207, 239, 216) : Media.Color.FromRgb(255, 233, 194);
                    drawing.DrawRectangle(new Media.SolidColorBrush(color), null,
                        new Wpf.Rect(0, visual.VisualTop - view.VerticalOffset, Math.Max(12, view.ActualWidth), visual.Height));
                }
            }
        }
        private readonly ScratchLargeTextEditor[] editors = { new ScratchLargeTextEditor(), new ScratchLargeTextEditor() };
        private readonly DifferenceColorizer[] colors = { new DifferenceColorizer(), new DifferenceColorizer() };
        private readonly Label[] titles = new Label[2];
        private readonly string[] savedText = { "", "" };
        private readonly CheckBox ignoreWhitespace = new CheckBox { Text = "忽略空格", AutoSize = true, Margin = new Padding(12, 8, 4, 0) };
        private readonly CheckBox ignoreCase = new CheckBox { Text = "忽略大小写", AutoSize = true, Margin = new Padding(8, 8, 4, 0) };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 34, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Button previous, next;
        private readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 400 };
        private readonly List<(int Left, int Right)> differences = new List<(int, int)>();
        private readonly Dictionary<int, int>[] lineMap = { new Dictionary<int, int>(), new Dictionary<int, int>() };
        private bool working, pending, closing, syncing;
        private int revision, selectedDifference = -1;
        private CancellationTokenSource comparison;
        private string operationNotice;
        private void Notice(string message) { operationNotice = message; status.Text = message; }
        private void ComparisonStatus(string message) { if (operationNotice == null) status.Text = message; }

        internal TextCompareForm()
        {
            Text = "小蟒同学 · 文本对比"; Icon = ShareXResources.Icon; Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; BackColor = Color.FromArgb(28, 37, 44); ForeColor = Color.FromArgb(231, 237, 242);
            MinimumSize = new Size(780, 460); Size = new Size(1340, 840); StartPosition = FormStartPosition.CenterScreen;
            HandleCreated += (_, _) => NativeMethods.UseImmersiveDarkMode(Handle, true);
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 5, 8, 5), WrapContents = true, BackColor = Color.FromArgb(38, 48, 57) };
            tools.Controls.Add(ToolButton("交换左右", (_, _) =>
            {
                string text = editors[0].Text;
                editors[0].Text = editors[1].Text; editors[1].Text = text;
                (titles[0].Text, titles[1].Text) = (titles[1].Text, titles[0].Text);
                (savedText[0], savedText[1]) = (savedText[1], savedText[0]);
            }));
            previous = ToolButton("↑ 上一个差异", (_, _) => Navigate(-1)); next = ToolButton("↓ 下一个差异", (_, _) => Navigate(1));
            tools.Controls.Add(previous); tools.Controls.Add(next); tools.Controls.Add(ignoreWhitespace); tools.Controls.Add(ignoreCase);
            foreach (var legend in new[] { ("● 删除", Color.FromArgb(255, 100, 110)), ("● 新增", Color.FromArgb(81, 210, 133)), ("● 修改", Color.FromArgb(255, 185, 79)) })
                tools.Controls.Add(new Label { Text = legend.Item1, ForeColor = legend.Item2, AutoSize = true, Margin = new Padding(12, 10, 0, 0) });
            tools.Controls[0].ForeColor = Color.FromArgb(255, 157, 72);
            ((Button)tools.Controls[0]).FlatAppearance.BorderColor = Color.FromArgb(255, 136, 62);
            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 4, BackColor = Color.FromArgb(65, 82, 94), Size = new Size(1080, 580), SplitterDistance = 540, Panel1MinSize = 250, Panel2MinSize = 250 };
            for (int i = 0; i < 2; i++)
            {
                int side = i; Panel panel = side == 0 ? split.Panel1 : split.Panel2;
                var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Padding = new Padding(8, 3, 5, 3), BackColor = Color.FromArgb(38, 48, 57) };
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                titles[side] = new Label { Text = side == 0 ? "左侧文本" : "右侧文本", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3), MinimumSize = new Size(40, 30) };
                var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
                actions.Controls.Add(ToolButton("打开", (_, _) => PickFile(side)));
                actions.Controls.Add(ToolButton("另存为", (_, _) => Save(side)));
                actions.Controls.Add(ToolButton("复制", (_, _) => Notice(ClipboardHelpers.CopyText(editors[side].Text) ? "已复制。" : "复制失败，请重试。")));
                header.Controls.Add(titles[side], 0, 0); header.Controls.Add(actions, 1, 0);
                var host = editors[side]; host.Dock = DockStyle.Fill;
                host.Editor.WordWrap = false; host.Editor.ShowLineNumbers = true;
                host.Editor.HorizontalScrollBarVisibility = Wpf.Controls.ScrollBarVisibility.Auto;
                host.Editor.VerticalScrollBarVisibility = Wpf.Controls.ScrollBarVisibility.Auto;
                host.Editor.LineNumbersForeground = Media.Brushes.Gray;
                host.Editor.TextArea.TextView.LineTransformers.Add(colors[side]);
                host.Editor.TextArea.TextView.BackgroundRenderers.Add(new EmptyLineDifferenceRenderer(colors[side]));
                host.InputRejected = Notice;
                host.ContentChanged += (_, _) => Changed(); host.SaveRequested += (_, _) => Save(side);
                host.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(side);
                // Accept file drops explicitly so AvalonEdit does not insert a file path as text.
                host.Editor.AllowDrop = true;
                host.Editor.PreviewDragOver += (_, e) => { if (e.Data.GetDataPresent(Wpf.DataFormats.FileDrop)) { e.Effects = Wpf.DragDropEffects.Copy; e.Handled = true; } };
                host.Editor.PreviewDrop += (_, e) => { if (e.Data.GetData(Wpf.DataFormats.FileDrop) is string[] paths) { e.Handled = true; if (paths.Length == 1) LoadFile(side, paths[0]); else Notice("每侧一次拖入一个文本文件。"); } };
                panel.Controls.Add(host); panel.Controls.Add(header);
            }
            status.BackColor = Color.FromArgb(38, 48, 57);
            status.Padding = new Padding(12, 0, 8, 0); status.AutoEllipsis = true;
            Controls.Add(split); Controls.Add(status); Controls.Add(tools);
            ignoreWhitespace.CheckedChanged += (_, _) => Changed(); ignoreCase.CheckedChanged += (_, _) => Changed();
            debounce.Tick += async (_, _) => { debounce.Stop(); await CompareAsync(); };
            FormClosing += (_, e) =>
            {
                if (Enumerable.Range(0, 2).Any(i => editors[i].Text != savedText[i]) &&
                    MessageBox.Show(this, "当前文本有未保存内容。关闭后将清除，是否直接关闭？\n如需保留，请取消后点击对应栏的“另存”。", "文本对比", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    e.Cancel = true;
            };
            Shown += (_, _) =>
            {
                editors[0].FocusText();
                // Shown is posted asynchronously: do not erase a file error raised just after Show().
                if (!working && !pending) { pending = true; debounce.Start(); }
            };
            FormClosed += (_, _) => { closing = true; comparison?.Cancel(); debounce.Stop(); };
            status.Text = "在左右两栏粘贴或拖入文本文件，自动比较；内容仅在本机处理。";
            previous.Enabled = next.Enabled = false;
        }
        private Button ToolButton(string text, EventHandler click)
        {
            var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(54, 32), Padding = new Padding(8, 3, 8, 3), Margin = new Padding(3), Cursor = Cursors.Hand, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(38, 48, 57), ForeColor = ForeColor };
            button.FlatAppearance.BorderColor = Color.FromArgb(65, 82, 94); button.FlatAppearance.MouseOverBackColor = Color.FromArgb(53, 70, 82); button.Click += click; return button;
        }
        private bool ReplaceAllowed(int side) => editors[side].Text == savedText[side] ||
            MessageBox.Show(this, "此栏有未保存内容，打开文件将替换它。是否继续？", "文本对比", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
        private void PickFile(int side)
        {
            using OpenFileDialog d = new OpenFileDialog { Filter = "文本文件|*.txt;*.md;*.log;*.json;*.xml;*.csv;*.ini;*.yaml;*.yml;*.html;*.cs;*.js;*.css|所有文件|*.*" };
            if (d.ShowDialog(this) == DialogResult.OK) LoadFile(side, d.FileName);
        }
        internal bool LoadFile(int side, string path)
        {
            try
            {
                if (new FileInfo(path).Length > ScratchLargeTextEditor.MaximumBytes) throw new IOException("文件超过 5 MB，请缩小内容范围。");
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length > ScratchLargeTextEditor.MaximumBytes) throw new IOException("文件超过 5 MB，请缩小内容范围。");
                string text = DecodeText(bytes);
                if (text.Contains('\0')) throw new IOException("此文件包含二进制内容，不能作为文本打开。");
                if (!ScratchLargeTextEditor.IsSupported(text, out string reason)) throw new IOException(reason);
                if (!ReplaceAllowed(side)) return false;
                editors[side].Text = text; savedText[side] = text; titles[side].Text = Path.GetFileName(path);
                operationNotice = null; Changed(); return true;
            }
            catch (Exception ex) { Notice("打开失败：" + ex.Message + "（支持 UTF-8 或带 BOM 的 Unicode 文本）"); return false; }
        }
        private static string DecodeText(byte[] bytes)
        {
            // BOM detection must not replace strict decoding with StreamReader's permissive encodings.
            Encoding encoding = new UTF8Encoding(false, true); int skip = 0;
            if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 254, 255 })) { encoding = new UTF32Encoding(true, true, true); skip = 4; }
            else if (bytes.AsSpan().StartsWith(new byte[] { 255, 254, 0, 0 })) { encoding = new UTF32Encoding(false, true, true); skip = 4; }
            else if (bytes.AsSpan().StartsWith(new byte[] { 254, 255 })) { encoding = new UnicodeEncoding(true, true, true); skip = 2; }
            else if (bytes.AsSpan().StartsWith(new byte[] { 255, 254 })) { encoding = new UnicodeEncoding(false, true, true); skip = 2; }
            else if (bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) skip = 3;
            return encoding.GetString(bytes, skip, bytes.Length - skip);
        }
        private void Save(int side)
        {
            using SaveFileDialog d = new SaveFileDialog { Filter = "文本文件|*.txt|Markdown|*.md|所有文件|*.*", FileName = side == 0 ? "左侧文本.txt" : "右侧文本.txt", OverwritePrompt = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string staging = d.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string text = editors[side].Text;
                File.WriteAllText(staging, text, new UTF8Encoding(false)); File.Move(staging, d.FileName, true);
                savedText[side] = text; titles[side].Text = Path.GetFileName(d.FileName); Notice("已保存为 UTF-8：" + d.FileName);
            }
            catch (Exception ex) { Notice("保存失败：" + ex.Message); }
            finally { if (File.Exists(staging)) { try { File.Delete(staging); } catch { } } }
        }
        private void Changed()
        {
            comparison?.Cancel(); operationNotice = null;
            revision++; pending = true; selectedDifference = -1; differences.Clear();
            previous.Enabled = next.Enabled = false;
            for (int i = 0; i < 2; i++) { colors[i].Lines.Clear(); lineMap[i].Clear(); editors[i].Editor.TextArea.TextView.Redraw(); }
            debounce.Stop(); debounce.Start(); status.Text = "等待比较…";
        }
        private async Task CompareAsync()
        {
            if (working || closing) return;
            working = true; pending = false; int version = revision;
            string left = editors[0].Text, right = editors[1].Text;
            bool whitespace = ignoreWhitespace.Checked, casing = ignoreCase.Checked;
            ComparisonStatus("正在比较…");
            using var cancellation = new CancellationTokenSource(); comparison = cancellation;
            try
            {
                SideBySideDiffModel model = await TextCompareWorker.CompareAsync(left, right, whitespace, casing, cancellation.Token);
                if (closing || IsDisposed || version != revision) return;
                Apply(model);
                ComparisonStatus(differences.Count == 0 ? "两侧文本一致（按当前忽略选项）" : $"共 {differences.Count} 处差异 · 红：删除  绿：新增  黄：修改");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closing && version == revision) ComparisonStatus("无法比较：" + ex.Message); }
            finally { comparison = null; working = false; if (!closing && pending) debounce.Start(); }
        }
        private void Apply(SideBySideDiffModel model)
        {
            var panes = new[] { model.OldText.Lines, model.NewText.Lines };
            for (int s = 0; s < 2; s++) colors[s].Lines = panes[s].Where(x => x.Position.HasValue).ToDictionary(x => x.Position.Value);
            int[] nearestLeft = NearestLines(model.OldText.Lines), nearestRight = NearestLines(model.NewText.Lines);
            bool wasChanged = false;
            for (int i = 0; i < model.OldText.Lines.Count; i++)
            {
                DiffPiece a = model.OldText.Lines[i], b = model.NewText.Lines[i];
                if (a.Position.HasValue) lineMap[0][a.Position.Value] = nearestRight[i];
                if (b.Position.HasValue) lineMap[1][b.Position.Value] = nearestLeft[i];
                bool changed = a.Type != ChangeType.Unchanged || b.Type != ChangeType.Unchanged;
                if (changed && !wasChanged) differences.Add((nearestLeft[i], nearestRight[i]));
                wasChanged = changed;
            }
            foreach (var editor in editors) editor.Editor.TextArea.TextView.Redraw();
            previous.Enabled = next.Enabled = differences.Count > 0;
        }
        private static int[] NearestLines(List<DiffPiece> pieces)
        {
            int[] result = new int[pieces.Count]; int next = pieces.LastOrDefault(x => x.Position.HasValue)?.Position ?? 1;
            for (int i = pieces.Count - 1; i >= 0; i--) { next = pieces[i].Position ?? next; result[i] = next; } return result;
        }
        private void Navigate(int direction)
        {
            if (differences.Count == 0) return;
            selectedDifference = selectedDifference < 0 ? (direction > 0 ? 0 : differences.Count - 1) : (selectedDifference + direction + differences.Count) % differences.Count;
            var pair = differences[selectedDifference]; syncing = true;
            try { for (int s = 0; s < 2; s++) { var editor = editors[s].Editor; int line = Math.Clamp(s == 0 ? pair.Left : pair.Right, 1, editor.Document.LineCount); editor.ScrollTo(line, 1); editor.Select(editor.Document.GetLineByNumber(line).Offset, editor.Document.GetLineByNumber(line).Length); } }
            finally { syncing = false; }
            ComparisonStatus($"差异 {selectedDifference + 1} / {differences.Count} · 左第 {pair.Left} 行，右第 {pair.Right} 行");
        }
        private void SyncScroll(int side)
        {
            if (syncing || closing || lineMap[side].Count == 0) return;
            var from = editors[side].Editor; var to = editors[1 - side].Editor;
            if (!from.TextArea.TextView.VisualLinesValid) return;
            var visual = from.TextArea.TextView.VisualLines.FirstOrDefault(v => v.VisualTop + v.Height > from.VerticalOffset);
            if (visual == null || !lineMap[side].TryGetValue(visual.FirstDocumentLine.LineNumber, out int target)) return;
            syncing = true;
            try { to.ScrollToVerticalOffset(to.TextArea.TextView.GetVisualTopByDocumentLine(Math.Clamp(target, 1, to.Document.LineCount)) + Math.Max(0, from.VerticalOffset - visual.VisualTop)); }
            finally { syncing = false; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { closing = true; comparison?.Cancel(); debounce.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
