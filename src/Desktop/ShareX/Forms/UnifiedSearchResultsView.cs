using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ShareX
{
    internal static class SearchAppearance
    {
        internal static readonly Color Background = Color.FromArgb(28, 35, 41);
        internal static readonly Color Surface = Color.FromArgb(34, 43, 51);
        internal static readonly Color Result = Color.FromArgb(35, 46, 56);
        internal static readonly Color Border = Color.FromArgb(54, 70, 82);
        internal static readonly Color Text = Color.FromArgb(229, 235, 240);
        internal static readonly Color Muted = Color.FromArgb(147, 164, 177);
        internal static readonly Color Accent = Color.FromArgb(255, 132, 55);

        internal static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath path = new(); int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        internal static void Glyph(Graphics g, string kind, Rectangle bounds, Color color)
        {
            var saved = g.Save(); g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 20f, bounds.Height / 20f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using Pen pen = new(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (kind == "搜索") { g.DrawEllipse(pen, 2, 2, 11, 11); g.DrawLine(pen, 12, 12, 18, 18); }
            else if (kind == "待办" || kind == "待办项") { g.DrawRectangle(pen, 3, 3, 14, 14); if (kind == "待办") g.DrawLines(pen, new[] { new PointF(6, 10), new PointF(9, 13), new PointF(14, 7) }); }
            else if (kind == "手动收藏")
            {
                var points = Enumerable.Range(0, 10).Select(i => { double a = -Math.PI / 2 + i * Math.PI / 5; float r = i % 2 == 0 ? 8 : 3.8f; return new PointF(10 + (float)Math.Cos(a) * r, 10 + (float)Math.Sin(a) * r); }).ToArray(); g.DrawPolygon(pen, points);
            }
            else if (kind == "固定文件") { g.DrawRectangle(pen, 3, 6, 14, 11); g.DrawLines(pen, new[] { new PointF(3, 6), new PointF(3, 3), new PointF(8, 3), new PointF(10, 6) }); }
            else if (kind == "剪切板") { g.DrawRectangle(pen, 4, 4, 12, 14); g.DrawRectangle(pen, 7, 2, 6, 4); g.DrawLine(pen, 7, 10, 13, 10); g.DrawLine(pen, 7, 14, 12, 14); }
            else { g.DrawLines(pen, new[] { new PointF(4, 2), new PointF(11, 2), new PointF(16, 7), new PointF(16, 18), new PointF(4, 18), new PointF(4, 2) }); g.DrawLines(pen, new[] { new PointF(11, 2), new PointF(11, 7), new PointF(16, 7) }); }
            g.Restore(saved);
        }
    }

    internal sealed class SearchInputPanel : Panel
    {
        private readonly TextBox editor;
        private readonly Button clear = new() { Text = "×", FlatStyle = FlatStyle.Flat, TabStop = false, AccessibleName = "清空搜索" };
        internal SearchInputPanel(TextBox editor)
        {
            this.editor = editor; DoubleBuffered = true; BackColor = SearchAppearance.Background;
            editor.BackColor = SearchAppearance.Surface; editor.ForeColor = SearchAppearance.Text;
            clear.FlatAppearance.BorderSize = 0; clear.ForeColor = SearchAppearance.Muted; clear.BackColor = SearchAppearance.Surface;
            clear.FlatAppearance.MouseOverBackColor = Color.FromArgb(47, 61, 73);
            clear.Click += (_, _) => { editor.Clear(); editor.Focus(); };
            editor.TextChanged += (_, _) => clear.Visible = editor.TextLength > 0;
            editor.GotFocus += (_, _) => Invalidate(); editor.LostFocus += (_, _) => Invalidate();
            clear.Visible = false; Controls.Add(editor); Controls.Add(clear);
            SizeChanged += (_, _) => Arrange(); FontChanged += (_, _) => Arrange();
        }
        private int D(int v) => (int)Math.Round(v * DeviceDpi / 96d);
        private void Arrange()
        {
            if (editor == null) return;
            editor.Bounds = new Rectangle(D(39), Math.Max(D(6), (Height - editor.PreferredHeight) / 2), Math.Max(D(20), Width - D(78)), editor.PreferredHeight);
            clear.Bounds = new Rectangle(Math.Max(0, Width - D(34)), D(6), D(28), Math.Max(D(20), Height - D(12)));
            Invalidate();
        }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Arrange(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = SearchAppearance.Rounded(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), D(7));
            using Brush surface = new SolidBrush(SearchAppearance.Surface); e.Graphics.FillPath(surface, shape);
            using Pen border = new(editor.Focused ? SearchAppearance.Accent : Color.FromArgb(77, 99, 115)); e.Graphics.DrawPath(border, shape);
            SearchAppearance.Glyph(e.Graphics, "搜索", new Rectangle(D(12), (Height - D(18)) / 2, D(18), D(18)), SearchAppearance.Accent);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); editor.Focus(); }
    }

    internal sealed class SearchHistoryCheckBox : CheckBox
    {
        internal SearchHistoryCheckBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); ForeColor = SearchAppearance.Text; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); int D(int v) => (int)Math.Round(v * DeviceDpi / 96d);
            var box = new Rectangle(D(1), (Height - D(15)) / 2, D(15), D(15)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = SearchAppearance.Rounded(box, D(3));
            if (Checked) { using Brush fill = new SolidBrush(SearchAppearance.Accent); e.Graphics.FillPath(fill, path); using Pen check = new(SearchAppearance.Background, D(2)); e.Graphics.DrawLines(check, new[] { new Point(box.Left + D(3), box.Top + D(8)), new Point(box.Left + D(6), box.Top + D(11)), new Point(box.Left + D(12), box.Top + D(4)) }); }
            else { using Pen border = new(SearchAppearance.Muted, Math.Max(1, D(1))); e.Graphics.DrawPath(border, path); }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(D(25), 0, Math.Max(0, Width - D(25)), Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -D(4)), ForeColor, BackColor);
        }
    }

    internal sealed class UnifiedSearchResultsView : Control
    {
        private sealed record Row(SearchHit Hit, int Index, string Source, int Count, string Title, string Detail, string Snippet);
        private readonly List<Row> rows = new();
        private SearchHit[] hits = Array.Empty<SearchHit>();
        private readonly ToolTip tip = new() { InitialDelay = 500, AutoPopDelay = 5000, ReshowDelay = 100 };
        private Font boldFont, smallFont;
        private string term = "", emptyMessage = "输入关键词，搜索待办、收藏和本地笔记";
        private int selectedIndex = -1, hoveredRow = -1, scrollOffset, contentHeight;
        private int pressedIndex = -1;
        private Point pressedPoint;
        private bool draggingScrollbar;
        private int dragY, dragOffset;
        private const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        internal event EventHandler ResultInvoked;
        internal int HitCount => hits.Length;
        internal SearchHit SelectedHit => selectedIndex >= 0 && selectedIndex < hits.Length ? hits[selectedIndex] : null;
        internal string EmptyMessage { get => emptyMessage; set { emptyMessage = value; Invalidate(); } }
        internal UnifiedSearchResultsView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = SearchAppearance.Result; ForeColor = SearchAppearance.Text; TabStop = true;
            AccessibleRole = AccessibleRole.List; AccessibleName = "全局搜索结果";
            RefreshFonts();
        }
        private int D(int v) => (int)Math.Round(v * DeviceDpi / 96d);
        private int HeaderHeight => D(26);
        private int RowHeight(Row row) => D(row.Hit == null ? 25 : 38);
        private int ViewHeight => Math.Max(1, Height - HeaderHeight);
        private int MaximumScroll => Math.Max(0, contentHeight - ViewHeight);
        private int ContentWidth => Math.Max(1, Width - (MaximumScroll > 0 ? D(12) : 0));
        private int Split => (int)Math.Round(ContentWidth * .34);
        private void RefreshFonts()
        {
            boldFont?.Dispose(); smallFont?.Dispose(); boldFont = new Font(Font, FontStyle.Bold); smallFont = new Font(Font.FontFamily, Math.Max(7f, Font.SizeInPoints - 1f));
        }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); RefreshFonts(); Invalidate(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); MeasureRows(); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); scrollOffset = Math.Min(scrollOffset, MaximumScroll); Invalidate(); }
        internal static string PlainSummary(string value)
        {
            value = Regex.Replace(value ?? "", @"!\[[^\]]*\]\([^)]*(?:\)|$)", "[图片]");
            value = Regex.Replace(value, @"diary-asset:[^\s)]+", "[图片]");
            value = Regex.Replace(value, @"\[([^\]]+)\]\([^)]*\)", "$1");
            value = Regex.Replace(value, @"(?:\*\*|__|~~|`{1,3})", "");
            value = Regex.Replace(value, @"(?:^|\s)#{1,6}\s+", " ");
            value = value.Replace('|', ' ');
            return Regex.Replace(value, @"\s+", " ").Trim();
        }
        internal void SetHits(SearchHit[] items, string query)
        {
            hits = items ?? Array.Empty<SearchHit>(); term = query ?? ""; rows.Clear();
            int index = 0;
            bool Exact(SearchHit hit) => term.Length > 0 && string.Equals(hit.Title, term, StringComparison.OrdinalIgnoreCase);
            var exact = hits.Where(Exact).ToArray();
            var groups = new List<(string Name, SearchHit[] Items)>();
            if (exact.Length > 0) groups.Add(("精确匹配", exact));
            groups.AddRange(hits.Where(x => !Exact(x)).GroupBy(x => x.Source).Select(g => (g.Key, g.ToArray())));
            foreach (var group in groups)
            {
                rows.Add(new Row(null, -1, group.Name, group.Items.Length, "", "", ""));
                foreach (var hit in group.Items)
                {
                    string title = hit.Title ?? "", detail = "";
                    if (hit.Source == "本地笔记")
                    {
                        title = Path.GetFileName(title.Replace('/', Path.DirectorySeparatorChar));
                        detail = Path.GetDirectoryName((hit.Path ?? hit.Title ?? "").Replace('/', Path.DirectorySeparatorChar)) ?? "";
                        detail = detail.Replace(Path.DirectorySeparatorChar.ToString(), " / ");
                    }
                    else if (hit.Source == "固定文件") detail = hit.Path ?? "";
                    bool file = hit.Source is "本地笔记" or "固定文件";
                    string displayedTitle = UnifiedSearchEngine.Snippet(title, term);
                    rows.Add(new Row(hit, index++, hit.Source, 0, file ? displayedTitle : PlainSummary(displayedTitle), detail,
                        hit.Source == "固定文件" ? UnifiedSearchEngine.Snippet(hit.Text, term) : PlainSummary(UnifiedSearchEngine.Snippet(hit.Text, term))));
                }
            }
            // Keep opening targets aligned after placing exact matches before source groups.
            hits = rows.Where(r => r.Hit != null).Select(r => r.Hit).ToArray();
            selectedIndex = hits.Length > 0 ? 0 : -1; hoveredRow = -1; scrollOffset = 0; tip.SetToolTip(this, null);
            MeasureRows(); UpdateAccessibility(); Invalidate();
        }
        private void MeasureRows() => contentHeight = rows.Sum(RowHeight);
        internal void FocusFirst() { Focus(); SelectHit(0); }
        internal void InvokeSelected() { if (SelectedHit != null) ResultInvoked?.Invoke(this, EventArgs.Empty); }
        private void UpdateAccessibility()
        {
            AccessibleDescription = SelectedHit == null ? "没有选中的结果" : $"{SelectedHit.Source}：{SelectedHit.Title}";
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.Selection, -1);
        }
        private void SelectHit(int index)
        {
            if (hits.Length == 0) return;
            selectedIndex = Math.Clamp(index, 0, hits.Length - 1);
            int y = 0;
            foreach (var row in rows)
            {
                int height = RowHeight(row);
                if (row.Index == selectedIndex) { if (y < scrollOffset) scrollOffset = y; else if (y + height > scrollOffset + ViewHeight) scrollOffset = y + height - ViewHeight; break; }
                y += height;
            }
            scrollOffset = selectedIndex == 0 ? 0 : Math.Clamp(scrollOffset, 0, MaximumScroll); UpdateAccessibility(); Invalidate();
        }
        private void SetScroll(int value) { scrollOffset = Math.Clamp(value, 0, MaximumScroll); hoveredRow = -1; tip.SetToolTip(this, null); Invalidate(); }
        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Enter || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int page = Math.Max(1, ViewHeight / Math.Max(1, D(38)) - 1);
            switch (e.KeyCode)
            {
                case Keys.Down: SelectHit(selectedIndex + 1); break;
                case Keys.Up: SelectHit(selectedIndex - 1); break;
                case Keys.Home: SelectHit(0); break;
                case Keys.End: SelectHit(hits.Length - 1); break;
                case Keys.PageDown: SelectHit(selectedIndex + page); break;
                case Keys.PageUp: SelectHit(selectedIndex - page); break;
                case Keys.Enter: InvokeSelected(); break;
                default: return;
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); int lines = SystemInformation.MouseWheelScrollLines; SetScroll(scrollOffset - (int)Math.Round(e.Delta / 120d * (lines < 0 ? ViewHeight : D(24) * lines))); }
        private int RowAt(Point point)
        {
            if (point.Y < HeaderHeight || point.X >= ContentWidth) return -1;
            int y = point.Y - HeaderHeight + scrollOffset, top = 0;
            for (int i = 0; i < rows.Count; i++) { top += RowHeight(rows[i]); if (y < top) return i; }
            return -1;
        }
        private Rectangle ScrollTrack => new(Math.Max(0, Width - D(10)), HeaderHeight + D(6), D(6), Math.Max(1, ViewHeight - D(12)));
        private Rectangle ScrollThumb
        {
            get
            {
                var track = ScrollTrack; int length = Math.Min(track.Height, Math.Max(D(32), (int)((long)track.Height * ViewHeight / Math.Max(1, contentHeight))));
                int y = track.Top + (int)((long)Math.Max(0, track.Height - length) * scrollOffset / Math.Max(1, MaximumScroll));
                return new Rectangle(track.Left, y, track.Width, length);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; pressedIndex = -1; pressedPoint = e.Location; Focus();
            if (MaximumScroll > 0 && e.X >= ContentWidth && e.Y >= HeaderHeight)
            {
                if (ScrollThumb.Contains(e.Location)) { draggingScrollbar = true; dragY = e.Y; dragOffset = scrollOffset; Capture = true; }
                else SetScroll(scrollOffset + (e.Y < ScrollThumb.Top ? -ViewHeight : ViewHeight));
                return;
            }
            int row = RowAt(e.Location);
            if (row >= 0 && rows[row].Hit != null) { pressedIndex = rows[row].Index; SelectHit(pressedIndex); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressedIndex >= 0 && (Math.Abs(e.X - pressedPoint.X) > SystemInformation.DragSize.Width / 2 ||
                Math.Abs(e.Y - pressedPoint.Y) > SystemInformation.DragSize.Height / 2)) pressedIndex = -1;
            if (draggingScrollbar) { int travel = ScrollTrack.Height - ScrollThumb.Height; if (travel > 0) SetScroll(dragOffset + (int)((long)(e.Y - dragY) * MaximumScroll / travel)); return; }
            int row = RowAt(e.Location);
            if (row != hoveredRow)
            {
                hoveredRow = row;
                tip.SetToolTip(this, row >= 0 && rows[row].Hit != null ? rows[row].Hit.Title + "\n" + rows[row].Snippet : null);
                Cursor = row >= 0 && rows[row].Hit != null ? Cursors.Hand : Cursors.Default; Invalidate();
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int row = RowAt(e.Location);
            bool open = !draggingScrollbar && pressedIndex >= 0 && row >= 0 && rows[row].Index == pressedIndex &&
                Math.Abs(e.X - pressedPoint.X) <= SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - pressedPoint.Y) <= SystemInformation.DragSize.Height / 2;
            draggingScrollbar = false; pressedIndex = -1; Capture = false;
            if (open) ResultInvoked?.Invoke(this, EventArgs.Empty);
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { draggingScrollbar = false; pressedIndex = -1; } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoveredRow = -1; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            using Brush headerBrush = new SolidBrush(SearchAppearance.Surface); g.FillRectangle(headerBrush, 0, 0, Width, HeaderHeight);
            using Pen divider = new(SearchAppearance.Border);
            g.DrawLine(divider, 0, HeaderHeight - 1, Width, HeaderHeight - 1); g.DrawLine(divider, Split, 0, Split, HeaderHeight);
            TextRenderer.DrawText(g, "内容", Font, new Rectangle(D(15), 0, Math.Max(0, Split - D(22)), HeaderHeight), SearchAppearance.Muted, TextFlags);
            TextRenderer.DrawText(g, "匹配摘要 / 路径", Font, new Rectangle(Split + D(15), 0, Math.Max(0, ContentWidth - Split - D(24)), HeaderHeight), SearchAppearance.Muted, TextFlags);
            if (rows.Count == 0) { TextRenderer.DrawText(g, emptyMessage, Font, new Rectangle(D(20), HeaderHeight, Math.Max(0, Width - D(40)), ViewHeight), SearchAppearance.Muted, TextFlags | TextFormatFlags.HorizontalCenter); return; }
            var saved = g.Save(); g.SetClip(new Rectangle(0, HeaderHeight, ContentWidth, ViewHeight));
            int top = HeaderHeight - scrollOffset;
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i]; int height = RowHeight(row);
                if (top + height > HeaderHeight && top < Height) DrawRow(g, row, new Rectangle(0, top, ContentWidth, height), i == hoveredRow, divider);
                top += height; if (top >= Height) break;
            }
            g.Restore(saved);
            if (MaximumScroll > 0) { using Brush fill = new SolidBrush(Color.FromArgb(117, 136, 151)); using var path = SearchAppearance.Rounded(ScrollThumb, D(3)); g.FillPath(fill, path); }
        }
        private void DrawRow(Graphics g, Row row, Rectangle bounds, bool hovered, Pen divider)
        {
            if (row.Hit == null)
            {
                using Brush fill = new SolidBrush(SearchAppearance.Background); g.FillRectangle(fill, bounds);
                SearchAppearance.Glyph(g, row.Source, new Rectangle(D(16), bounds.Top + D(4), D(17), D(17)), SearchAppearance.Accent);
                var label = new Rectangle(D(43), bounds.Top, Math.Max(0, ContentWidth - D(100)), bounds.Height);
                TextRenderer.DrawText(g, row.Source, boldFont, label, ForeColor, TextFlags);
                int nameWidth = TextRenderer.MeasureText(g, row.Source, boldFont, Size.Empty, TextFormatFlags.NoPadding).Width;
                string count = row.Count.ToString(); int countWidth = TextRenderer.MeasureText(g, count, smallFont, Size.Empty, TextFormatFlags.NoPadding).Width + D(15);
                var pill = new Rectangle(D(43) + nameWidth + D(10), bounds.Top + D(4), countWidth, D(17));
                using Brush pillBrush = new SolidBrush(Color.FromArgb(48, 64, 77)); using var path = SearchAppearance.Rounded(pill, D(8)); g.FillPath(pillBrush, path);
                TextRenderer.DrawText(g, count, smallFont, pill, SearchAppearance.Muted, TextFlags | TextFormatFlags.HorizontalCenter);
                g.DrawLine(divider, 0, bounds.Bottom - 1, ContentWidth, bounds.Bottom - 1); return;
            }
            bool selected = row.Index == selectedIndex;
            if (selected || hovered) { using Brush fill = new SolidBrush(selected ? Color.FromArgb(43, 62, 77) : Color.FromArgb(41, 54, 65)); g.FillRectangle(fill, bounds); }
            if (selected) { using Brush accent = new SolidBrush(SearchAppearance.Accent); g.FillRectangle(accent, 0, bounds.Top + D(1), D(3), bounds.Height - D(2)); }
            SearchAppearance.Glyph(g, row.Source == "待办" ? "待办项" : row.Source, new Rectangle(D(23), bounds.Top + (bounds.Height - D(17)) / 2, D(17), D(17)), row.Source == "手动收藏" ? SearchAppearance.Accent : ForeColor);
            int textLeft = D(53), titleWidth = Math.Max(0, Split - textLeft - D(12));
            var title = new Rectangle(textLeft, bounds.Top + (row.Detail.Length > 0 ? D(2) : D(9)), titleWidth, D(20));
            DrawHighlighted(g, row.Title, Font, title);
            if (row.Detail.Length > 0) TextRenderer.DrawText(g, row.Detail, smallFont, new Rectangle(textLeft, bounds.Top + D(21), titleWidth, D(15)), SearchAppearance.Muted, TextFlags | TextFormatFlags.PathEllipsis);
            DrawHighlighted(g, row.Snippet, Font, new Rectangle(Split + D(15), bounds.Top, Math.Max(0, ContentWidth - Split - D(40)), bounds.Height));
            g.DrawLine(divider, Split, bounds.Top, Split, bounds.Bottom); g.DrawLine(divider, D(14), bounds.Bottom - 1, ContentWidth, bounds.Bottom - 1);
            if (selected) { using Pen arrow = new(SearchAppearance.Text, Math.Max(1, D(1))); int x = ContentWidth - D(20), y = bounds.Top + bounds.Height / 2; g.DrawLines(arrow, new[] { new Point(x, y - D(4)), new Point(x + D(4), y), new Point(x, y + D(4)) }); }
        }
        private void DrawHighlighted(Graphics g, string text, Font font, Rectangle bounds)
        {
            if (bounds.Width <= 0) return;
            TextRenderer.DrawText(g, text, font, bounds, ForeColor, TextFlags);
            if (term.Length == 0) return;
            int Measure(string value) => TextRenderer.MeasureText(g, value, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            bool fits = Measure(text) <= bounds.Width;
            var saved = g.Save(); g.SetClip(bounds, CombineMode.Intersect);
            for (int start = 0, count = 0; start < text.Length && count < 12; count++)
            {
                int at = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase); if (at < 0) break;
                int x = bounds.Left + Measure(text.Substring(0, at)), width = Measure(text.Substring(at, term.Length));
                if (x + width > bounds.Right - (fits ? 0 : D(18))) break;
                var match = new Rectangle(x, bounds.Top + (bounds.Height - font.Height) / 2, width, font.Height);
                using Brush fill = new SolidBrush(Color.FromArgb(106, 67, 39)); g.FillRectangle(fill, match);
                TextRenderer.DrawText(g, text.Substring(at, term.Length), font, match, Color.FromArgb(255, 207, 164), TextFlags & ~TextFormatFlags.EndEllipsis);
                start = at + term.Length;
            }
            g.Restore(saved);
        }
        protected override void Dispose(bool disposing) { if (disposing) { tip.Dispose(); boldFont?.Dispose(); smallFont?.Dispose(); } base.Dispose(disposing); }
    }
}
