using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX
{
    internal static class ProductUpdateStyle
    {
        internal static readonly Color Background = Color.FromArgb(25, 34, 41);
        internal static readonly Color Surface = Color.FromArgb(34, 46, 55);
        internal static readonly Color Border = Color.FromArgb(57, 72, 82);
        internal static readonly Color Text = Color.FromArgb(232, 237, 241);
        internal static readonly Color Muted = Color.FromArgb(153, 170, 181);
        internal static readonly Color Accent = Color.FromArgb(255, 138, 61);
        internal static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }

    internal sealed class ProductUpdateButton : Button
    {
        private bool primary, hover;
        internal bool Primary { get => primary; set { primary = value; Invalidate(); } }
        internal bool RefreshGlyph { get; set; }
        internal ProductUpdateButton()
        {
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = ProductUpdateStyle.Background; ForeColor = ProductUpdateStyle.Text;
            Padding = new Padding(14, 7, 14, 7); Margin = new Padding(8, 0, 0, 0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            var size = TextRenderer.MeasureText(Text, Font);
            if (RefreshGlyph)
            {
                int inset = (int)Math.Round(12 * DeviceDpi / 96f);
                int glyph = (int)Math.Round(Font.Height * .85f);
                return new Size(size.Width + glyph + (int)Math.Round(7 * DeviceDpi / 96f) + inset * 2,
                    Math.Max(size.Height + (int)Math.Round(12 * DeviceDpi / 96f), (int)Math.Round(30 * DeviceDpi / 96f)));
            }
            return new Size(size.Width + Padding.Horizontal + (RefreshGlyph ? (int)(24 * DeviceDpi / 96f) : 0), size.Height + Padding.Vertical);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;
            using var shape = ProductUpdateStyle.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 5 * s);
            Color fill = !Enabled ? ProductUpdateStyle.Surface : primary ? (hover ? Color.FromArgb(255, 158, 91) : ProductUpdateStyle.Accent) : hover ? ProductUpdateStyle.Surface : BackColor;
            Color ink = !Enabled ? Color.FromArgb(111, 130, 143) : primary ? Color.FromArgb(35, 30, 25) : ForeColor;
            using var brush = new SolidBrush(fill); using var pen = new Pen(primary && Enabled ? fill : ProductUpdateStyle.Border);
            g.FillPath(brush, shape); g.DrawPath(pen, shape);
            var textRect = ClientRectangle;
            if (RefreshGlyph)
            {
                float size = Font.Height * .85f, gap = 7 * s;
                int textWidth = TextRenderer.MeasureText(Text, Font).Width;
                float x = (Width - size - gap - textWidth) / 2f, y = (Height - size) / 2f;
                using var glyph = new Pen(ink, Math.Max(1, size / 14)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                float inset = size * .12f, diameter = size - inset * 2;
                // Opposed arcs leave clear gaps; arrowheads follow each arc's tangent.
                foreach (float start in new[] { 35f, 215f })
                {
                    const float sweep = 130f;
                    g.DrawArc(glyph, x + inset, y + inset, diameter, diameter, start, sweep);
                    double angle = (start + sweep) * Math.PI / 180;
                    var tip = new PointF(x + size / 2 + diameter / 2 * (float)Math.Cos(angle),
                        y + size / 2 + diameter / 2 * (float)Math.Sin(angle));
                    var tangent = new PointF(-(float)Math.Sin(angle), (float)Math.Cos(angle));
                    float length = size * .19f, halfWidth = size * .12f;
                    g.DrawLines(glyph, new[] {
                        new PointF(tip.X - tangent.X * length - tangent.Y * halfWidth, tip.Y - tangent.Y * length + tangent.X * halfWidth),
                        tip,
                        new PointF(tip.X - tangent.X * length + tangent.Y * halfWidth, tip.Y - tangent.Y * length - tangent.X * halfWidth)
                    });
                }
                textRect = new Rectangle((int)Math.Round(x + size + gap), 0, textWidth, Height);
            }
            TextRenderer.DrawText(g, Text, Font, textRect, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4), ink, fill);
        }
    }

    internal sealed class ProductUpdateStatusIcon : Control
    {
        internal string State = "checking";
        internal ProductUpdateStatusIcon() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float size = Math.Min(Width, Height) - 4, x = (Width - size) / 2, y = (Height - size) / 2;
            if (size <= 0) return;
            Color color = State == "current" ? Color.FromArgb(44, 177, 97) : State == "failed" ? Color.FromArgb(244, 177, 64) : ProductUpdateStyle.Accent;
            using var brush = new SolidBrush(color); g.FillEllipse(brush, x, y, size, size);
            using var pen = new Pen(Color.White, size / 16) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            PointF P(float a, float b) => new(x + size * a, y + size * b);
            if (State == "current") g.DrawLines(pen, new[] { P(.27f,.5f), P(.43f,.66f), P(.74f,.33f) });
            else if (State == "failed") { g.DrawLine(pen, P(.5f,.25f), P(.5f,.55f)); g.DrawLine(pen, P(.5f,.73f), P(.5f,.74f)); }
            else if (State == "checking") g.DrawArc(pen, x + size * .25f, y + size * .25f, size * .5f, size * .5f, 20, 285);
            else { g.DrawLine(pen, P(.5f,.22f), P(.5f,.61f)); g.DrawLines(pen, new[] { P(.32f,.46f), P(.5f,.64f), P(.68f,.46f) }); g.DrawLine(pen, P(.27f,.78f), P(.73f,.78f)); }
        }
    }

    internal sealed class ProductUpdateProgress : Control
    {
        private int value;
        private ProgressBarStyle style;
        internal int Value { get => value; set { this.value = Math.Clamp(value, 0, 100); Invalidate(); } }
        internal ProgressBarStyle Style { get => style; set { style = value; Invalidate(); } }
        internal ProductUpdateProgress() { DoubleBuffered = true; TabStop = false; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            float s = DeviceDpi / 96f, trackWidth = Width - 55 * s;
            if (trackWidth <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0, (Height - 7 * s) / 2f, trackWidth, 7 * s);
            using var path = ProductUpdateStyle.Rounded(r, 3 * s);
            using var background = new SolidBrush(ProductUpdateStyle.Border);
            using var fill = new SolidBrush(ProductUpdateStyle.Accent);
            e.Graphics.FillPath(background, path);
            var state = e.Graphics.Save(); e.Graphics.SetClip(path);
            e.Graphics.FillRectangle(fill, r.X, r.Y, r.Width * (style == ProgressBarStyle.Marquee ? 1 : value / 100f), r.Height);
            e.Graphics.Restore(state);
            TextRenderer.DrawText(e.Graphics, style == ProgressBarStyle.Marquee ? "处理中" : value + "%", Font,
                new Rectangle((int)trackWidth, 0, Width - (int)trackWidth, Height), ProductUpdateStyle.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}
