using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShareX.ScreenCaptureLib
{
    internal enum LongCaptureFrameResult
    {
        First,
        Appended,
        Prepended,
        Unchanged,
        NoOverlap,
        LimitReached
    }

    internal interface ILongCaptureStitcher : IDisposable
    {
        int Width { get; }
        int Height { get; }
        bool HasImage { get; }
        LongCaptureFrameResult AddFrame(Bitmap frame);
        Bitmap CreatePreview(int maxWidth, int maxHeight);
        Bitmap CreateResult();
    }

    /// <summary>
    /// Collects only newly exposed rows. Keeping strips instead of reallocating a growing
    /// full-size bitmap on every wheel tick keeps long sessions responsive.
    /// </summary>
    internal sealed class LongCaptureStitcher : ILongCaptureStitcher
    {
        internal const int MaximumHeight = 24000;
        private readonly List<Bitmap> strips = new List<Bitmap>();
        private byte[] previousSamples;
        private int sampleColumns;
        private int frameHeight;
        private int width;
        private int viewportTop;
        private int capturedTop;
        private int capturedBottom;
        private int fixedHeaderRows;

        public int Width => width;
        public int Height { get; private set; }
        public bool HasImage => strips.Count > 0;

        public LongCaptureFrameResult AddFrame(Bitmap frame)
        {
            if (frame == null || frame.Width < 120 || frame.Height < 100)
            {
                return LongCaptureFrameResult.NoOverlap;
            }

            using (Bitmap normalized = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(normalized))
                {
                    graphics.DrawImageUnscaled(frame, 0, 0);
                }

                int columns = Math.Min(96, Math.Max(32, normalized.Width / 8));
                byte[] samples = SampleGrayscale(normalized, columns);

                if (!HasImage)
                {
                    width = normalized.Width;
                    frameHeight = normalized.Height;
                    sampleColumns = columns;
                    strips.Add((Bitmap)normalized.Clone());
                    previousSamples = samples;
                    Height = frameHeight;
                    viewportTop = 0;
                    capturedTop = 0;
                    capturedBottom = frameHeight;
                    return LongCaptureFrameResult.First;
                }

                if (normalized.Width != width || normalized.Height != frameHeight)
                {
                    return LongCaptureFrameResult.NoOverlap;
                }

                double unchangedDifference = CompareSamples(previousSamples, samples, sampleColumns, frameHeight, 0, 18);
                if (unchangedDifference < 2.0)
                {
                    return LongCaptureFrameResult.Unchanged;
                }

                int maximumShift = frameHeight - 72;
                int bestDownShift = 0;
                int bestUpShift = 0;
                double bestDownSample = double.MaxValue;
                double bestUpSample = double.MaxValue;
                for (int shift = 1; shift <= maximumShift; shift++)
                {
                    double down = CompareSamples(previousSamples, samples, sampleColumns, frameHeight, shift, 64);
                    if (down < bestDownSample)
                    {
                        bestDownSample = down;
                        bestDownShift = shift;
                    }

                    double up = CompareSamples(samples, previousSamples, sampleColumns, frameHeight, shift, 64);
                    if (up < bestUpSample)
                    {
                        bestUpSample = up;
                        bestUpShift = shift;
                    }
                }

                double downDifference = bestDownShift == 0 ? double.MaxValue :
                    CompareSamples(previousSamples, samples, sampleColumns, frameHeight, bestDownShift, frameHeight);
                double upDifference = bestUpShift == 0 ? double.MaxValue :
                    CompareSamples(samples, previousSamples, sampleColumns, frameHeight, bestUpShift, frameHeight);
                bool movingDown = downDifference < upDifference;
                double bestDifference = movingDown ? downDifference : upDifference;
                double otherDifference = movingDown ? upDifference : downDifference;
                int shiftAmount = movingDown ? bestDownShift : bestUpShift;

                // Require a clear direction as well as a strong overlap. Repeated or animated
                // content must not silently duplicate rows at either end of the result.
                if (shiftAmount == 0 || bestDifference > 12.0 ||
                    bestDifference > unchangedDifference * 0.72 ||
                    otherDifference <= bestDifference + Math.Max(1.5, bestDifference * 0.25))
                {
                    return LongCaptureFrameResult.NoOverlap;
                }

                int nextViewportTop = viewportTop + (movingDown ? shiftAmount : -shiftAmount);
                int nextViewportBottom = nextViewportTop + frameHeight;
                int prependRows = Math.Max(0, capturedTop - nextViewportTop);
                int appendRows = Math.Max(0, nextViewportBottom - capturedBottom);
                int newRowCount = prependRows + appendRows;
                if (newRowCount > MaximumHeight - Height)
                {
                    return LongCaptureFrameResult.LimitReached;
                }

                if (fixedHeaderRows == 0 && capturedTop == 0 && strips[0].Height == frameHeight)
                {
                    fixedHeaderRows = DetectFixedHeaderRows(previousSamples, samples, sampleColumns,
                        frameHeight, shiftAmount, movingDown);
                }

                if (prependRows > 0)
                {
                    if (fixedHeaderRows > 0)
                    {
                        SeparateFixedHeader();
                        strips.Insert(1, normalized.Clone(new Rectangle(0, fixedHeaderRows, width, prependRows),
                            PixelFormat.Format32bppArgb));
                    }
                    else
                    {
                        strips.Insert(0, normalized.Clone(new Rectangle(0, 0, width, prependRows),
                            PixelFormat.Format32bppArgb));
                    }
                    capturedTop = nextViewportTop;
                }
                else if (appendRows > 0)
                {
                    strips.Add(normalized.Clone(new Rectangle(0, frameHeight - appendRows, width, appendRows),
                        PixelFormat.Format32bppArgb));
                    capturedBottom = nextViewportBottom;
                }

                previousSamples = samples;
                viewportTop = nextViewportTop;
                Height += newRowCount;
                return prependRows > 0 ? LongCaptureFrameResult.Prepended :
                    appendRows > 0 ? LongCaptureFrameResult.Appended : LongCaptureFrameResult.Unchanged;
            }
        }

        private void SeparateFixedHeader()
        {
            if (strips[0].Height == fixedHeaderRows) return;
            Bitmap first = strips[0];
            Bitmap header = first.Clone(new Rectangle(0, 0, width, fixedHeaderRows), PixelFormat.Format32bppArgb);
            Bitmap content;
            try
            {
                content = first.Clone(new Rectangle(0, fixedHeaderRows, width, frameHeight - fixedHeaderRows),
                    PixelFormat.Format32bppArgb);
            }
            catch
            {
                header.Dispose();
                throw;
            }
            strips[0] = header;
            strips.Insert(1, content);
            first.Dispose();
        }

        private static int DetectFixedHeaderRows(byte[] previous, byte[] current, int columns,
            int rows, int shift, bool movingDown)
        {
            int examinedRows = Math.Min(rows / 4, rows - shift);
            long improvement = 0;
            long bestImprovement = 0;
            int bestRows = 0;
            for (int y = 0; y < examinedRows; y++)
            {
                int sameOffset = y * columns;
                int previousAlignedOffset = (y + (movingDown ? shift : 0)) * columns;
                int currentAlignedOffset = (y + (movingDown ? 0 : shift)) * columns;
                for (int x = 0; x < columns; x++)
                {
                    int same = Math.Abs(previous[sameOffset + x] - current[sameOffset + x]);
                    int aligned = Math.Abs(previous[previousAlignedOffset + x] - current[currentAlignedOffset + x]);
                    improvement += aligned - same;
                }
                if (improvement >= bestImprovement)
                {
                    bestImprovement = improvement;
                    bestRows = y + 1;
                }
            }
            return bestRows >= 12 && bestImprovement >= (long)bestRows * columns * 4 ? bestRows : 0;
        }

        private static double CompareSamples(byte[] previous, byte[] current, int columns, int rows, int shift, int targetRows)
        {
            int overlap = rows - shift;
            if (overlap < 72) return double.MaxValue;

            int firstRow = Math.Max(8, overlap / 8);
            int lastRow = Math.Min(overlap - 8, overlap * 7 / 8);
            int availableRows = lastRow - firstRow;
            int sampledRows = Math.Min(availableRows, targetRows);
            long difference = 0;
            int count = 0;
            uint rowSequence = 0x9E3779B9;

            for (int i = 0; i < sampledRows; i++)
            {
                rowSequence ^= rowSequence << 13;
                rowSequence ^= rowSequence >> 17;
                rowSequence ^= rowSequence << 5;
                int y = sampledRows == availableRows ? firstRow + i :
                    firstRow + (int)(rowSequence % (uint)availableRows);
                int previousOffset = (y + shift) * columns;
                int currentOffset = y * columns;
                for (int x = 0; x < columns; x++)
                {
                    difference += Math.Abs(previous[previousOffset + x] - current[currentOffset + x]);
                    count++;
                }
            }

            return count > 0 ? (double)difference / count : double.MaxValue;
        }

        private static byte[] SampleGrayscale(Bitmap bitmap, int columns)
        {
            Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * bitmap.Height];
                if (data.Stride > 0)
                {
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                else
                {
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * stride, stride);
                    }
                }
                byte[] samples = new byte[columns * bitmap.Height];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < columns; x++)
                    {
                        int pixel = row + ((x * bitmap.Width + bitmap.Width / 2) / columns) * 4;
                        samples[y * columns + x] = (byte)((pixels[pixel] * 29 + pixels[pixel + 1] * 150 + pixels[pixel + 2] * 77) >> 8);
                    }
                }
                return samples;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public Bitmap CreatePreview(int maxWidth, int maxHeight)
        {
            if (!HasImage) return null;
            double scale = Math.Min(1.0, Math.Min((double)maxWidth / width, (double)maxHeight / Height));
            int previewWidth = Math.Max(1, (int)Math.Floor(width * scale));
            int previewHeight = Math.Max(1, (int)Math.Floor(Height * scale));
            Bitmap preview = new Bitmap(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(preview))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                int top = 0;
                foreach (Bitmap strip in strips)
                {
                    float y = (float)(top * scale);
                    float nextY = (float)((top + strip.Height) * scale);
                    graphics.DrawImage(strip, new RectangleF(0, y, previewWidth, nextY - y),
                        new RectangleF(0, 0, strip.Width, strip.Height), GraphicsUnit.Pixel);
                    top += strip.Height;
                }
            }
            return preview;
        }

        public Bitmap CreateResult()
        {
            if (!HasImage) return null;
            Bitmap result = new Bitmap(width, Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                int top = 0;
                foreach (Bitmap strip in strips)
                {
                    graphics.DrawImageUnscaled(strip, 0, top);
                    top += strip.Height;
                }
            }
            return result;
        }

        public void Dispose()
        {
            foreach (Bitmap strip in strips) strip.Dispose();
            strips.Clear();
            previousSamples = null;
        }
    }

    /// <summary>Rotate the frames for the existing overlap matcher, then return horizontal images.</summary>
    internal sealed class HorizontalLongCaptureStitcher : ILongCaptureStitcher
    {
        private readonly LongCaptureStitcher rotated = new LongCaptureStitcher();

        public int Width => rotated.Height;
        public int Height => rotated.Width;
        public bool HasImage => rotated.HasImage;

        public LongCaptureFrameResult AddFrame(Bitmap frame)
        {
            if (frame == null) return LongCaptureFrameResult.NoOverlap;
            using (Bitmap turned = (Bitmap)frame.Clone())
            {
                turned.RotateFlip(RotateFlipType.Rotate90FlipNone);
                return rotated.AddFrame(turned);
            }
        }

        public Bitmap CreatePreview(int maxWidth, int maxHeight)
        {
            Bitmap preview = rotated.CreatePreview(maxHeight, maxWidth);
            preview?.RotateFlip(RotateFlipType.Rotate270FlipNone);
            return preview;
        }

        public Bitmap CreateResult()
        {
            Bitmap image = rotated.CreateResult();
            image?.RotateFlip(RotateFlipType.Rotate270FlipNone);
            return image;
        }

        public void Dispose() => rotated.Dispose();
    }
}
