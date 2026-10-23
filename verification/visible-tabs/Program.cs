using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShareX;

static class Tests
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static readonly string[] Modules={"Todo","Clipboard","Favorites","News","Diary"};
    static readonly string[] Names={"待办","剪切板","收藏夹","工具","日记"};
    static readonly string[] ButtonFields={"todoModuleButton","clipboardModuleButton","favoritesModuleButton","newsModuleButton","diaryModuleButton"};
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
    static Type T(string n)=>typeof(TodoSettings).Assembly.GetType("ShareX."+n,true)!;
    static void Set(string n,object x)=>T("Program").GetProperty(n,F)!.SetValue(null,x);
    static object Field(object o,string n)=>o.GetType().GetField(n,F)!.GetValue(o)!;
    static object Call(object o,string n,params object[] args)=>(o as Type??o.GetType()).GetMethod(n,F)!.Invoke(o is Type?null:o,args)!;
    static void Pump(int ms=15){var w=Stopwatch.StartNew();do{Application.DoEvents();Thread.Sleep(1);}while(w.ElapsedMilliseconds<ms);}
    [STAThread]static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(args.Contains("--dpi96")?HighDpiMode.DpiUnaware:HighDpiMode.PerMonitorV2);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();
            string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
            Set("Sandbox",true);Set("CustomPersonalPath",Path.Combine(root,"isolated-profile"));
            var config=new ApplicationConfig();Set("Settings",config);
            Check(Modules.All(config.TodoSettings.IsModuleVisible),"New product profile enables every module");
            foreach(var json in new[]{"{}","{\"VisibleModules\":null}"})Check(Modules.All(JsonConvert.DeserializeObject<TodoSettings>(json)!.IsModuleVisible),"Old profile with missing or null field shows all tabs");
            var sourceNames=new[]{"待办","剪切板","手动收藏","本地笔记","固定文件"};
            var sourceModules=new[]{"Todo","Clipboard","Favorites","Diary","Favorites"};
            for(int mask=0;mask<32;mask++)
            {
                var setting=new TodoSettings{VisibleModules=Modules.Where((_,i)=>(mask&(1<<i))!=0).ToList()};
                var copy=JsonConvert.DeserializeObject<TodoSettings>(JsonConvert.SerializeObject(setting))!;
                Check(copy.VisibleModules.SequenceEqual(setting.VisibleModules),"All 32 combinations survive save/reopen");
                for(int i=0;i<5;i++)Check(copy.IsModuleVisible(Modules[i])==((mask&(1<<i))!=0),"Every tab is independently controlled");
                for(int i=0;i<sourceNames.Length;i++)
                {
                    bool allowed=true;
                    try{Call(copy,"EnsureSearchSourceVisible",sourceNames[i]);}
                    catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException){allowed=false;Check(e.InnerException.Message.Contains(Names[Array.IndexOf(Modules,sourceModules[i])]),"Hidden source hint names the correct tab");}
                    Check(allowed==copy.IsModuleVisible(sourceModules[i]),"Search does not silently re-enable hidden tabs");
                }
            }
            var item=new TodoItem{Text="Synthetic pending reminder",ReminderAt=DateTime.Now.AddHours(3)};
            var favorite=new FavoriteTextItem{Text="Synthetic favorite"};
            config.TodoSettings.Items.Add(item);config.TodoSettings.FavoriteItems.Add(favorite);
            using var widget=new TodoWidgetForm(new NoteLogStore(Path.Combine(root,"isolated-profile")));
            widget.TopMost=false;widget.GetType().GetField("showingWidgetOverlay",F)!.SetValue(widget,true);widget.Show();Pump();
            var buttons=ButtonFields.Select(n=>(Button)Field(widget,n)).ToArray();
            bool remindersInitially=((System.Windows.Forms.Timer)Field(widget,"reminderTimer")).Enabled;
            var watch=new Stopwatch();var timings=new List<double>();
            for(int mask=0;mask<32;mask++)
            {
                config.TodoSettings.VisibleModules=Modules.Where((_,i)=>(mask&(1<<i))!=0).ToList();
                config.TodoSettings.ActiveModule="Diary";watch.Restart();Call(widget,"ApplyVisibleModules");watch.Stop();timings.Add(watch.Elapsed.TotalMilliseconds);
                string expected=(mask&16)!=0?"Diary":config.TodoSettings.VisibleModules.FirstOrDefault()??"";
                Check(config.TodoSettings.ActiveModule==expected,"Hidden current page falls back in fixed visual order");
                Call(widget,"SetExpanded",true);Pump();
                Check((bool)Field(widget,"expanded")== (mask!=0),"No empty expanded panel when all tabs are hidden");
                int number=0;
                for(int i=0;i<5;i++)
                {
                    bool enabled=(mask&(1<<i))!=0;
                    Check(buttons[i].Visible==enabled,"Exactly the selected stickers are visible");
                    if(enabled)Check(buttons[i].Text.Replace("\n","")==(++number).ToString()+Names[i],"Visible tabs keep continuous numbering");
                }
                var visible=buttons.Where(x=>x.Visible).ToArray();
                Check(visible.Length==config.TodoSettings.VisibleModules.Count,"Visible sticker count matches preferences");
                for(int i=1;i<visible.Length;i++)Check(visible[i].Top>visible[i-1].Bottom,"Reflow preserves spacing without overlap");
                Check(config.TodoSettings.Items.Single().Id==item.Id&&config.TodoSettings.FavoriteItems.Single().Id==favorite.Id,"Toggles preserve saved todo and favorite data");
                Check(((System.Windows.Forms.Timer)Field(widget,"reminderTimer")).Enabled==remindersInitially,"Reminder timer stays active while hiding tabs");
                if(mask==0)
                {
                    Call(widget,"OpenPanelGenie");Check(!(bool)Field(widget,"animatingGenie")&&!(bool)Field(widget,"expanded"),"Robot cannot animate an empty page");
                    widget.OpenPanel();Check(!(bool)Field(widget,"expanded")&&widget.Visible,"All-hidden mode retains robot window");
                    Call(widget,"ShowSearchMenu");var menu=(ContextMenuStrip)Field(widget,"searchMenu");
                    Check(menu.Items.Cast<ToolStripItem>().Any(x=>x.Text=="主菜单"),"Robot context menu retains recovery entry");menu.Close();
                    Check(widget.AddClipboardText("Synthetic clipboard while hidden"),"Clipboard capture still works with all tabs hidden");
                }
            }
            config.TodoSettings.VisibleModules=Modules.ToList();Call(widget,"ApplyVisibleModules");
            var uninitialized=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ApplicationSettingsForm));
            var flow=(Panel)Call(uninitialized,"CreateVisibleModuleOptions");
            using var host=new Form{Size=new Size(1600,700),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)};
            var surfaceType=T("SettingsDesignSurface");var surface=(Control)Activator.CreateInstance(surfaceType,F,null,new object[]{"常规"},null)!;host.Controls.Add(surface);
            Call(surface,"Section","启动和页签");Call(surface,"Editor","页签",flow,null,300,null);Call(surface,"Finish");host.Show();Pump();
            var options=flow.Controls.OfType<CheckBox>().ToArray();
            Check(options.Length==5&&options.All(x=>x.Checked&&x.AutoCheck),"All five controls begin enabled and support clicking");
            foreach(int width in new[]{1600,860,570})
            {
                host.Width=width;host.PerformLayout();Pump();
                Check(flow.Controls.Cast<Control>().Select(x=>x.Top).Distinct().Count()==1,"Options remain compact in narrow and wide layouts");
                foreach(Control option in flow.Controls)Check(option.Width>0&&option.Right<=flow.Width&&option.Bottom<=flow.Height,"Options stay inside layout");
                using var bmp=new Bitmap(surface.Width,surface.Height);surface.DrawToBitmap(bmp,surface.ClientRectangle);bmp.Save(Path.Combine(root,"ui-"+width+".png"));
            }
            foreach(var option in options){typeof(CheckBox).GetMethod("OnClick",F)!.Invoke(option,new object[]{EventArgs.Empty});Check(!option.Checked,"Every control can be unchecked including todo");}
            Check(config.TodoSettings.VisibleModules.Count==0,"All unchecked preference is persisted as empty list");
            string savedPath=Path.Combine(root,"saved-profile.json");
            File.WriteAllText(savedPath,((JObject)Call(T("PersonalDataPreferences"),"Json",config)).ToString());
            var saved=ApplicationConfig.Load(savedPath,fallbackSupport:false);
            Check(saved.TodoSettings.VisibleModules.Count==0,"Restart does not restore default tabs over an existing empty selection");
            foreach(var option in options)option.Checked=true;
            Check(config.TodoSettings.VisibleModules.SequenceEqual(Modules),"All five tabs can be restored");
            var projection=(JObject)Call(T("PersonalDataPreferences"),"Export",(JObject)Call(T("PersonalDataPreferences"),"Json",config));
            Check(projection["TodoSettings"]?["VisibleModules"]?.ToObject<string[]>()?.SequenceEqual(Modules)==true,"Personal settings backup includes visibility selection");
            File.WriteAllText(Path.Combine(root,"results.json"),System.Text.Json.JsonSerializer.Serialize(new{Passed=true,Checks=checks,Dpi=host.DeviceDpi,Combinations=32,VisibilityUpdateMilliseconds=new{Minimum=timings.Min(),Maximum=timings.Max(),Average=timings.Average()},Scope="Actual UI and widget over synthetic data; new/legacy defaults, all 32 combinations, reminders and clipboard retention; no actual audio recording"},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            widget.Close();Console.WriteLine("PASS "+checks);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
