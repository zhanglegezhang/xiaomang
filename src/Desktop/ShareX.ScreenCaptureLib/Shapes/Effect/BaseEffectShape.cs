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
using ShareX.ScreenCaptureLib.Properties;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ShareX.ScreenCaptureLib
{
    public abstract class BaseEffectShape : BaseShape
    {
        public override ShapeCategory ShapeCategory { get; } = ShapeCategory.Effect;

        public abstract string OverlayText { get; }

        internal bool PreviewStable { get; private set; }
        internal bool PreviewDisposed { get; private set; }
        internal long PreviewRevision { get; private set; }

        public abstract void ApplyEffect(Bitmap bmp);

        public override BaseShape Duplicate()
        {
            var manager = Manager;
            try
            {
                var shape = (BaseEffectShape)base.Duplicate();
                shape.PreviewStable = shape.PreviewDisposed = false;
                shape.PreviewRevision = 0;
                return shape;
            }
            finally { Manager = manager; }
        }

        public virtual void OnDraw(Graphics g) => Manager?.DrawEffectPreviews(g);
        public virtual void OnDrawOverlay(Graphics g)
        {
            OnDrawOverlay(g, OverlayText);
        }

        public void OnDrawOverlay(Graphics g, string overlayText)
        {
            if (ShapeType == ShapeType.EffectBlur) return;
            if (ShapeType != ShapeType.EffectBlur)
            {
                using Brush brush = new SolidBrush(Color.FromArgb(150, Color.Black));
                g.FillRectangle(brush, Rectangle);
            }

            g.DrawCornerLines(Rectangle.Offset(1), Pens.White, 25);

            if (ShapeType == ShapeType.EffectBlur || string.IsNullOrEmpty(overlayText)) return;

            using (Font font = new Font("Verdana", 12))
            {
                Size textSize = g.MeasureString(overlayText, font).ToSize();

                if (Rectangle.Width > textSize.Width && Rectangle.Height > textSize.Height)
                {
                    using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        g.DrawString(overlayText, font, Brushes.White, Rectangle, sf);
                    }
                }
            }
        }

        public virtual void OnDrawFinal(Graphics g, Bitmap bmp)
        {
            Rectangle cropRect = System.Drawing.Rectangle.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height), Rectangle.Round());

            if (!cropRect.IsEmpty)
            {
                using (Bitmap croppedImage = ImageHelpers.CropBitmap(bmp, cropRect))
                {
                    ApplyEffect(croppedImage);

                    g.CompositingMode = CompositingMode.SourceCopy;

                    g.DrawImage(croppedImage, cropRect);

                    g.CompositingMode = CompositingMode.SourceOver;
                }
            }
        }

        public override void OnCreated()
        {
            base.OnCreated();
            UpdatePreview(true);
        }

        public override void OnMoving() => UpdatePreview(false);
        public override void OnMoved() => UpdatePreview(true);
        public override void OnResizing() => UpdatePreview(false);
        public override void OnResized() => UpdatePreview(true);

        private void UpdatePreview(bool stable)
        {
            if (PreviewDisposed) return;
            PreviewStable = stable;
            PreviewRevision++;
            Manager?.RequestEffectPreview();
        }

        public override void Dispose()
        {
            if (PreviewDisposed) return;
            PreviewDisposed = true;
            PreviewStable = false;
            Manager?.RequestEffectPreview();
        }
    }
}
