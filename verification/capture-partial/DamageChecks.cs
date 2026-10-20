using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using ShareX.ScreenCaptureLib;

internal static class DamageChecks
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Get(object obj,string name) => obj.GetType().GetProperty(name,All)!.GetValue(obj)!;
    static void Set(object obj,string name,object value) => obj.GetType().GetProperty(name,All)!.SetValue(obj,value);
    static object Call(object obj,string name,params object[] args) => obj.GetType().GetMethod(name,All)!.Invoke(obj,args)!;
    static int checks;
    static void Check(bool valid,string name) { if(!valid)throw new Exception(name);checks++;Console.WriteLine("PASS "+name); }
    static string Pixels(Bitmap bmp)
    {
        var data=bmp.LockBits(new Rectangle(Point.Empty,bmp.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try { byte[] pixels=new byte[Math.Abs(data.Stride)*data.Height];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);return Convert.ToHexString(SHA256.HashData(pixels)); }
        finally { bmp.UnlockBits(data); }
    }
    static void Mouse(RegionCaptureForm form,Point position)
    {
        var input=Get(Get(form,"ShapeManager"),"InputManager");
        var field=input.GetType().GetField("mouseState",All)!;object state=field.GetValue(input)!;
        Set(state,"ClientPosition",position);Set(state,"Position",position);field.SetValue(input,state);
    }
    static RegionCaptureForm New(Size size,RegionCaptureMode mode=RegionCaptureMode.Annotation)
    {
        var bitmap=new Bitmap(size.Width,size.Height);
        using(var g=Graphics.FromImage(bitmap)) {g.Clear(Color.FromArgb(30,60,120));g.FillRectangle(Brushes.Orange,100,100,200,150);g.DrawLine(Pens.White,0,0,size.Width-1,size.Height-1);}
        var options=new RegionCaptureOptions{DetectWindows=false,DetectControls=false,ShowMagnifier=false,ShowInfo=true,EnableAnimations=false,RememberMenuState=false,BackgroundDimStrength=20,ImageEditorStartMode=ImageEditorStartMode.AutoSize};
        var form=new RegionCaptureForm(mode,options,bitmap,true);
        _ = form.Handle;
        Set(form,"CanvasRectangle",(RectangleF)new Rectangle(Point.Empty,size));Set(form,"ClientArea",new Rectangle(Point.Empty,size));
        Mouse(form,new Point(150,150));return form;
    }
    static object Add(RegionCaptureForm form,RectangleF rect,ShapeType type=ShapeType.RegionRectangle)
    {
        object manager=Get(form,"ShapeManager"),shape=manager.GetType().GetMethod("CreateShape",All,null,new[]{typeof(ShapeType)},null)!.Invoke(manager,new object[]{type})!;
        Set(shape,"Rectangle",rect);manager.GetType().GetMethods(All).Single(m=>m.Name=="AddShape"&&m.GetParameters().Length==1).Invoke(manager,new[]{shape});return shape;
    }
    static void Draw(RegionCaptureForm form,Bitmap target,Region? damage=null)
    {
        using var g=Graphics.FromImage(target);if(damage!=null)g.SetClip(damage,System.Drawing.Drawing2D.CombineMode.Replace);
        Call(form,"DrawBackground",g);Call(form,"DrawShapes",g);
    }
    static void PixelSequence(Size size)
    {
        using var form=New(size);object manager=Get(form,"ShapeManager");
        using var partial=new Bitmap(size.Width,size.Height);using var reference=new Bitmap(size.Width,size.Height);
        object? previous=null;
        void Frame(string label)
        {
            using var measure=Graphics.FromImage(reference);object snapshot=Call(form,"CaptureDamage",measure);
            if(snapshot==null)Console.WriteLine($"STATE canvas={form.CanvasRectangle}; client={form.ClientArea}; tool={Get(manager,"CurrentTool")}; snap={Get(manager,"IsSnapResizing")}; zoom={Get(form,"IsZoomed")}");
            Check(snapshot!=null,"rectangular fast path eligible: "+label);
            using Region damage=(Region)snapshot!.GetType().GetMethod("Difference",All)!.Invoke(snapshot,new[]{previous})!;
            double area=(double)snapshot.GetType().GetMethod("Area",All)!.Invoke(null,new object[]{damage})!;
            Draw(form,partial,damage);Draw(form,reference);
            Check(Pixels(partial)==Pixels(reference),"partial matches every full-render pixel: "+label);
            if(previous!=null&&label=="small drag")Check(area<size.Width*size.Height/4d,"drag repaints less than quarter canvas");
            previous=snapshot;
        }
        Frame("initial "+size);Mouse(form,new Point(200,190));Frame("cursor moved");
        object shape=Add(form,new RectangleF(100,120,460,280));Frame("new selection");
        Set(shape,"Rectangle",new RectangleF(100,120,468,290));Frame("small drag");
        Set(shape,"Rectangle",new RectangleF(100.5f,119.5f,470.5f,292.5f));Frame("fractional coordinates");
        Set(shape,"Rectangle",new RectangleF(size.Width-700,230,510,300));Mouse(form,new Point(size.Width-50,240));Frame("jump across virtual screens");
        form.Options.ShowMagnifier=true;Frame("magnifier enabled");Mouse(form,new Point(3,3));Frame("magnifier at edge");
        form.Options.ShowMagnifier=false;form.Options.ShowInfo=false;Frame("overlays removed");
        object second=Add(form,new RectangleF(size.Width-400,350,320,260));Frame("overlapping multi-region");
        Set(second,"Rectangle",new RectangleF(40,470,320,200));Frame("second region moved");
        ((System.Collections.IList)Get(manager,"Shapes")).Clear();Set(manager,"CurrentShape",null!);Frame("selections deleted");
        // Output image data comes from the unmodified original canvas, not the dimmed display.
        Add(form,new RectangleF(100,100,200,150));Set(form,"Result",RegionResult.Region);Call(form,"UpdateRegionPath");
        using Bitmap result=form.GetResultImage();Check(result.Size==new Size(200,150)&&result.GetPixel(50,30).ToArgb()==Color.Orange.ToArgb(),"original-resolution output and original colors retained "+size);
        Set(form,"ScreenBounds",new Rectangle(-3840,-200,size.Width,size.Height));using Bitmap negative=form.GetResultImage();Check(form.ResultScreenLocation==new Point(-3740,-100),"negative screen origin retained "+size);
    }
    static void Fallbacks()
    {
        using var form=New(new Size(800,600));using var image=new Bitmap(800,600);using var graphics=Graphics.FromImage(image);
        bool Full()=>Call(form,"CaptureDamage",graphics)==null;
        form.Options.ShowCrosshair=true;Check(Full(),"screen-wide crosshair uses full refresh");form.Options.ShowCrosshair=false;
        Set(form,"ZoomFactor",2f);Check(Full(),"zoom uses full refresh");Set(form,"ZoomFactor",1f);
        form.Options.DetectWindows=true;Check(Full(),"animated window detection uses full refresh");form.Options.DetectWindows=false;
        object shape=Add(form,new RectangleF(80,80,180,150));Set(shape,"CornerRadius",12);Check(Full(),"rounded selection uses full refresh");Set(shape,"CornerRadius",0);
        Add(form,new RectangleF(80,80,180,150),ShapeType.DrawingRectangle);Check(Full(),"annotation drawings use full refresh");
        using var editor=New(new Size(800,600),RegionCaptureMode.Editor);Check(Call(editor,"CaptureDamage",graphics)==null,"image editor uses full refresh");
    }
    internal static int Run(bool dpi96)
    {
        try {PixelSequence(new Size(1280,800));PixelSequence(new Size(10560,2160));Fallbacks();LiveFrames();Console.WriteLine($"RESULT {checks} passed, 0 failed");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }

    static void LiveFrames()
    {
        using var form=New(new Size(1280,800));
        Set(form,"IsFullscreen",false);Point pointer=Control.MousePosition;form.Bounds=new Rectangle(pointer.X-100,pointer.Y-100,1280,800);
        form.Show();Call(form,"Resume");
        void Pump(int ms){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(1);}}
        Pump(500);
        long partial=(long)form.GetType().GetField("partialFrames",All)!.GetValue(form)!;
        Console.WriteLine($"LIVE frames={form.GetType().GetField("paintedFrames",All)!.GetValue(form)} partial={partial} canvas={form.CanvasRectangle} client={form.ClientArea} tool={Get(Get(form,"ShapeManager"),"CurrentTool")} pause={form.GetType().GetField("pause",All)!.GetValue(form)}");
        Check(partial>0,"real WM_PAINT presents partial frames after the first full frame");
        // Simulate a state-mutating event arriving between scheduled refresh and paint.
        form.GetType().GetField("framePending",All)!.SetValue(form,true);
        form.GetType().GetField("framePrepared",All)!.SetValue(form,true);
        Call(form,"RequestInputRefresh",form,EventArgs.Empty);
        Check(!(bool)form.GetType().GetField("framePrepared",All)!.GetValue(form)!,"interrupted queued paint expands to a fresh full redraw");
        Pump(100);Call(form,"RegionCaptureForm_KeyDown",form,new KeyEventArgs(Keys.Escape));
        Check(form.IsDisposed,"real partial-render window closes and releases on Escape");
    }

    internal static int Golden(string destination)
    {
        var hashes=new Dictionary<string,string>();
        using var form=New(new Size(1280,800));using var image=new Bitmap(1280,800);
        void Frame(string name){Draw(form,image);hashes[name]=Pixels(image);}
        Frame("initial");Mouse(form,new Point(250,210));Frame("cursor");
        object shape=Add(form,new RectangleF(100,120,461,283));Frame("selection");
        form.Options.ShowMagnifier=true;Frame("magnifier");Mouse(form,new Point(1270,790));Frame("edge");
        Set(shape,"Rectangle",new RectangleF(100.5f,119.5f,470.5f,292.5f));Frame("fractional");
        ((System.Collections.IList)Get(Get(form,"ShapeManager"),"Shapes")).Clear();Set(Get(form,"ShapeManager"),"CurrentShape",null!);Frame("removed");
        File.WriteAllText(destination,JsonSerializer.Serialize(hashes));return 0;
    }
}
