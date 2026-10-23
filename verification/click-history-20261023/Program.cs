using System.Drawing;
using System.IO;
using System.Reflection;
using ShareX.ScreenCaptureLib;
class Check {
const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
static object Get(object o,string n)=>o.GetType().GetProperty(n,F)?.GetValue(o)??o.GetType().GetField(n,F)!.GetValue(o)!;
static void Set(object o,string n,object v){var p=o.GetType().GetProperty(n,F);if(p!=null)p.SetValue(o,v);else o.GetType().GetField(n,F)!.SetValue(o,v);}
static object Call(object o,string n,params object[] a)=>o.GetType().GetMethods(F).First(m=>m.Name==n&&m.GetParameters().Length==a.Length&&m.GetParameters().Select((p,i)=>a[i]==null||p.ParameterType.IsInstanceOfType(a[i])).All(x=>x)).Invoke(o,a)!;
static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
static int pass,fail;
static void Test(string name,Action a){try{a();pass++;Console.WriteLine("PASS "+name);}catch(Exception e){fail++;Console.WriteLine("FAIL "+name+": "+(e.InnerException??e).Message);}}
[STAThread]static int Main(){
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
foreach(var seed in new[]{ShapeType.DrawingStep,ShapeType.DrawingCursor})
foreach(var effect in new[]{ShapeType.EffectBlur,ShapeType.EffectPixelate,ShapeType.EffectHighlight})
Test(seed+" -> five empty "+effect+" clicks -> keyboard undo/redo",()=>{
using var b=new Bitmap(900,600);using var f=new RegionCaptureForm(RegionCaptureMode.Annotation,new RegionCaptureOptions{DetectWindows=false,DetectControls=false,RememberMenuState=false},(Bitmap)b.Clone(),true);_=f.Handle;
var m=Get(f,"ShapeManager");Call(m,"CreateToolbar");var h=Get(m,"history");List<BaseShape> Shapes()=>(List<BaseShape>)Get(m,"Shapes");int History()=>(int)Get(Get(h,"undoMementoStack"),"Count");
Set(m,"CurrentTool",seed);
for(int i=0;i<5;i++){Set(m,"IsCtrlModifier",true);Call(m,"StartRegionSelection");Call(m,"form_MouseUp",f,new MouseEventArgs(MouseButtons.Left,1,20,20,0));}
Console.WriteLine($"  created={Shapes().Count} history={History()}");
Set(m,"CurrentTool",effect);
for(int i=0;i<5;i++){Set(m,"IsCtrlModifier",true);Call(m,"StartRegionSelection");Call(m,"form_MouseUp",f,new MouseEventArgs(MouseButtons.Left,1,20,20,0));}
Need(Shapes().Count==5,"empty clicks left shapes");
var timer=System.Diagnostics.Stopwatch.StartNew();
Call(m,"form_KeyDown",f,new KeyEventArgs(Keys.Control|Keys.Z));
timer.Stop();Console.WriteLine($"  first keyboard undo handler={timer.Elapsed.TotalMilliseconds:F3} ms");
Need(Shapes().Count==4,$"first undo left {Shapes().Count}, history={History()}");
for(int i=0;i<12;i++)Call(m,"form_KeyDown",f,new KeyEventArgs(Keys.Control|Keys.Z));
Need(Shapes().Count==0,"repeated undo stuck");
for(int i=0;i<5;i++)Call(m,"form_KeyDown",f,new KeyEventArgs(Keys.Control|Keys.Y));
Need(Shapes().Count==5,"redo missing");
if(seed==ShapeType.DrawingStep)Need(Shapes().Cast<StepDrawingShape>().Select(s=>s.Number).SequenceEqual(Enumerable.Range(1,5)),"sequence corrupted");
Need(!(bool)Get(m,"IsCreating")&&!(bool)Get(m,"IsMoving"),"input stuck");
});
foreach(var type in new[]{ShapeType.EffectBlur,ShapeType.EffectPixelate,ShapeType.EffectHighlight})
Test(type+" / detected window empty click, valid drag and redo preservation",()=>{
using var b=new Bitmap(900,600);using var f=new RegionCaptureForm(RegionCaptureMode.Annotation,new RegionCaptureOptions{DetectWindows=true,DetectControls=true},(Bitmap)b.Clone(),true);_=f.Handle;var m=Get(f,"ShapeManager");var h=Get(m,"history");Set(m,"CurrentTool",type);
Set(m,"Windows",new List<SimpleWindowInfo>{new SimpleWindowInfo(IntPtr.Zero,new Rectangle(-100000,-100000,200000,200000))});
var s=(BaseShape)Call(m,"AddShape");s.OnCreating();Call(m,"EndRegionSelection");Need(((List<BaseShape>)Get(m,"Shapes")).Count==0,"window became an effect");Need(!(bool)Get(h,"CanUndo"),"empty history");
s=(BaseShape)Call(m,"AddShape");s.OnCreating();s.Rectangle=new RectangleF(100,100,200,100);Call(m,"EndRegionSelection");Need((bool)Get(h,"CanUndo"),"drag missing history");Call(h,"Undo");
s=(BaseShape)Call(m,"AddShape");s.OnCreating();Call(m,"EndRegionSelection");Need((bool)Get(h,"CanRedo"),"empty click erased redo");Call(h,"Redo");Need(((List<BaseShape>)Get(m,"Shapes")).Count==1,"redo missing effect");
});
string fixture=Path.Combine(Path.GetTempPath(),"xiaomang-history-"+Guid.NewGuid()+".png");
try {
using(var image=new Bitmap(40,40))image.Save(fixture);
foreach(var type in new[]{ShapeType.DrawingImage,ShapeType.DrawingSticker})
Test(type+" / real file quick insert undo/redo",()=>{
using var b=new Bitmap(900,600);var options=new RegionCaptureOptions{DetectWindows=false,DetectControls=false};options.AnnotationOptions.LastImageFilePath=fixture;options.AnnotationOptions.LastStickerPath=fixture;
using var f=new RegionCaptureForm(RegionCaptureMode.Annotation,options,(Bitmap)b.Clone(),true);_=f.Handle;var m=Get(f,"ShapeManager");var h=Get(m,"history");Call(m,"CreateToolbar");Set(m,"CurrentTool",type);Set(m,"IsCtrlModifier",true);Call(m,"StartRegionSelection");Call(m,"form_MouseUp",f,new MouseEventArgs(MouseButtons.Left,1,20,20,0));
Need(((List<BaseShape>)Get(m,"Shapes")).Count==1,"file not loaded");Call(m,"form_KeyDown",f,new KeyEventArgs(Keys.Control|Keys.Z));Need(((List<BaseShape>)Get(m,"Shapes")).Count==0,"insert not undone");Call(h,"Redo");Need(((List<BaseShape>)Get(m,"Shapes")).Count==1,"insert not redone");
});
}finally{File.Delete(fixture);}
Console.WriteLine($"TOTAL pass={pass} fail={fail}; creation and keyboard handler integration, no desktop input simulation.");return fail==0?0:1;
}
}

