using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class MainMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Func<ToolStripItem, bool> active;
        private readonly bool navigation;
        internal MainMenuRenderer(Func<ToolStripItem, bool> active = null) { this.active = active; navigation = active != null; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(MainMenuAppearance.Sidebar);
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (navigation) return;
            using Pen pen = new(MainMenuAppearance.Edge);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => DrawMenuItemSurface(e);
        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => DrawMenuItemSurface(e);
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) => DrawMenuItemSurface(e);
        private void DrawMenuItemSurface(ToolStripItemRenderEventArgs e)
        {
            bool selected = active?.Invoke(e.Item) == true;
            if (!selected && !e.Item.Selected && !e.Item.Pressed) return;
            GraphicsState state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = MainMenuAppearance.Round(new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2), MainMenuAppearance.Scale(e.ToolStrip, 5));
            using SolidBrush brush = new(selected ? MainMenuAppearance.Active : MainMenuAppearance.Selected);
            e.Graphics.FillPath(brush, path);
            if (selected)
            {
                using SolidBrush accent = new(MainMenuAppearance.Orange);
                e.Graphics.FillRectangle(accent, 1, MainMenuAppearance.Scale(e.ToolStrip, 8), MainMenuAppearance.Scale(e.ToolStrip, 3), e.Item.Height - MainMenuAppearance.Scale(e.ToolStrip, 16));
            }
            e.Graphics.Restore(state);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool selected = active?.Invoke(e.Item) == true;
            e.TextColor = !e.Item.Enabled ? MainMenuAppearance.Muted : selected ? MainMenuAppearance.Orange : MainMenuAppearance.Text;
            if (navigation)
            {
                Rectangle rect = new(MainMenuAppearance.Scale(e.ToolStrip, 40), 0, e.Item.Width - MainMenuAppearance.Scale(e.ToolStrip, 58), e.Item.Height);
                string text = e.Item.Text.TrimEnd('.', '…');
                TextRenderer.DrawText(e.Graphics, text, e.TextFont, rect, e.TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            else base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = MainMenuAppearance.Muted; base.OnRenderArrow(e); }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using Pen p = new(MainMenuAppearance.Edge);
            e.Graphics.DrawLine(p, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            string text = e.Item.Text;
            string icon = text.Contains("快捷键") || text == "输入法" ? "keyboard" : text.Contains("全局设置") ? "settings" : text.Contains("任务设置") ? "tasks" :
                text == "工具" ? "tools" : text.Contains("拾色") ? "picker" : text.Contains("便签文件夹") ? "folder" : text.Contains("截图文件夹") ? "image" :
                text.Contains("临时文件夹") ? "document" : text.StartsWith("关于") ? "about" : text == "everything" ? "search" :
                text == "幕布" ? "palette" : text == "DeskPins" ? "pin" : text.Contains("数据整理") ? "html" : null;
            if (icon == null) { base.OnRenderItemImage(e); return; }
            Rectangle rect = e.ImageRectangle;
            if (navigation)
            {
                int size = MainMenuAppearance.Scale(e.ToolStrip, 18);
                rect = new Rectangle(MainMenuAppearance.Scale(e.ToolStrip, 13), (e.Item.Height - size) / 2, size, size);
            }
            Color color = active?.Invoke(e.Item) == true ? MainMenuAppearance.Orange : MainMenuAppearance.Text;
            if (!navigation && icon == "pin") color = Color.FromArgb(255, 110, 104);
            DrawGlyph(e.Graphics, icon, rect, color);
        }

        internal static void DrawGlyph(Graphics g, string icon, Rectangle r, Color color)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            GraphicsState state = g.Save();
            try
            {
                g.TranslateTransform(r.Left, r.Top); g.ScaleTransform(r.Width / 24f, r.Height / 24f);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using Pen pen = new(color, 1.7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                using SolidBrush fill = new(color);
                void Line(float x, float y, float xx, float yy) => g.DrawLine(pen, x, y, xx, yy);
                void Lines(params PointF[] points) => g.DrawLines(pen, points);
                switch (icon)
                {
                    case "export": case "import":
                        Lines(new PointF(4, 15), new PointF(4, 21), new PointF(20, 21), new PointF(20, 15));
                        Line(12, 3, 12, 16);
                        if (icon == "export") Lines(new PointF(7, 8), new PointF(12, 3), new PointF(17, 8));
                        else Lines(new PointF(7, 11), new PointF(12, 16), new PointF(17, 11));
                        break;
                    case "restore":
                        g.DrawArc(pen, 4, 4, 16, 16, -90, 285);
                        Lines(new PointF(3, 4), new PointF(3, 10), new PointF(9, 10));
                        Line(12, 8, 12, 12); Line(12, 12, 16, 14); break;
                    case "capture":
                        Lines(new PointF(3, 9), new PointF(3, 3), new PointF(9, 3)); Lines(new PointF(15, 3), new PointF(21, 3), new PointF(21, 9));
                        Lines(new PointF(21, 15), new PointF(21, 21), new PointF(15, 21)); Lines(new PointF(9, 21), new PointF(3, 21), new PointF(3, 15)); break;
                    case "record": g.DrawEllipse(pen, 2, 2, 20, 20); g.FillEllipse(fill, 6, 6, 12, 12); break;
                    case "bell":
                        g.DrawArc(pen, 6, 4, 12, 12, 180, 180);
                        Lines(new PointF(6, 10), new PointF(6, 15), new PointF(3, 18), new PointF(21, 18), new PointF(18, 15), new PointF(18, 10));
                        Line(12, 2, 12, 4); g.DrawArc(pen, 10, 18, 4, 4, 0, 180); break;
                    case "sliders":
                        Line(3, 6, 8, 6); Line(12, 6, 21, 6); g.DrawEllipse(pen, 8, 4, 4, 4);
                        Line(3, 12, 14, 12); Line(18, 12, 21, 12); g.DrawEllipse(pen, 14, 10, 4, 4);
                        Line(3, 18, 6, 18); Line(10, 18, 21, 18); g.DrawEllipse(pen, 6, 16, 4, 4); break;
                    case "news":
                        g.DrawRectangle(pen, 3, 3, 18, 18); g.DrawRectangle(pen, 6, 7, 5, 6);
                        Line(14, 7, 18, 7); Line(14, 10, 18, 10); Line(14, 13, 18, 13); Line(6, 17, 18, 17); break;
                    case "microphone":
                        g.DrawArc(pen, 8, 2, 8, 8, 180, 180); Line(8, 6, 8, 11); Line(16, 6, 16, 11);
                        g.DrawArc(pen, 8, 7, 8, 8, 0, 180); g.DrawArc(pen, 5, 4, 14, 14, 0, 180);
                        Line(12, 18, 12, 22); Line(8, 22, 16, 22); break;
                    case "video":
                        g.DrawRectangle(pen, 2, 6, 13, 12);
                        Lines(new PointF(15, 10), new PointF(22, 6), new PointF(22, 18), new PointF(15, 14)); break;
                    case "ocr":
                        Lines(new PointF(2, 8), new PointF(2, 3), new PointF(7, 3)); Lines(new PointF(17, 3), new PointF(22, 3), new PointF(22, 8));
                        Lines(new PointF(22, 16), new PointF(22, 21), new PointF(17, 21)); Lines(new PointF(7, 21), new PointF(2, 21), new PointF(2, 16));
                        Line(8, 8, 16, 8); Line(12, 8, 12, 17); Line(9, 17, 15, 17); break;
                    case "picker":
                        Lines(new PointF(14, 7), new PointF(4, 17), new PointF(3, 21), new PointF(7, 20), new PointF(17, 10));
                        Line(12, 5, 19, 12); Lines(new PointF(15, 7), new PointF(19, 3), new PointF(22, 6), new PointF(18, 10)); break;
                    case "keyboard":
                        g.DrawRectangle(pen, 2, 5, 20, 14);
                        for (int y = 9; y <= 13; y += 4) for (int x = 6; x <= 18; x += 4) g.FillEllipse(fill, x - 1, y - 1, 1.6f, 1.6f);
                        Line(8, 16, 16, 16); break;
                    case "settings":
                        g.DrawEllipse(pen, 5, 5, 14, 14); g.DrawEllipse(pen, 9, 9, 6, 6);
                        for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; Line(12 + (float)Math.Cos(a) * 7, 12 + (float)Math.Sin(a) * 7, 12 + (float)Math.Cos(a) * 10, 12 + (float)Math.Sin(a) * 10); } break;
                    case "tasks": for (int y = 5; y <= 19; y += 7) { g.FillEllipse(fill, 2, y - 1, 2, 2); Line(8, y, 22, y); } break;
                    case "folder": Lines(new PointF(2, 7), new PointF(2, 4), new PointF(9, 4), new PointF(12, 7), new PointF(22, 7), new PointF(22, 20), new PointF(2, 20), new PointF(2, 7), new PointF(22, 7)); break;
                    case "image": g.DrawRectangle(pen, 3, 3, 18, 18); g.DrawEllipse(pen, 7, 6, 3, 3); Lines(new PointF(4, 18), new PointF(10, 12), new PointF(14, 16), new PointF(17, 12), new PointF(21, 16)); break;
                    case "document": case "html":
                        Lines(new PointF(5, 2), new PointF(14, 2), new PointF(20, 8), new PointF(20, 22), new PointF(5, 22), new PointF(5, 2));
                        Lines(new PointF(14, 2), new PointF(14, 8), new PointF(20, 8)); Line(9, 12, 16, 12); Line(9, 16, 16, 16); break;
                    case "about": g.DrawEllipse(pen, 2, 2, 20, 20); Line(12, 11, 12, 18); g.FillEllipse(fill, 11, 6, 2, 2); break;
                    case "search": g.DrawEllipse(pen, 3, 3, 13, 13); Line(15, 15, 22, 22); break;
                    case "pin": Lines(new PointF(8, 3), new PointF(17, 3), new PointF(16, 9), new PointF(20, 15), new PointF(5, 15), new PointF(9, 9), new PointF(8, 3)); Line(12, 15, 12, 22); break;
                    case "palette": g.DrawArc(pen, 2, 2, 20, 19, 45, 295); Lines(new PointF(19, 18), new PointF(13, 17), new PointF(12, 21), new PointF(8, 21)); g.FillEllipse(fill, 6, 7, 3, 3); g.FillEllipse(fill, 12, 5, 3, 3); Line(13, 14, 22, 10); break;
                    default: Lines(new PointF(14, 3), new PointF(11, 7), new PointF(12, 11), new PointF(4, 19), new PointF(6, 22), new PointF(15, 13), new PointF(19, 13), new PointF(22, 9), new PointF(17, 10), new PointF(15, 8), new PointF(17, 3), new PointF(14, 3)); break;
                }
            }
            finally { g.Restore(state); }
        }
    }
}
