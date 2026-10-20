using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ShareX.HelpersLib;
static class Host{
 [STAThread]static void Main(string[] args){
  string runtime=Path.GetFullPath(args[0]),root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
  AssemblyLoadContext.Default.Resolving+=(_,n)=>{var p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
  Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
  var form=new Form{Text="小蟒长截图实际验证（隔离页面）",ClientSize=new Size(1000,1100),StartPosition=FormStartPosition.CenterScreen};
  var browser=new WebView2{Dock=DockStyle.Fill};var start=new Button{Text="开始逐项截图测试（高亮隔离在截图区域外）",Dock=DockStyle.Top,Height=300,Enabled=false};form.Controls.Add(browser);form.Controls.Add(start);
  var screen=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.ScreenCaptureLib.dll"));var type=screen.GetType("ShareX.ScreenCaptureLib.ScrollingCaptureManager",true)!;var optionsType=screen.GetType("ShareX.ScreenCaptureLib.ScrollingCaptureOptions",true)!;
  const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
  form.Shown+=async(_,_)=>{try{var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"webview-profile"));await browser.EnsureCoreWebView2Async(env);browser.Source=new Uri(Path.Combine(root,"..","scroll","normal","page.html"));start.Enabled=true;}catch(Exception e){File.WriteAllText(Path.Combine(root,"error.txt"),e.ToString());start.Text=e.Message;}};
  start.Click+=async(_,_)=>{
   start.Enabled=false;var results=new List<object>();
   try{
    foreach(string kind in new[]{"normal","short-final-scroll","blank-tail","fixed-footer","edge-only-tail"}){
     string file=Path.GetFullPath(Path.Combine(root,"..","scroll",kind,"page.html"));
     browser.Source=new Uri(file);await Task.Delay(800);await browser.ExecuteScriptAsync("window.scrollTo(0,0)");browser.Focus();await Task.Delay(200);
     var options=Activator.CreateInstance(optionsType)!;optionsType.GetProperty("ShowRegion")!.SetValue(options,false);optionsType.GetProperty("ScrollDelay")!.SetValue(options,300);optionsType.GetProperty("ScrollMethod")!.SetValue(options,Enum.Parse(optionsType.GetProperty("ScrollMethod")!.PropertyType,"PageDown"));
     using var manager=(IDisposable)Activator.CreateInstance(type,options)!;
     type.GetField("selectedWindow",F)!.SetValue(manager,new WindowInfo(form.Handle));type.GetField("selectedRectangle",F)!.SetValue(manager,browser.RectangleToScreen(browser.ClientRectangle));
     var timer=new System.Windows.Forms.Timer{Interval=20000};timer.Tick+=(_,_)=>{type.GetMethod("StopCapture",F)!.Invoke(manager,null);timer.Stop();};timer.Start();
     var clock=Stopwatch.StartNew();object task=type.GetMethod("StartCapture",F)!.Invoke(manager,null)!;await (Task)task;timer.Stop();timer.Dispose();
     var image=(Bitmap)type.GetProperty("Result",F)!.GetValue(manager)!;image.Save(Path.Combine(root,kind+"-native.png"));
     int marker=0;for(int y=Math.Max(0,image.Height-300);y<image.Height;y++)for(int x=0;x<image.Width-35;x++){var c=image.GetPixel(x,y);if(c.R>220&&c.G<50&&c.B>100)marker++;}
     string scroll=await browser.ExecuteScriptAsync("JSON.stringify({y:scrollY,height:innerHeight,total:document.documentElement.scrollHeight})");
     results.Add(new{kind,image.Width,image.Height,marker,Milliseconds=clock.Elapsed.TotalMilliseconds,Scroll=scroll,Status=task.GetType().GetProperty("Result")!.GetValue(task)!.ToString()});
     File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
    start.Text="测试完成，可关闭";start.Visible=true;form.Text="小蟒长截图实际验证 — 已完成";
   }catch(Exception e){File.WriteAllText(Path.Combine(root,"error.txt"),e.ToString());start.Text="测试失败："+e.Message;}
  };
  Application.Run(form);
 }
}
