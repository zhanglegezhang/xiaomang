using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using ShareX;
using ShareX.HelpersLib;

sealed class ProbeMain : MainForm
{
 public readonly List<double> NativeDpiMs = new();
 protected override void OnShown(EventArgs e) { }
 protected override void OnDpiChanged(DpiChangedEventArgs e) {var sw=Stopwatch.StartNew();base.OnDpiChanged(e);NativeDpiMs.Add(sw.Elapsed.TotalMilliseconds);}
}
static class Check
{
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static readonly Type P=typeof(MainForm).Assembly.GetType("ShareX.Program")!;
 static object Field(MainForm main,string name)=>typeof(MainForm).GetField(name,F)!.GetValue(main)!;
 static object Call(MainForm main,string name,params object[] args)=>typeof(MainForm).GetMethod(name,F)!.Invoke(main,args)!;
 static IEnumerable<Control> All(Control root)=>new[]{root}.Concat(root.Controls.Cast<Control>().SelectMany(All));
 static IEnumerable<TreeNode> Nodes(TreeNodeCollection nodes)=>nodes.Cast<TreeNode>().SelectMany(n=>new[]{n}.Concat(Nodes(n.Nodes)));
 static void Pump(){Application.DoEvents();Application.DoEvents();}
 [STAThread] static int Main(string[] args){try{Run(args);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 static void Run(string[] args)
 {
  Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
  Thread.CurrentThread.CurrentUICulture=CultureInfo.GetCultureInfo("zh-CN");
  string output=Path.GetFullPath(args.FirstOrDefault(x=>x.StartsWith("--out="))?[6..]??"build/menu-native-dpi-20261021/baseline");Directory.CreateDirectory(output);
  var config=new ApplicationConfig{ShowTray=false,DisableHotkeys=true};
  foreach(var pair in new (string,object)[]{("Settings",config),("DefaultTaskSettings",config.DefaultTaskSettings),("HotkeysConfig",new HotkeysConfig()),("UploadersConfig",new ShareX.UploadersLib.UploadersConfig()),("Sandbox",true),("CustomPersonalPath",Path.Combine(output,"profile")),("CLI",new ShareXCLIManager(Array.Empty<string>()))})P.GetProperty(pair.Item1,F)!.SetValue(null,pair.Item2);
  using var main=new ProbeMain();P.GetProperty("MainForm",F)!.SetValue(null,main);
  main.HandleCreated-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),main,typeof(MainForm).GetMethod("MainForm_HandleCreated",F)!);
  using var keyOwner=new HotkeyForm();var manager=new HotkeyManager(keyOwner);typeof(HotkeyManager).GetProperty("Hotkeys",F)!.SetValue(manager,HotkeyManager.GetDefaultHotkeyList());P.GetProperty("HotkeyManager",F)!.SetValue(null,manager);
  main.ClientSize=new Size(1400,900);main.Show();Pump();
  var kind=typeof(MainForm).GetNestedType("InlineSettingsKind",F)!;
  void Open(string name){Call(main,"OpenInlineSettings",Enum.Parse(kind,name));Pump();}
  Open("Application");var settings=(ApplicationSettingsForm)Field(main,"cachedApplicationSettingsForm");var tree=All(settings).OfType<TreeView>().First();
  foreach(var node in Nodes(tree.Nodes).ToArray()){tree.SelectedNode=node;Pump();}
  Open("InputMethod");Open("Hotkeys");
  var dpiChanged=typeof(MainForm).GetMethod("OnDpiChanged",F)!;
  var dpiArgs=typeof(DpiChangedEventArgs).GetConstructor(F,null,new[]{typeof(int),typeof(Message)},null)!;
  var dpiField=typeof(Control).GetField("_deviceDpi",F)!;
  var all=All(main).ToArray();var counts=new Dictionary<Control,int>();int paints=0,fontChanges=0;
  foreach(var c in all){counts[c]=0;c.Layout+=(_,_)=>counts[c]++;c.Paint+=(_,_)=>paints++;c.FontChanged+=(_,_)=>fontChanges++;}
  if(args.Contains("--physical")){
   var physical=new List<object>();
   if(Screen.AllScreens.Length<2)throw new Exception("Physical test requires at least two exposed monitors");
   foreach(string page in args.Contains("--verify-only")?Array.Empty<string>():new[]{"Hotkeys","Application","InputMethod","About"}){
    Open(page);if(page=="Application"){tree.SelectedNode=Nodes(tree.Nodes).First(n=>n.Text.Contains("常规"));Pump();}
    main.ClientSize=new Size(1200,800);Pump();
    for(int repeat=0;repeat<4;repeat++)foreach(var screen in Screen.AllScreens){
     counts.Keys.ToList().ForEach(c=>counts[c]=0);paints=fontChanges=0;main.NativeDpiMs.Clear();
     var sw=Stopwatch.StartNew();main.Location=new Point(screen.WorkingArea.Left+60,screen.WorkingArea.Top+60);double move=sw.Elapsed.TotalMilliseconds;Pump();sw.Stop();
     physical.Add(new{Page=page,Repeat=repeat,Screen=screen.DeviceName,Dpi=main.DeviceDpi,MoveMs=move,TotalMs=sw.Elapsed.TotalMilliseconds,NativeDpiMs=main.NativeDpiMs.ToArray(),Layouts=counts.Values.Sum(),HiddenSettingsLayouts=All(settings).Where(c=>!c.Visible).Sum(c=>counts.GetValueOrDefault(c)),FontChanges=fontChanges,Paints=paints,main.Bounds,Font=main.Font.SizeInPoints});
    }
   }
   if(args.Contains("--verify")){
    int checks=0;var activation=new List<object>();void Assert(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
    var appearance=typeof(MainForm).Assembly.GetType("ShareX.MainMenuAppearance")!;
    int fontBaseline=(int)appearance.GetField("FontBaselineDpi",F)!.GetValue(null)!;
    var sizes=new Dictionary<int,Size>();
    for(int repeat=0;repeat<3;repeat++)foreach(var screen in Screen.AllScreens){
     main.Location=new Point(screen.WorkingArea.Left+60,screen.WorkingArea.Top+60);Pump();
     int dpi=main.DeviceDpi;
     Assert(dpi is 144 or 192,"actual DPI is 150% or 200%");
     if(sizes.TryGetValue(dpi,out var old))Assert(main.Size==old,"round trip retains window size at "+dpi);else sizes[dpi]=main.Size;
     foreach(string page in new[]{"Hotkeys","Application","InputMethod","About"}){
      var activateClock=Stopwatch.StartNew();Open(page);activateClock.Stop();activation.Add(new{Page=page,Dpi=dpi,Milliseconds=activateClock.Elapsed.TotalMilliseconds});var strip=(ToolStrip)Field(main,"tsMain");
      Assert(Math.Abs(strip.Font.SizeInPoints-9.5f*dpi/fontBaseline)<.02,"navigation font "+page+" at "+dpi);
      Assert(strip.Items.Cast<ToolStripItem>().All(i=>i.Bounds.Right<=strip.Width),"navigation bounds "+page+" at "+dpi);
      Assert((bool)typeof(MainForm).GetField("navigationDpiRefreshQueued",F)!.GetValue(main)! == false,"DPI queue completes");
      Assert((IntPtr)typeof(MainForm).GetField("dpiRedrawHandle",F)!.GetValue(main)! == IntPtr.Zero,"painting restored");
      if(page=="Application")foreach(var node in Nodes(tree.Nodes).ToArray()){
       tree.SelectedNode=node;Pump();
       foreach(var surface in All(settings).Where(c=>c.GetType().Name=="SettingsDesignSurface"&&c.Visible)){
        Assert(Math.Abs(surface.Font.SizeInPoints-8f*dpi/fontBaseline)<.05,"settings font "+node.Text+" at "+dpi);
        Assert(All(surface).OfType<Button>().Where(b=>b.Visible).All(b=>Math.Abs(b.Font.SizeInPoints-surface.Font.SizeInPoints)<.02),"settings button font "+node.Text);
        var content=surface.Controls.Find("settingsDesignContent",false).First();
        Assert(content.Width>0&&content.Left>=0&&content.Right<=surface.Width,"settings content bounds "+node.Text);
       }
      }
     }
     Open("Application");tree.SelectedNode=Nodes(tree.Nodes).First(n=>n.Text.Contains("常规"));Pump();
     if(repeat==2){using var image=new Bitmap(main.Width,main.Height);main.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(output,"settings-"+dpi+".png"));}
    }
    Call(main,"QueueMainMenuDpiLayout");main.Hide();Pump();Assert(!main.Visible&&!IsWindowVisible(main.Handle),"hide during queued DPI stays hidden");main.Show();Pump();Assert(main.Visible&&IsWindowVisible(main.Handle),"window reopens after interrupted DPI");
    Assert((IntPtr)typeof(MainForm).GetField("dpiRedrawHandle",F)!.GetValue(main)! == IntPtr.Zero,"interrupted painting restored");
    Call(main,"QueueMainMenuDpiLayout");Pump();Assert(main.Visible,"DPI sizing query without transition recovers");
    Assert(config.DisableHotkeys,"manual hotkey preference preserved");
    File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,Passed=true,Activation=activation}));
   }
   File.WriteAllText(Path.Combine(output,"physical.json"),JsonSerializer.Serialize(new{Scope="Actual monitor moves of isolated MainForm; elapsed includes native DPI handling and DoEvents layout/paint",Screens=Screen.AllScreens.Select(s=>new{s.DeviceName,s.Bounds,s.WorkingArea}),Results=physical},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
   Console.WriteLine("DONE PHYSICAL "+Screen.AllScreens.Length);return;
  }
  var results=new List<object>();int oldDpi=main.DeviceDpi;
  foreach(string page in new[]{"Hotkeys","Application","InputMethod","About"}){
   Open(page);
   var samples=new List<object>();
   for(int i=0;i<8;i++){
    int dpi=oldDpi==192?144:192;
    foreach(var c in All(main))dpiField.SetValue(c,dpi);
    counts.Keys.ToList().ForEach(c=>counts[c]=0);paints=fontChanges=0;
    var sw=Stopwatch.StartNew();
    // Execute MainForm.OnDpiChanged, including the framework scaling routine.
    // Child-control DPI is supplied because the runner exposes only one monitor.
    var next=new Rectangle(main.Left,main.Top,(int)Math.Round(main.Width*dpi/(double)oldDpi),(int)Math.Round(main.Height*dpi/(double)oldDpi));
    IntPtr rect=Marshal.AllocHGlobal(16);
    try {
     Marshal.WriteInt32(rect,0,next.Left);Marshal.WriteInt32(rect,4,next.Top);Marshal.WriteInt32(rect,8,next.Right);Marshal.WriteInt32(rect,12,next.Bottom);
     var message=Message.Create(main.Handle,0x02E0,new IntPtr(dpi|(dpi<<16)),rect);
     dpiChanged.Invoke(main,new[]{dpiArgs.Invoke(new object[]{oldDpi,message})});
    } finally {Marshal.FreeHGlobal(rect);}
    double native=sw.Elapsed.TotalMilliseconds;
    Pump();sw.Stop();
    var top=counts.OrderByDescending(p=>p.Value).Take(6).Select(p=>new{Name=p.Key.Name,Type=p.Key.GetType().Name,p.Value}).ToArray();
    samples.Add(new{Dpi=dpi,FrameworkMs=native,TotalMs=sw.Elapsed.TotalMilliseconds,Layouts=counts.Values.Sum(),HiddenSettingsLayouts=All(settings).Where(c=>!c.Visible).Sum(c=>counts.GetValueOrDefault(c)),FontChanges=fontChanges,Paints=paints,Top=top});oldDpi=dpi;
   }
   results.Add(new{Page=page,Samples=samples});Console.WriteLine(page+" "+JsonSerializer.Serialize(samples));
  }
  File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Framework=typeof(Form).Assembly.GetName().Version,Displays=Screen.AllScreens.Length,Scope="MainForm.OnDpiChanged and framework scaling with fixture DPI; not physical monitor timing",Results=results},new JsonSerializerOptions{WriteIndented=true}));
  main.Dispose();Console.WriteLine("DONE");
 }
}
