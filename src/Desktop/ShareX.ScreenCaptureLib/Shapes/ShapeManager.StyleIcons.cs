using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        // Bound color/DPI variants so repeated switches reuse images without
        // keeping every arbitrary user-selected color for the capture lifetime.
        private readonly Dictionary<(ShapeType Type, bool Fill, int Color, int Foreground, Size Size), Bitmap> styleIconCache = new();

        private Bitmap GetStyleIcon(ShapeType type, bool fill, Color color, Size size)
        {
            Color foreground = type == ShapeType.DrawingStep && fill ? GetStyleColor(false) : Color.Empty;
            var key = (type, fill, color.ToArgb(), foreground.ToArgb(), size);
            if (styleIconCache.TryGetValue(key, out var image)) return image;
            if (styleIconCache.Count >= 32)
            {
                var oldest = styleIconCache.First(pair => pair.Value != styleBorder.Image && pair.Value != styleFill.Image);
                styleIconCache.Remove(oldest.Key);
                oldest.Value.Dispose();
            }
            image = CreateStyleIcon(type, fill, color, size, foreground);
            styleIconCache.Add(key, image);
            return image;
        }

        private void DisposeStyleIcons()
        {
            foreach (var image in styleIconCache.Values) image.Dispose();
            styleIconCache.Clear();
        }

        private static Bitmap CreateStyleIcon(ShapeType type, bool fill, Color color, Size size, Color foreground)
        {
            var image = new Bitmap(size.Width, size.Height);
            using var g = Graphics.FromImage(image);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            bool step = type == ShapeType.DrawingStep;
            if (step && fill)
            {
                float side = Math.Min(size.Width, size.Height);
                g.TranslateTransform((size.Width - side) / 2, (size.Height - side) / 2);
                g.ScaleTransform(side / 24f, side / 24f);
            }
            else g.ScaleTransform(size.Width / 24f, size.Height / 24f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath();
            bool blackOutline = !fill && color.A > 0 && color.GetBrightness() < .1f;
            Color stroke = fill || color.A == 0 ? Color.WhiteSmoke : color;
            using var pen = new Pen(stroke, 1.8f)
            {
                DashStyle = fill ? DashStyle.Dash : DashStyle.Solid,
                LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round
            };
            if (step && !fill)
            {
                using var digit = CreateStepPreviewDigit(18f * .9f);
                path.AddPath(digit, false);
            }
            else if (type == ShapeType.DrawingTextOutline || !fill && type == ShapeType.DrawingSpeechBalloon)
            {
                path.AddLine(5, 5, 19, 5); path.StartFigure(); path.AddLine(12, 5, 12, 20);
                path.StartFigure(); path.AddLine(8, 20, 16, 20);
            }
            else if (type is ShapeType.DrawingArrow or ShapeType.DrawingFreehandArrow)
            {
                path.AddLine(4, 20, 20, 4); path.StartFigure();
                path.AddLines(new[] { new PointF(10, 4), new PointF(20, 4), new PointF(20, 14) });
            }
            else if (type == ShapeType.DrawingLine) path.AddLine(4, 20, 20, 4);
            else if (type == ShapeType.DrawingFreehand)
                path.AddBezier(3, 17, 7, 2, 16, 24, 21, 7);
            else if (type == ShapeType.DrawingSpeechBalloon)
                path.AddPolygon(new[] { new PointF(3, 4), new PointF(21, 4), new PointF(21, 16), new PointF(11, 16), new PointF(6, 21), new PointF(6, 16), new PointF(3, 16) });
            else if (step) path.AddEllipse(1, 1, 22, 22);
            else if (type == ShapeType.DrawingEllipse) path.AddEllipse(3, 5, 18, 14);
            else if (type == ShapeType.DrawingMagnify) path.AddEllipse(3, 3, 13, 13);
            else path.AddRectangle(new RectangleF(3, 5, 18, 14));

            if (fill && color.A > 0)
            {
                Color iconFill = step ? Color.FromArgb(255, color) : color;
                using var brush = new SolidBrush(iconFill);
                g.FillPath(brush, path);
            }
            if (blackOutline)
            {
                using var contrast = new Pen(Color.FromArgb(125, 140, 150), step ? 1 : 3)
                { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawPath(contrast, path);
            }
            // The sequence background icon is a solid disc, unlike other fills.
            if (step && !fill)
            {
                using var ink = new SolidBrush(stroke);
                g.FillPath(ink, path);
            }
            else if (!step || color.A == 0) g.DrawPath(pen, path);
            if (step && fill)
            {
                using var digit = CreateStepPreviewDigit(16f * .9f);
                // One point at 96 DPI, converted into the 24-unit template
                // used by the 18-logical-pixel toolbar icon. Leave the disc fixed.
                using var offset = new Matrix();
                offset.Translate(-(96f / 72f) * 24f / 18f, 0);
                digit.Transform(offset);
                using var ink = new SolidBrush(foreground);
                g.FillPath(ink, digit);
            }
            if (type == ShapeType.DrawingMagnify)
            {
                g.DrawLine(pen, 15, 15, 21, 21);
                if (!fill) { g.DrawLine(pen, 6, 9.5f, 13, 9.5f); g.DrawLine(pen, 9.5f, 6, 9.5f, 13); }
            }
            if (color.A == 0) g.DrawLine(pen, 6, 18, 18, 6);
            return image;
        }

        private static GraphicsPath CreateStepPreviewDigit(float height)
        {
            using var font = new Font("Microsoft YaHei", 20, FontStyle.Regular, GraphicsUnit.Pixel);
            var path = new GraphicsPath();
            path.AddString("1", font.FontFamily, (int)FontStyle.Regular, font.Size, PointF.Empty, StringFormat.GenericTypographic);
            // Fit the visible glyph, excluding font bearings, so both previews
            // remain visually centred and scale consistently with toolbar DPI.
            RectangleF bounds = path.GetBounds();
            float scale = height / bounds.Height;
            using var transform = new Matrix(scale, 0, 0, scale,
                12 - (bounds.X + bounds.Width / 2) * scale, 12 - (bounds.Y + bounds.Height / 2) * scale);
            path.Transform(transform);
            return path;
        }
    }
}
