#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ShareX.ScreenCaptureLib
{
    internal class ScrollingCaptureManager : IDisposable
    {
        public ScrollingCaptureOptions Options { get; private set; }
        public Bitmap Result { get; private set; }
        public bool IsCapturing { get; private set; }

        private Bitmap lastScreenshot;
        private Bitmap previousScreenshot;
        private bool stopRequested;
        private ScrollingCaptureStatus status;
        private int unchangedFrames;
        private WindowInfo selectedWindow;
        private Rectangle selectedRectangle;

        public ScrollingCaptureManager(ScrollingCaptureOptions options)
        {
            Options = options;
        }

        public void Dispose()
        {
            Reset();
        }

        private void Reset(bool keepResult = false)
        {
            if (lastScreenshot != null)
            {
                lastScreenshot.Dispose();
                lastScreenshot = null;
            }

            if (previousScreenshot != null)
            {
                previousScreenshot.Dispose();
                previousScreenshot = null;
            }

            if (!keepResult && Result != null)
            {
                Result.Dispose();
                Result = null;
            }
        }

        public async Task<ScrollingCaptureStatus> StartCapture()
        {
            if (!IsCapturing && selectedWindow != null && !selectedRectangle.IsEmpty)
            {
                IsCapturing = true;
                stopRequested = false;
                status = ScrollingCaptureStatus.Failed;
                unchangedFrames = 0;
                Reset();

                ScrollingCaptureRegionForm regionForm = null;

                if (Options.ShowRegion)
                {
                    regionForm = new ScrollingCaptureRegionForm(selectedRectangle);
                    regionForm.Show();
                }

                try
                {
                    selectedWindow.Activate();

                    await Task.Delay(Options.StartDelay);

                    if (Options.AutoScrollTop)
                    {
                        InputHelpers.SendKeyPress(VirtualKeyCode.HOME);
                        NativeMethods.SendMessage(selectedWindow.Handle, (int)WindowsMessages.VSCROLL, (int)ScrollBarCommands.SB_TOP, 0);

                        await Task.Delay(Options.ScrollDelay);
                    }

                    Screenshot screenshot = new Screenshot()
                    {
                        CaptureCursor = false
                    };

                    while (!stopRequested)
                    {
                        lastScreenshot = screenshot.CaptureRectangle(selectedRectangle);

                        bool unchanged = CompareLastTwoImages();
                        bool pageThroughBlank = unchanged && IsUniformImage(lastScreenshot);
                        if (ShouldStopForUnchangedImage(unchanged))
                        {
                            break;
                        }
                        if (unchanged)
                        {
                            lastScreenshot.Dispose();
                            lastScreenshot = null;
                        }

                        if (pageThroughBlank)
                        {
                            // A blank viewport provides no pixel anchor. PageDown keeps
                            // viewport overlap while probing farther than a small wheel step.
                            InputHelpers.SendKeyPress(VirtualKeyCode.NEXT);
                            status = ScrollingCaptureStatus.PartiallySuccessful;
                        }
                        else switch (Options.ScrollMethod)
                        {
                            case ScrollMethod.MouseWheel:
                                InputHelpers.SendMouseWheel(-120 * Options.ScrollAmount);
                                break;
                            case ScrollMethod.DownArrow:
                                for (int i = 0; i < Options.ScrollAmount; i++)
                                {
                                    InputHelpers.SendKeyPress(VirtualKeyCode.DOWN);
                                }
                                break;
                            case ScrollMethod.PageDown:
                                InputHelpers.SendKeyPress(VirtualKeyCode.NEXT);
                                break;
                            case ScrollMethod.ScrollMessage:
                                for (int i = 0; i < Options.ScrollAmount; i++)
                                {
                                    NativeMethods.SendMessage(selectedWindow.Handle, (int)WindowsMessages.VSCROLL, (int)ScrollBarCommands.SB_LINEDOWN, 0);
                                }
                                break;
                        }

                        Stopwatch timer = Stopwatch.StartNew();

                        if (lastScreenshot != null)
                        {
                            Bitmap newResult = await CombineImagesAsync(Result, lastScreenshot);

                            if (newResult != null)
                            {
                                Result?.Dispose();
                                Result = newResult;
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (stopRequested)
                        {
                            break;
                        }

                        if (lastScreenshot != null)
                        {
                            if (previousScreenshot != null)
                            {
                                previousScreenshot.Dispose();
                            }

                            previousScreenshot = lastScreenshot;
                            lastScreenshot = null;
                        }

                        int delay = Options.ScrollDelay - (int)timer.ElapsedMilliseconds;

                        if (delay > 0)
                        {
                            await Task.Delay(delay);
                        }
                    }
                }
                finally
                {
                    regionForm?.Close();

                    Reset(true);
                    IsCapturing = false;
                }
            }

            return status;
        }

        public void StopCapture()
        {
            if (IsCapturing)
            {
                stopRequested = true;
            }
        }

        public bool SelectWindow()
        {
            return RegionCaptureTasks.GetRectangleRegion(out selectedRectangle, out selectedWindow, new RegionCaptureOptions());
        }

        private bool IsScrollReachedBottom(IntPtr handle)
        {
            SCROLLINFO scrollInfo = new SCROLLINFO();
            scrollInfo.cbSize = (uint)Marshal.SizeOf(scrollInfo);
            scrollInfo.fMask = (uint)(ScrollInfoMask.SIF_RANGE | ScrollInfoMask.SIF_PAGE | ScrollInfoMask.SIF_TRACKPOS);

            if (NativeMethods.GetScrollInfo(handle, (int)SBOrientation.SB_VERT, ref scrollInfo))
            {
                return scrollInfo.nMax == scrollInfo.nTrackPos + scrollInfo.nPage - 1;
            }

            return CompareLastTwoImages();
        }

        private bool CompareLastTwoImages()
        {
            if (lastScreenshot != null && previousScreenshot != null)
            {
                return ImageHelpers.CompareImages(lastScreenshot, previousScreenshot);
            }

            return false;
        }

        private bool ShouldStopForUnchangedImage(bool unchanged)
        {
            unchangedFrames = unchanged ? unchangedFrames + 1 : 0;
            // One unchanged frame can be a slow repaint or a blank part of a page.
            return unchangedFrames >= 3;
        }

        private static bool IsUniformImage(Bitmap image)
        {
            if (image == null) return false;
            Color reference = image.GetPixel(image.Width / 2, image.Height / 2);
            for (int y = 0; y < image.Height; y += Math.Max(1, image.Height / 16))
                for (int x = 0; x < image.Width; x += Math.Max(1, image.Width / 16))
                    if (image.GetPixel(x, y).ToArgb() != reference.ToArgb()) return false;
            return true;
        }

        private async Task<Bitmap> CombineImagesAsync(Bitmap result, Bitmap currentImage)
        {
            return await Task.Run(() => CombineImages(result, currentImage));
        }

        private Bitmap CombineImages(Bitmap result, Bitmap currentImage)
        {
            if (result == null)
            {
                status = ScrollingCaptureStatus.Successful;
                return (Bitmap)currentImage.Clone();
            }
            Bitmap previous = previousScreenshot;
            bool ownPrevious = previous == null;
            if (ownPrevious)
                previous = result.Clone(new Rectangle(0, result.Height - currentImage.Height, result.Width, currentImage.Height), PixelFormat.Format32bppArgb);
            try
            {
                if (ImageHelpers.CompareImages(previous, currentImage)) return (Bitmap)result.Clone();
                int height = currentImage.Height;
                int side = Math.Min(Math.Max(50, currentImage.Width / 20), currentImage.Width / 3);
                int compareLength = (currentImage.Width - side * 2) * 4;
                int top = Math.Min(50, height / 10), bottom = 0, shift = -1, bestInformation = 0;
                BitmapData oldData = previous.LockBits(new Rectangle(0, 0, previous.Width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                BitmapData newData = null;
                try
                {
                    newData = currentImage.LockBits(new Rectangle(0, 0, currentImage.Width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    IntPtr OldRow(int y) => oldData.Scan0 + y * oldData.Stride + side * 4;
                    IntPtr NewRow(int y) => newData.Scan0 + y * newData.Stride + side * 4;
                    if (Options.AutoIgnoreBottomEdge)
                        while (bottom < height / 3 && NativeMethods.memcmp(OldRow(height - bottom - 1), NewRow(height - bottom - 1), compareLength) == 0) bottom++;
                    uint Signature(IntPtr row)
                    {
                        uint hash = 2166136261;
                        for (int x = 0; x < compareLength; x += Math.Max(4, (compareLength / 128) & ~3))
                            hash = unchecked((hash ^ (uint)Marshal.ReadInt32(row, x)) * 16777619);
                        return hash;
                    }
                    uint[] oldRows = new uint[height], newRows = new uint[height];
                    for (int y = 0; y < height; y++) { oldRows[y] = Signature(OldRow(y)); newRows[y] = Signature(NewRow(y)); }
                    for (int candidate = 1; candidate < height - top - bottom - 8; candidate++)
                    {
                        int end = height - candidate - bottom;
                        bool matches = true;
                        var information = new System.Collections.Generic.HashSet<uint>();
                        for (int y = top; y < end; y++)
                        {
                            if (oldRows[y + candidate] != newRows[y]) { matches = false; break; }
                            information.Add(newRows[y]);
                        }
                        if (!matches || information.Count < 4) continue;
                        // Verify the entire available overlap, not just a blank/repeated anchor.
                        for (int y = top; y < end; y++)
                            if (NativeMethods.memcmp(OldRow(y + candidate), NewRow(y), compareLength) != 0) { matches = false; break; }
                        if (!matches) continue;
                        if (information.Count > bestInformation)
                        {
                            bestInformation = information.Count;
                            shift = candidate;
                        }
                    }
                }
                finally
                {
                    if (newData != null) currentImage.UnlockBits(newData);
                    previous.UnlockBits(oldData);
                }
                bool certain = shift > 0;
                int newHeight = certain ? checked(result.Height + shift) : checked(result.Height + height);
                if ((long)newHeight * currentImage.Width > 128L * 1024 * 1024)
                    throw new InvalidOperationException("长图已达到安全内存上限，现有结果已保留；请分段截图。");
                Bitmap combined = new Bitmap(result.Width, newHeight);
                using (Graphics graphics = Graphics.FromImage(combined))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    if (certain)
                    {
                        int retained = result.Height - bottom;
                        graphics.DrawImage(result, new Rectangle(0, 0, result.Width, retained), new Rectangle(0, 0, result.Width, retained), GraphicsUnit.Pixel);
                        int appended = shift + bottom;
                        graphics.DrawImage(currentImage, new Rectangle(0, retained, currentImage.Width, appended), new Rectangle(0, height - appended, currentImage.Width, appended), GraphicsUnit.Pixel);
                    }
                    else
                    {
                        graphics.DrawImageUnscaled(result, 0, 0);
                        graphics.DrawImageUnscaled(currentImage, 0, result.Height);
                    }
                }
                if (!certain) status = ScrollingCaptureStatus.PartiallySuccessful;
                return combined;
            }
            finally { if (ownPrevious) previous.Dispose(); }
        }
    }
}
