using System.Drawing;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using ShareX;
using ShareX.HelpersLib;

internal static class Check
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type ProgramType = typeof(MainForm).Assembly.GetType("ShareX.Program", true)!;
    static string root = Path.GetFullPath(Environment.GetEnvironmentVariable("XIAOMANG_SETTINGS_REPORT") ?? "test-results/settings-views");
    static void Set(string name, object value) => ProgramType.GetProperty(name, F)!.SetValue(null, value);
    static object Field(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o)!;
    static object Call(object o, string name, params object[] values) => o.GetType().GetMethod(name, F)!.Invoke(o, values)!;
    static void Pump() { Application.DoEvents(); Thread.Sleep(80); Application.DoEvents(); }
    static void Save(Form form, string name)
    {
        Pump();
        using Bitmap image = new(form.Width, form.Height);
        using Graphics g = Graphics.FromImage(image);
        g.CopyFromScreen(form.Location, Point.Empty, form.Size);
        image.Save(Path.Combine(root, name + ".png"));
        Console.WriteLine("Snapshot: " + name);
    }
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
    }
    [STAThread] static void Main(string[] args)
    {
        Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        // Mirror production startup's Languages/ satellite assembly lookup.
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (string.IsNullOrEmpty(name.CultureName) || !name.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;
            string resource = Path.Combine(AppContext.BaseDirectory, "Languages", name.CultureName, name.Name + ".dll");
            return File.Exists(resource) ? context.LoadFromAssemblyPath(resource) : null;
        };
        Application.EnableVisualStyles();
        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        root = Path.Combine(root, args.Contains("--dpi96") ? "dpi96" : "dpi192");
        Directory.CreateDirectory(root);
        string personal = Path.Combine(root, "personal"); Directory.CreateDirectory(personal);
        Set("CustomPersonalPath", personal);
        string month = DateTime.Today.ToString("yyyy-MM");
        string shots = Path.Combine(personal, "Screenshots", month); Directory.CreateDirectory(shots);
        using (Bitmap fixture = new(640, 360))
        {
            using Graphics g = Graphics.FromImage(fixture); g.Clear(Color.SteelBlue);
            using Font font = new("Microsoft YaHei UI", 24);
            g.DrawString("隔离测试图片", font, Brushes.White, 30, 30);
            fixture.Save(Path.Combine(shots, "菜单视觉测试.png"));
        }
        string temporary = Path.Combine(personal, "临时文件夹", month); Directory.CreateDirectory(temporary);
        File.WriteAllText(Path.Combine(temporary, "测试笔记.md"), "# 测试笔记\n\n仅用于主菜单回归。");
        File.WriteAllText(Path.Combine(temporary, "测试文档.txt"), "隔离测试文档");
        var logs = new NoteLogStore(personal);
        foreach (NoteLogKind kind in Enum.GetValues<NoteLogKind>())
            if (logs.Read(kind).Count == 0) logs.Append(kind, "新增", "这是一条隔离测试记录，用于验证列表显示与筛选。");
        var config = new ApplicationConfig { ShowTray = false, DisableHotkeys = true };
        Set("Settings", config); Set("DefaultTaskSettings", config.DefaultTaskSettings);
        Set("HotkeysConfig", new HotkeysConfig());
        Set("UploadersConfig", new ShareX.UploadersLib.UploadersConfig());
        Set("CLI", new ShareXCLIManager(Array.Empty<string>()));
        Set("UpdateManager", Activator.CreateInstance(typeof(MainForm).Assembly.GetType("ShareX.ShareXUpdateManager", true)!, true)!);
        typeof(SystemOptions).GetProperty("DisableUpload", F)!.SetValue(null, true);
        typeof(SystemOptions).GetProperty("DisableUpdateCheck", F)!.SetValue(null, true);
        using MainForm main = new();
        Set("MainForm", main);
        // This harness does not register a clipboard listener, show notes, start network jobs or run app startup.
        main.HandleCreated -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), main, typeof(MainForm).GetMethod("MainForm_HandleCreated", F)!);
        if (args.Contains("--settings-construction-only"))
        {
            for (int repeat = 0; repeat < 10; repeat++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); Application.DoEvents();
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                using var settingsForm = new ApplicationSettingsForm();
                watch.Stop();
                if (repeat >= 2) Console.WriteLine($"CONSTRUCT {repeat - 2}: {watch.Elapsed.TotalMilliseconds:F2} ms; {GC.GetAllocatedBytesForCurrentThread() - bytes} bytes; {Descendants(settingsForm).Count()} controls");
            }
            return;
        }
        using HotkeyForm hotkeyOwner = new();
        var manager = new HotkeyManager(hotkeyOwner);
        var settings = new List<HotkeySettings>();
        foreach (var (job, keys) in new[] { (HotkeyType.RectangleRegion, Keys.Control | Keys.D1), (HotkeyType.ScreenRecorder, Keys.Control | Keys.D2), (HotkeyType.ScreenColorPicker, Keys.Control | Keys.D3), (HotkeyType.ActiveWindowTopMost, Keys.Control | Keys.F10) })
            settings.Add(new HotkeySettings { TaskSettings = new TaskSettings { Job = job }, HotkeyInfo = new HotkeyInfo(keys) });
        typeof(HotkeyManager).GetProperty("Hotkeys", F)!.SetValue(manager, settings);
        Set("HotkeyManager", manager);
        main.StartPosition = FormStartPosition.Manual;
        main.Bounds = new Rectangle(40, 40, Math.Min(2100, Screen.PrimaryScreen.WorkingArea.Width - 80), Math.Min(1350, Screen.PrimaryScreen.WorkingArea.Height - 80));
        main.TopMost = true; main.WindowState=FormWindowState.Maximized; main.Show(); Pump();
        Call(main, "AfterApplicationSettingsJobs");
        if (((NotifyIcon)Field(main, "niTray")).Text != "小蟒") throw new Exception("Tray tooltip is not 小蟒");
        Console.WriteLine("PASS: tray tooltip is 小蟒");
        Call(main, "UpdateMainWindowLayout");
        ToolStrip nav = (ToolStrip)Field(main, "tsMain");
        Console.WriteLine($"Nav bounds={nav.Bounds} pad={nav.Padding} parent={nav.Parent.Bounds} item={nav.Items[0].Bounds} dpi={main.DeviceDpi}");
        if (nav.Width > nav.Parent.ClientSize.Width) throw new Exception("Navigation exceeds sidebar");
        if(args.Contains("--font-audit"))
        {
            main.Text="Menu font audit";
            var appearance=typeof(MainForm).Assembly.GetType("ShareX.MainMenuAppearance")!;
            void Inspect(string name, Action select)
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();select();Application.DoEvents();timer.Stop();
                Console.WriteLine($"TIME {main.DeviceDpi} {name}: {timer.Elapsed.TotalMilliseconds:F2} ms");
                Save(main,name);
                if(Field(main,"inlineSettingsForm") is Form embedded && embedded.Visible && (embedded.Width<embedded.Parent.ClientSize.Width || embedded.Height<embedded.Parent.ClientSize.Height)) throw new Exception("Embedded page does not fill viewport: "+name);
                foreach(var c in Descendants(main).Where(c=>c.Visible && (c is Label || c is Button || c is TextBoxBase || c is ComboBox || c is RadioButton)))
                {
                    Console.WriteLine($"FONT {name} {c.GetType().Name} {c.Name}: {c.Font.Name} {c.Font.SizeInPoints:F2} {c.Font.Style} height={c.Height}/{c.Font.Height} dpi={c.DeviceDpi}");
                    if(args.Contains("--assert-fonts") && c.DeviceDpi!=main.DeviceDpi) throw new Exception("Stale DPI: "+name+" "+c.Name+" "+c.DeviceDpi);
                    if(args.Contains("--assert-fonts") && (c.Font.Name!="Microsoft YaHei UI" || c.Height<c.Font.Height)) throw new Exception("Font/height mismatch: "+name+" "+c.Name);
                }
                if(args.Contains("--assert-fonts"))
                {
                    foreach(var panel in Descendants(main).OfType<ScrollableControl>().Where(c=>c.Visible&&c.AutoScroll&&c.VerticalScroll.Visible).ToArray())
                    {
                        panel.AutoScrollPosition=new Point(0,panel.VerticalScroll.Maximum);Application.DoEvents();Save(main,name+"-bottom");panel.AutoScrollPosition=Point.Empty;
                    }
                }
            }
            for(int pass=0;pass<(args.Contains("--roundtrip")?7:2);pass++) { if(pass>0 && pass<5 && args.Contains("--roundtrip")){ int previous=main.DeviceDpi; main.TopMost=false; main.WindowState=FormWindowState.Minimized; Console.WriteLine("WAIT DPI "+previous); var wait=System.Diagnostics.Stopwatch.StartNew(); while(main.DeviceDpi==previous && wait.Elapsed.TotalMinutes<10) { Pump(); if(main.WindowState==FormWindowState.Minimized && File.Exists(Path.Combine(root,"resume"))) { File.Delete(Path.Combine(root,"resume")); main.WindowState=FormWindowState.Maximized; } } if(main.DeviceDpi==previous)throw new Exception("DPI transition timeout"); for(int settle=0;settle<10;settle++)Pump(); main.WindowState=FormWindowState.Maximized; main.TopMost=true; Pump(); Console.WriteLine("DPI NOW "+main.DeviceDpi); } foreach(string page in new[]{"全局设置","快捷键","输入法","便签文件夹","截图文件夹","临时文件夹","关于"})
            {
                Inspect($"{pass}-{page}",()=>nav.Items.Cast<ToolStripItem>().Single(i=>i.Text.StartsWith(page)).PerformClick());
                if(page=="全局设置" || page=="输入法")
                {
                    var form=(Form)Field(main,"inlineSettingsForm");
                    var tree=Descendants(form).OfType<TreeView>().First();
                    foreach(TreeNode node in tree.Nodes) Inspect($"{pass}-{page}-{node.Text}",()=>tree.SelectedNode=node);
                }
            }
            } main.Hide();Console.WriteLine("AUDIT COMPLETE");return;
        }
        throw new ArgumentException();
    }
}
