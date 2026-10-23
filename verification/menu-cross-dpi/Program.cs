using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using ShareX;
using ShareX.HelpersLib;

sealed class ProbeMain : MainForm { protected override void OnShown(EventArgs e) { } }
static class Check
{
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static readonly Type P=typeof(MainForm).Assembly.GetType("ShareX.Program")!;
 static readonly Type Appearance=typeof(MainForm).Assembly.GetType("ShareX.MainMenuAppearance")!;
 static object Field(object o,string name)=>typeof(MainForm).GetField(name,F)!.GetValue(o)!;
 static void Set(string name,object value)=>P.GetProperty(name,F)!.SetValue(null,value);
 static object Call(object o,string name,params object[] args)=>(o is Type type?type:o.GetType()).GetMethod(name,F)!.Invoke(o is Type?null:o,args)!;
 static object MainCall(MainForm main,string name,params object[] args)=>typeof(MainForm).GetMethod(name,F)!.Invoke(main,args)!;
 static IEnumerable<Control> All(Control root)=>new[]{root}.Concat(root.Controls.Cast<Control>().SelectMany(All));
 static IEnumerable<TreeNode> Nodes(TreeNodeCollection nodes)=>nodes.Cast<TreeNode>().SelectMany(n=>new[]{n}.Concat(Nodes(n.Nodes)));
 static void Pump(int ms=30){var clock=Stopwatch.StartNew();do{Application.DoEvents();Thread.Sleep(1);}while(clock.ElapsedMilliseconds<ms);}
 static int checks;
 static void Assert(bool condition,string text){if(!condition)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
 static double Measure(Action action,int count=12){var data=new List<double>();for(int i=0;i<count+2;i++){var clock=Stopwatch.StartNew();action();clock.Stop();if(i>=2)data.Add(clock.Elapsed.TotalMilliseconds);}data.Sort();return data[data.Count/2];}
 [STAThread] static int Main(string[] args){try{Run(args);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 static void Run(string[] args)
 {
  Application.SetHighDpiMode(args.Contains("--96")?HighDpiMode.DpiUnaware:HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
  Thread.CurrentThread.CurrentUICulture=CultureInfo.GetCultureInfo("zh-CN");
  string output=Path.GetFullPath(args.FirstOrDefault(x=>x.StartsWith("--out="))?[6..]??"build/menu-cross-dpi-20261021/baseline");Directory.CreateDirectory(output);
  var config=new ApplicationConfig{ShowTray=false,DisableHotkeys=true};
  foreach(var pair in new (string,object)[]{("Settings",config),("DefaultTaskSettings",config.DefaultTaskSettings),("HotkeysConfig",new HotkeysConfig()),("UploadersConfig",new ShareX.UploadersLib.UploadersConfig()),("Sandbox",true),("CustomPersonalPath",Path.Combine(output,"profile")),("CLI",new ShareXCLIManager(Array.Empty<string>()))})Set(pair.Item1,pair.Item2);
  using var main=new ProbeMain();Set("MainForm",main);
  main.HandleCreated-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),main,typeof(MainForm).GetMethod("MainForm_HandleCreated",F)!);
  using var keyOwner=new HotkeyForm();var manager=new HotkeyManager(keyOwner);typeof(HotkeyManager).GetProperty("Hotkeys",F)!.SetValue(manager,HotkeyManager.GetDefaultHotkeyList());Set("HotkeyManager",manager);
  main.ClientSize=new Size(1400,900);main.Show();Pump();
  var kind=typeof(MainForm).GetNestedType("InlineSettingsKind",F)!;
  void Open(string name)=>MainCall(main,"OpenInlineSettings",Enum.Parse(kind,name));
  Open("Application");Pump();var settings=(ApplicationSettingsForm)Field(main,"cachedApplicationSettingsForm");
  var tree=All(settings).OfType<TreeView>().First();
  foreach(var node in Nodes(tree.Nodes).ToArray()){tree.SelectedNode=node;Pump(1);}
  tree.SelectedNode=Nodes(tree.Nodes).First(n=>n.Text.Contains("常规"));Pump();
  Console.WriteLine($"WARM settingsControls={All(settings).Count()}; dpi={main.DeviceDpi}; displays={Screen.AllScreens.Length}");
  var measurePaint=All(settings).ToArray();int hiddenInvalidations=0;foreach(var c in measurePaint)c.Invalidated+=(_,_)=>hiddenInvalidations++;
  Open("InputMethod");Pump();Open("Hotkeys");Pump();
  double warm=Measure(()=>MainCall(main,"ConfigureMainMenuAppearance"));
  hiddenInvalidations=0;MainCall(main,"ConfigureMainMenuAppearance");int hiddenThemeInvalidations=hiddenInvalidations;
  var dpiMethod=typeof(MainForm).GetMethod("RefreshMainMenuDpi",F);
  hiddenInvalidations=0;
  double dpiExtra=Measure(()=>{if(dpiMethod!=null)dpiMethod.Invoke(main,null);else MainCall(main,"ConfigureMainMenuAppearance");});
  int hiddenDpiInvalidations=hiddenInvalidations;
  int paints=0;main.Paint+=(_,_)=>paints++;
  double resize=Measure(()=>{main.ClientSize=new Size(main.ClientSize.Width==1400?1380:1400,900);});
  int syncPaints=paints;
  Console.WriteLine($"BENCH themeMedianMs={warm:F3}; dpiExtraMedianMs={dpiExtra:F3}; hiddenThemeInvalidations={hiddenThemeInvalidations}; hiddenDpiInvalidations={hiddenDpiInvalidations}; resizeMedianMs={resize:F3}; synchronousResizePaints={syncPaints}");
  var pageBenchmarks=new List<object>();
  void BenchPage(string name)
  {
   double elapsed=Measure(()=>{if(dpiMethod!=null)dpiMethod.Invoke(main,null);else MainCall(main,"ConfigureMainMenuAppearance");},6);
   double flushed=Measure(()=>{if(dpiMethod!=null)dpiMethod.Invoke(main,null);else MainCall(main,"ConfigureMainMenuAppearance");Application.DoEvents();},6);
   pageBenchmarks.Add(new{Page=name,ExtraDpiMedianMs=elapsed,WithQueuedLayoutAndPaintMedianMs=flushed});
   Console.WriteLine($"PAGE {name}: {elapsed:F3} ms; flush={flushed:F3} ms");
  }
  foreach(string name in new[]{"Hotkeys","InputMethod","About"}){Open(name);Pump();BenchPage(name);}
  Open("Application");Pump();
  foreach(var node in Nodes(tree.Nodes).ToArray()){tree.SelectedNode=node;Pump();BenchPage("Settings/"+node.Text);}
  foreach(string name in new[]{"OpenScreenshotFolderPage","OpenTemporaryFilePage"}){MainCall(main,name);Pump();BenchPage(name);}
  var transitions=new List<object>();
  if(Screen.AllScreens.Length>1){
   for(int repeat=0;repeat<3;repeat++)foreach(var screen in Screen.AllScreens){var clock=Stopwatch.StartNew();main.Location=new Point(screen.WorkingArea.X+30,screen.WorkingArea.Y+30);Pump(60);transitions.Add(new{Display=screen.DeviceName,Dpi=main.DeviceDpi,ElapsedMs=clock.Elapsed.TotalMilliseconds});}
  }
  var dpiField=typeof(Control).GetField("_deviceDpi",F)!;
  int initial=main.DeviceDpi;
  // Controlled DPI geometry exercise; native cross-screen timings above are separate.
  foreach(int dpi in args.Contains("--96")?new[]{96}:new[]{144,192,144,192}){
   Open("Hotkeys");Pump();
   foreach(var control in All(main))dpiField.SetValue(control,dpi);
   dpiField.SetValue(settings,dpi);
   if(dpiMethod!=null)dpiMethod.Invoke(main,null);else MainCall(main,"ConfigureMainMenuAppearance");
   var strip=(ToolStrip)Field(main,"tsMain");var item=strip.Items[0];
   Assert(item.Bounds.Left>=0&&item.Bounds.Right<=strip.ClientSize.Width,"navigation not clipped at "+dpi);
   Assert(item.Width==(int)Math.Round(156*dpi/96d),"navigation width follows DPI "+dpi);
   int fontBaseline=(int)Appearance.GetField("FontBaselineDpi",F)!.GetValue(null)!;
   Assert(Math.Abs(strip.Font.SizeInPoints-9.5f*dpi/fontBaseline)<0.02,"navigation font follows DPI "+dpi);
   Open("Application");Pump();
   foreach(var node in Nodes(tree.Nodes).ToArray()){
    tree.SelectedNode=node;Pump(1);
    foreach(var surface in All(settings).Where(c=>c.GetType().Name=="SettingsDesignSurface"&&c.Visible)){
     Assert(surface.Width>0&&surface.Height>0,"visible settings surface sized "+node.Text);
     var buttons=All(surface).OfType<Button>().Where(b=>b.Visible).ToArray();
     Assert(buttons.All(b=>Math.Abs(b.Font.SizeInPoints-surface.Font.SizeInPoints)<0.02),"settings button font retained "+node.Text);
     Assert(All(surface).OfType<ComboBox>().All(c=>c.ItemHeight>=Math.Round(24*surface.DeviceDpi/96d)),"drop-down row height follows DPI "+node.Text);
    }
   }
  }
  main.ClientSize=new Size(1500,1000);Pump();using(var shot=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(output,"settings.png"));}
  if(dpiMethod!=null){Assert(hiddenDpiInvalidations==0,"DPI refresh does not restyle hidden settings");Assert(syncPaints==0,"resize does not synchronously repaint form");}
  File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Version=P.GetProperty("ProductVersion",F)!.GetValue(null),ThemeMedianMs=warm,DpiExtraMedianMs=dpiExtra,HiddenThemeInvalidations=hiddenThemeInvalidations,HiddenDpiInvalidations=hiddenDpiInvalidations,ResizeMedianMs=resize,SynchronousResizePaints=syncPaints,PageBenchmarks=pageBenchmarks,Displays=Screen.AllScreens.Select(s=>new{s.DeviceName,s.Bounds}).ToArray(),NativeTransitions=transitions,Checks=checks,Simulation="Control DPI and navigation geometry; not native OS font autoscaling"},new JsonSerializerOptions{WriteIndented=true}));
  main.Dispose();Console.WriteLine($"PASS {checks} checks");
 }
}
