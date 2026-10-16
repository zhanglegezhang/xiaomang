using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;
using ShareX.Properties;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private enum OtherIcon
        {
            DomesticNews, InternationalNews, Canvas, Search, Data,
            TextFile, MarkdownFile, Gif, Calculator, Archive, Forecast, Rain, Collage, Compare, InputMethod
        }

        private sealed class OtherActionButton : Button
        {
            public bool LeftAlignedText { get; set; }
            private bool pointerInside;
            private bool pointerDown;

            public OtherActionButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                pointerInside = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                pointerInside = false;
                pointerDown = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                pointerDown = e.Button == MouseButtons.Left;
                Invalidate();
                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                pointerDown = false;
                Invalidate();
                base.OnMouseUp(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Color fill = pointerDown ? FlatAppearance.MouseDownBackColor :
                    pointerInside ? FlatAppearance.MouseOverBackColor : BackColor;
                if (fill.IsEmpty) fill = BackColor;
                e.Graphics.Clear(Parent?.BackColor ?? WidgetBackgroundColor);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using GraphicsPath card = CreateRoundedRectanglePath(
                    new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 7f * DeviceDpi / 96f, false);
                using (Brush background = new SolidBrush(fill)) e.Graphics.FillPath(background, card);
                using (Pen border = new Pen(pointerInside ? WidgetAccentColor : WidgetBorderColor, 1f))
                    e.Graphics.DrawPath(border, card);

                int iconCenter = (int)Math.Round(20d * DeviceDpi / 96d);
                int textLeft = iconCenter;
                if (Image != null)
                {
                    Rectangle iconBounds = new Rectangle(iconCenter - Image.Width / 2,
                        (Height - Image.Height) / 2,
                        Image.Width, Image.Height);
                    e.Graphics.DrawImage(Image, iconBounds);
                    textLeft = iconBounds.Right + (int)Math.Round(4d * DeviceDpi / 96d);
                }

                if (LeftAlignedText && Image == null) textLeft = Padding.Left;
                Rectangle textBounds = new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft - (LeftAlignedText ? Padding.Right : 0)), Height);
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds,
                    Enabled ? ForeColor : SystemColors.GrayText,
                    (LeftAlignedText ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
            }
        }

        private sealed class SingleLineStatusLabel : Label
        {
            public SingleLineStatusLabel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class RecordingCompactLabel : Label
        {
            public bool IsRecording { get; set; }
            public float PulseOpacity { get; set; } = 1f;
            public int PendingCount { get; set; }
            public bool PointerInside { get; set; }

            public RecordingCompactLabel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                PointerInside = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                PointerInside = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(WidgetCompactBackgroundColor);
                DrawCompactBackground(e.Graphics, ClientSize, DeviceDpi, PointerInside);
                DrawContent(e.Graphics, ClientSize);
            }

            public void DrawContent(Graphics graphics, Size size)
            {
                float scale = DeviceDpi / 96f;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (!IsRecording)
                {
                    float logoSize = 33.6f * scale;
                    if (Image != null)
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(Image, new RectangleF((size.Width - logoSize) / 2,
                            (size.Height - logoSize) / 2, logoSize, logoSize));
                    }
                    if (PendingCount > 0)
                    {
                        string count = PendingCount > 99 ? "99+" : PendingCount.ToString();
                        using FontFamily badgeFamily = new FontFamily("Segoe UI");
                        using GraphicsPath glyph = new GraphicsPath();
                        glyph.AddString(count, badgeFamily, (int)FontStyle.Regular, 9f * scale,
                            PointF.Empty, StringFormat.GenericTypographic);
                        RectangleF glyphBounds = glyph.GetBounds();
                        float badgeHeight = 13f * scale;
                        float badgeWidth = Math.Max(16f * scale, glyphBounds.Width + 6f * scale);
                        RectangleF badge = new RectangleF(size.Width - badgeWidth - 2f * scale,
                            3f * scale, badgeWidth, badgeHeight);
                        using GraphicsPath path = CreateRoundedRectanglePath(badge, 4f * scale, false);
                        using Brush fill = new SolidBrush(Color.FromArgb(245, 135, 69));
                        graphics.FillPath(fill, path);
                        using Matrix placement = new Matrix();
                        placement.Translate(badge.Left + (badge.Width - glyphBounds.Width) / 2 - glyphBounds.Left,
                            badge.Top + (badge.Height - glyphBounds.Height) / 2 - glyphBounds.Top);
                        glyph.Transform(placement);
                        using Brush textFill = new SolidBrush(Color.White);
                        graphics.FillPath(textFill, glyph);
                    }
                    return;
                }

                DrawRecordingMicrophone(graphics, size, DeviceDpi, PulseOpacity);
            }
        }

        private static void DrawCompactBackground(Graphics graphics, Size size, int dpi, bool hovered)
        {
            float scale = dpi / 96f;
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                PointF[] corners = { new PointF(0, 0), new PointF(size.Width, 0),
                    new PointF(size.Width, size.Height), new PointF(0, size.Height) };
                using GraphicsPath outline = CreateRoundedPolygonPath(corners,
                    new[] { 10f * scale, 0f, 0f, 10f * scale });
                using Brush background = new SolidBrush(hovered
                    ? Color.FromArgb(24, 39, 59) : WidgetCompactBackgroundColor);
                graphics.FillPath(background, outline);
                graphics.SetClip(outline, CombineMode.Intersect);
                // Static, subdued nebulae and stars; no animation timer or background work.
                foreach (var cloud in new[] { (0.7f, 0.3f, Color.FromArgb(26, 71, 108, 151)),
                    (0.18f, 0.8f, Color.FromArgb(20, 67, 61, 120)) })
                {
                    using GraphicsPath ellipse = new GraphicsPath();
                    ellipse.AddEllipse(size.Width * (cloud.Item1 - 0.65f), size.Height * (cloud.Item2 - 0.65f),
                        size.Width * 1.3f, size.Height * 1.3f);
                    using PathGradientBrush haze = new PathGradientBrush(ellipse)
                    {
                        CenterColor = cloud.Item3,
                        SurroundColors = new[] { Color.FromArgb(0, cloud.Item3) }
                    };
                    graphics.FillPath(haze, ellipse);
                }
                using Brush star = new SolidBrush(Color.FromArgb(165, 194, 220, 255));
                foreach (PointF point in new[] { new PointF(.12f, .16f), new PointF(.73f, .12f),
                    new PointF(.30f, .31f), new PointF(.87f, .45f), new PointF(.14f, .62f),
                    new PointF(.81f, .80f), new PointF(.37f, .88f) })
                {
                    float diameter = .6f * scale;
                    graphics.FillEllipse(star, point.X * size.Width - diameter / 2,
                        point.Y * size.Height - diameter / 2, diameter, diameter);
                }
            }
            finally { graphics.Restore(state); }
        }

        // Both entry points share one renderer and UI timer; painting never changes focus.
        private static void DrawRecordingMicrophone(Graphics graphics, Size size, int dpi, float pulseOpacity)
        {
            float scale = dpi / 96f;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int iconWidth = Math.Max(6, (int)Math.Round(9 * scale));
            int capsuleHeight = Math.Max(10, (int)Math.Round(13 * scale));
            float cradleWidth = iconWidth * 1.5f;
            float cradleHeight = capsuleHeight * 0.85f;
            float cradleTop = capsuleHeight * 0.25f;
            float stemLength = Math.Max(3f, capsuleHeight * 0.23f);
            float baseWidth = Math.Max(7f, iconWidth * 0.85f);
            float totalHeight = cradleTop + cradleHeight + stemLength + 1f;
            float x = (size.Width - iconWidth) / 2f;
            float y = (size.Height - totalHeight) / 2f;
            int alpha = Math.Clamp((int)Math.Round(75 + 180 * pulseOpacity), 0, 255);
            using Pen pen = new Pen(Color.FromArgb(alpha, 255, 91, 105), Math.Max(2.2f, dpi / 42f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using GraphicsPath capsule = CreateRoundedRectanglePath(new RectangleF(x, y, iconWidth, capsuleHeight), iconWidth / 2f, false);
            graphics.DrawPath(pen, capsule);
            float cradleX = (size.Width - cradleWidth) / 2f;
            graphics.DrawArc(pen, cradleX, y + cradleTop, cradleWidth, cradleHeight, 0, 180);
            float centerX = size.Width / 2f;
            float stemTop = y + cradleTop + cradleHeight;
            graphics.DrawLine(pen, centerX, stemTop, centerX, stemTop + stemLength);
            graphics.DrawLine(pen, centerX - baseWidth / 2f, stemTop + stemLength,
                centerX + baseWidth / 2f, stemTop + stemLength);
        }

        private sealed class RecordingTitleLogo : PictureBox
        {
            internal bool IsRecording { get; set; }
            internal float PulseOpacity { get; set; } = 1f;
            protected override void OnPaint(PaintEventArgs e)
            {
                if (IsRecording) DrawRecordingMicrophone(e.Graphics, ClientSize, DeviceDpi, PulseOpacity);
                else base.OnPaint(e);
            }
        }

        private sealed class StickerButton : Button
        {
            private bool pointerInside;

            public StickerButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                pointerInside = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                pointerInside = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnPaintBackground(PaintEventArgs pevent)
            {
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                int slant = Math.Max(3, Height / 5);
                float radius = Math.Max(3f, 5f * DeviceDpi / 96f);
                using GraphicsPath path = CreateRoundedTrapezoidPath(Width, Height, slant, radius, 0.5f);
                Color fillColor = pointerInside ? FlatAppearance.MouseOverBackColor : BackColor;
                // The button uses a shaped Region and suppresses OnPaintBackground. Clear its
                // complete backing bitmap before anti-aliased drawing so edge pixels cannot
                // retain glyphs from an earlier module or native button paint.
                e.Graphics.Clear(fillColor);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.SetClip(path);
                using (Brush brush = new SolidBrush(fillColor)) e.Graphics.FillPath(brush, path);
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using Brush textBrush = new SolidBrush(ForeColor);
                using StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                e.Graphics.DrawString(Text, Font, textBrush, ClientRectangle, format);
            }
        }

        // A shaped WinForms Region clips at whole pixels. Draw the exposed sticker edges
        // into a layered window so their partially covered pixels retain their alpha.
        private sealed class StickerOverlayForm : LayeredForm
        {
            private readonly TodoWidgetForm widget;
            private Button[] buttons = Array.Empty<Button>();
            private int hoveredIndex = -1;
            private int stickerWidth;
            private float[] pullAmounts = Array.Empty<float>();
            private readonly Timer pullTimer;
            private (Button Button, Rectangle Bounds, string Text, Color Back, Color Hover, Color Fore, Font Font)[] renderedButtons;
            private int renderedDpi;

            public StickerOverlayForm(TodoWidgetForm widget)
            {
                this.widget = widget;
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                Cursor = Cursors.Hand;
                pullTimer = new Timer { Interval = 16 };
                pullTimer.Tick += PullTimer_Tick;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams parameters = base.CreateParams;
                    parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                    return parameters;
                }
            }

            public void UpdateStickers(Button[] visibleButtons, Point screenLocation, int width)
            {
                var visuals = visibleButtons.Select(button => (button, button.Bounds, button.Text,
                    button.BackColor, button.FlatAppearance.MouseOverBackColor, button.ForeColor, button.Font)).ToArray();
                bool redraw = renderedButtons == null || renderedDpi != widget.DeviceDpi ||
                    stickerWidth != width + 1 || !renderedButtons.SequenceEqual(visuals);
                buttons = visibleButtons;
                stickerWidth = width + 1;
                if (pullAmounts.Length != buttons.Length) pullAmounts = new float[buttons.Length];
                int height = buttons.Length == 0 ? 1 : buttons[^1].Bottom;
                Size = new Size(stickerWidth + widget.D(10), height);
                // Windows imposes a minimum width on top-level forms. Keep the unused
                // part fully transparent and align the drawn edge with the main window.
                Location = new Point(screenLocation.X - (Width - stickerWidth), screenLocation.Y);
                if (hoveredIndex >= buttons.Length) hoveredIndex = -1;
                if (redraw)
                {
                    renderedButtons = visuals;
                    renderedDpi = widget.DeviceDpi;
                    Redraw();
                }
            }

            internal bool ContainsStickerPoint(Point screenPoint) => HitTest(PointToClient(screenPoint)) >= 0;

            private int HitTest(Point point)
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    Button button = buttons[i];
                    int pull = (int)Math.Round(pullAmounts[i] * widget.D(10));
                    int tabWidth = stickerWidth + pull;
                    using GraphicsPath path = CreateRoundedTrapezoidPath(
                        tabWidth, button.Height, Math.Max(3, button.Height / 5),
                        Math.Max(3f, 5f * widget.DeviceDpi / 96f), 0.5f);
                    if (path.IsVisible(point.X - (Width - tabWidth), point.Y - button.Top)) return i;
                }
                return -1;
            }

            private void Redraw()
            {
                if (IsDisposed || Width <= 0 || Height <= 0) return;
                using Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        Button button = buttons[i];
                        int pull = (int)Math.Round(pullAmounts[i] * widget.D(10));
                        int tabWidth = stickerWidth + pull;
                        using GraphicsPath path = CreateRoundedTrapezoidPath(
                            tabWidth, button.Height, Math.Max(3, button.Height / 5),
                            Math.Max(3f, 5f * widget.DeviceDpi / 96f), 0.5f);
                        using Brush background = new SolidBrush(i == hoveredIndex ? button.FlatAppearance.MouseOverBackColor : button.BackColor);
                        using Brush foreground = new SolidBrush(button.ForeColor);
                        using StringFormat format = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        graphics.TranslateTransform(Width - tabWidth, button.Top);
                        graphics.FillPath(background, path);
                        graphics.DrawString(button.Text, button.Font, foreground,
                            new RectangleF(0, 0, tabWidth - 1, button.Height), format);
                        graphics.ResetTransform();
                    }
                }
                SelectBitmap(bitmap);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int nextIndex = HitTest(e.Location);
                if (nextIndex != hoveredIndex)
                {
                    hoveredIndex = nextIndex;
                    pullTimer.Start();
                    Redraw();
                }
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                widget.Widget_MouseEnter(this, e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hoveredIndex = -1;
                pullTimer.Start();
                Redraw();
                widget.Widget_MouseLeave(this, e);
            }

            private void PullTimer_Tick(object sender, EventArgs e)
            {
                bool moving = false;
                for (int i = 0; i < pullAmounts.Length; i++)
                {
                    float target = i == hoveredIndex ? 1f : 0f;
                    float distance = target - pullAmounts[i];
                    if (Math.Abs(distance) < 0.04f) pullAmounts[i] = target;
                    else
                    {
                        pullAmounts[i] += distance * 0.32f;
                        moving = true;
                    }
                }
                Redraw();
                if (!moving) pullTimer.Stop();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) pullTimer.Dispose();
                base.Dispose(disposing);
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (e.Button == MouseButtons.Right) { widget.ShowSearchMenu(); return; }
                if (e.Button != MouseButtons.Left) return;
                int index = HitTest(e.Location);
                if (index >= 0 && index < buttons.Length) buttons[index].PerformClick();
            }
        }

        private sealed class GenieAnimationForm : LayeredForm
        {
            private readonly TodoWidgetForm widget;
            private readonly Bitmap compactImage;
            private Bitmap contentImage;
            private Button[] stickers = Array.Empty<Button>();
            private Rectangle targetBounds;

            public GenieAnimationForm(TodoWidgetForm widget, Bitmap compactImage)
            {
                this.widget = widget;
                this.compactImage = compactImage;
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams parameters = base.CreateParams;
                    parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                    return parameters;
                }
            }

            public void SetContent(Bitmap content, Button[] visibleStickers, Rectangle finalBounds)
            {
                contentImage = content;
                stickers = visibleStickers;
                targetBounds = finalBounds;
            }

            public void UpdateFrame(Rectangle bounds, float progress)
            {
                Size = bounds.Size;
                Location = new Point(bounds.Right - Width, bounds.Top);
                if (IsDisposed || Width <= 0 || Height <= 0) return;

                float t = Math.Clamp(progress, 0f, 1f);
                float narrow = Math.Clamp((1f - t) * (1f - t), 0f, 1f);
                Rectangle startBounds = widget.genieStartBounds;
                float throatHeight = Height - (Height - startBounds.Height) * narrow;
                float throatTop = (Height - throatHeight) / 2f;
                float curveX = Width * 0.68f;
                using Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    using GraphicsPath funnel = new GraphicsPath();
                    funnel.StartFigure();
                    funnel.AddBezier(new PointF(0, 0), new PointF(curveX, 0),
                        new PointF(Width - curveX, throatTop), new PointF(Width, throatTop));
                    funnel.AddLine(new PointF(Width, throatTop),
                        new PointF(Width, throatTop + throatHeight));
                    funnel.AddBezier(new PointF(Width, throatTop + throatHeight),
                        new PointF(Width - curveX, Height - 1), new PointF(curveX, Height - 1),
                        new PointF(0, Height - 1));
                    funnel.CloseFigure();
                    using Brush background = new SolidBrush(WidgetBackgroundColor);
                    graphics.FillPath(background, funnel);
                    GraphicsState state = graphics.Save();
                    graphics.SetClip(funnel);
                    if (contentImage != null)
                    {
                        int contentX = targetBounds.Left + widget.moduleSidebar.Width - Left;
                        int contentY = targetBounds.Top - Top;
                        graphics.DrawImageUnscaled(contentImage, contentX, contentY);
                    }
                    graphics.Restore(state);

                    float iconAlpha = 1f - Math.Clamp((t - 0.08f) / 0.46f, 0f, 1f);
                    if (iconAlpha > 0f)
                    {
                        using ImageAttributes attributes = new ImageAttributes();
                        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = iconAlpha });
                        int x = Width - compactImage.Width;
                        int y = (Height - compactImage.Height) / 2;
                        graphics.DrawImage(compactImage, new Rectangle(x, y, compactImage.Width, compactImage.Height),
                            0, 0, compactImage.Width, compactImage.Height, GraphicsUnit.Pixel, attributes);
                    }

                    if (t > 0.75f)
                    {
                        float alpha = Math.Clamp((t - 0.75f) / 0.25f, 0f, 1f);
                        for (int i = 0; i < stickers.Length; i++)
                        {
                            Button button = stickers[i];
                            int tabWidth = widget.moduleSidebar.Width;
                            using GraphicsPath tab = CreateRoundedTrapezoidPath(tabWidth, button.Height,
                                Math.Max(3, button.Height / 5), widget.D(5), 0.5f);
                            GraphicsState tabState = graphics.Save();
                            graphics.TranslateTransform(targetBounds.Left - Left, targetBounds.Top - Top + button.Top);
                            using Brush fill = new SolidBrush(Color.FromArgb((int)Math.Round(255 * alpha), button.BackColor));
                            graphics.FillPath(fill, tab);
                            graphics.Restore(tabState);
                        }
                    }
                }
                SelectBitmap(bitmap);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left) widget.FinishGenieAnimation();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) compactImage.Dispose();
                base.Dispose(disposing);
            }
        }

        private sealed class CompactHintForm : Form
        {
            private Font hintFont;
            private int textInset;

            public CompactHintForm()
            {
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                DoubleBuffered = true;
                BackColor = WidgetCompactHoverColor;
                ForeColor = ShareXResources.Theme.TextColor;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams parameters = base.CreateParams;
                    // Native topmost avoids Form.Show's managed TopMost focus path.
                    parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020 | 0x00000008;
                    return parameters; // No activation, taskbar entry or mouse interception.
                }
            }

            public Size UpdateText(string text, int dpi)
            {
                Font systemFont = SystemFonts.MessageBoxFont;
                float fontPixels = systemFont.SizeInPoints * dpi / 72f;
                if (hintFont == null || Math.Abs(hintFont.Size - fontPixels) > 0.01f)
                {
                    Font oldFont = hintFont;
                    hintFont = new Font(systemFont.FontFamily, fontPixels, systemFont.Style, GraphicsUnit.Pixel);
                    Font = hintFont;
                    oldFont?.Dispose();
                }
                Text = text;
                textInset = Math.Max(3, (int)Math.Round(5 * dpi / 96d));
                Size textSize = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                Invalidate();
                return new Size(textSize.Width + textInset * 2, textSize.Height + textInset * 2);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using Pen border = new Pen(SystemColors.InfoText);
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
                TextRenderer.DrawText(e.Graphics, Text, Font,
                    new Rectangle(textInset, textInset, Math.Max(1, Width - textInset * 2), Math.Max(1, Height - textInset * 2)),
                    ForeColor, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) hintFont?.Dispose();
                base.Dispose(disposing);
            }
        }

        private sealed class CompactOverlayForm : LayeredForm
        {
            private const float HoverScale = 1.15f;
            private const double HoverDurationMilliseconds = 160;
            private readonly TodoWidgetForm widget;
            private readonly Timer hoverTimer;
            private readonly Stopwatch hoverClock = new Stopwatch();
            private Size compactSize;
            private Point compactLocation;
            private float currentScale = 1f;
            private float animationStartScale = 1f;
            private float animationTargetScale = 1f;
            private bool hoverPaused;

            public CompactOverlayForm(TodoWidgetForm widget)
            {
                this.widget = widget;
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                Cursor = Cursors.Hand;
                hoverTimer = new Timer { Interval = 16 };
                hoverTimer.Tick += HoverTimer_Tick;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams parameters = base.CreateParams;
                    parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                    return parameters;
                }
            }

            public void UpdateCompact(Point screenLocation, Size size)
            {
                compactSize = size;
                compactLocation = screenLocation;
                RefreshVisual();
            }

            public Rectangle VisualBounds => new Rectangle(
                compactLocation.X + compactSize.Width - (int)Math.Round(compactSize.Width * currentScale),
                compactLocation.Y, (int)Math.Round(compactSize.Width * currentScale),
                (int)Math.Round(compactSize.Height * currentScale));

            public void RefreshVisual()
            {
                if (IsDisposed || compactSize.Width <= 0 || compactSize.Height <= 0) return;
                if (hoverPaused && !widget.compactDragging && !widget.compactRecording && Visible)
                {
                    hoverPaused = false;
                    StartHoverAnimation();
                }
                // Leave the widget's saved bounds alone. Only the layered visual grows,
                // with its original top and right edge anchored in screen coordinates.
                int visualWidth = Math.Max(1, (int)Math.Round(compactSize.Width * currentScale));
                int visualHeight = Math.Max(1, (int)Math.Round(compactSize.Height * currentScale));
                Size = new Size(visualWidth, visualHeight);
                Location = new Point(compactLocation.X + compactSize.Width - Width, compactLocation.Y);
                int left = Width - visualWidth;
                using Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.TranslateTransform(left, 0);
                    graphics.ScaleTransform(currentScale, currentScale);
                    DrawCompactBackground(graphics, compactSize, widget.compactLabel.DeviceDpi,
                        widget.compactLabel.PointerInside);
                    // Draw paths at the final hover scale instead of enlarging a raster badge.
                    widget.compactLabel.DrawContent(graphics, compactSize);
                    graphics.ResetTransform();
                }
                SelectBitmap(bitmap);
            }

            private void StartHoverAnimation()
            {
                if (IsDisposed || Disposing || !Visible) return;
                if (widget.compactDragging || widget.compactRecording)
                {
                    PauseHoverAnimation();
                    return;
                }
                Rectangle area = widget.dockScreen.WorkingArea;
                float availableScale = Math.Min((area.Bottom - compactLocation.Y) / (float)compactSize.Height,
                    (compactLocation.X + compactSize.Width - area.Left) / (float)compactSize.Width);
                float target = widget.compactLabel.PointerInside ? Math.Max(1f, Math.Min(HoverScale, availableScale)) : 1f;
                if (hoverTimer.Enabled && Math.Abs(target - animationTargetScale) < 0.0001f) return;
                if (Math.Abs(target - currentScale) < 0.0001f)
                {
                    hoverTimer.Stop();
                    hoverClock.Stop();
                    return;
                }
                animationStartScale = currentScale;
                animationTargetScale = target;
                hoverClock.Restart();
                hoverTimer.Start();
            }

            private void PauseHoverAnimation()
            {
                hoverPaused = true;
                hoverTimer.Stop();
                hoverClock.Stop();
            }

            private void HoverTimer_Tick(object sender, EventArgs e)
            {
                if (IsDisposed || Disposing || !Visible) return;
                if (widget.compactDragging || widget.compactRecording)
                {
                    PauseHoverAnimation();
                    return;
                }
                float progress = Math.Clamp((float)(hoverClock.Elapsed.TotalMilliseconds / HoverDurationMilliseconds), 0f, 1f);
                float eased = progress * progress * (3f - 2f * progress);
                currentScale = animationStartScale + (animationTargetScale - animationStartScale) * eased;
                if (progress >= 1f)
                {
                    hoverTimer.Stop();
                    hoverClock.Stop();
                }
                try { RefreshVisual(); }
                catch (Exception error) { widget.RecoverCompactOverlay(error); }
            }

            protected override void OnVisibleChanged(EventArgs e)
            {
                base.OnVisibleChanged(e);
                if (IsDisposed || Disposing || Visible) return;
                hoverTimer.Stop();
                hoverClock.Reset();
                currentScale = animationStartScale = animationTargetScale = 1f;
                hoverPaused = false;
                widget.compactLabel.PointerInside = false;
                widget.CompactSurface_MouseLeave(this);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                widget.compactLabel.PointerInside = true;
                widget.CompactSurface_MouseEnter(this);
                StartHoverAnimation();
                widget.RefreshCompactOverlay();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                widget.compactLabel.PointerInside = false;
                widget.CompactSurface_MouseLeave(this);
                StartHoverAnimation();
                widget.RefreshCompactOverlay();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left) PauseHoverAnimation();
                widget.CompactLabel_MouseDown(this, e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                widget.CompactLabel_MouseMove(this, e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                widget.CompactLabel_MouseUp(this, e);
                if (e.Button == MouseButtons.Left && Visible) widget.RefreshCompactOverlay();
            }

            protected override void OnMouseCaptureChanged(EventArgs e)
            {
                base.OnMouseCaptureChanged(e);
                widget.CompactSurface_MouseCaptureChanged(this);
                if (!Capture && Visible) widget.RefreshCompactOverlay();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    hoverTimer.Dispose();
                    hoverClock.Stop();
                }
                base.Dispose(disposing);
            }
        }

        private enum FrameCorner { TopRight, BottomRight, BottomLeft }

        private sealed class FrameCornerOverlayForm : LayeredForm
        {
            private readonly FrameCorner corner;
            private Size contentSize;
            private Rectangle patchBounds;
            private int patchSize;
            private float radius;

            public FrameCornerOverlayForm(FrameCorner corner)
            {
                this.corner = corner;
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                ShowInTaskbar = false;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams parameters = base.CreateParams;
                    parameters.ExStyle |= 0x08000020; // WS_EX_NOACTIVATE | WS_EX_TRANSPARENT
                    return parameters;
                }
            }

            public void UpdateCorner(Point contentLocation, Size contentSize, float radius, int patchSize)
            {
                bool redraw = this.contentSize != contentSize || this.radius != radius || this.patchSize != patchSize;
                this.contentSize = contentSize;
                this.radius = radius;
                this.patchSize = patchSize;
                patchBounds = corner switch
                {
                    FrameCorner.TopRight => new Rectangle(contentSize.Width - patchSize, 0, patchSize, patchSize),
                    FrameCorner.BottomRight => new Rectangle(contentSize.Width - patchSize,
                        contentSize.Height - patchSize, patchSize, patchSize),
                    _ => new Rectangle(0, contentSize.Height - patchSize, patchSize, patchSize)
                };
                Size = new Size(patchSize, patchSize);
                Point offset = BitmapOffset;
                Location = new Point(contentLocation.X + patchBounds.Left - offset.X,
                    contentLocation.Y + patchBounds.Top - offset.Y);
                if (redraw) Redraw();
            }

            private Point BitmapOffset => new Point(corner == FrameCorner.BottomLeft ? 0 : Width - patchSize,
                corner == FrameCorner.TopRight ? 0 : Height - patchSize);

            private void Redraw()
            {
                if (IsDisposed || Width <= 0 || Height <= 0) return;
                using Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    Point offset = BitmapOffset;
                    graphics.TranslateTransform(offset.X - patchBounds.Left, offset.Y - patchBounds.Top);
                    using GraphicsPath outline = CreateRoundedRectanglePath(
                        new RectangleF(0, 0, contentSize.Width, contentSize.Height), radius, true);
                    using Brush brush = new SolidBrush(WidgetBackgroundColor);
                    graphics.FillPath(brush, outline);
                }
                SelectBitmap(bitmap);
            }
        }

        private sealed class RoundedBorderPanel : Panel
        {
            public Color BorderColor { get; set; } = WidgetBorderColor;
            public int CornerRadius { get; set; } = 5;

            public RoundedBorderPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                float radius = Math.Max(1f, CornerRadius * DeviceDpi / 96f);
                using GraphicsPath path = CreateRoundedRectanglePath(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), radius, true);
                using Pen pen = new Pen(BorderColor, 1f);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawPath(pen, path);
            }
        }

        private sealed class FavoriteRowPanel : Panel
        {
            public FavoriteRowPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(WidgetBackgroundColor);
                using GraphicsPath path = CreateRoundedRectanglePath(
                    new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 5f * DeviceDpi / 96f, false);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using Brush fill = new SolidBrush(BackColor);
                e.Graphics.FillPath(fill, path);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using GraphicsPath path = CreateRoundedRectanglePath(
                    new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 5f * DeviceDpi / 96f, false);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using Pen border = new Pen(WidgetBorderColor, 1f);
                e.Graphics.DrawPath(border, path);
            }
        }

        private sealed class RoundedIconButton : Button
        {
            public Color SoftBorderColor { get; set; } = Color.FromArgb(105, 105, 105);
            public int CornerRadius { get; set; } = 5;
            private bool pointerInside;

            public RoundedIconButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                pointerInside = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                pointerInside = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnPaintBackground(PaintEventArgs pevent)
            {
                pevent.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                float radius = Math.Max(1f, CornerRadius * DeviceDpi / 96f);
                using GraphicsPath path = CreateRoundedRectanglePath(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), radius, false);
                e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = pointerInside ? FlatAppearance.MouseOverBackColor : BackColor;
                using (Brush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                using (Pen pen = new Pen(SoftBorderColor, 1f)) e.Graphics.DrawPath(pen, path);
                if (Image != null)
                {
                    int x = (Width - Image.Width) / 2;
                    int y = (Height - Image.Height) / 2;
                    e.Graphics.DrawImageUnscaled(Image, x, y);
                }
                else if (!string.IsNullOrEmpty(Text))
                {
                    TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        private sealed class DarkFlowLayoutPanel : FlowLayoutPanel
        {
            public bool DarkScrollBarThemeApplied { get; private set; }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                DarkScrollBarThemeApplied = SetWindowTheme(Handle, "DarkMode_Explorer", null) == 0;
            }
        }

        private sealed class VirtualClipboardPanel : Panel
        {
            public bool DarkScrollBarThemeApplied { get; private set; }
            public event EventHandler ViewportChanged;
            private int contentHeight;
            private int scrollOffset;

            public int ScrollMaximum => Math.Max(0, contentHeight - ClientSize.Height);
            public int ScrollPageSize => Math.Max(1, ClientSize.Height);
            public bool HasVerticalScroll => ScrollMaximum > 0;
            public int ScrollOffset => scrollOffset;

            public new Size AutoScrollMinSize
            {
                get => new Size(0, contentHeight);
                set => SetContentHeight(value.Height);
            }

            public new Point AutoScrollPosition
            {
                get => new Point(0, -scrollOffset);
                set => SetScrollOffset(Math.Max(0, value.Y));
            }

            public VirtualClipboardPanel()
            {
                AutoScroll = false;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            }

            public void SetContentHeight(int height)
            {
                contentHeight = Math.Max(0, height);
                SetScrollOffset(scrollOffset);
                ViewportChanged?.Invoke(this, EventArgs.Empty);
            }

            public void SetScrollOffset(int value)
            {
                int next = Math.Clamp(value, 0, ScrollMaximum);
                if (scrollOffset == next) return;
                scrollOffset = next;
                ViewportChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }

            protected override void OnClientSizeChanged(EventArgs e)
            {
                base.OnClientSizeChanged(e);
                SetScrollOffset(scrollOffset);
                ViewportChanged?.Invoke(this, EventArgs.Empty);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                DarkScrollBarThemeApplied = SetWindowTheme(Handle, "DarkMode_Explorer", null) == 0;
            }
        }

        private sealed class SlimListScrollBar : Control
        {
            private readonly VirtualClipboardPanel list;
            private readonly FileFavoriteList fileList;
            private bool dragging;
            private int dragOffset;

            public SlimListScrollBar(VirtualClipboardPanel list, int width)
            {
                this.list = list;
                Width = width;
                BackColor = WidgetBackgroundColor;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                list.Scroll += (s, e) => RefreshFromList();
                list.Layout += (s, e) => RefreshFromList();
                list.SizeChanged += (s, e) => RefreshFromList();
                list.ClientSizeChanged += (s, e) => RefreshFromList();
                list.ViewportChanged += (s, e) => RefreshFromList();
                MouseWheel += (s, e) =>
                {
                    if (e.Delta == 0 || !list.HasVerticalScroll) return;
                    int movement = (int)Math.Round(-(double)e.Delta * Scale(55) / SystemInformation.MouseWheelScrollDelta);
                    if (movement == 0) movement = -Math.Sign(e.Delta) * Scale(3);
                    list.SetScrollOffset(list.ScrollOffset + movement);
                    if (e is HandledMouseEventArgs handled) handled.Handled = true;
                };
                Visible = false;
            }

            public SlimListScrollBar(FileFavoriteList list, int width)
            {
                fileList = list; Width = width; BackColor = list.BackColor;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                list.ViewportChanged += (_, _) => RefreshFromList();
                list.SizeChanged += (_, _) => RefreshFromList();
                MouseWheel += (_, e) => { fileList.ScrollWheel(e.Delta);if(e is HandledMouseEventArgs handled)handled.Handled=true; };
                Visible = false;
            }

            private int Scale(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));
            private int MaximumValue => fileList?.ScrollMaximum ?? list.ScrollMaximum;
            private int CurrentValue => fileList?.TopIndex ?? list.ScrollOffset;
            private int PageSize => fileList?.ScrollPageSize ?? list.ScrollPageSize;

            public void RefreshFromList()
            {
                if (IsDisposed || Disposing) return;
                bool needed = fileList != null ? fileList.IsHandleCreated && fileList.ScrollMaximum > 0 : list.IsHandleCreated && list.HasVerticalScroll;
                Visible = needed;
                if (Visible) Invalidate();
            }

            private Rectangle GetThumb()
            {
                int inset = Scale(4);
                int trackHeight = Math.Max(0, ClientSize.Height - inset * 2);
                int page = PageSize;
                int maximum = MaximumValue;
                int thumbHeight = Math.Max(Scale(24), (int)Math.Round((double)trackHeight * page / (maximum + page)));
                thumbHeight = Math.Min(trackHeight, thumbHeight);
                int travel = Math.Max(0, trackHeight - thumbHeight);
                int top = maximum > 0 ? (int)Math.Round((double)travel * CurrentValue / maximum) : 0;
                return new Rectangle(Scale(4), inset + top, Scale(8), thumbHeight);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.Clear(BackColor);
                if (!Visible) return;
                Rectangle thumb = GetThumb();
                using GraphicsPath path = CreateRoundedRectanglePath(thumb, Scale(4), false);
                using (Brush brush = new SolidBrush(Color.FromArgb(154, 154, 154))) e.Graphics.FillPath(brush, path);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left || MaximumValue <= 0) return;
                Rectangle thumb = GetThumb();
                if (thumb.Contains(e.Location))
                {
                    dragging = true;
                    dragOffset = e.Y - thumb.Top;
                    Capture = true;
                }
                else
                {
                    int page = PageSize;
                    SetValue(CurrentValue + (e.Y < thumb.Top ? -page : page));
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (!dragging) return;
                int inset = Scale(4);
                Rectangle thumb = GetThumb();
                int travel = Math.Max(1, ClientSize.Height - inset * 2 - thumb.Height);
                int top = Math.Clamp(e.Y - dragOffset - inset, 0, travel);
                SetValue((int)Math.Round((double)top * MaximumValue / travel));
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                dragging = false;
                Capture = false;
            }

            private void SetValue(int value)
            {
                int next = Math.Clamp(value, 0, MaximumValue);
                if (next == CurrentValue) return;
                if (fileList != null) fileList.SetScrollOffset(next); else list.SetScrollOffset(next);
                Invalidate();
            }
        }

        private sealed class ArrowlessTextScrollBar : Control
        {
            private const int EmGetFirstVisibleLine = 0x00CE;
            private const int EmGetLineCount = 0x00BA;
            private const int EmLineScroll = 0x00B6;
            private readonly TextBox textBox;
            private bool dragging;
            private int dragOffset;

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

            public ArrowlessTextScrollBar(TextBox textBox)
            {
                this.textBox = textBox;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
                Cursor = Cursors.Hand;
                textBox.TextChanged += (sender, e) => Invalidate();
                textBox.Resize += (sender, e) => Invalidate();
                textBox.KeyUp += (sender, e) => Invalidate();
                textBox.MouseWheel += (sender, e) => BeginInvoke(Invalidate);
            }

            private int FirstVisibleLine => textBox.IsHandleCreated ? SendMessage(textBox.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;
            private int TotalLines => textBox.IsHandleCreated ? Math.Max(1, SendMessage(textBox.Handle, EmGetLineCount, IntPtr.Zero, IntPtr.Zero).ToInt32()) : 1;
            private int VisibleLines => Math.Max(1, textBox.ClientSize.Height / Math.Max(1, textBox.Font.Height));
            private int Maximum => Math.Max(0, TotalLines - VisibleLines);

            private Rectangle ThumbBounds
            {
                get
                {
                    int maximum = Maximum;
                    if (maximum == 0) return Rectangle.Empty;
                    int height = Math.Max(12, Height * VisibleLines / Math.Max(1, TotalLines));
                    height = Math.Min(Height, height);
                    int y = (Height - height) * Math.Min(maximum, FirstVisibleLine) / maximum;
                    return new Rectangle(0, y, Width, height);
                }
            }

            private void ScrollTo(int line)
            {
                if (!textBox.IsHandleCreated) return;
                int target = Math.Clamp(line, 0, Maximum);
                int delta = target - FirstVisibleLine;
                if (delta != 0) SendMessage(textBox.Handle, EmLineScroll, IntPtr.Zero, new IntPtr(delta));
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                Rectangle thumb = ThumbBounds;
                if (thumb.IsEmpty) return;
                using Brush track = new SolidBrush(Color.FromArgb(65, 65, 65));
                using Brush handle = new SolidBrush(Color.FromArgb(155, 155, 155));
                e.Graphics.FillRectangle(track, ClientRectangle);
                e.Graphics.FillRectangle(handle, thumb);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left || Maximum == 0) return;
                Rectangle thumb = ThumbBounds;
                dragging = true;
                Capture = true;
                dragOffset = thumb.Contains(e.Location) ? e.Y - thumb.Top : thumb.Height / 2;
                ScrollFromPointer(e.Y);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (dragging) ScrollFromPointer(e.Y);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                dragging = false;
                Capture = false;
            }

            private void ScrollFromPointer(int y)
            {
                Rectangle thumb = ThumbBounds;
                int travel = Math.Max(1, Height - thumb.Height);
                int top = Math.Clamp(y - dragOffset, 0, travel);
                ScrollTo((int)Math.Round((double)top * Maximum / travel));
            }
        }

        private static PointF MoveTowards(PointF from, PointF to, float distance)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            if (length <= 0.01f || distance <= 0) return from;
            float ratio = Math.Min(0.45f, distance / length);
            return new PointF(from.X + dx * ratio, from.Y + dy * ratio);
        }

        private static GraphicsPath CreateRoundedPolygonPath(PointF[] vertices, float[] radii)
        {
            GraphicsPath path = new GraphicsPath();
            PointF firstStart = MoveTowards(vertices[0], vertices[^1], radii[0]);
            PointF previousEnd = firstStart;
            path.StartFigure();
            for (int i = 0; i < vertices.Length; i++)
            {
                PointF current = vertices[i];
                PointF previous = vertices[(i + vertices.Length - 1) % vertices.Length];
                PointF next = vertices[(i + 1) % vertices.Length];
                PointF start = MoveTowards(current, previous, radii[i]);
                PointF end = MoveTowards(current, next, radii[i]);
                if (i == 0) path.AddLine(firstStart, start);
                else path.AddLine(previousEnd, start);
                PointF control1 = new PointF(start.X + (current.X - start.X) * 2f / 3f, start.Y + (current.Y - start.Y) * 2f / 3f);
                PointF control2 = new PointF(end.X + (current.X - end.X) * 2f / 3f, end.Y + (current.Y - end.Y) * 2f / 3f);
                path.AddBezier(start, control1, control2, end);
                previousEnd = end;
            }
            path.AddLine(previousEnd, firstStart);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath CreateRoundedTrapezoidPath(float width, float height, float slant, float radius, float inset = 0)
        {
            PointF[] vertices =
            {
                new PointF(inset, inset), new PointF(width - inset, inset),
                new PointF(width - inset, height - inset), new PointF(inset, height - slant - inset)
            };
            // The two right corners join the popup and stay square. Only exposed corners
            // receive the requested five-pixel softening.
            return CreateRoundedPolygonPath(vertices, new[] { radius, 0f, 0f, radius });
        }

        private static GraphicsPath CreateRoundedRectanglePath(RectangleF bounds, float radius, bool squareTopLeft)
        {
            PointF[] vertices =
            {
                new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top),
                new PointF(bounds.Right, bounds.Bottom), new PointF(bounds.Left, bounds.Bottom)
            };
            return CreateRoundedPolygonPath(vertices, new[] { squareTopLeft ? 0f : radius, radius, radius, radius });
        }



        private Bitmap CreateOtherButtonIcon(OtherIcon kind)
        {
            int size = Math.Max(21, D(21));
            Bitmap icon = new Bitmap(size, size);
            using Graphics graphics = Graphics.FromImage(icon);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.ScaleTransform(size / 16f, size / 16f);
            Color accent = kind switch
            {
                OtherIcon.DomesticNews or OtherIcon.Search => Color.FromArgb(255, 119, 55),
                OtherIcon.InternationalNews => Color.FromArgb(28, 145, 255),
                OtherIcon.Canvas or OtherIcon.Forecast => Color.FromArgb(56, 178, 242),
                OtherIcon.Data => Color.FromArgb(21, 209, 100),
                OtherIcon.MarkdownFile => Color.FromArgb(91, 189, 244),
                OtherIcon.Gif => Color.FromArgb(176, 83, 234),
                OtherIcon.Calculator => Color.FromArgb(120, 169, 255),
                OtherIcon.Rain => Color.FromArgb(255, 195, 33),
                OtherIcon.Collage => Color.FromArgb(245, 74, 150),
                OtherIcon.Compare => Color.FromArgb(249, 85, 72),
                OtherIcon.InputMethod => Color.FromArgb(81, 197, 162),
                _ => Color.White
            };
            Color ink = Color.FromArgb(30, 43, 52);
            using Pen pen = new Pen(accent, 1.65f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using Pen darkPen = new Pen(ink, 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using Pen whitePen = new Pen(Color.White, 1.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using Brush accentBrush = new SolidBrush(accent);
            using Brush darkBrush = new SolidBrush(ink);
            PointF P(float x, float y) => new PointF(x, y);
            void Line(Pen stroke, float x1, float y1, float x2, float y2) =>
                graphics.DrawLine(stroke, x1, y1, x2, y2);
            void FillRounded(float x, float y, float width, float height, float radius, Brush fill)
            {
                using GraphicsPath path = CreateRoundedRectanglePath(new RectangleF(x, y, width, height), radius, false);
                graphics.FillPath(fill, path);
            }
            void Page(Brush fill)
            {
                graphics.FillPolygon(fill, new[] { P(2.3f, 0.8f), P(10.2f, 0.8f), P(13.8f, 4.3f),
                    P(13.8f, 15.2f), P(2.3f, 15.2f) });
                Line(darkPen, 10.2f, 0.9f, 10.2f, 4.3f);
                Line(darkPen, 10.2f, 4.3f, 13.6f, 4.3f);
            }

            switch (kind)
            {
                case OtherIcon.DomesticNews:
                    FillRounded(2.3f, 1.4f, 11.4f, 13.2f, 1.1f, accentBrush);
                    graphics.FillRectangle(darkBrush, 4.1f, 4f, 2.2f, 2.2f);
                    Line(darkPen, 7.5f, 4.4f, 11.8f, 4.4f);
                    Line(darkPen, 7.5f, 6f, 11.8f, 6f);
                    Line(darkPen, 4.1f, 8.2f, 11.8f, 8.2f);
                    Line(darkPen, 4.1f, 10.3f, 11.8f, 10.3f);
                    Line(darkPen, 4.1f, 12.4f, 9.8f, 12.4f);
                    break;
                case OtherIcon.InternationalNews:
                    graphics.DrawEllipse(pen, 1.4f, 1.4f, 13.2f, 13.2f);
                    graphics.DrawEllipse(pen, 5.1f, 1.4f, 5.8f, 13.2f);
                    Line(pen, 1.9f, 8f, 14.1f, 8f);
                    graphics.DrawArc(pen, 3.3f, 3.4f, 9.4f, 9.2f, 194, 151);
                    graphics.DrawArc(pen, 3.3f, 3.4f, 9.4f, 9.2f, 14, 151);
                    break;
                case OtherIcon.Canvas:
                    graphics.FillEllipse(accentBrush, 1.1f, 1.2f, 13.8f, 13.8f);
                    graphics.FillEllipse(darkBrush, 2.9f, 6.3f, 3.5f, 3.5f);
                    graphics.FillEllipse(darkBrush, 4.2f, 3.2f, 1.7f, 1.7f);
                    graphics.FillEllipse(darkBrush, 8.1f, 3f, 1.7f, 1.7f);
                    graphics.FillEllipse(darkBrush, 10.9f, 5.1f, 1.7f, 1.7f);
                    Line(darkPen, 9f, 12.8f, 14.1f, 5.7f);
                    break;
                case OtherIcon.Search:
                    using (Pen searchPen = new Pen(accent, 2.8f) { StartCap = LineCap.Round })
                    {
                        graphics.DrawEllipse(searchPen, 2f, 1.5f, 8.3f, 8.3f);
                        Line(searchPen, 9.2f, 9.2f, 14f, 14f);
                    }
                    break;
                case OtherIcon.Data:
                    Page(accentBrush);
                    Line(darkPen, 4.6f, 5.4f, 8.7f, 5.4f);
                    Line(darkPen, 4.6f, 7.9f, 11.2f, 7.9f);
                    Line(darkPen, 4.6f, 10.4f, 11.2f, 10.4f);
                    Line(darkPen, 4.6f, 12.7f, 8.7f, 12.7f);
                    break;
                case OtherIcon.TextFile:
                    Page(accentBrush);
                    Line(darkPen, 4.5f, 7.1f, 11.6f, 7.1f);
                    Line(darkPen, 4.5f, 9.4f, 11.6f, 9.4f);
                    Line(darkPen, 4.5f, 11.7f, 10f, 11.7f);
                    break;
                case OtherIcon.MarkdownFile:
                    using (Brush tile = new LinearGradientBrush(new RectangleF(0.8f, 2.1f, 14.4f, 11.8f),
                        Color.FromArgb(108, 204, 248), Color.FromArgb(59, 161, 219), LinearGradientMode.Vertical))
                        FillRounded(0.8f, 2.1f, 14.4f, 11.8f, 1.1f, tile);
                    using (Pen glyph = new Pen(ink, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        Line(glyph, 3.2f, 10.7f, 3.2f, 5.4f);
                        Line(glyph, 3.2f, 5.4f, 5.4f, 8.2f);
                        Line(glyph, 5.4f, 8.2f, 7.5f, 5.4f);
                        Line(glyph, 7.5f, 5.4f, 7.5f, 10.7f);
                        Line(glyph, 10.9f, 5.1f, 10.9f, 10.2f);
                        Line(glyph, 8.8f, 8.8f, 10.9f, 10.9f);
                        Line(glyph, 10.9f, 10.9f, 13f, 8.8f);
                    }
                    break;
                case OtherIcon.Gif:
                    FillRounded(1f, 3.2f, 10f, 9.6f, 1.5f, accentBrush);
                    graphics.FillPolygon(accentBrush, new[] { P(10.2f, 6.1f), P(15f, 3.8f), P(15f, 12.1f), P(10.2f, 9.8f) });
                    graphics.FillPolygon(Brushes.White, new[] { P(4.4f, 5.7f), P(4.4f, 10.4f), P(8.2f, 8f) });
                    break;
                case OtherIcon.Calculator:
                    FillRounded(2.4f, 0.7f, 11.2f, 14.6f, 1.2f, accentBrush);
                    FillRounded(4.3f, 2.6f, 7.4f, 2.8f, 0.4f, darkBrush);
                    foreach (float x in new[] { 5f, 8f, 11f })
                        foreach (float y in new[] { 8f, 11.7f }) graphics.FillEllipse(darkBrush, x - 0.65f, y - 0.65f, 1.3f, 1.3f);
                    break;
                case OtherIcon.Archive:
                    FillRounded(1.1f, 1.3f, 13.8f, 13.4f, 0.8f, accentBrush);
                    graphics.FillRectangle(darkBrush, 2.4f, 3.3f, 11.2f, 9.4f);
                    using (Font archiveFont = new Font("Arial", 8.4f, FontStyle.Bold, GraphicsUnit.Pixel))
                        graphics.DrawString("7z", archiveFont, Brushes.White, 3.2f, 4f, StringFormat.GenericTypographic);
                    break;
                case OtherIcon.Forecast:
                    using (GraphicsPath cloud = new GraphicsPath())
                    {
                        cloud.StartFigure();
                        cloud.AddBezier(P(3.8f, 12.9f), P(0.4f, 12.9f), P(0.2f, 8.7f), P(3.8f, 7.5f));
                        cloud.AddBezier(P(3.8f, 7.5f), P(4.8f, 2.5f), P(11.1f, 2.2f), P(12.3f, 7.2f));
                        cloud.AddBezier(P(12.3f, 7.2f), P(15.7f, 7.1f), P(16.2f, 12.4f), P(12.2f, 12.9f));
                        cloud.CloseFigure();
                        using Brush cloudFill = new LinearGradientBrush(new RectangleF(0f, 3f, 16f, 10f),
                            Color.FromArgb(85, 198, 252), Color.FromArgb(41, 157, 225), LinearGradientMode.Vertical);
                        graphics.FillPath(cloudFill, cloud);
                    }
                    using (Pen cloudHighlight = new Pen(Color.FromArgb(120, 183, 234, 255), 0.7f))
                        graphics.DrawArc(cloudHighlight, 4.6f, 4.2f, 6.8f, 6.2f, 196f, 135f);
                    break;
                case OtherIcon.Rain:
                    using (Pen sunRay = new Pen(Color.FromArgb(255, 196, 34), 1.35f)
                    { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    foreach (int angle in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
                    {
                        float radians = angle * (float)Math.PI / 180f;
                        Line(sunRay, 8f + 5.6f * (float)Math.Cos(radians), 8f + 5.6f * (float)Math.Sin(radians),
                            8f + 7.1f * (float)Math.Cos(radians), 8f + 7.1f * (float)Math.Sin(radians));
                    }
                    using (Brush sunFill = new LinearGradientBrush(new RectangleF(4.1f, 4.1f, 7.8f, 7.8f),
                        Color.FromArgb(255, 221, 69), Color.FromArgb(255, 165, 12), LinearGradientMode.Vertical))
                        graphics.FillEllipse(sunFill, 4.1f, 4.1f, 7.8f, 7.8f);
                    using (Pen sunRim = new Pen(Color.FromArgb(255, 202, 45), 0.6f))
                        graphics.DrawEllipse(sunRim, 4.1f, 4.1f, 7.8f, 7.8f);
                    break;
                case OtherIcon.Collage:
                    FillRounded(1.2f, 1.2f, 6.2f, 6.2f, 0.7f, accentBrush);
                    FillRounded(8.6f, 1.2f, 6.2f, 6.2f, 0.7f, accentBrush);
                    FillRounded(1.2f, 8.6f, 6.2f, 6.2f, 0.7f, accentBrush);
                    FillRounded(8.6f, 8.6f, 6.2f, 6.2f, 0.7f, accentBrush);
                    break;
                case OtherIcon.Compare:
                    FillRounded(1.6f, 3.4f, 10.3f, 2.5f, 0.4f, accentBrush);
                    graphics.FillPolygon(accentBrush, new[] { P(9.5f, 1.3f), P(15f, 4.7f), P(9.5f, 8f) });
                    FillRounded(4.1f, 10.1f, 10.3f, 2.5f, 0.4f, accentBrush);
                    graphics.FillPolygon(accentBrush, new[] { P(6.5f, 8f), P(1f, 11.3f), P(6.5f, 14.7f) });
                    break;
                case OtherIcon.InputMethod:
                    FillRounded(0.8f, 3.3f, 14.4f, 9.4f, 1.3f, accentBrush);
                    foreach (float x in new[] { 3.2f, 6.4f, 9.6f, 12.8f })
                        foreach (float y in new[] { 5.9f, 8.3f })
                            graphics.FillRectangle(darkBrush, x - 0.7f, y - 0.6f, 1.4f, 1.2f);
                    Line(darkPen, 4.8f, 10.7f, 11.2f, 10.7f);
                    break;
            }
            return icon;
        }

        private Bitmap CreateAddIcon()
        {
            int size = Math.Max(12, D(12));
            float scale = size / 12f;
            Bitmap icon = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(icon))
            using (Pen pen = new Pen(Color.WhiteSmoke, Math.Max(1f, 1.05f * scale)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawLine(pen, 2.5f * scale, 6 * scale, 9.5f * scale, 6 * scale);
                graphics.DrawLine(pen, 6 * scale, 2.5f * scale, 6 * scale, 9.5f * scale);
            }
            return icon;
        }

        private Bitmap CreateDeleteIcon()
        {
            // Draw at the current monitor DPI so the icon remains large inside the button,
            // while keeping the strokes visually light.
            int size = Math.Max(15, D(15));
            float scale = size / 22f;
            Bitmap icon = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(icon))
            using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(0.85f, 1.05f * scale)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                PointF P(float x, float y) => new PointF(x * scale, y * scale);
                graphics.DrawLine(pen, P(4, 6), P(18, 6));
                graphics.DrawLine(pen, P(8.5f, 3.5f), P(13.5f, 3.5f));
                graphics.DrawLine(pen, P(6, 8), P(7.5f, 19));
                graphics.DrawLine(pen, P(16, 8), P(14.5f, 19));
                graphics.DrawLine(pen, P(7.5f, 19), P(14.5f, 19));
                graphics.DrawLine(pen, P(9.5f, 9), P(10, 17));
                graphics.DrawLine(pen, P(12.5f, 9), P(12, 17));
            }
            return icon;
        }

        private Bitmap CreateSpeakerIcon(bool stop)
        {
            int size = Math.Max(13, D(13));
            float scale = size / 16f;
            Bitmap icon = new Bitmap(size, size);
            using Graphics graphics = Graphics.FromImage(icon);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = Color.FromArgb(185, 255, 255, 255);
            if (stop)
            {
                using Brush brush = new SolidBrush(color);
                graphics.FillRectangle(brush, 4 * scale, 4 * scale, 8 * scale, 8 * scale);
                return icon;
            }

            using Pen pen = new Pen(color, Math.Max(1f, 1.25f * scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            PointF[] speaker =
            {
                new PointF(2.5f * scale, 6f * scale), new PointF(5.5f * scale, 6f * scale),
                new PointF(9f * scale, 3f * scale), new PointF(9f * scale, 13f * scale),
                new PointF(5.5f * scale, 10f * scale), new PointF(2.5f * scale, 10f * scale)
            };
            graphics.DrawPolygon(pen, speaker);
            graphics.DrawArc(pen, 7.5f * scale, 5f * scale, 5f * scale, 6f * scale, -55, 110);
            graphics.DrawArc(pen, 7f * scale, 2.5f * scale, 8f * scale, 11f * scale, -52, 104);
            return icon;
        }

        private Button CreateOtherButton(string text)
        {
            Button button = new OtherActionButton
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 8f),
                BackColor = WidgetInputColor,
                ForeColor = ShareXResources.Theme.TextColor
            };
            button.FlatAppearance.BorderColor = WidgetRowColor;
            button.FlatAppearance.MouseOverBackColor = WidgetHoverColor;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(88, 88, 88);
            return button;
        }

        private Button CreateCompactAddButton(string accessibleName)
        {
            RoundedIconButton button = new RoundedIconButton
            {
                Text = "+ 添加",
                Font = new Font(Font.FontFamily, 8f),
                BackColor = WidgetInputColor,
                ForeColor = ShareXResources.Theme.TextColor,
                SoftBorderColor = WidgetBorderColor,
                CornerRadius = 7,
                AccessibleName = accessibleName
            };
            button.FlatAppearance.MouseOverBackColor = WidgetHoverColor;
            return button;
        }

        private Button CreateButton(string text, Point location, Size size)
        {
            Button button = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 8f),
                BackColor = WidgetInputColor,
                ForeColor = ShareXResources.Theme.TextColor
            };
            button.FlatAppearance.BorderColor = WidgetRowColor;
            button.FlatAppearance.MouseOverBackColor = WidgetHoverColor;
            return button;
        }

    }
}
