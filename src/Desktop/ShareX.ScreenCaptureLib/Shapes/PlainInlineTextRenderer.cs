using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    // Use RichEdit's own glyph layout for the committed image as well as input.
    // Cache outside the shape: undo snapshots must never copy native handles.
    internal static class PlainInlineTextRenderer
    {
        private static readonly ConditionalWeakTable<TextDrawingShape, Raster> rasters = new();
        internal const int BalloonHorizontalPadding = 8;
        internal const int VerticalPadding = 6;

        internal static Font CreateInputFont(TextDrawingOptions options, int dpi, float zoom = 1)
        {
            float pixels = options.SizeInPixels ? options.Size : options.Size * dpi / 72f;
            return new Font(options.Font, Math.Max(1, pixels * zoom * 72f / dpi), options.Style, GraphicsUnit.Point);
        }

        private static RichTextBox CreateLayout(TextDrawingOptions options, string text, bool balloon = false)
        {
            var editor = new LayoutBox
            {
                BorderStyle = System.Windows.Forms.BorderStyle.None, Multiline = true, WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.None, DetectUrls = false,
                BackColor = Color.White, ForeColor = Color.Black, Size = new Size(32760, 32760),
                ExplicitHorizontalMargins = balloon
            };
            _ = editor.Handle;
            editor.Font = CreateInputFont(options, editor.DeviceDpi);
            if (options.InlineWrapWidth > 0)
            {
                editor.Width = Math.Max(1, (int)Math.Round(options.InlineWrapWidth));
                editor.WordWrap = true;
            }
            editor.Text = text;
            return editor;
        }

        internal static Size MeasureEditor(RichTextBox editor)
        {
            if (editor is LayoutBox layout && layout.ContentSize.Height > 0) return layout.ContentSize;
            return new Size(MeasureUnwrappedWidth(editor), Math.Max(1, editor.PreferredSize.Height));
        }

        internal static int MeasureUnwrappedWidth(RichTextBox editor, int trailingSpace = 8)
        {
            int width = 4;
            foreach (string line in editor.Text.Replace("\r", "").Split('\n'))
                width = Math.Max(width, TextRenderer.MeasureText(line.Length == 0 ? " " : line, editor.Font,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.ExpandTabs).Width + trailingSpace);
            return width;
        }

        internal class LayoutBox : RichTextBox
        {
            internal Size ContentSize { get; private set; }
            private string inkText;
            private Font inkFont;
            private Size inkSize;
            private Rectangle inkBounds;

            internal Rectangle GetVisibleBounds()
            {
                Size size = MeasureEditor(this);
                if (inkText == Text && ReferenceEquals(inkFont, Font) && inkSize == size) return inkBounds;
                // Match the export raster budget. Large documents keep the
                // existing line-box fallback without allocating an unbounded bitmap.
                Rectangle bounds = new Rectangle(Point.Empty, size);
                if (!string.IsNullOrWhiteSpace(Text) && size.Width <= 16384 && (long)size.Width * size.Height <= 16000000)
                {
                    Size = size;
                    using var mask = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
                    DrawToBitmap(mask, new Rectangle(Point.Empty, mask.Size));
                    bounds = FindVisibleBounds(mask);
                    if (bounds.IsEmpty) bounds = new Rectangle(Point.Empty, size);
                }
                inkText = Text;
                inkFont = Font;
                inkSize = size;
                return inkBounds = bounds;
            }
            private bool explicitHorizontalMargins;
            internal bool ExplicitHorizontalMargins
            {
                get => explicitHorizontalMargins;
                set { explicitHorizontalMargins = value; ApplyHorizontalMargins(); }
            }
            [DllImport("user32.dll", EntryPoint = "SendMessageW")]
            private static extern IntPtr SetMargins(IntPtr window, int message, IntPtr parameter, IntPtr margins);
            private void ApplyHorizontalMargins()
            {
                if (ExplicitHorizontalMargins && IsHandleCreated)
                    SetMargins(Handle, 0x00D3, new IntPtr(3), IntPtr.Zero); // EM_SETMARGINS, both edges
            }
            [StructLayout(LayoutKind.Sequential)]
            private struct ParagraphFormat
            {
                internal uint Size, Mask;
                internal ushort Numbering, Effects;
                internal int StartIndent, RightIndent, Offset;
                internal ushort Alignment, TabCount;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] internal int[] Tabs;
                internal int SpaceBefore, SpaceAfter, LineSpacing;
                internal short Style;
                internal byte LineSpacingRule, OutlineLevel;
                internal ushort ShadingWeight, ShadingStyle, NumberingStart, NumberingStyle, NumberingTab, BorderSpace, BorderWidth, Borders;
            }
            [DllImport("user32.dll", EntryPoint = "SendMessageW")]
            private static extern IntPtr SetParagraph(IntPtr window, int message, IntPtr parameter, ref ParagraphFormat format);
            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // RichEdit otherwise substitutes a separate Latin font (for
                // example Calibri) during keyboard input. Plain annotations
                // retain only one configured font when committed, so keep the
                // input and raster controls on that same font and size.
                LanguageOption &= ~(RichTextBoxLanguageOptions.AutoFont |
                    RichTextBoxLanguageOptions.AutoFontSizeAdjust | RichTextBoxLanguageOptions.DualFont);
                ApplyHorizontalMargins();
                SetCompactLineSpacing();
            }
            protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ApplyHorizontalMargins(); SetCompactLineSpacing(); }
            private void SetCompactLineSpacing()
            {
                if (!IsHandleCreated) return;
                var format = new ParagraphFormat
                {
                    Size = (uint)Marshal.SizeOf<ParagraphFormat>(), Mask = 0x100, Tabs = new int[32],
                    LineSpacing = (int)Math.Ceiling(Font.GetHeight(DeviceDpi) * 1440 / DeviceDpi), LineSpacingRule = 4
                };
                int start = SelectionStart, length = SelectionLength;
                SelectAll();
                SetParagraph(Handle, 0x0447, IntPtr.Zero, ref format); // EM_SETPARAFORMAT, exact natural font line height
                Select(start, length);
            }
            protected override void OnContentsResized(ContentsResizedEventArgs e)
            {
                // Native content bounds are independent of the caret's scroll
                // offset and already include RichEdit's real line spacing.
                ContentSize = new Size(Math.Max(1, e.NewRectangle.Width), Math.Max(1, e.NewRectangle.Height));
                base.OnContentsResized(e);
            }
        }

        internal static Size Measure(TextDrawingOptions options, string text, bool balloon = false)
        {
            using var editor = CreateLayout(options, text ?? "", balloon);
            var font = editor.Font;
            try { return MeasureEditor(editor); }
            finally { editor.Font = Control.DefaultFont; font.Dispose(); }
        }

        private static Rectangle FindVisibleBounds(Bitmap mask)
        {
            var rect = new Rectangle(Point.Empty, mask.Size);
            var bits = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int top = mask.Height, bottom = -1;
            var row = new byte[mask.Width * 4];
            bool HasInk(int y)
            {
                Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, row.Length);
                for (int p = 0; p < row.Length; p += 4)
                    if (row[p] + row[p + 1] + row[p + 2] < 384) return true;
                return false;
            }
            try
            {
                for (int y = 0; y < mask.Height; y++) if (HasInk(y)) { top = y; break; }
                for (int y = mask.Height - 1; y >= top; y--) if (HasInk(y)) { bottom = y; break; }
            }
            finally { mask.UnlockBits(bits); }
            return bottom >= top ? new Rectangle(0, top, mask.Width, bottom - top + 1) : Rectangle.Empty;
        }

        internal static void Draw(TextDrawingShape shape, Graphics graphics)
        {
            if (string.IsNullOrEmpty(shape.Text)) return;
            var options = shape.TextOptions;
            bool balloon = shape is SpeechBalloonDrawingShape;
            string key = string.Join("|", shape.Text, options.Font, options.Size, options.SizeInPixels, options.Style, options.Color.ToArgb(), options.InlineWrapWidth);
            Raster raster = rasters.GetValue(shape, _ => new Raster());
            if (raster.Key != key)
            {
                using var editor = CreateLayout(options, shape.Text, balloon);
                var font = editor.Font;
                try
                {
                    Size measured = MeasureEditor(editor);
                    // Oversized pasted documents use the vector fallback below;
                    // never silently crop text to fit the raster cache budget.
                    if (measured.Width > 16384 || (long)measured.Width * measured.Height > 16000000)
                    {
                        raster.Replace(key, null, measured, new Rectangle(Point.Empty, measured));
                    }
                    else
                    {
                    int width = Math.Max(1, measured.Width);
                    int height = Math.Max(1, measured.Height);
                    editor.Size = new Size(width, height);
                    using var mask = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    editor.DrawToBitmap(mask, new Rectangle(Point.Empty, mask.Size));
                    Rectangle visible = FindVisibleBounds(mask);
                    if (visible.IsEmpty) visible = new Rectangle(Point.Empty, measured);
                    Bitmap image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    var rect = new Rectangle(Point.Empty, mask.Size);
                    var src = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    var dst = image.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        var row = new byte[width * 4];
                        for (int y = 0; y < height; y++)
                        {
                            Marshal.Copy(src.Scan0 + y * src.Stride, row, 0, row.Length);
                            for (int pixel = 0; pixel < row.Length; pixel += 4)
                            {
                                int coverage = 255 - (row[pixel] + row[pixel + 1] + row[pixel + 2]) / 3;
                                row[pixel] = options.Color.B; row[pixel + 1] = options.Color.G; row[pixel + 2] = options.Color.R;
                                row[pixel + 3] = (byte)(coverage * options.Color.A / 255);
                            }
                            Marshal.Copy(row, 0, dst.Scan0 + y * dst.Stride, row.Length);
                        }
                    }
                    finally { mask.UnlockBits(src); image.UnlockBits(dst); }
                    raster.Replace(key, image, measured, visible);
                    }
                }
                finally { editor.Font = Control.DefaultFont; font.Dispose(); }
            }
            float inset = balloon ? BalloonHorizontalPadding : 0;
            float x = shape.Rectangle.X + inset;
            float contentWidth = Math.Max(1, shape.Rectangle.Width - inset * 2);
            if (options.AlignmentHorizontal == StringAlignment.Center) x += (contentWidth - raster.Measured.Width) / 2;
            else if (options.AlignmentHorizontal == StringAlignment.Far) x += contentWidth - raster.Measured.Width;
            float yOffset = options.AlignmentVertical == StringAlignment.Center ? (shape.Rectangle.Height - raster.Visible.Height) / 2 - raster.Visible.Top :
                options.AlignmentVertical == StringAlignment.Far ? shape.Rectangle.Height - raster.Measured.Height : 0;
            if (raster.Image == null)
            {
                using var font = new Font(options.Font, options.SizeInPixels ? options.Size : options.Size * 96f / 72f, options.Style, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(options.Color);
                using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces };
                if (options.InlineWrapWidth > 0)
                    graphics.DrawString(shape.Text, font, brush, new RectangleF(x, shape.Rectangle.Y + yOffset, options.InlineWrapWidth, raster.Measured.Height), format);
                else graphics.DrawString(shape.Text, font, brush, new PointF(x, shape.Rectangle.Y + yOffset), format);
                return;
            }
            graphics.DrawImage(raster.Image, new RectangleF(x, shape.Rectangle.Y + yOffset, raster.Image.Width, raster.Image.Height),
                new RectangleF(0, 0, raster.Image.Width, raster.Image.Height), GraphicsUnit.Pixel);
        }

        internal static void Release(TextDrawingShape shape)
        {
            if (rasters.TryGetValue(shape, out var raster)) { raster.Dispose(); rasters.Remove(shape); }
        }

        private sealed class Raster : IDisposable
        {
            internal string Key; internal Bitmap Image; internal Size Measured; internal Rectangle Visible;
            internal void Replace(string key, Bitmap image, Size measured, Rectangle visible) { Image?.Dispose(); Key = key; Image = image; Measured = measured; Visible = visible; }
            public void Dispose() { Image?.Dispose(); Image = null; GC.SuppressFinalize(this); }
            ~Raster() { Image?.Dispose(); }
        }
    }
}
