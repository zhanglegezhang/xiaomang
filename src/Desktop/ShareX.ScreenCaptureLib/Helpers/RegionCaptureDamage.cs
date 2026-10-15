using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ShareX.ScreenCaptureLib
{
    // A snapshot contains only client coordinates, never another copy of the desktop bitmap.
    internal sealed class RegionCaptureDamage
    {
        internal Rectangle Bounds { get; }
        internal List<RectangleF> Selections { get; } = new List<RectangleF>();
        internal List<RectangleF> Overlays { get; } = new List<RectangleF>();

        internal RegionCaptureDamage(Rectangle bounds) => Bounds = bounds;

        internal void AddOverlay(RectangleF rect, int padding = 3)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            rect.Inflate(padding, padding);
            rect.Intersect(Bounds);
            if (rect.Width > 0 && rect.Height > 0) Overlays.Add(rect);
        }

        internal void AddBorder(RectangleF rect)
        {
            AddOverlay(new RectangleF(rect.Left, rect.Top, rect.Width, 1));
            AddOverlay(new RectangleF(rect.Left, rect.Bottom - 1, rect.Width, 1));
            AddOverlay(new RectangleF(rect.Left, rect.Top, 1, rect.Height));
            AddOverlay(new RectangleF(rect.Right - 1, rect.Top, 1, rect.Height));
        }

        private Region SelectionRegion()
        {
            var region = new Region();
            region.MakeEmpty();
            foreach (RectangleF rect in Selections) region.Union(rect);
            return region;
        }

        internal Region Difference(RegionCaptureDamage previous)
        {
            if (previous == null || previous.Bounds != Bounds) return new Region(Bounds);
            // Only changed dim/undim pixels, rather than the union of two potentially huge selections.
            Region damage = SelectionRegion();
            using (Region old = previous.SelectionRegion()) damage.Xor(old);
            foreach (RectangleF rect in previous.Overlays) damage.Union(rect);
            foreach (RectangleF rect in Overlays) damage.Union(rect);
            damage.Intersect(Bounds);
            return damage;
        }

        internal static double Area(Region region)
        {
            double area = 0;
            using (var identity = new Matrix())
                foreach (RectangleF rect in region.GetRegionScans(identity)) area += rect.Width * rect.Height;
            return area;
        }
    }
}
