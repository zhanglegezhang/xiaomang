using ShareX;
using ShareX.ScreenCaptureLib;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;
using System.IO;

class Probe : CaptureBase
{
    public int Executed, Ended;
    public bool Fail;
    protected override TaskMetadata Execute(TaskSettings s) { Executed++; if(Fail)throw new IOException("Synthetic failure"); return null; }
    protected override void EndCaptureWorkflow(){Ended++;}
}
class RegionProbe : CaptureRegion
{
    public int Executed;
    public bool Fail;
    protected override TaskMetadata Execute(TaskSettings s){Executed++;if(Fail)throw new IOException("Synthetic selection failure");return null;}
}
static class Check
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static int count;
    static Type ProgramType=>typeof(MainForm).Assembly.GetType("ShareX.Program",true)!;
    static void Set(string name,object value)=>ProgramType.GetProperty(name,F)!.SetValue(null,value);
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F)!.Invoke(o,args)!;
    static void Assert(bool ok,string name){if(!ok)throw new Exception(name);count++;}
    static IEnumerable<Control> All(Control c){yield return c;foreach(Control x in c.Controls)foreach(var y in All(x))yield return y;}
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(args.Contains("--96")?HighDpiMode.DpiUnaware:HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
            string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
            var config=new ApplicationConfig();Set("Sandbox",true);Set("CustomPersonalPath",output);Set("Settings",config);Set("DefaultTaskSettings",config.DefaultTaskSettings);
            Set("CLI",new ShareXCLIManager(Array.Empty<string>()));Set("HotkeysConfig",new HotkeysConfig());Set("UploadersConfig",new ShareX.UploadersLib.UploadersConfig());
            var timings=new List<double>();
            foreach(decimal delay in new decimal[]{0,3,300,decimal.MaxValue,-1})
            {
                var settings=JsonConvert.DeserializeObject<TaskSettings>("{\"CaptureSettings\":{\"ScreenshotDelay\":"+delay.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"CaptureAutoHideDesktopIcons\":false},\"GeneralSettings\":{\"ToastWindowAutoHide\":false}}")!;
                var probe=new Probe();var watch=Stopwatch.StartNew();probe.Capture(settings,false);watch.Stop();timings.Add(watch.Elapsed.TotalMilliseconds);
                Assert(probe.Executed==1&&probe.Ended==1,"Legacy delay does not defer capture or completion: "+delay);
                Assert(settings.CaptureSettings.ScreenshotDelay==delay,"Legacy configuration can still be read without migration");
                probe.Fail=true;probe.Capture(settings,false);Assert(probe.Executed==2&&probe.Ended==2,"Error cleanup remains synchronous");
                var region=new RegionProbe();region.Fail=true;region.Capture(settings,false);region.Fail=false;region.Capture(settings,false);
                Assert(region.Executed==2,"Selection workflow lock released after failure");
            }
            foreach(string name in new[]{"nudScreenshotDelay","lblScreenshotDelay","lblScreenshotDelayInfo"})Assert(typeof(TaskSettingsForm).GetField(name,F)==null,"Retired control is not allocated: "+name);
            var loads=new List<object>();
            foreach(int dpi in new[]{96,192})
            {
                using var owner=new TaskSettingsForm(config.DefaultTaskSettings,true,true);
                var watch=Stopwatch.StartNew();var page=(TabPage)Call(owner,"LoadDefaultSettingsPage","tpCaptureGeneral",dpi);watch.Stop();
                var controls=All(page).ToArray();Assert(!controls.Any(x=>x.Name.Contains("ScreenshotDelay")),"No delay in capture settings");
                Assert(controls.Any(x=>x.Name=="cbShowCursor")&&controls.Any(x=>x.Name=="nudCaptureShadowOffset"),"Other capture settings preserved");
                Assert(ReferenceEquals(page,Call(owner,"LoadDefaultSettingsPage","tpCaptureGeneral",dpi)),"Lazy page reuse");
                using var host=new Form{ClientSize=new Size(850,950),Opacity=0,ShowInTaskbar=false};var container=new TabControl{Dock=DockStyle.Fill};container.TabPages.Add(page);host.Controls.Add(container);host.Show();Application.DoEvents();
                using var bitmap=new Bitmap(host.ClientSize.Width,host.ClientSize.Height);host.DrawToBitmap(bitmap,host.ClientRectangle);bitmap.Save(Path.Combine(output,"capture-"+dpi+".png"));host.Hide();
                loads.Add(new{Dpi=dpi,LoadMilliseconds=watch.Elapsed.TotalMilliseconds});
            }
            using(var app=new ApplicationSettingsForm())
            {
                var tabs=(TabControl)typeof(ApplicationSettingsForm).GetField("tcSettings",F)!.GetValue(app)!;
                var page=tabs.TabPages.Cast<TabPage>().Single(x=>x.Name=="tpCaptureGeneral");
                Assert((bool)Call(app,"EnsureSettingsPage",page),"Combined screenshot settings loads");
                Assert(!All(page).Any(x=>x.Name.Contains("ScreenshotDelay")||x.Text=="截图延迟"),"Combined settings contains no delay row");
            }
            using(var canvas=new Bitmap(640,480))using(var region=new RegionCaptureForm(RegionCaptureMode.Annotation,new RegionCaptureOptions{DetectWindows=false,DetectControls=false},canvas,true))
            {Assert(!region.IsDisposed,"Actual selection form initializes on synthetic canvas");}
            File.WriteAllText(Path.Combine(output,"results.json"),System.Text.Json.JsonSerializer.Serialize(new{Passed=true,Checks=count,CaptureDispatchMilliseconds=timings,SettingsLoads=loads,Scope="Compiled capture base and selection workflow lifecycle; actual deferred and combined settings; synthetic selection canvas, no live desktop capture"},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("PASS "+count);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
