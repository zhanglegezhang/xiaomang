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
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ShareX.ScreenCaptureLib
{
    public class SpeechBalloonDrawingShape : TextDrawingShape
    {
        public override ShapeType ShapeType { get; } = ShapeType.DrawingSpeechBalloon;

        private PointF tailPosition;

        public PointF TailPosition
        {
            get
            {
                return tailPosition;
            }
            private set
            {
                tailPosition = value;
                TailNode.Position = tailPosition;
            }
        }

        public bool TailVisible => !Rectangle.Contains(TailPosition);
        internal void SetInlineTailPosition(PointF position) => TailPosition = position;

        internal ResizeNode TailNode => Manager.ResizeNodes[(int)NodePosition.Extra];



        public override void OnCreated()
        {
            AutoSize(true);
            TailPosition = Rectangle.Location.Add(8, Rectangle.Height + 18);
            OnCreated(false);
        }

        protected override void UseLightResizeNodes()
        {
            ChangeNodeShape(NodeShape.Square);
            Manager.ResizeNodes[(int)NodePosition.Extra].Shape = NodeShape.Circle;
        }

        public override void OnNodeVisible()
        {
            base.OnNodeVisible();

            TailNode.Position = TailPosition;
            TailNode.Visible = true;
        }

        public override void OnNodeUpdate()
        {
            base.OnNodeUpdate();

            if (TailNode.IsDragging)
            {
                TailPosition = Manager.Form.ScaledClientMousePosition;
            }
        }

        public override void Move(float x, float y)
        {
            base.Move(x, y);

            TailPosition = TailPosition.Add(x, y);
        }

        public override void OnDraw(Graphics g)
        {
            if (Rectangle.Width > 10 && Rectangle.Height > 10)
            {
                DrawSpeechBalloon(g);
                DrawText(g);
            }
        }

        protected void DrawSpeechBalloon(Graphics g)
        {
            if (Shadow)
            {
                if (IsBorderVisible)
                {
                    DrawSpeechBalloon(g, ShadowColor, BorderSize, Color.Transparent, Rectangle.LocationOffset(ShadowOffset), TailPosition.Add(ShadowOffset));
                }
                else if (FillColor.A == 255)
                {
                    DrawSpeechBalloon(g, Color.Transparent, 0, ShadowColor, Rectangle.LocationOffset(ShadowOffset), TailPosition.Add(ShadowOffset));
                }
            }

            DrawSpeechBalloon(g, BorderColor, BorderSize, FillColor, Rectangle, TailPosition);
        }

        protected void DrawSpeechBalloon(Graphics g, Color borderColor, int borderSize, Color fillColor, RectangleF rect, PointF tailPosition)
        {
            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using GraphicsPath path = CreateBalloonPath(rect, tailPosition);
                if (fillColor.A > 0)
                {
                    using var brush = new SolidBrush(fillColor);
                    g.FillPath(brush, path);
                }
                if (borderSize > 0 && borderColor.A > 0)
                {
                    using var pen = new Pen(borderColor, borderSize) { LineJoin = LineJoin.Round };
                    g.DrawPath(pen, path);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        // A single outline avoids seams and double opacity where the tail joins the body.
        protected GraphicsPath CreateBalloonPath(RectangleF rect, PointF tip)
        {
            var path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0) return path;
            float radius = Math.Min(8f, Math.Min(rect.Width, rect.Height) / 5f);
            float diameter = radius * 2;
            PointF center = rect.Center();
            float dx = (tip.X - center.X) / (rect.Width / 2);
            float dy = (tip.Y - center.Y) / (rect.Height / 2);
            int side = rect.Contains(tip) ? -1 : Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);

            void Edge(PointF start, PointF end, int edge)
            {
                if (side != edge) { path.AddLine(start, end); return; }
                float length = Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y);
                float halfBase = Math.Min(12f, length * .2f);
                float position = edge == 0 ? tip.X - start.X : edge == 1 ? tip.Y - start.Y
                    : edge == 2 ? start.X - tip.X : start.Y - tip.Y;
                position = Math.Clamp(position, halfBase, length - halfBase);
                float ux = (end.X - start.X) / length;
                float uy = (end.Y - start.Y) / length;
                PointF first = new PointF(start.X + ux * (position - halfBase), start.Y + uy * (position - halfBase));
                PointF last = new PointF(start.X + ux * (position + halfBase), start.Y + uy * (position + halfBase));
                path.AddLine(start, first);
                path.AddLine(first, tip);
                path.AddLine(tip, last);
                path.AddLine(last, end);
            }

            Edge(new PointF(rect.Left + radius, rect.Top), new PointF(rect.Right - radius, rect.Top), 0);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            Edge(new PointF(rect.Right, rect.Top + radius), new PointF(rect.Right, rect.Bottom - radius), 1);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            Edge(new PointF(rect.Right - radius, rect.Bottom), new PointF(rect.Left + radius, rect.Bottom), 2);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            Edge(new PointF(rect.Left, rect.Bottom - radius), new PointF(rect.Left, rect.Top + radius), 3);
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.CloseFigure();
            return path;
        }
    }
}