using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal static class SelectionCursor
    {
        internal static Bitmap Draw(int dpi, float? angle)
        {
            float scale = Math.Clamp(dpi / 96f, 1f, 4f);
            int size = (int)Math.Ceiling(33 * scale) | 1;
            var image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(image);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(size / 2, size / 2);
            g.ScaleTransform(scale, scale);
            using var path = new GraphicsPath();
            if (angle.HasValue)
            {
                g.RotateTransform(angle.Value);
                path.AddLine(-11, 0, 11, 0);
                path.StartFigure(); path.AddLines(new[] { new PointF(-6, -5), new PointF(-11, 0), new PointF(-6, 5) });
                path.StartFigure(); path.AddLines(new[] { new PointF(6, -5), new PointF(11, 0), new PointF(6, 5) });
            }
            else
            {
                path.AddLine(-12, 0, -4, 0); path.StartFigure(); path.AddLine(4, 0, 12, 0);
                path.StartFigure(); path.AddLine(0, -12, 0, -4); path.StartFigure(); path.AddLine(0, 4, 0, 12);
            }
            using var outline = new Pen(Color.Black, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var stroke = new Pen(Color.White, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawPath(outline, path); g.DrawPath(stroke, path);
            return image;
        }

        internal static Cursor Create(int dpi, float? angle = null)
        {
            using Bitmap image = Draw(dpi, angle);
            int width = image.Width, height = image.Height, maskStride = ((width + 31) / 32) * 4;
            using var data = new MemoryStream();
            using (var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, true))
            {
                writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)1);
                writer.Write((byte)width); writer.Write((byte)height); writer.Write((ushort)0);
                writer.Write((ushort)(width / 2)); writer.Write((ushort)(height / 2));
                writer.Write(40 + width * height * 4 + maskStride * height); writer.Write(22);
                writer.Write(40); writer.Write(width); writer.Write(height * 2);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(width * height * 4);
                writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                BitmapData pixels = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[width * 4]; var masks = new byte[maskStride * height];
                    for (int y = height - 1; y >= 0; y--)
                    {
                        Marshal.Copy(pixels.Scan0 + y * pixels.Stride, row, 0, row.Length);
                        writer.Write(row);
                        for (int x = 0; x < width; x++)
                            if (row[x * 4 + 3] == 0) masks[(height - 1 - y) * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));
                    }
                    writer.Write(masks);
                }
                finally { image.UnlockBits(pixels); }
            }
            data.Position = 0;
            return new Cursor(data);
        }
    }
}
