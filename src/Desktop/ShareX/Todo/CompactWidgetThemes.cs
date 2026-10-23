using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using ShareX.HelpersLib;

namespace ShareX
{
    internal static class CompactWidgetThemes
    {
        internal static readonly string[] Names = { "默认", "绯红", "赤雪", "青瓷", "酒红", "霜雪", "薄荷" };
        // The small immutable bitmaps are shared for the process lifetime.
        // Default-theme users never decode the optional atlas.
        private static readonly Lazy<Bitmap[]> backgrounds = new Lazy<Bitmap[]>(() => Load(false));
        internal static int Normalize(int theme) => theme >= 0 && theme < Names.Length ? theme : 0;

        private static readonly Lazy<Bitmap[]> robots = new Lazy<Bitmap[]>(() => Load(true));

        internal static Image GetLogo(int theme, Image fallback)
        {
            theme = Normalize(theme);
            return theme == 0 ? fallback : robots.Value?[theme - 1] ?? fallback;
        }

        private static Bitmap[] Load(bool robot)
        {
            Bitmap[] result = new Bitmap[6];
            try
            {
                using var stream = typeof(CompactWidgetThemes).Assembly.GetManifestResourceStream(robot ? "ShareX.Assets.CompactThemeRobots.png" : "ShareX.Assets.CompactThemes.png");
                if (stream == null) return null;
                using Image atlas = Image.FromStream(stream);
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = new Bitmap(256, 256);
                    using Graphics g = Graphics.FromImage(result[i]);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    RectangleF source = new RectangleF(i * atlas.Width / 6f, 0, atlas.Width / 6f, atlas.Height);
                    RectangleF target = new RectangleF(0, 0, 256, 256);
                    if (robot)
                    {
                        source.Y = atlas.Height * 0.15f;
                        source.Height = atlas.Height * 0.70f;
                        float width = 256 * source.Width / source.Height;
                        target = new RectangleF((256 - width) / 2, 0, width, 256);
                    }
                    g.DrawImage(atlas, target, source, GraphicsUnit.Pixel);
                }
                return result;
            }
            catch (Exception error)
            {
                foreach (Bitmap image in result) image?.Dispose();
                DebugHelper.WriteException(error, "贴片主题图片加载失败，使用默认背景。");
                return null;
            }
        }

        internal static bool Draw(Graphics graphics, Size size, int theme, bool hovered)
        {
            theme = Normalize(theme);
            if (theme == 0) return false;
            Bitmap[] images = backgrounds.Value;
            if (images == null) return false;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(images[theme - 1], new Rectangle(Point.Empty, size));
            if (hovered)
            {
                using Brush tint = new SolidBrush(Color.FromArgb(14, Color.White));
                graphics.FillRectangle(tint, new Rectangle(Point.Empty, size));
            }
            return true;
        }
    }
}
