using System.Drawing;
using System.Drawing.Drawing2D;

namespace ShareX
{
    internal static class FavoriteFileIcon
    {
        internal static void Draw(Graphics graphics, RectangleF bounds, bool folder, bool pinned, Color background)
        {
            var state = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color accent = folder ? Color.FromArgb(255, 197, 66) : Color.FromArgb(75, 193, 255);
            using var pen = new Pen(accent, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var tint = new SolidBrush(Color.FromArgb(35, accent));
            using var path = new GraphicsPath();
            if (folder)
            {
                path.AddLine(4,4,8.5f,4);path.AddBezier(8.5f,4,9.3f,4,10,6.5f,11.5f,6.5f);
                path.AddLine(11.5f,6.5f,19.5f,6.5f);path.AddArc(18,6.5f,3.5f,3.5f,270,90);
                path.AddLine(21.5f,8.25f,21.5f,18);path.AddArc(17.5f,16,4,4,0,90);
                path.AddLine(19.5f,20,4.2f,20);path.AddArc(2.2f,16,4,4,90,90);
                path.AddLine(2.2f,18,2.2f,6);path.AddArc(2.2f,4,4,4,180,90);
                path.CloseFigure(); graphics.FillPath(tint,path);graphics.DrawPath(pen,path);
                graphics.DrawLine(pen,3,9,20.5f,9);
            }
            else
            {
                path.AddLine(7,2.5f,13.4f,2.5f);path.AddBezier(13.4f,2.5f,14,2.5f,14.2f,2.8f,14.5f,3.2f);
                path.AddLine(14.5f,3.2f,19.5f,8.2f);path.AddBezier(19.5f,8.2f,20,8.7f,20,9,20,9.5f);
                path.AddLine(20,9.5f,20,19.5f);path.AddArc(16,17.5f,4,4,0,90);
                path.AddLine(18,21.5f,7,21.5f);path.AddArc(5,17.5f,4,4,90,90);
                path.AddLine(5,19.5f,5,4.5f);path.AddArc(5,2.5f,4,4,180,90);
                path.CloseFigure(); graphics.FillPath(tint,path);graphics.DrawPath(pen,path);
                graphics.DrawLines(pen,new[] {new PointF(14,3),new PointF(14,8.5f),new PointF(19.5f,8.5f)});
                graphics.DrawLine(pen,8.5f,12.5f,16.5f,12.5f);graphics.DrawLine(pen,8.5f,16.5f,15,16.5f);
            }
            if (pinned)
            {
                using var halo = new SolidBrush(background);
                graphics.FillEllipse(halo,5.8f,5.2f,12.4f,13.6f);
                graphics.TranslateTransform(12,11.5f);graphics.RotateTransform(45);
                using var pin = new SolidBrush(Color.FromArgb(255,133,75));
                using var needle = new Pen(Color.FromArgb(255,206,179),1.4f) {StartCap=LineCap.Round,EndCap=LineCap.Round};
                using var head=new GraphicsPath();head.AddArc(-3.3f,-4.5f,1.2f,1.2f,180,90);head.AddArc(2.1f,-4.5f,1.2f,1.2f,270,90);head.AddArc(2.1f,-3.7f,1.2f,1.2f,0,90);head.AddArc(-3.3f,-3.7f,1.2f,1.2f,90,90);head.CloseFigure();graphics.FillPath(pin,head);
                graphics.FillRectangle(pin,-1.6f,-2.5f,3.2f,3.5f);
                graphics.FillPolygon(pin,new[]{new PointF(-3.7f,2),new PointF(-1.8f,.7f),new PointF(1.8f,.7f),new PointF(3.7f,2)});
                graphics.DrawLine(needle,0,2,0,5.5f);
            }
            graphics.Restore(state);
        }
    }
}
