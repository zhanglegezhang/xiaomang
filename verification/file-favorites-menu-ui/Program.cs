using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using ShareX;

internal static class Check
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    [STAThread] static int Main(string[] args)
    {
        string assembly = Path.GetFullPath(args[0]);
        string dependencies = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            string path = name.Name == "ShareX" ? assembly : Path.Combine(dependencies,name.Name+".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        try { Run(args); return 0; } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static IEnumerable<Control> Children(Control root)
    {
        foreach(Control child in root.Controls) { yield return child; foreach(var nested in Children(child)) yield return nested; }
    }
    static void Run(string[] args)
    {
        if(args.Contains("--modern-repair")||args.Contains("--modern-state")||args.Contains("--modern-cycle"))
        {
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",Path.GetDirectoryName(Path.GetFullPath(args[0]))!+Path.DirectorySeparatorChar);
            Type menu=typeof(ApplicationConfig).Assembly.GetType("ShareX.ModernFileFavoritesMenu",true)!;
            Console.WriteLine("MODERN_BASE="+AppContext.BaseDirectory);
            Console.WriteLine("MODERN_STATUS="+(string)menu.GetMethod("Status",Flags)!.Invoke(null,null)!);
            if(args.Contains("--modern-state"))return;
            if(args.Contains("--modern-cycle"))
            {
                try
                {
                    Task.Run(async()=>await (Task)menu.GetMethod("ChangeAsync",Flags)!.Invoke(null,new object[]{false,false})!).GetAwaiter().GetResult();
                    string removed=(string)menu.GetMethod("Status",Flags)!.Invoke(null,null)!;
                    if(removed!="外层菜单尚未注册。")throw new Exception(removed);
                    Console.WriteLine("PASS owned modern menu removes without changing files or pins");
                }
                finally
                {
                    Task.Run(async()=>await (Task)menu.GetMethod("ChangeAsync",Flags)!.Invoke(null,new object[]{true,false})!).GetAwaiter().GetResult();
                }
            }
            Task.Run(async()=>await (Task)menu.GetMethod("ChangeAsync",Flags)!.Invoke(null,new object[]{true,false})!).GetAwaiter().GetResult();
            string state=(string)menu.GetMethod("Status",Flags)!.Invoke(null,null)!;
            if(!state.StartsWith("外层菜单已注册"))throw new Exception(state);
            Console.WriteLine("PASS registered menu repair is idempotent and stays at the current program location");
            return;
        }
        Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Thread.CurrentThread.CurrentUICulture=CultureInfo.GetCultureInfo("zh-CN");
        Type program=typeof(ApplicationConfig).Assembly.GetType("ShareX.Program",true)!;
        string fixture=Path.GetFullPath("build/file-favorites-20261013/menu-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var config=new ApplicationConfig{ShowTray=false,DisableHotkeys=true};
        program.GetProperty("CustomPersonalPath",Flags)!.SetValue(null,fixture);
        program.GetProperty("Settings",Flags)!.SetValue(null,config);
        program.GetProperty("DefaultTaskSettings",Flags)!.SetValue(null,config.DefaultTaskSettings);
        program.GetProperty("HotkeysConfig",Flags)!.SetValue(null,new HotkeysConfig());
        program.GetProperty("UploadersConfig",Flags)!.SetValue(null,new ShareX.UploadersLib.UploadersConfig());
        var times=new List<double>();
        for(int i=0;i<6;i++) { var clock=Stopwatch.StartNew(); using var settings=new ApplicationSettingsForm();clock.Stop();times.Add(clock.Elapsed.TotalMilliseconds); }
        Console.WriteLine($"SETTINGS_CONSTRUCT_FIRST_MS={times[0]:F2}; WARM_MEDIAN_MS={times.Skip(1).Order().ElementAt(2):F2}");
        if(args.Contains("--baseline")) return;
        using var form=new ApplicationSettingsForm();
        if(args.Contains("--compact-metrics"))
        {
            form.ClientSize=new Size(800*form.DeviceDpi/96,600*form.DeviceDpi/96);form.Show();
            var jump=typeof(ApplicationSettingsForm).GetMethod("SelectRegistrationPage",Flags)!;
            var clock=Stopwatch.StartNew();jump.Invoke(form,new object[]{"recording"});Application.DoEvents();clock.Stop();
            Console.WriteLine($"COMPACT_PAGE_FIRST_MS={clock.Elapsed.TotalMilliseconds:F2}");
            for(int i=0;i<8;i++){jump.Invoke(form,new object[]{"voice"});Application.DoEvents();}
            var previewPage=(TabPage)typeof(ApplicationSettingsForm).GetField("registrationTab",Flags)!.GetValue(form)!;
            jump.Invoke(form,new object[]{"recording"});Application.DoEvents();
            string output=Path.GetFullPath("build/registration-batch-20261014/registration-page.png");
            using var bitmap=new Bitmap(previewPage.Width,previewPage.Height);previewPage.DrawToBitmap(bitmap,new Rectangle(Point.Empty,previewPage.Size));bitmap.Save(output);
            Console.WriteLine("COMPACT_PREVIEW="+output);
            return;
        }
        if(args.Contains("--registration")){Registration(form,fixture);return;}
        var page=(TabPage)typeof(ApplicationSettingsForm).GetField("tpGeneral",Flags)!.GetValue(form)!;
        page.Parent!.Controls.Remove(page);
        using var host=new Form { Text="文件收藏菜单设置布局验证", AutoScaleMode=AutoScaleMode.None };
        var tabs=new TabControl{Dock=DockStyle.Fill}; tabs.TabPages.Add(page);host.Controls.Add(tabs);host.Show();
        foreach(int width in new[]{420,600})
        {
            host.ClientSize=new Size(width*host.DeviceDpi/96,650*host.DeviceDpi/96);
            for(int i=0;i<5;i++){host.PerformLayout();page.PerformLayout();Application.DoEvents();}
            var controls=Children(page).ToArray();
            foreach(string name in new[]{"enableFileFavoritesMenu","removeFileFavoritesMenu","fileFavoritesMenuStatus"})
            {
                var control=controls.Single(c=>c.Name==name);
                if(control.Right>control.Parent!.ClientSize.Width || control.Height<control.GetPreferredSize(new Size(control.Width,0)).Height) throw new Exception("Clipped: "+name);
            }
            if(page.HorizontalScroll.Visible) throw new Exception("Unnecessary horizontal scroll");
            Console.WriteLine($"PASS {width} width at {host.DeviceDpi} DPI: menu buttons and status fit");
            var status=controls.Single(c=>c.Name=="fileFavoritesMenuStatus");
            status.Text="系统入口已更新，但旧用户入口未清理完成，请重试修复或移除。";
            Application.DoEvents();
            if(status.Height<status.GetPreferredSize(new Size(status.Width,0)).Height) throw new Exception("Error message clipped");
            Console.WriteLine("PASS wrapped error status fits");
        }
    }

    static void Registration(ApplicationSettingsForm form,string fixture)
    {
        int passed=0;
        void Assert(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);passed++;}
        void Pump(){for(int i=0;i<5;i++){form.PerformLayout();Application.DoEvents();}}
        var tree=Children(form).OfType<TreeView>().Single();
        Assert(tree.Nodes.Count>1 && tree.Nodes[0].Text=="常规" && tree.Nodes[1].Text=="注册","registration navigation immediately follows General");
        var general=(TabPage)typeof(ApplicationSettingsForm).GetField("tpGeneral",Flags)!.GetValue(form)!;
        Assert(!Children(general).Any(c=>c.Name=="enableFileFavoritesMenu"),"legacy registration no longer in General");
        form.Show();
        var navigate=typeof(ApplicationSettingsForm).GetMethod("SelectRegistrationPage",Flags)!;
        foreach(string section in new[]{"recording","files","voice","text"})
        {
            var timer=Stopwatch.StartNew();navigate.Invoke(form,new object[]{section});Pump();timer.Stop();
            var registration=(TabPage)typeof(ApplicationSettingsForm).GetField("registrationTab",Flags)!.GetValue(form)!;
            Assert(registration.Visible,"jump selects visible registration page: "+section);
            var controls=Children(registration).Where(c=>c.Visible).ToArray();
            string required=section switch{"recording"=>"downloadRecordingComponent","files"=>"installModernFileMenu","voice"=>"registerVoiceInputComponent",_=>"installXiaomangInputMethod"};
            Assert(controls.Any(c=>c.Name==required),"destination contains expected controls: "+section);
            Console.WriteLine($"REGISTRATION_{section}_FIRST_MS={timer.Elapsed.TotalMilliseconds:F2}");
        }
        foreach(int width in new[]{600,900})
        {
            form.ClientSize=new Size(width*form.DeviceDpi/96,650*form.DeviceDpi/96);
            foreach(string section in new[]{"recording","files"})
            {
                navigate.Invoke(form,new object[]{section});Pump();
                foreach(var button in Children(form).OfType<Button>().Where(c=>c.Visible&&c.Name.Contains("FileMenu",StringComparison.OrdinalIgnoreCase)))
                    Assert(button.Right<=button.Parent!.ClientSize.Width,$"{width} {section} action fits: {button.Name}");
            }
        }
        navigate.Invoke(form,new object[]{"files"});Pump();
        var fileControls=Children(form).Where(c=>c.Visible).ToArray();
        var compactPage=(TabPage)typeof(ApplicationSettingsForm).GetField("registrationTab",Flags)!.GetValue(form)!;
        Assert(!Children(compactPage).OfType<TabControl>().Any(),"registration groups share one page without sheets");
        Assert(!Children(compactPage).OfType<CheckBox>().Any()&&!Children(compactPage).Any(c=>c.Name=="enableFileFavoritesMenu"),"no consent checkbox or classic menu configuration");
        Assert(fileControls.Any(c=>c.Text=="鼠标右键·收藏（需管理员授权）")&&!Children(compactPage).Any(c=>c.Text=="输入法"||c.Text=="录屏组件"),"approved concise group headings");
        Assert(fileControls.Any(c=>c.Name=="registerAllComponents")&&Children(compactPage).OfType<ProgressBar>().Any(c=>c.Name=="registrationOverallProgress"&&!c.Visible),"progress bar hidden while idle");
        var appearance=typeof(ApplicationConfig).Assembly.GetType("ShareX.MainMenuAppearance",true)!;
        appearance.GetMethod("Apply",Flags)!.Invoke(null,new object[]{form});Pump();
        using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,form.ClientRectangle);bitmap.Save(Path.GetFullPath("build/registration-redesign-20261014/preview-"+form.DeviceDpi+".png"));}
        if(File.Exists(Path.Combine(AppContext.BaseDirectory,"ThirdParty","FileFavoritesMenu","Xiaomang.FileFavoritesMenu.msix")))throw new Exception("Refusing to test failure with a real installer payload");
        fileControls.OfType<Button>().Single(c=>c.Name=="installModernFileMenu").PerformClick();Pump();
        var menuStatus=fileControls.Single(c=>c.Name=="registrationOperationMessage");
        string refused=menuStatus.Text;
        var wait=Stopwatch.StartNew();while(wait.ElapsedMilliseconds<2300){Application.DoEvents();Thread.Sleep(10);}
        Assert(refused.Contains("操作失败")&&menuStatus.Text==refused,"registration failure remains visible after periodic status refresh");
        Assert(refused.Contains("步骤：")&&refused.Contains("错误码：")&&refused.Contains("相关路径：")&&refused.Contains("诊断日志："),"menu failure includes stage, native error, path and log location");
        Assert(menuStatus.Height>=menuStatus.GetPreferredSize(new Size(menuStatus.Width,0)).Height,"detailed failure text is not vertically clipped");
        var modern=typeof(ApplicationConfig).Assembly.GetType("ShareX.ModernFileFavoritesMenu",true)!;
        int originalExit=Environment.ExitCode;
        Assert((bool)modern.GetMethod("TryRun",Flags)!.Invoke(null,new object[]{new[]{"-TrustFileMenuPublisher","untrusted-path.cer"}})!&&Environment.ExitCode==1,"elevated certificate helper rejects caller-supplied paths");
        Environment.ExitCode=originalExit;
        var assembly=typeof(ApplicationConfig).Assembly;
        using var summary=(Form)Activator.CreateInstance(assembly.GetType("ShareX.InputMethodPageForm",true)!,Flags,null,new object[]{false},null)!;
        summary.Show();Pump();
        Assert(Children(summary).Any(c=>c.Name=="goToVoiceRegistration")&&!Children(summary).Any(c=>c.Name=="registerVoiceInputComponent"),"voice summary contains registration jump without maintenance buttons");
        var summaryTree=Children(summary).OfType<TreeView>().Single();
        int needed=TextRenderer.MeasureText("语音输入",summaryTree.Font).Width+60*summary.DeviceDpi/96;
        Assert(summaryTree.ClientSize.Width>=needed,"voice/text summary navigation fits full labels at current DPI");
        var jumpButton=Children(summary).OfType<Button>().Single(c=>c.Name=="goToVoiceRegistration");
        Assert(jumpButton.Width>=TextRenderer.MeasureText(jumpButton.Text,jumpButton.Font).Width+16*summary.DeviceDpi/96,"registration jump button fits its complete caption");
        var voiceLabel=Children(summary).OfType<Label>().Single(c=>c.Text.StartsWith("按住 Ctrl + Alt"));
        Assert(voiceLabel.Text.Split('\n').Length==3,"voice summary preserves first three description lines");
        summary.GetType().GetMethod("SelectInputPage",Flags)!.Invoke(summary,new object[]{true});Pump();
        Assert(Children(summary).Any(c=>c.Name=="openXiaomangInputSettings")&&Children(summary).Any(c=>c.Name=="goToTextRegistration")&&!Children(summary).Any(c=>c.Name=="installXiaomangInputMethod"),"text summary preserves settings and adds registration jump");
        Assert(Children(summary).OfType<Label>().Any(c=>c.Text.StartsWith("小蟒文本")&&c.Text.Split('\n').Length==6),"text summary preserves six description lines");
        using var about=(Form)Activator.CreateInstance(assembly.GetType("ShareX.AboutPageForm",true)!,true)!;
        Assert(Children(about).Any(c=>c.Text=="产品介绍：")&&Children(about).Any(c=>c.Name=="productMindMapLink"),"about shows product introduction and HTML guide link");
        string output=Path.Combine(fixture,"导图.html");
        about.GetType().GetMethod("ExportMindMap",Flags)!.Invoke(null,new object[]{output});
        string html=File.ReadAllText(output);
        Assert(html.Contains("产品功能思维导图")&&html.Contains("<html")&&!html.Contains("<script src="),"embedded HTML exports as an offline document");
        Assert(new ApplicationConfig().StartWithWindowsByDefault,"new profile default enables startup preference");
        Console.WriteLine($"{passed} registration and guide assertions passed at DPI {form.DeviceDpi}");
    }
}
