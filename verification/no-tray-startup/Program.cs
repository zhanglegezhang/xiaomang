using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShareX;
using ShareX.HelpersLib;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.IO;
using System.Windows.Forms;

static class Checks
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static Type T(string n)=>typeof(MainForm).Assembly.GetType("ShareX."+n,true)!;
    static object Field(object x,string n)=>x.GetType().GetField(n,F)!.GetValue(x)!;
    static object Call(object x,string n,params object[] args)=>(x as Type??x.GetType()).GetMethod(n,F)!.Invoke(x is Type?null:x,args)!;
    static void Set(string n,object value)=>T("Program").GetProperty(n,F)!.SetValue(null,value);
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static IEnumerable<Control> All(Control c){foreach(Control x in c.Controls){yield return x;foreach(var y in All(x))yield return y;}}
    static void Pump(int ms=30){var w=Stopwatch.StartNew();do{Application.DoEvents();Thread.Sleep(1);}while(w.ElapsedMilliseconds<ms);}
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(args.Contains("--dpi96")?HighDpiMode.DpiUnaware:HighDpiMode.PerMonitorV2);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();
            string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
            if(args.Contains("--export-log"))
            {
                string source=File.ReadAllText(args[1]);
                using var stream=typeof(MainForm).Assembly.GetManifestResourceStream("ShareX.Assets.XiaomangVersionLog.txt")!;
                using var reader=new StreamReader(stream);Check(reader.ReadToEnd()==source,"Embedded log matches current source");
                string path=Path.Combine(output,"exported-log.txt");Call(T("AboutPageForm"),"ExportVersionLog",path);
                Check(File.ReadAllText(path).Contains(source.Trim()),"About page exports verified log");return 0;
            }
            foreach(bool tray in new[]{false,true})foreach(bool silent in new[]{false,true})
            {
                string legacyJson=new JObject{["ShowTray"]=tray,["SilentRun"]=silent,["DisableHotkeys"]=true,["ScratchTextWindowSize"]="814, 567",["Language"]=3}.ToString();
                string legacyPath=Path.Combine(output,"legacy-"+tray+silent+".json");File.WriteAllText(legacyPath,legacyJson);
                var config=ApplicationConfig.Load(legacyPath,fallbackSupport:false);
                Check(!config.ShowTray&&!config.SilentRun,"Legacy startup options normalize");
                Check(config.DisableHotkeys&&config.ScratchTextWindowSize==new Size(814,567),"Unrelated preferences survive normalization");
                JsonConvert.PopulateObject("{\"ShowTray\":true,\"SilentRun\":true}",config);
                Check(!config.ShowTray&&!config.SilentRun,"Populate/import cannot restore retired options");
            }
            Check(new ApplicationConfig().StartWithWindowsByDefault&&!new ApplicationConfig().ShowTray,"Fresh profile starts with approved defaults");
            var projected=(JObject)Call(T("PersonalDataPreferences"),"Export",JObject.Parse("{\"ShowTray\":true,\"SilentRun\":true,\"Language\":3}"));
            Check(projected["ShowTray"]==null&&projected["SilentRun"]==null&&projected["Language"]!=null,"Portable preference export excludes retired switches");
            Call(T("UpgradeStartupCheck"),"ValidateSettings",JObject.Parse("{\"ShowTray\":true,\"SilentRun\":true,\"Language\":3}"),JObject.Parse("{\"ShowTray\":false,\"SilentRun\":false,\"Language\":3}"));
            Check(true,"Upgrade permits intentional startup normalization");
            bool rejected=false;try{Call(T("UpgradeStartupCheck"),"ValidateSettings",JObject.Parse("{\"Language\":3}"),JObject.Parse("{\"Language\":4}"));}catch(TargetInvocationException e)when(e.InnerException is IOException){rejected=true;}
            Check(rejected,"Upgrade still rejects unrelated preference loss");
            StartupRules.Run(Check);
            var profile=new ApplicationConfig{DisableHotkeys=true,ShowTray=true,SilentRun=true};
            Set("Sandbox",true);Set("CustomPersonalPath",output);Set("Settings",profile);Set("DefaultTaskSettings",profile.DefaultTaskSettings);
            var keys=new HotkeysConfig();keys.Hotkeys.Clear();Set("HotkeysConfig",keys);Set("UploadersConfig",new ShareX.UploadersLib.UploadersConfig());
            Set("CLI",new ShareXCLIManager(Array.Empty<string>()));Set("UpdateManager",Activator.CreateInstance(T("ShareXUpdateManager"),true)!);
            typeof(SystemOptions).GetProperty("DisableUpload",F)!.SetValue(null,true);typeof(SystemOptions).GetProperty("DisableUpdateCheck",F)!.SetValue(null,true);
            using var hotkeyWindow=new HotkeyForm();var hotkeys=new HotkeyManager(hotkeyWindow);Set("HotkeyManager",hotkeys);
            var timings=new List<object>();
            foreach(bool silent in new[]{true,false})
            {
                Set("SilentRun",silent);
                using var main=new MainForm();Set("MainForm",main);
                main.HandleCreated-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),main,typeof(MainForm).GetMethod("MainForm_HandleCreated",F)!);
                main.Opacity=0;main.Show();Pump();
                Check(main.Visible==!silent,"Silent launch is hidden; manual launch is visible even with old saved values");
                ((NotifyIcon)Field(main,"niTray")).Visible=false;
                var update=(Task)Call(main,"UpdateControls");while(!update.IsCompleted)Pump(5);update.GetAwaiter().GetResult();
                Check(!((NotifyIcon)Field(main,"niTray")).Visible,"Main tray remains hidden even if legacy in-memory field is true");
                var timer=Stopwatch.StartNew();using var settings=new ApplicationSettingsForm();
                settings.Opacity=0;settings.Show();Pump();timer.Stop();
                var controls=All(settings).ToArray();
                Check(typeof(ApplicationSettingsForm).GetField("cbShowTray",F)==null&&typeof(ApplicationSettingsForm).GetField("cbSilentRun",F)==null,"Removed controls have no backing fields or allocation");
                Check(!controls.Any(x=>x.Text.Contains("显示托盘")||x.Text.Contains("隐藏主窗口")||x.Text.Contains("关闭窗口仍在后台运行")),"Retired rows and hint are absent from rendered settings");
                Check(controls.Any(x=>x.Name=="cbStartWithWindows"),"Startup switch remains available");
                settings.Hide();
                Call(main,"ShowMainMenu","no-tray verification");Pump();Check(main.Visible,"Hidden menu can be reopened");
                main.Close();Pump();Check(!main.IsDisposed&&!main.Visible,"Closing menu keeps application alive without tray");
                Call(main,"ShowMainMenu","widget reopen verification");Pump();Check(main.Visible,"Menu reopens after close");
                Check(All(main).Any(x=>x.Name=="exitXiaomangButton"),"Explicit exit entry is reachable without tray");
                if(!silent)
                {
                    using var widget=new TodoWidgetForm();widget.Opacity=0;widget.Show();Pump();main.Hide();
                    if(args.Contains("--all-tabs-hidden"))
                    {
                        profile.TodoSettings.VisibleModules=new List<string>();
                        Call(widget,"ApplyVisibleModules");
                        Check(!(bool)Field(widget,"expanded")&&widget.Visible,"All-hidden mode retains compact recovery surface");
                    }
                    Call(widget,"ShowSearchMenu");Pump();var menu=(ContextMenuStrip)Field(widget,"searchMenu");
                    menu.Items.Cast<ToolStripItem>().Single(x=>x.Text=="主菜单").PerformClick();menu.Close();Pump();
                    Check(main.Visible,"Actual widget context-menu action opens hidden main menu");main.Hide();
                    var logo=All(widget).First(x=>x.Name=="TitleLogo");
                    typeof(Control).GetMethod("OnMouseDoubleClick",F)!.Invoke(logo,new object[]{new MouseEventArgs(MouseButtons.Left,2,2,2,0)});Pump();
                    Check(main.Visible,"Actual widget logo double-click opens hidden main menu");
                    bool sawDialog=false;using var cancel=new System.Windows.Forms.Timer{Interval=20};
                    cancel.Tick+=(_,_)=>{var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(x=>x.GetType().Name=="ExitConfirmationForm");if(dialog!=null){sawDialog=true;All(dialog).OfType<Button>().Single(x=>x.Text=="取消").PerformClick();cancel.Stop();}};
                    cancel.Start();menu.Items.Cast<ToolStripItem>().Single(x=>x.Text=="退出小蟒").PerformClick();Pump(80);
                    Check(sawDialog&&!main.IsDisposed,"Widget exit action reaches real confirmation; cancel keeps application alive");widget.Close();
                }
                var kind=typeof(MainForm).GetNestedType("InlineSettingsKind",F)!;
                Call(main,"OpenInlineSettings",Enum.Parse(kind,"Application"));Pump();
                Check(((Form)Field(main,"inlineSettingsForm")) is ApplicationSettingsForm,"Global settings open from main menu");
                timings.Add(new{Silent=silent,SettingsOpenMilliseconds=timer.Elapsed.TotalMilliseconds,Dpi=settings.DeviceDpi});
                if(!silent){var panel=(Control)Field(settings,"tpGeneral");using var bmp=new Bitmap(panel.Width,panel.Height);panel.DrawToBitmap(bmp,panel.ClientRectangle);bmp.Save(Path.Combine(output,"general.png"));}
                main.GetType().GetField("forceClose",F)!.SetValue(main,true);main.GetType().GetField("exitPreparationComplete",F)!.SetValue(main,true);main.Close();Pump();Check(main.IsDisposed,"Prepared explicit exit closes main window");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),System.Text.Json.JsonSerializer.Serialize(new{Passed=true,Checks=count,Timings=timings,Scope="Actual compiled settings and MainForm; background services detached for isolation; startup policy compiled from unchanged method with in-memory OS state; no real reboot"},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("PASS "+count);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
