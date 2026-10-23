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
using System.Drawing;

namespace ShareX.ScreenCaptureLib
{
    public class BlurEffectShape : BaseEffectShape
    {
        public override ShapeType ShapeType { get; } = ShapeType.EffectBlur;

        public override string OverlayText => string.Empty;

        internal void DrawEditBounds(Graphics graphics)
        {
            if (Manager == null || Manager.CurrentShape != this ||
                !(Manager.IsCreating || Manager.IsMoving || Manager.IsResizing)) return;
            if (Rectangle.Width <= 0 || Rectangle.Height <= 0) return;
            var state = graphics.Save();
            try
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                RectangleF bounds = Rectangle;
                using var black = new Pen(Color.Black, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                using var white = new Pen(Color.White, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                graphics.DrawRectangle(black, bounds.X - 1, bounds.Y - 1, bounds.Width + 2, bounds.Height + 2);
                graphics.DrawRectangle(white, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
            finally { graphics.Restore(state); }
        }

        private int blurRadius = 23;
        public int BlurRadius
        {
            get => blurRadius;
            set => blurRadius = BlurStrengthPresets.Normalize(value);
        }

        public override void OnConfigLoad()
        {
            BlurRadius = AnnotationOptions.BlurRadius;
        }

        public override void OnConfigSave()
        {
            AnnotationOptions.BlurRadius = BlurRadius;
        }

        public override void ApplyEffect(Bitmap bmp)
        {
            ImageHelpers.BoxBlur(bmp, BlurRadius);
        }

        public override void OnDrawFinal(Graphics g, Bitmap bmp)
        {
            if (BlurRadius > 1)
            {
                base.OnDrawFinal(g, bmp);
            }
        }
    }
}
