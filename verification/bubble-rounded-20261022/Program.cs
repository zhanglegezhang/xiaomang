using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using ShareX.ScreenCaptureLib;

static class Check
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int count;
    static void Assert(bool ok, string message) { count++; if (!ok) throw new Exception(message); }
    static bool Equal(Bitmap a, Bitmap b)
    {
        for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
    [STAThread] static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Directory.CreateDirectory("build/bubble-rounded-20261022/verification");
        using var canvas = new Bitmap(700, 400);
        using var form = new RegionCaptureForm(RegionCaptureMode.Annotation,
            new RegionCaptureOptions { DetectWindows = false, DetectControls = false, RememberMenuState = false }, (Bitmap)canvas.Clone(), true);
        object manager = form.GetType().GetProperty("ShapeManager", Flags)!.GetValue(form)!;
        var shapes = (List<BaseShape>)manager.GetType().GetProperty("Shapes", Flags)!.GetValue(manager)!;
        var create = manager.GetType().GetMethod("CreateShape", Flags, null, new[] { typeof(ShapeType) }, null)!;
        var render = manager.GetType().GetMethod("RenderOutputImage", Flags, null, new[] { typeof(Bitmap) }, null)!;
        using var shape = (SpeechBalloonDrawingShape)create.Invoke(manager, new object[] { ShapeType.DrawingSpeechBalloon })!;
        var setTail = typeof(SpeechBalloonDrawingShape).GetMethod("SetInlineTailPosition", Flags)!;
        var pathMethod = typeof(SpeechBalloonDrawingShape).GetMethod("CreateBalloonPath", Flags)!;
        var paint = typeof(SpeechBalloonDrawingShape).GetMethod("DrawSpeechBalloon", Flags, null,
            new[] { typeof(Graphics), typeof(Color), typeof(int), typeof(Color), typeof(RectangleF), typeof(PointF) }, null)!;
        RectangleF rect = new RectangleF(120, 100, 320, 100);
        PointF[] tips = { new(140,218), new(280,60), new(480,150), new(80,150), new(280,150), new(480,240), new(80,60) };
        shape.Text = "气泡文字\n保留紧凑排版";
        shape.TextOptions = new TextDrawingOptions { Font="Microsoft YaHei", Size=18, Color=Color.Black };
        foreach (int dpi in new[] {96,144,192})
        foreach (PointF tip in tips)
        foreach (int alpha in new[] {0,128,255})
        foreach (bool shadow in new[] {false,true})
        {
            shape.Rectangle=rect;
            setTail.Invoke(shape,new object[]{tip});
            shape.FillColor=Color.FromArgb(alpha,255,255,255);
            shape.BorderColor=Color.DarkSlateGray; shape.BorderSize=2;
            shape.Shadow=shadow; shape.ShadowColor=Color.Gray; shape.ShadowOffset=new Point(2,2);
            using var bitmap=new Bitmap(700,400); bitmap.SetResolution(dpi,dpi);
            using (var g=Graphics.FromImage(bitmap)) shape.OnDraw(g);
            Assert(shape.Rectangle==rect && shape.TailPosition==tip,"render changed geometry");
            Assert(shape.TextOptions.Size==18 && shape.FillColor.A==alpha,"render changed settings");
            manager.GetType().GetField("CurrentDPI",Flags)!.SetValue(manager,new PointF(dpi,dpi));
            shapes.Add(shape);
            using var exported=(Bitmap)render.Invoke(manager,new object[]{canvas})!;
            shapes.Remove(shape);
            Assert(Equal(bitmap,exported),"preview/export mismatch");
        }
        using (var bitmap=new Bitmap(700,400))
        using (var g=Graphics.FromImage(bitmap))
        {
            paint.Invoke(shape,new object[]{g,Color.Transparent,0,Color.FromArgb(128,255,255,255),rect,tips[0]});
            Assert(bitmap.GetPixel(120,100).A==0,"corner not rounded");
            Assert(bitmap.GetPixel(200,150).A==128,"body alpha changed");
            Assert(bitmap.GetPixel(141,201).A==128,"tail joint double opacity or seam");
            g.Clear(Color.Transparent);
            g.SetClip(new Rectangle(160,120,30,30));
            var clip=g.ClipBounds; g.SmoothingMode=SmoothingMode.None;
            paint.Invoke(shape,new object[]{g,Color.Black,2,Color.White,rect,tips[0]});
            Assert(g.ClipBounds==clip && g.SmoothingMode==SmoothingMode.None,"graphics state leaked");
            Assert(bitmap.GetPixel(200,150).A==0,"clip ignored");
        }
        foreach(var size in new[]{new SizeF(11,11),new SizeF(20,300),new SizeF(600,12)})
        foreach(var tip in tips)
        {
            using var path=(GraphicsPath)pathMethod.Invoke(shape,new object[]{new RectangleF(100,100,size.Width,size.Height),tip})!;
            Assert(path.PointCount>0 && path.PathPoints.All(p=>float.IsFinite(p.X)&&float.IsFinite(p.Y)),"invalid narrow path");
        }
        shape.Rectangle=rect; setTail.Invoke(shape,new object[]{tips[0]}); shape.Move(5,7);
        Assert(shape.Rectangle.X==125 && shape.TailPosition==new PointF(145,225),"move disconnected tail");
        using var preview=new Bitmap(1000,440);
        using(var g=Graphics.FromImage(preview))
        {
            g.Clear(Color.FromArgb(30,40,48));
            using var font=new Font("Microsoft YaHei",21,FontStyle.Regular,GraphicsUnit.Pixel);
            var examples=new[]{new RectangleF(50,60,450,78),new RectangleF(60,260,240,70),new RectangleF(560,220,360,115)};
            string[] text={"这里是需要说明的内容","重点看这里","支持多行文字\n保持紧凑清晰"};
            for(int i=0;i<examples.Length;i++)
            {
                var r=examples[i];
                paint.Invoke(shape,new object[]{g,Color.FromArgb(60,70,80),2,Color.White,r,new PointF(r.X+8,r.Bottom+18)});
                using var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
                g.DrawString(text[i],font,Brushes.Black,r,sf);
            }
        }
        preview.Save("build/bubble-rounded-20261022/verification/preview.png");
        using(var b=new Bitmap(700,400)) using(var g=Graphics.FromImage(b))
        {
            var sw=Stopwatch.StartNew();
            for(int i=0;i<2000;i++) paint.Invoke(shape,new object[]{g,Color.Black,2,Color.White,rect,tips[0]});
            Console.WriteLine($"Body paint including reflection: {sw.Elapsed.TotalMilliseconds/2000:F4} ms/call");
        }
        Console.WriteLine($"PASS {count} assertions; 126 render scenarios; automated bitmap checks only.");
    }
}
