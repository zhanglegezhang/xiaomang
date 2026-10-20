using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics;
using System.Text.Json;
using System.Drawing.Imaging;
static class Check{
 const BindingFlags F=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static Assembly app;static int count;static string output;static bool navigationCancelled;
 static Type T(string name)=>app.GetType("ShareX."+name,true)!;
 static object Get(object o,string n){var t=o as Type??o.GetType();var f=t.GetField(n,F);return f!=null?f.GetValue(o is Type?null:o)!:t.GetProperty(n,F)!.GetValue(o is Type?null:o)!;}
 static void Set(object o,string n,object v){var t=o as Type??o.GetType();var f=t.GetField(n,F);if(f!=null)f.SetValue(o is Type?null:o,v);else t.GetProperty(n,F)!.SetValue(o is Type?null:o,v);}
 static object Call(object o,string n,params object[] a)=>(o as Type??o.GetType()).GetMethod(n,F)!.Invoke(o is Type?null:o,a)!;
 static object New(string n,params object[] a)=>Activator.CreateInstance(T(n),F,null,a,null)!;
 static void Assert(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);count++;}
 static void Wait(Task t){var watch=Stopwatch.StartNew();while(!t.IsCompleted){if(watch.ElapsedMilliseconds>15000)throw new TimeoutException();Application.DoEvents();Thread.Sleep(5);}t.GetAwaiter().GetResult();}
 [STAThread]static int Main(string[] args){try{
  string runtime=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
  AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
  app=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.dll"));
  Application.SetHighDpiMode(args.Contains("--dpi96")?HighDpiMode.DpiUnaware:HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
  if(args.Contains("--scroll")){Scroll(args[2]);return 0;}
  var config=New("ApplicationConfig");Set(T("Program"),"Settings",config);Set(T("Program"),"Sandbox",true);Set(T("Program"),"CustomPersonalPath",Path.Combine(output,"profile"));Set(T("Program"),"DefaultTaskSettings",Get(config,"DefaultTaskSettings"));
  try{Call(T("XiaomangInputMethodManager"),"OpenSettings");throw new Exception("Missing settings unexpectedly opened");}catch(TargetInvocationException e){Assert(e.InnerException is FileNotFoundException&&e.InnerException.Message.Contains("全局设置 → 注册 → 文本输入")&&e.InnerException.Message.Contains("保留"),"BUG-08 missing input settings provides installation path and preservation hint");}
  string guide=Path.Combine(output,"guide.html"),log=Path.Combine(output,"versions.txt");Call(T("AboutPageForm"),"ExportMindMap",guide);Call(T("AboutPageForm"),"ExportVersionLog",log);
  string version=(string)Get(T("Program"),"ProductVersion");Assert(File.ReadAllText(guide).Contains("小蟒"+version)&&!File.ReadAllText(guide).Contains("__XIAOMANG_PRODUCT_VERSION__"),"BUG-10 exported guide uses actual product version");Assert(File.ReadAllText(log).StartsWith("当前运行版本：小蟒"+version),"BUG-10 version log identifies running version");
  var manager=New("DailyNewsManager",Path.Combine(output,"news"));var regionType=T("DailyNewsRegion");
  foreach(string region in new[]{"Domestic","International"}){
   using var window=(Form)New("DailyNewsWindow",Enum.Parse(regionType,region),manager);
   var watch=Stopwatch.StartNew();Wait((Task)Call(window,"InitializeAsync"));
   var content=(Control)Get(window,"contentPanel");var labels=content.Controls.OfType<Label>().ToArray();
   Assert(labels.Any(x=>x.Text.Contains("尚未配置资讯服务")),"BUG-09 "+region+" missing-key empty state is visible native text");
   Assert(content.Controls.OfType<Button>().Any(x=>x.Name=="openDailyNewsSettings"),"BUG-09 "+region+" provides settings entry");Assert(Get(window,"browser")==null,"BUG-09 "+region+" does not initialize WebView for empty state");
   Wait((Task)Call(window,"UpdateNewsAsync"));Assert(((Label)Get(window,"statusLabel")).Text.Contains("API Key"),"BUG-09 "+region+" manual refresh explains missing key");
   Console.WriteLine("EMPTY_STATE_MS "+region+" "+watch.Elapsed.TotalMilliseconds);
   window.Show();Application.DoEvents();using var image=new Bitmap(window.Width,window.Height);window.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(output,region+"-empty.png"));window.Close();
  }
  string history=(string)Call(manager,"GetTodayHtmlPath",Enum.Parse(regionType,"Domestic"));File.WriteAllText(history,"<!doctype html><meta charset='utf-8'><p>HISTORY-MUST-SURVIVE</p>");
  using(var window=(Form)New("DailyNewsWindow",Enum.Parse(regionType,"Domestic"),manager)){
   window.Show();var wait=Stopwatch.StartNew();while(!(bool)Get(window,"browserReady")){Application.DoEvents();Thread.Sleep(10);if(wait.ElapsedMilliseconds>15000)throw new Exception("History browser initialization timeout");}
   var browser=Get(window,"browser");var core=Get(browser,"CoreWebView2");var ev=core.GetType().GetEvent("NavigationStarting")!;ev.AddEventHandler(core,Delegate.CreateDelegate(ev.EventHandlerType!,typeof(Check).GetMethod("Navigation",F)!));bool rendered=false;var renderWatch=Stopwatch.StartNew();while(renderWatch.ElapsedMilliseconds<8000){var loaded=(Task)Call(browser,"ExecuteScriptAsync","document.body.textContent");Wait(loaded);if(((string)Get(loaded,"Result")).Contains("HISTORY-MUST-SURVIVE")){rendered=true;break;}Application.DoEvents();Thread.Sleep(50);}if(!rendered){var diagnostic=(Task)Call(browser,"ExecuteScriptAsync","location.href+' '+document.documentElement.outerHTML");Wait(diagnostic);Console.WriteLine("HISTORY_DIAGNOSTIC "+Get(diagnostic,"Result"));}Assert(rendered,"BUG-09 existing history renders without API key");
   navigationCancelled=false;Call(core,"Navigate","data:text/html;base64,"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<p>UNTRUSTED</p>")));var deniedWatch=Stopwatch.StartNew();while(!navigationCancelled&&deniedWatch.ElapsedMilliseconds<3000){Application.DoEvents();Thread.Sleep(10);}Assert(navigationCancelled,"BUG-09 unrelated data navigation remains blocked");
   Wait((Task)Call(window,"UpdateNewsAsync"));Assert(ReferenceEquals(browser,Get(window,"browser"))&&((Label)Get(window,"statusLabel")).Text.Contains("保留历史资讯"),"BUG-09 missing-key refresh preserves visible history");window.Close();
  }
  File.Delete(history);
  Call(Get(config,"DailyNewsSettings"),"SetApiKey","synthetic-offline-only");
  using(var window=(Form)New("DailyNewsWindow",Enum.Parse(regionType,"Domestic"),manager)){Wait((Task)Call(window,"InitializeAsync"));Assert(((Control)Get(window,"contentPanel")).Controls.OfType<Label>().Any(x=>x.Text.Contains("还没有资讯")),"configured but no history explains first refresh");}
  Console.WriteLine("RESULT "+count+" checks passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 static void Navigation(object sender,object args){navigationCancelled=(bool)Get(args,"Cancel");Console.WriteLine("HISTORY_NAV_CANCEL="+navigationCancelled);}
 static void Scroll(string fixtures){
  var screen=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(app.Location)!,"ShareX.ScreenCaptureLib.dll"));
  var type=screen.GetType("ShareX.ScreenCaptureLib.ScrollingCaptureManager",true)!;var optionsType=screen.GetType("ShareX.ScreenCaptureLib.ScrollingCaptureOptions",true)!;
  var cases=JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures,"cases.json"))).RootElement;
  var results=new List<object>();foreach(var item in cases.EnumerateArray()){
   string kind=item.GetProperty("kind").GetString()!,dir=item.GetProperty("directory").GetString()!;
   using var manager=(IDisposable)Activator.CreateInstance(type,Activator.CreateInstance(optionsType))!;Bitmap result=null;
   try{foreach(var file in item.GetProperty("files").EnumerateArray()){
    using var image=new Bitmap(Path.Combine(dir,file.GetString()!));var next=(Bitmap)Call(manager,"CombineImages",result,image);if(next==null)break;result?.Dispose();result=next;
   }
   result!.Save(Path.Combine(output,kind+"-stitched.png"));int marker=0;for(int y=Math.Max(0,result.Height-100);y<result.Height;y++)for(int x=0;x<result.Width-40;x++){var c=result.GetPixel(x,y);if(c.R>220&&c.G<50&&c.B>100)marker++;}
   results.Add(new{kind,Height=result.Height,ExpectedHeight=item.GetProperty("height").GetInt32(),BottomMarkerPixels=marker,Status=Get(manager,"status").ToString()});
   Console.WriteLine(JsonSerializer.Serialize(results[^1]));
   }finally{result?.Dispose();}
  }
  File.WriteAllText(Path.Combine(output,"scroll-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
 }
}
