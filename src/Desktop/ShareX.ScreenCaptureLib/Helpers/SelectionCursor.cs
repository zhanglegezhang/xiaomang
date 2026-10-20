using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;
using ShareX.HelpersLib;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal static class SelectionCursor
    {
        internal static Bitmap Draw(int dpi, float? angle)
        {
            // Match the approved B preview: a 25px viewport using the same
            // 32-unit paths, with rounded 3.4/1.7-unit dark and white strokes.
            const float geometryScale = 25f / 32f;
            float scale = Math.Clamp(dpi / 96f, 1f, 4f);
            int size = (int)Math.Ceiling(25 * scale) | 1;
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
            using var geometry = new Matrix();
            geometry.Scale(geometryScale, geometryScale);
            path.Transform(geometry);
            using var outline = new Pen(Color.FromArgb(16, 20, 24), 3.4f * geometryScale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var stroke = new Pen(Color.White, 1.7f * geometryScale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawPath(outline, path); g.DrawPath(stroke, path);
            return image;
        }

        private static readonly ConditionalWeakTable<Cursor, NativeCursorHandle> handles = new();

        private sealed class NativeCursorHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal NativeCursorHandle(IntPtr value) : base(true) => SetHandle(value);
            protected override bool ReleaseHandle() => NativeMethods.DestroyIcon(handle);
        }

        internal static Cursor Create(int dpi, float? angle = null)
        {
            using Bitmap image = Draw(dpi, angle);
            IntPtr icon = image.GetHicon();
            IconInfo info = default;
            try
            {
                if (!NativeMethods.GetIconInfo(icon, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                info.fIcon = false;
                info.xHotspot = image.Width / 2; info.yHotspot = image.Height / 2;
                // Cursor(Stream) scales to the system cursor size, undoing the
                // smaller artwork at high DPI. This retains exact pixel size.
                IntPtr pointer = NativeMethods.CreateIconIndirect(ref info);
                if (pointer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                var owner = new NativeCursorHandle(pointer);
                try
                {
                    var cursor = new Cursor(pointer);
                    handles.Add(cursor, owner);
                    return cursor;
                }
                catch { owner.Dispose(); throw; }
            }
            finally
            {
                if (info.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(info.hbmColor);
                if (info.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(info.hbmMask);
                NativeMethods.DestroyIcon(icon);
            }
        }

        internal static void Dispose(Cursor cursor)
        {
            if (cursor == null) return;
            cursor.Dispose();
            if (handles.TryGetValue(cursor, out NativeCursorHandle owner))
            {
                handles.Remove(cursor); owner.Dispose();
            }
        }
    }
}
