using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        private void ConfigureEditorToolbarSurface()
        {
            menuForm.BackColor = tsMain.BackColor = CaptureToolbarRenderer.Surface;
            // Preserve the editor's existing icons and interaction colors.
            var colors = (tsMain.Renderer as ToolStripProfessionalRenderer)?.ColorTable ?? new ProfessionalColorTable();
            tsMain.Renderer = new CaptureToolbarSurfaceRenderer(colors);
            tsMain.ImageScalingSize = new Size((int)Math.Round(18 * menuForm.DeviceDpi / 96d), (int)Math.Round(18 * menuForm.DeviceDpi / 96d));
            ConfigureStyleToolbarAppearance();
        }

        private void ConfigureCaptureToolbarAppearance()
        {
            // 80% of the 40px concept toolbar. Scale once for the current screen DPI.
            float scale = menuForm.DeviceDpi / 96f;
            int S(int value) => (int)Math.Round(value * scale);
            menuForm.AutoScaleMode = AutoScaleMode.None;
            menuForm.BackColor = CaptureToolbarRenderer.Surface;
            tsMain.SuspendLayout();
            tsMain.Renderer = new CaptureToolbarRenderer();
            tsMain.BackColor = CaptureToolbarRenderer.Surface;
            tsMain.ForeColor = Color.FromArgb(224, 233, 240);
            tsMain.ImageScalingSize = new Size(S(18), S(18));
            tsMain.MinimumSize = new Size(S(10), S(32));
            tsMain.Padding = new Padding(S(4));
            tsMain.ShowItemToolTips = false;
            tsbCopyCapture.Name = "capture-copy";
            tsbLongCapture.Name = "capture-long";
            tsbPinCapture.Name = "capture-pin";
            tsbOCRCapture.Name = "capture-ocr";
            tsbTodoCapture.Name = "capture-todo";
            tsbSaveCaptureAs.Name = "capture-save-as";
            foreach (ToolStripItem item in tsMain.Items)
            {
                item.Margin = new Padding(S(1), 0, S(1), 0);
                if (item is ToolStripButton)
                {
                    item.AutoSize = false;
                    item.Size = new Size(S(24), S(24));
                    item.Padding = Padding.Empty;
                    if (item.Text == "取消") item.Name = "capture-cancel";
                }
                else if (item == tslDragLeft || item == tslDragRight)
                {
                    item.Name = "capture-grip";
                    item.AutoSize = false;
                    item.Size = new Size(S(9), S(24));
                    item.Padding = Padding.Empty;
                }
            }
            tsbLongCapture.Margin = new Padding(S(5), 0, S(1), 0);
            tsMain.ResumeLayout(true);
            ConfigureStyleToolbarAppearance();
        }
    }

    internal sealed class CaptureToolbarSurfaceRenderer : ToolStripRoundedEdgeRenderer
    {
        internal CaptureToolbarSurfaceRenderer(ProfessionalColorTable colors) : base(colors) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
            e.Graphics.Clear(CaptureToolbarRenderer.Surface);
    }

    internal sealed class CaptureToolbarRenderer : ToolStripProfessionalRenderer
    {
        internal static void DrawTextSizeIcon(Graphics graphics, Color color)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            graphics.DrawLines(pen, new[] { new PointF(2.5f, 20), new PointF(8, 4), new PointF(13.5f, 20) });
            graphics.DrawLine(pen, 4.5f, 14, 11.5f, 14);
            graphics.DrawLines(pen, new[] { new PointF(14.5f, 20), new PointF(18, 10.5f), new PointF(21.5f, 20) });
            graphics.DrawLine(pen, 15.8f, 16.5f, 20.2f, 16.5f);
        }
        internal static readonly Color Surface = Color.FromArgb(34, 43, 51);
        private static readonly Color Accent = Color.FromArgb(255, 139, 67);

        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Surface);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip.Width < 2 || e.ToolStrip.Height < 2) return;
            GraphicsState state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = Round(new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 7 * e.ToolStrip.DeviceDpi / 96f);
            using Pen edge = new Pen(Color.FromArgb(61, 77, 89));
            e.Graphics.DrawPath(edge, path);
            e.Graphics.Restore(state);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            bool active = e.Item is ToolStripButton button && button.Checked;
            bool finish = e.Item.Name == "capture-copy" && e.Item.Enabled;
            if (!active && !finish && !e.Item.Selected && !e.Item.Pressed) return;
            Color fill = finish ? Accent : active ? Color.FromArgb(81, 60, 47) : Color.FromArgb(49, 65, 77);
            if (e.Item.Pressed) fill = finish ? Color.FromArgb(223, 111, 45) : Color.FromArgb(69, 85, 96);
            GraphicsState state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = Round(new RectangleF(0, 0, e.Item.Width - 1, e.Item.Height - 1), 5 * e.ToolStrip.DeviceDpi / 96f);
            using SolidBrush brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);
            e.Graphics.Restore(state);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(224, 233, 240);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => OnRenderButtonBackground(e);

        protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
        {
            using SolidBrush brush = new SolidBrush(e.Item.Selected ? Color.FromArgb(49, 65, 77) : Surface);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            using Pen pen = new Pen(Color.FromArgb(224, 233, 240), 1.5f);
            int x = e.Item.Width / 2, y = e.Item.Height / 2;
            e.Graphics.DrawLines(pen, new[] { new Point(x - 3, y - 2), new Point(x, y + 1), new Point(x + 3, y - 2) });
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            string icon = e.Item.Tag is ShapeType type ? type.ToString() : e.Item.Name;
            if (!(icon.StartsWith("capture-", StringComparison.Ordinal) ||
                icon is "DrawingRectangle" or "DrawingEllipse" or "DrawingArrow" or "DrawingTextOutline" or
                    "DrawingSpeechBalloon" or "DrawingStep" or "DrawingMagnify" or "EffectBlur" or "EffectHighlight"))
            {
                base.OnRenderItemImage(e);
                return;
            }
            bool active = e.Item is ToolStripButton button && button.Checked;
            Color color = !e.Item.Enabled ? Color.FromArgb(101, 116, 127) :
                e.Item.Name == "capture-copy" ? Surface : active ? Accent : Color.FromArgb(224, 233, 240);
            Graphics g = e.Graphics;
            GraphicsState saved = g.Save();
            try
            {
                float size = e.ToolStrip.ImageScalingSize.Width;
                g.TranslateTransform((e.Item.Width - size) / 2, (e.Item.Height - size) / 2);
                g.ScaleTransform(size / 24, size / 24);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using Pen p = new Pen(color, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                using SolidBrush b = new SolidBrush(color);
                void Line(float x1, float y1, float x2, float y2) => g.DrawLine(p, x1, y1, x2, y2);
                void Lines(params PointF[] points) => g.DrawLines(p, points);
                switch (icon)
                {
                    case "capture-text-size": DrawTextSizeIcon(g, color); break;
                    case "DrawingRectangle": g.DrawRectangle(p, 3, 5, 18, 14); break;
                    case "DrawingEllipse": g.DrawEllipse(p, 3, 4, 18, 16); break;
                    case "DrawingArrow": Line(4, 20, 20, 4); Lines(new PointF(10, 4), new PointF(20, 4), new PointF(20, 14)); break;
                    case "DrawingTextOutline": Lines(new PointF(4, 20), new PointF(12, 4), new PointF(20, 20)); Line(8, 14, 16, 14); break;
                    case "DrawingSpeechBalloon": Lines(new PointF(4, 4), new PointF(20, 4), new PointF(20, 16), new PointF(11, 16), new PointF(6, 21), new PointF(6, 16), new PointF(4, 16), new PointF(4, 4)); break;
                    case "DrawingStep": g.DrawEllipse(p, 2, 2, 20, 20); Lines(new PointF(9, 9), new PointF(12, 6), new PointF(12, 18)); Line(9, 18, 15, 18); break;
                    case "DrawingMagnify": g.DrawEllipse(p, 3, 3, 13, 13); Line(15, 15, 21, 21); Line(6, 9, 13, 9); Line(9.5f, 6, 9.5f, 13); break;
                    case "EffectBlur":
                        Line(4, 16, 8, 20);
                        Line(4, 10, 14, 20);
                        Line(4, 4, 20, 20);
                        Line(10, 4, 20, 14);
                        Line(16, 4, 20, 8);
                        break;
                    case "EffectHighlight": Lines(new PointF(5, 14), new PointF(14, 4), new PointF(20, 10), new PointF(11, 20), new PointF(5, 14)); Line(3, 21, 10, 21); Line(8, 11, 14, 17); break;
                    case "capture-long":
                        Lines(new PointF(7, 4), new PointF(3, 4), new PointF(3, 20), new PointF(7, 20));
                        Lines(new PointF(17, 4), new PointF(21, 4), new PointF(21, 20), new PointF(17, 20));
                        Line(12, 3, 12, 21); Lines(new PointF(9, 6), new PointF(12, 3), new PointF(15, 6)); Lines(new PointF(9, 18), new PointF(12, 21), new PointF(15, 18)); break;
                    case "capture-pin":
                        g.TranslateTransform(12, 12);
                        g.RotateTransform(45);
                        g.TranslateTransform(-12, -12);
                        Lines(new PointF(8, 3), new PointF(16, 3), new PointF(15, 10), new PointF(19, 15), new PointF(5, 15), new PointF(9, 10), new PointF(8, 3)); Line(12, 15, 12, 22); break;
                    case "capture-ocr":
                        Lines(new PointF(3, 8), new PointF(3, 3), new PointF(8, 3)); Lines(new PointF(16, 3), new PointF(21, 3), new PointF(21, 8));
                        Lines(new PointF(21, 16), new PointF(21, 21), new PointF(16, 21)); Lines(new PointF(8, 21), new PointF(3, 21), new PointF(3, 16));
                        Line(8, 8, 16, 8); Line(12, 8, 12, 16); Line(9, 16, 15, 16); break;
                    case "capture-todo": g.DrawRectangle(p, 4, 3, 16, 18); Lines(new PointF(8, 12), new PointF(11, 15), new PointF(17, 8)); break;
                    case "capture-save-as":
                        Lines(new PointF(4, 3), new PointF(16, 3), new PointF(21, 8), new PointF(21, 21), new PointF(4, 21), new PointF(4, 3));
                        g.DrawRectangle(p, 8, 3, 7, 6); g.DrawRectangle(p, 8, 14, 9, 7); break;
                    case "capture-cancel": Line(5, 5, 19, 19); Line(19, 5, 5, 19); break;
                    case "capture-copy": Lines(new PointF(4, 12), new PointF(10, 18), new PointF(21, 6)); break;
                    case "capture-grip":
                        for (int y = 5; y <= 19; y += 7) { g.FillEllipse(b, 8, y - 1, 2, 2); g.FillEllipse(b, 14, y - 1, 2, 2); }
                        break;
                }
            }
            finally { g.Restore(saved); }
        }
    }
}

