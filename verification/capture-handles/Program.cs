using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO;
using ShareX.ScreenCaptureLib;

internal static class Program
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static int checks; static string report;
    static Type managerType = typeof(RegionCaptureForm).Assembly.GetType("ShareX.ScreenCaptureLib.ShapeManager", true)!;
    static object Get(object obj, string name) => obj.GetType().GetProperty(name, All)!.GetValue(obj)!;
    static void Set(object obj, string name, object value) => obj.GetType().GetProperty(name, All)!.SetValue(obj, value);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethods(All).Single(m => m.Name==name && m.GetParameters().Length==args.Length && m.GetParameters().Select((p,i)=>p.ParameterType.IsInstanceOfType(args[i])).All(x=>x)).Invoke(obj,args)!;
    static object Field(object obj, string name) => obj.GetType().GetField(name, All)!.GetValue(obj)!;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
    static string Hit(RectangleF rect, PointF point, float scale) => managerType.GetMethod("HitSelectionBorder", All)!.Invoke(null, new object[] { rect, point, scale })?.ToString() ?? "none";
    static RegionCaptureForm New(bool editor = false)
    {
        var canvas = new Bitmap(900, 650); using(var g = Graphics.FromImage(canvas)) g.Clear(Color.FromArgb(40, 64, 85));
        var form = new RegionCaptureForm(editor ? RegionCaptureMode.Editor : RegionCaptureMode.Annotation,
            new RegionCaptureOptions { DetectWindows = false, DetectControls = false, ShowMagnifier = false, ShowInfo = false, EnableAnimations = false, RememberMenuState = false, ShowEditorPanTip = false, ImageEditorStartMode = ImageEditorStartMode.AutoSize }, canvas, true);
        _ = form.Handle; Set(form, "ClientArea", new Rectangle(0, 0, 900, 650)); Set(form, "CanvasRectangle", new RectangleF(0, 0, 900, 650));
        return form;
    }
    static object Select(RegionCaptureForm form, RectangleF rect, ShapeType type = ShapeType.RegionRectangle)
    {
        object manager = Get(form, "ShapeManager"), shape = Call(manager, "CreateShape", type);
        Set(shape, "Rectangle", rect);
        managerType.GetMethods(All).Single(m => m.Name == "AddShape" && m.GetParameters().Length == 1).Invoke(manager, new[] { shape });
        Set(manager, "CurrentShape", shape); Set(manager, "NodesVisible", true);
        return shape;
    }
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(args.Contains("96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        if(args.Contains("damage")) return DamageChecks.Run(args.Contains("96"));
        report = Path.GetFullPath(Path.Combine(args[0], "build/capture-handles-20261018/results")); Directory.CreateDirectory(report);
        try
        {
            using var form = New(); object manager = Get(form, "ShapeManager"); int dpi = form.DeviceDpi; float scale = dpi / 96f;
            CheckGeometry(scale);
            RectangleF rect = new RectangleF(160, 150, 501, 301); object shape = Select(form, rect);
            Check((bool)Get(manager, "UsesSelectionResize"), "Selected screenshot rectangle uses new behaviour");
            var samples = new[] { new PointF(300, 150 - 5 * scale), new PointF(660 + 5 * scale, 270), new PointF(350, 450 + 5 * scale), new PointF(160 - 5 * scale, 300), new PointF(160 - 8 * scale, 150 - 8 * scale), new PointF(660 + 8 * scale, 450 + 8 * scale) };
            var moves = new[] { new PointF(0, -31), new PointF(38, 0), new PointF(0, 29), new PointF(-36, 0), new PointF(-23, -17), new PointF(22, 19) };
            for (int i = 0; i < samples.Length; i++)
            {
                Set(shape, "Rectangle", rect); Call(shape, "OnNodePositionUpdate");
                var down = new MouseEventArgs(MouseButtons.Left, 1, (int)samples[i].X, (int)samples[i].Y, 0);
                Call(manager, "form_MouseDown", form, down);
                Check((bool)Get(manager, "IsResizing") && !(bool)Get(manager, "IsMoving"), "Border mouse-down starts resize rather than move");
                Check((RectangleF)Get(shape, "Rectangle") == rect, "Off-centre edge press never jumps");
                var released = new PointF(samples[i].X + moves[i].X, samples[i].Y + moves[i].Y);
                Call(manager, "form_MouseUp", form, new MouseEventArgs(MouseButtons.Left, 1, (int)released.X, (int)released.Y, 0));
                RectangleF after = (RectangleF)Get(shape, "Rectangle");
                Check(!(bool)Get(manager, "IsResizing") && after != rect, "Release between frames commits resize");
                if (i == 0) Check(after.Height == rect.Height + 31 && after.Width == rect.Width && after.Bottom == rect.Bottom, "Top edge changes only height and fixes opposite edge");
                if (i == 1) Check(after.Width == rect.Width + 38 && after.Left == rect.Left && after.Height == rect.Height, "Right edge changes only width");
                if (i == 4) Check(after.Left == rect.Left - 23 && after.Top == rect.Top - 17 && after.Bottom == rect.Bottom && after.Right == rect.Right, "Corner anchors opposite corner");
            }
            Set(shape, "Rectangle", rect); Call(shape, "OnNodePositionUpdate");
            Set(manager, "IsCtrlModifier", true);
            Check(Call(manager, "SelectionNodeAt", new PointF(300, 150)) == null, "Ctrl retains prior bypass behaviour"); Set(manager, "IsCtrlModifier", false);
            Set(manager, "CurrentTool", ShapeType.DrawingRectangle);
            Check(!(bool)Get(manager, "UsesSelectionResize"), "Annotation tool does not get capture resize hot zones");
            Set(manager, "CurrentTool", ShapeType.RegionRectangle); Set(manager, "CurrentShape", shape); Set(manager, "NodesVisible", true);
            using(var button = new Button { Bounds = new Rectangle(285, 140, 70, 35) })
            {
                form.Controls.Add(button); _ = button.Handle;
                Check(Call(manager, "SelectionNodeAt", new PointF(300, 150)) == null, "Overlaid control keeps priority"); form.Controls.Remove(button);
            }
            using(var editor = New(true)) { Select(editor, rect); Check(!(bool)Get(Get(editor, "ShapeManager"), "UsesSelectionResize"), "Existing image editor stays unchanged"); }
            CheckCursorsAndRendering(form, manager, shape, dpi, rect);
            CheckFrameInput(form, manager, shape, rect, scale);
            var watch = Stopwatch.StartNew(); for(int i=0;i<100000;i++) Hit(rect, new PointF(300, 150), scale); watch.Stop();
            Check(watch.Elapsed.TotalMilliseconds < 3000, "Hit testing has no visible per-move delay");
            string json = JsonSerializer.Serialize(new { Status = "PASS", Dpi = dpi, Checks = checks, HitTestMicrosecondsIncludingReflection = watch.Elapsed.TotalMilliseconds * 10 / 1000,
                CaptureHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(RegionCaptureForm).Assembly.Location))) });
            File.WriteAllText(Path.Combine(report, "checks-" + dpi + ".json"), json); Console.WriteLine(json); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void CheckGeometry(float s)
    {
        var r = new RectangleF(100, 100, 601, 401);
        foreach(var item in new[] { (new PointF(300,100-6*s),"Top"), (new PointF(700+6*s,250),"Right"), (new PointF(300,500+6*s),"Bottom"), (new PointF(100-6*s,250),"Left"), (new PointF(100-10*s,100-10*s),"TopLeft"), (new PointF(700+10*s,100-10*s),"TopRight"), (new PointF(100-10*s,500+10*s),"BottomLeft"), (new PointF(700+10*s,500+10*s),"BottomRight") }) Check(Hit(r,item.Item1,s)==item.Item2,"All edge and corner limits reachable");
        Check(Hit(r,new PointF(300,100-6*s-0.2f),s)=="none", "Outside edge tolerance is not grabbed");
        Check(Hit(r,new PointF(400,300),s)=="none", "Normal selection interior remains movable");
        foreach(int size in new[]{5,10,20,30}) Check(Hit(new RectangleF(100,100,size,size),new PointF(100+(size-1)/2f,100+(size-1)/2f),s)=="none","Tiny selection has free centre");
        Check(Hit(new RectangleF(-200,-100,601,401),new PointF(-200,-100),s)=="TopLeft","Negative monitor coordinates supported");
        Check(Hit(RectangleF.Empty,PointF.Empty,s)=="none","Empty selection cannot be resized");
    }
    static void Mouse(object manager, Point position, MouseButtons buttons)
    {
        object input=Get(manager,"InputManager"),previous=Field(input,"mouseState"),current=Field(input,"mouseState");
        input.GetType().GetField("oldMouseState",All)!.SetValue(input,previous);
        Set(current,"ClientPosition",position);Set(current,"Position",position);Set(current,"Buttons",buttons);
        input.GetType().GetField("mouseState",All)!.SetValue(input,current);
    }
    static void CheckFrameInput(RegionCaptureForm form,object manager,object shape,RectangleF rect,float scale)
    {
        Set(shape,"Rectangle",rect); Call(shape,"OnNodePositionUpdate");
        Point start=new Point(350,(int)(150-5*scale));
        Mouse(manager,start,MouseButtons.None); Call(manager,"UpdateObjects");
        Check(form.Cursor==(Cursor)Field(form,"resizeVertical"),"Scheduled hover uses vertical resize cursor");
        Mouse(manager,start,MouseButtons.Left); Call(manager,"UpdateObjects");
        Check((bool)Get(manager,"IsResizing") && (RectangleF)Get(shape,"Rectangle")==rect,"Scheduled mouse press starts without jumping");
        Mouse(manager,new Point(start.X,start.Y-20),MouseButtons.Left);Call(manager,"UpdateNodes");
        Check(((RectangleF)Get(shape,"Rectangle")).Top==rect.Top-20,"Scheduled movement applies correct drag delta");
        Call(manager,"form_MouseUp",form,new MouseEventArgs(MouseButtons.Left,1,start.X,start.Y-35,0));
        Check(((RectangleF)Get(shape,"Rectangle")).Top==rect.Top-35,"Mouse-up commits movement after last scheduled frame");
        Mouse(manager,new Point(400,300),MouseButtons.None);Call(manager,"UpdateObjects");
        Check(form.Cursor==(Cursor)Field(form,"defaultCursor"),"Moving off the border restores crosshair");
    }
    static void CheckCursorsAndRendering(RegionCaptureForm form, object manager, object shape, int dpi, RectangleF rect)
    {
        Type factory = typeof(RegionCaptureForm).Assembly.GetType("ShareX.ScreenCaptureLib.SelectionCursor",true)!;
        var cursorImage = new Bitmap(750,250); using(var g=Graphics.FromImage(cursorImage))
        {
            g.Clear(Color.FromArgb(40,64,85)); int index=0;
            foreach(float? angle in new float?[]{null,0,90,45,-45})
            {
                using var bitmap=(Bitmap)factory.GetMethod("Draw",All)!.Invoke(null,new object[]{dpi,angle})!;
                if(!angle.HasValue) Check(bitmap.GetPixel(bitmap.Width/2,bitmap.Height/2).A==0,"Crosshair centre stays transparent");
                g.DrawImageUnscaled(bitmap,30+index++*145,70);
                using var cursor=(Cursor)factory.GetMethod("Create",All)!.Invoke(null,new object[]{dpi,angle})!;
                Check(cursor.Handle!=IntPtr.Zero && cursor.HotSpot.X==cursor.HotSpot.Y,"Native cursor loads with centred hotspot");
            }
        }
        cursorImage.Save(Path.Combine(report,"cursors-"+dpi+".png")); cursorImage.Dispose();
        Set(shape,"Rectangle",rect);Call(shape,"OnNodePositionUpdate");
        var nodes=(Array)Get(manager,"ResizeNodes");
        using var normal=new Bitmap(900,650);using(var g=Graphics.FromImage(normal)){g.Clear(Color.FromArgb(40,64,85));Call(form,"DrawRegionArea",g,rect,true);}
        Set(nodes.GetValue(1)!,"IsCursorHover",true);
        using var hover=new Bitmap(900,650);using(var g=Graphics.FromImage(hover)){g.Clear(Color.FromArgb(40,64,85));Call(form,"DrawRegionArea",g,rect,true);}
        Check(hover.GetPixel(350,150).R>220 && hover.GetPixel(350,150).G>90,"Hovered border has visible orange highlight");
        Check(((Pen)Field(form,"selectionBorder")).Width==2*dpi/96f,"Visible border is two logical pixels");
        normal.Save(Path.Combine(report,"selection-"+dpi+".png"));hover.Save(Path.Combine(report,"hover-"+dpi+".png"));
        var positionType=typeof(RegionCaptureForm).Assembly.GetType("ShareX.ScreenCaptureLib.NodePosition",true)!;
        for(int i=0;i<8;i++){Call(form,"SetSelectionResizeCursor",Enum.ToObject(positionType,i));Check(form.Cursor.Handle!=IntPtr.Zero,"Directional cursor for all eight directions");}
        Set(nodes.GetValue(1)!,"IsCursorHover",false);Call(form,"SetDefaultCursor");
        Check(form.Cursor==(Cursor)Field(form,"defaultCursor"),"Crosshair restored after resize hover");
    }
}
