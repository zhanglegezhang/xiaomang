using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class CompactContextMenuStyle : ToolStripProfessionalRenderer
    {
        private static readonly Color Background = Color.FromArgb(38, 48, 57);
        private static readonly Color Accent = Color.FromArgb(255, 136, 61);
        private static readonly Font MenuFont = new Font(MainMenuAppearance.MenuFont.FontFamily,
            MainMenuAppearance.MenuFont.SizeInPoints - 2f, FontStyle.Regular, GraphicsUnit.Point);

        internal static void Apply(ContextMenuStrip menu)
        {
            if (menu.Renderer is CompactContextMenuStyle) return;
            menu.Renderer = new CompactContextMenuStyle();
            menu.Font = MenuFont;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.BackColor = Background;
            menu.ForeColor = Color.FromArgb(240, 244, 248);
            menu.Opening += (_, _) => Layout(menu);
            menu.SizeChanged += (_, _) => RoundMenu(menu);
            Layout(menu);
        }

        private static int S(ToolStrip menu, int value) => MainMenuAppearance.Scale(menu, value);
        private static void Layout(ContextMenuStrip menu)
        {
            menu.Padding = new Padding(S(menu, 3));
            menu.AutoSize = false;
            int width = S(menu, 115) - menu.Padding.Horizontal;
            int height = menu.Padding.Vertical;
            foreach (ToolStripItem item in menu.Items)
            {
                item.AutoSize = false;
                item.Size = new Size(width, S(menu, item is ToolStripSeparator ? 5 : 26));
                item.Margin = Padding.Empty;
                height += item.Height;
            }
            menu.Size = new Size(width + menu.Padding.Horizontal, height);
        }

        private static void RoundMenu(ContextMenuStrip menu)
        {
            if (menu.Width < 2 || menu.Height < 2) return;
            using var path = MainMenuAppearance.Round(new Rectangle(0, 0, menu.Width, menu.Height), S(menu, 6));
            var old = menu.Region;
            menu.Region = new Region(path);
            old?.Dispose();
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Background);
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = MainMenuAppearance.Round(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), S(e.ToolStrip, 6));
            using var pen = new Pen(Color.FromArgb(79, 96, 110));
            e.Graphics.DrawPath(pen, path);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = MainMenuAppearance.Round(new Rectangle(0, 0, e.Item.Width - 1, e.Item.Height - 1), S(e.ToolStrip, 4));
            using var fill = new SolidBrush(Color.FromArgb(57, 72, 86));
            e.Graphics.FillPath(fill, path);
            using var accent = new SolidBrush(Accent);
            e.Graphics.FillRectangle(accent, 0, S(e.ToolStrip, 6), S(e.ToolStrip, 2), e.Item.Height - S(e.ToolStrip, 12));
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(65, 80, 93));
            e.Graphics.DrawLine(pen, S(e.ToolStrip, 6), e.Item.Height / 2, e.Item.Width - S(e.ToolStrip, 6), e.Item.Height / 2);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var color = e.Item.Enabled ? Color.FromArgb(240, 244, 248) : Color.FromArgb(132, 147, 159);
            var iconColor = e.Item.Selected && e.Item.Enabled ? Accent : Color.FromArgb(185, 202, 218);
            var r = new Rectangle(S(e.ToolStrip, 8), (e.Item.Height - S(e.ToolStrip, 14)) / 2, S(e.ToolStrip, 14), S(e.ToolStrip, 14));
            string text = e.Item.Text;
            if (text.Contains("搜索") || text.Contains("快捷键"))
                MainMenuRenderer.DrawGlyph(e.Graphics, text.Contains("搜索") ? "search" : "keyboard", r, iconColor);
            else
            {
                var state = e.Graphics.Save();
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TranslateTransform(r.X, r.Y);
                e.Graphics.ScaleTransform(r.Width / 24f, r.Height / 24f);
                using var pen = new Pen(iconColor, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                if (e.Item.Tag as string == "compact-theme")
                {
                    if (e.Item is ToolStripMenuItem choice && choice.Checked)
                        e.Graphics.DrawLines(pen, new[] { new Point(4, 12), new Point(10, 18), new Point(21, 6) });
                }
                else if (text == "主题")
                {
                    e.Graphics.DrawEllipse(pen, 3, 3, 18, 18);
                    e.Graphics.DrawEllipse(pen, 7, 7, 3, 3);
                    e.Graphics.DrawEllipse(pen, 14, 7, 3, 3);
                    e.Graphics.DrawEllipse(pen, 7, 14, 3, 3);
                }
                else if (text.Contains("主菜单"))
                    e.Graphics.DrawLines(pen, new[] { new Point(3, 11), new Point(12, 3), new Point(21, 11), new Point(21, 21), new Point(15, 21), new Point(15, 14), new Point(9, 14), new Point(9, 21), new Point(3, 21), new Point(3, 11) });
                else { e.Graphics.DrawArc(pen, 3, 4, 18, 18, -50, 280); e.Graphics.DrawLine(pen, 12, 2, 12, 12); }
                e.Graphics.Restore(state);
            }
            TextRenderer.DrawText(e.Graphics, text, e.TextFont, new Rectangle(S(e.ToolStrip, 30), 0, e.Item.Width - S(e.ToolStrip, 36), e.Item.Height), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
