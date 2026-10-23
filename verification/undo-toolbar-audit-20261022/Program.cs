using System.Drawing;
using System.Reflection;
using ShareX.ScreenCaptureLib;
class Check {
const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
static object Get(object o,string n)=>o.GetType().GetProperty(n,F)?.GetValue(o)??o.GetType().GetField(n,F)!.GetValue(o)!;
static void Set(object o,string n,object v)=>o.GetType().GetProperty(n,F)!.SetValue(o,v);
static object Call(object o,string n,params object[] a)=>o.GetType().GetMethods(F).First(m=>m.Name==n&&m.GetParameters().Length==a.Length&&m.GetParameters().Select((p,i)=>a[i]==null||p.ParameterType.IsInstanceOfType(a[i])).All(x=>x)).Invoke(o,a)!;
[STAThread]static void Main(){
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
foreach(var type in new[]{ShapeType.DrawingRectangle,ShapeType.DrawingEllipse,ShapeType.DrawingArrow,ShapeType.DrawingLine,ShapeType.DrawingMagnify,ShapeType.EffectBlur,ShapeType.EffectPixelate,ShapeType.EffectHighlight}){
using var b=new Bitmap(800,600);using var f=new RegionCaptureForm(RegionCaptureMode.Annotation,new RegionCaptureOptions{DetectWindows=false,DetectControls=false},(Bitmap)b.Clone(),true);_=f.Handle;
var m=Get(f,"ShapeManager");var h=Get(m,"history");int Count()=> (int)Get(Get(h,"undoMementoStack"),"Count");
Set(m,"CurrentTool",type);
var seed=(BaseShape)Call(m,"CreateShape",type);seed.Rectangle=new RectangleF(20,20,100,100);Call(m,"AddShape",seed);Call(h,"Undo");
int before=Count();var created=(BaseShape)Call(m,"AddShape");Call(m,"DeleteShape",created);
Console.WriteLine($"CANCEL {type}: undo {before}->{Count()}; redo={(bool)Get(h,"CanRedo")}"); if(Count()!=before || !(bool)Get(h,"CanRedo"))throw new Exception("Cancel history damaged");
created=(BaseShape)Call(m,"AddShape");created.Rectangle=new RectangleF(10,10,90,60);Call(m,"CommitCreatedShape",created);if(Count()!=before+1)throw new Exception("Creation not one step");Call(h,"Undo");if(((List<BaseShape>)Get(m,"Shapes")).Contains(created))throw new Exception("Creation undo failed");Call(h,"Redo");if(((List<BaseShape>)Get(m,"Shapes")).Count!=1)throw new Exception("Creation redo failed");
}
using var bmp=new Bitmap(800,600);using var form=new RegionCaptureForm(RegionCaptureMode.Annotation,new RegionCaptureOptions{DetectWindows=false,DetectControls=false},(Bitmap)bmp.Clone(),true);_=form.Handle;
var manager=Get(form,"ShapeManager");var hist=Get(manager,"history");int N()=> (int)Get(Get(hist,"undoMementoStack"),"Count");
Set(manager,"CurrentTool",ShapeType.DrawingRectangle);
var a=(BaseShape)Call(manager,"CreateShape",ShapeType.DrawingRectangle);a.Rectangle=new RectangleF(10,10,100,100);Call(manager,"AddShape",a);
var bshape=(BaseShape)Call(manager,"CreateShape",ShapeType.DrawingRectangle);bshape.Rectangle=new RectangleF(30,30,100,100);Call(manager,"AddShape",bshape);
int n=N();Call(manager,"MoveShapeTop",a);Console.WriteLine($"ORDER changed={ReferenceEquals(((List<BaseShape>)Get(manager,"Shapes")).Last(),a)}, history {n}->{N()}");
Set(manager,"CurrentShape",a);var options=Get(Get(manager,"Options"),"AnnotationOptions");n=N();int old=((RectangleDrawingShape)a).CornerRadius;Set(options,"DrawingCornerRadius",old+5);Call(manager,"UpdateShapeFromMenu");Console.WriteLine($"CORNER {old}->{((RectangleDrawingShape)a).CornerRadius}, history {n}->{N()}");
Set(manager,"CurrentTool",ShapeType.DrawingArrow);var arrow=(ArrowDrawingShape)Call(manager,"CreateShape",ShapeType.DrawingArrow);arrow.Rectangle=new RectangleF(10,200,200,40);Call(manager,"AddShape",arrow);arrow.BorderSize=4;n=N();Call(manager,"PreviewArrowWidth",2);Call(manager,"PreviewArrowWidth",1);Call(manager,"CommitArrowWidth");Console.WriteLine($"ARROW return-to-original width={arrow.BorderSize}, history {n}->{N()}");
Set(manager,"CurrentTool",ShapeType.EffectBlur);var blur=(BlurEffectShape)Call(manager,"CreateShape",ShapeType.EffectBlur);blur.Rectangle=new RectangleF(10,10,100,100);Call(manager,"AddShape",blur);int radius=blur.BlurRadius;n=N();Call(manager,"PreviewBlurStrength",radius==3?23:3);Call(manager,"PreviewBlurStrength",radius);Call(manager,"CommitBlurStrength");Console.WriteLine($"BLUR return-to-original radius={blur.BlurRadius}, history {n}->{N()}");
Set(manager,"CurrentTool",ShapeType.DrawingRectangle);var rect=(RectangleDrawingShape)Call(manager,"CreateShape",ShapeType.DrawingRectangle);rect.Rectangle=new RectangleF(20,20,100,100);Call(manager,"AddShape",rect);rect.OnConfigSave();n=N();Call(manager,"ApplyStyleColor",false,(Color)Call(manager,"GetStyleColor",false));if(N()!=n)throw new Exception("Same color created undo");Console.WriteLine("PASS same-color no-op");
Call(manager,"ApplyStyleColor",false,Color.Magenta);if(N()!=n+1)throw new Exception("Color must create one step");Call(hist,"Undo");Call(hist,"Redo");Console.WriteLine("PASS color change one history step and undo/redo");
n=N();Call(manager,"RestartStepSequence");if(N()!=n)throw new Exception("Restart preference should not edit existing drawing");Console.WriteLine("PASS restart step sequence does not create invisible history");
n=N();Call(manager,"CutOut",RectangleF.Empty);Console.WriteLine($"CUTOUT empty history {n}->{N()}");
// Drive the real node press/release handler with isolated input state.
Set(manager,"CurrentTool",ShapeType.DrawingRectangle);
var target=(RectangleDrawingShape)Call(manager,"CreateShape",ShapeType.DrawingRectangle);target.Rectangle=new RectangleF(40,40,100,80);Call(manager,"AddShape",target);
var nodes=(Array)Get(manager,"ResizeNodes");var node=nodes.GetValue(0)!;Set(node,"Visible",true);Set(node,"Rectangle",new RectangleF(10,10,20,20));
var controls=Array.CreateInstance(node.GetType().BaseType!,1);controls.SetValue(node,0);
var input=Get(manager,"InputManager");
void Mouse(bool down){object now=new MouseState(),oldState=new MouseState();Set(now,"Buttons",down?MouseButtons.Left:MouseButtons.None);Set(oldState,"Buttons",down?MouseButtons.None:MouseButtons.Left);input.GetType().GetField("mouseState",F)!.SetValue(input,now);input.GetType().GetField("oldMouseState",F)!.SetValue(input,oldState);Call(manager,"UpdateObjects",controls,new PointF(15,15));}
n=N();Mouse(true);Mouse(false);if(N()!=n)throw new Exception("Node click added history");
var bounds=target.Rectangle;Mouse(true);target.Rectangle=new RectangleF(40,40,120,80);target.Rectangle=bounds;Mouse(false);if(N()!=n)throw new Exception("Node backtrack added history");
Mouse(true);target.Rectangle=new RectangleF(40,40,120,80);Mouse(false);if(N()!=n+1)throw new Exception("Resize not one step");Call(hist,"Undo");if(((List<BaseShape>)Get(manager,"Shapes")).Last().Rectangle!=bounds)throw new Exception("Resize undo failed");
Console.WriteLine("PASS node click/backtrack/resize one-step undo");
var menuType=manager.GetType().GetNestedType("CaptureMenuForm",BindingFlags.NonPublic)!;
using(var menu=(Form)Activator.CreateInstance(menuType,F,null,new[]{manager},null)!){
var command=menuType.GetMethod("ProcessCmdKey",F)!;int count=((List<BaseShape>)Get(manager,"Shapes")).Count;
command.Invoke(menu,new object[]{new Message(),Keys.Control|Keys.Z});if(((List<BaseShape>)Get(manager,"Shapes")).Count!=count-1)throw new Exception("Toolbar first undo failed");
command.Invoke(menu,new object[]{new Message(),Keys.Control|Keys.Y});if(((List<BaseShape>)Get(manager,"Shapes")).Count!=count)throw new Exception("Toolbar redo failed");
Console.WriteLine("PASS toolbar command routing undo/redo");}
Console.WriteLine("Handler regression complete; no desktop mouse injection.");
}
}
