#nullable enable
using ShareX;
using ShareX.HelpersLib;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

// Performance probe for a frozen copy of the daily binary. No product code is changed.
// A fresh process and personal directory reset application caches, not the OS file cache.
internal static class Probe
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly App = typeof(MainForm).Assembly;
    static Type T(string name) => App.GetType("ShareX." + name, true)!;
    static object Field(object target, string name) => target.GetType().GetField(name, Flags)!.GetValue(target)!;
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args)!;
    static void Set(string name, object value) => T("Program").GetProperty(name, Flags)!.SetValue(null, value);
    static IEnumerable<Control> Children(Control root)
    { yield return root; foreach (Control child in root.Controls) foreach (Control nested in Children(child)) yield return nested; }
    static void Pump(int milliseconds)
    { var watch = Stopwatch.StartNew(); do { Application.DoEvents(); Thread.Sleep(1); } while (watch.ElapsedMilliseconds < milliseconds); }
    static List<object> results = new();
    static string run = "", mode = "";
    static MainForm main = null!;
    static double maxQueueDelay;
    static long epoch;

    static string Location()
    {
        string kind = Field(main, "inlineSettingsKind").ToString()!;
        if (kind is "Application" or "InputMethod")
        {
            var form = (Form)Field(main, "inlineSettingsForm");
            var tree = form == null || form.IsDisposed ? null : Children(form).OfType<TreeView>().FirstOrDefault();
            return kind + "/" + (tree?.SelectedNode?.Text ?? "transition");
        }
        if (kind != "None") return kind;
        foreach (string name in new[] { "noteLogPageActive", "screenshotFolderPageActive", "temporaryFilePageActive" })
            if ((bool)Field(main, name)) return name;
        return "Main";
    }
    static void Measure(string page, string phase, Action action, Func<bool>? ready = null, bool noOp = false)
    {
        Pump(30);
        string from = Location();
        long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        long current = Interlocked.Increment(ref epoch);
        maxQueueDelay = 0;
        using var sampler = new System.Threading.Timer(_ =>
        {
            long started = Stopwatch.GetTimestamp();
            try { main.BeginInvoke(new Action(() => { if (current == epoch) maxQueueDelay = Math.Max(maxQueueDelay, Stopwatch.GetElapsedTime(started).TotalMilliseconds); })); }
            catch (InvalidOperationException) { }
        }, null, 0, 20);
        var watch = Stopwatch.StartNew();
        string? error = null;
        double handler = 0, painted = 0, completed = 0;
        try
        {
            action(); handler = watch.Elapsed.TotalMilliseconds;
            // Drain queued layout/invalidation and complete pending normal paint messages.
            Application.DoEvents(); main.Update(); Application.DoEvents();
            painted = watch.Elapsed.TotalMilliseconds;
            while (ready != null && !ready() && watch.ElapsedMilliseconds < 10000) Pump(1);
            completed = watch.Elapsed.TotalMilliseconds;
            if (ready != null && !ready()) error = "data readiness timeout";
            Pump(80); // Observe callbacks without adding the observation window to timings.
        }
        catch (Exception ex) { error = ex.GetBaseException().ToString(); completed = watch.Elapsed.TotalMilliseconds; }
        Interlocked.Increment(ref epoch);
        var row = new { Run = run, Mode = mode, From = from, Page = page, Phase = phase, NoOp = noOp,
            HandlerMs = handler, PaintDrainMs = painted, DataReadyMs = completed,
            MaxUiQueueDelayMs = maxQueueDelay, UiAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedStart, Error = error };
        results.Add(row);
        Console.WriteLine(JsonSerializer.Serialize(row));
        if (error != null)
            Console.WriteLine("READINESS_FAILURE " + string.Join(" | ", Children(main).OfType<Label>().Where(c => c.Visible).Select(c => c.Name + ":" + c.Text)));
    }
    static Form Open(string kind)
    {
        object parsed = Enum.Parse(typeof(MainForm).GetNestedType("InlineSettingsKind", Flags)!, kind);
        Call(main, "OpenInlineSettings", parsed);
        return (Form)Field(main, "inlineSettingsForm");
    }
    static TreeView Tree(Form form) => Children(form).OfType<TreeView>().First();
    static bool FolderReady(string name)
    {
        var viewer = (Control)Field(main, name);
        return !Children(viewer).OfType<Label>().Any(c => c.Text.Contains("正在读取"));
    }
    static bool NotesReady()
    {
        var viewer = (Control)Field(main, "noteLogViewer");
        return !Children(viewer).OfType<Label>().Any(c => c.Text.Contains("正在加载") || c.Text.Contains("正在刷新"));
    }
    static void Primary(string kind, string phase)
    {
        switch (kind)
        {
            case "便签文件夹": Measure(kind, phase, () => Call(main, "OpenNoteLogs", Enum.Parse(T("NoteLogKind"), "Todo")), NotesReady); break;
            case "截图文件夹": Measure(kind, phase, () => Call(main, "OpenScreenshotFolderPage"), () => FolderReady("screenshotFolderViewer")); break;
            case "临时文件夹": Measure(kind, phase, () => Call(main, "OpenTemporaryFilePage"), () => FolderReady("temporaryFileViewer")); break;
            default: Measure(kind, phase, () => Open(kind)); break;
        }
    }
    static void Secondary(string kind, string phase, bool reverse)
    {
        Form form = Open(kind);
        TreeView tree = Tree(form);
        var nodes = tree.Nodes.Cast<TreeNode>().ToArray();
        if (reverse) Array.Reverse(nodes);
        foreach (TreeNode node in nodes)
        {
            if (tree.SelectedNode == node && phase == "first")
            { Console.WriteLine("DEFAULT_PAGE " + kind + "/" + node.Text + " included in primary-open timing"); continue; }
            Measure(kind + "/" + node.Text, phase, () => tree.SelectedNode = node, noOp: tree.SelectedNode == node);
        }
    }
    [STAThread] static int Main(string[] args)
    {
        run = args[0]; mode = args.Length > 1 ? args[1] : "empty";
        string reportRoot = Path.GetFullPath("build/github-sync-34-20261023/menus");
        string personal = Path.Combine(reportRoot, "fixtures", run);
        Directory.CreateDirectory(personal);
        Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Control.CheckForIllegalCrossThreadCalls = true;
        Application.EnableVisualStyles();
        Set("CustomPersonalPath", personal);
        var defaults = Stopwatch.StartNew();
        var config = new ApplicationConfig { ShowTray = false, DisableHotkeys = true, UseCustomScreenshotsPath = false };
        Set("Settings", config); Set("DefaultTaskSettings", config.DefaultTaskSettings);
        var hotkeys = new HotkeysConfig(); Set("HotkeysConfig", hotkeys);
        Set("UploadersConfig", new ShareX.UploadersLib.UploadersConfig());
        Set("CLI", new ShareXCLIManager(Array.Empty<string>()));
        Set("UpdateManager", Activator.CreateInstance(T("ShareXUpdateManager"), true)!);
        typeof(SystemOptions).GetProperty("DisableUpload", Flags)!.SetValue(null, true);
        typeof(SystemOptions).GetProperty("DisableUpdateCheck", Flags)!.SetValue(null, true);
        LanguageHelper.ChangeLanguage(SupportedLanguage.SimplifiedChinese);
        using var hotkeyForm = new HotkeyForm();
        var manager = new HotkeyManager(hotkeyForm); manager.UpdateHotkeys(hotkeys.Hotkeys, false); Set("HotkeyManager", manager);
        Console.WriteLine("DEFAULTS_MS " + defaults.Elapsed.TotalMilliseconds);
        if (mode == "populated")
        {
            string screenshots = Path.Combine(personal, "Screenshots"); Directory.CreateDirectory(screenshots);
            using var bitmap = new Bitmap(80, 50); using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CadetBlue);
            bitmap.Save(Path.Combine(screenshots, "seed.png"));
            for (int i = 0; i < 1000; i++) File.Copy(Path.Combine(screenshots, "seed.png"), Path.Combine(screenshots, $"test-{i:D4}.png"));
            string temp = Path.Combine(personal, "临时文件夹", "2026-10"); Directory.CreateDirectory(temp);
            for (int i = 0; i < 2000; i++) File.WriteAllText(Path.Combine(temp, $"test-{i:D4}.txt"), "Synthetic performance fixture");
            var store = Activator.CreateInstance(T("NoteLogStore"), personal)!;
            for (int i = 0; i < 1000; i++) Call(store, "Append", Enum.Parse(T("NoteLogKind"), "Todo"), "测试", "Synthetic note " + i, DateTime.Now);
        }
        var constructor = Stopwatch.StartNew();
        main = new MainForm(); Set("MainForm", main);
        Console.WriteLine("MAIN_CONSTRUCTOR_MS " + constructor.Elapsed.TotalMilliseconds);
        // Skip production startup jobs: no clipboard watcher, scheduler, ASR or keyboard worker.
        main.HandleCreated -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), main, typeof(MainForm).GetMethod("MainForm_HandleCreated", Flags)!);
        main.StartPosition = FormStartPosition.Manual; main.Location = new Point(100, 90);
        main.Size = new Size(1450, 920); main.Text = "小蟒 · 隔离菜单耗时测试 " + run;
        var show = Stopwatch.StartNew();
        int exitCode = 0;
        main.Shown += (_, _) => main.BeginInvoke(new Action(() =>
        {
          try
          {
            main.Update(); Console.WriteLine("MAIN_SHOW_MS " + show.Elapsed.TotalMilliseconds);
            if (!args.Contains("--diagnose-only"))
            {
            string[] primary = { "Hotkeys", "Application", "InputMethod", "便签文件夹", "截图文件夹", "临时文件夹", "About" };
            if (args.Contains("--reverse")) Array.Reverse(primary);
            foreach (string page in primary)
            {
                Primary(page, "first");
                if (page is "Application" or "InputMethod") Secondary(page, "first", false);
                if (page == "便签文件夹")
                    foreach (string category in new[] { "Clipboard", "Favorites", "Todo" })
                        Measure(page + "/" + category, "first", () => Call(main, "OpenNoteLogs", Enum.Parse(T("NoteLogKind"), category)), NotesReady);
            }
            for (int cycle = 0; cycle < (args.Contains("--short") ? 2 : 5); cycle++)
            {
                foreach (string page in primary) Primary(page, "warm-" + cycle);
                Secondary("Application", "warm-" + cycle, cycle % 2 == 0);
                Secondary("InputMethod", "warm-" + cycle, cycle % 2 == 0);
                foreach (string category in new[] { "Clipboard", "Favorites", "Todo" })
                    Measure("便签文件夹/" + category, "warm-" + cycle, () => Call(main, "OpenNoteLogs", Enum.Parse(T("NoteLogKind"), category)), NotesReady);
            }
            }
            if (args.Contains("--diagnose"))
            {
                Form app = Open("Application");
                var tree = Tree(app);
                tree.SelectedNode = tree.Nodes.Cast<TreeNode>().Single(n => ((TabPage)n.Tag!).Name == "tpNotifications");
                Pump(10);
                Control notification = (Control)Field(app, "notificationSettingsControl");
                int layoutEvents = 0;
                foreach (Control c in Children(notification)) c.Layout += (_, _) => layoutEvents++;
                for (int i = 0; i < 3; i++)
                {
                    layoutEvents = 0;
                    Measure("诊断/通知LoadSettings", "normal-" + i, () => Call(notification, "LoadSettings"));
                    Console.WriteLine("LAYOUT_EVENTS normal " + layoutEvents);
                    layoutEvents = 0;
                    var parents = Children(app).ToArray();
                    Measure("诊断/通知LoadSettings暂停布局", "suspended-" + i, () =>
                    {
                        foreach (Control c in parents) c.SuspendLayout();
                        try { Call(notification, "LoadSettings"); }
                        finally { foreach (Control c in parents.Reverse()) c.ResumeLayout(false); app.PerformLayout(); }
                    });
                    Console.WriteLine("LAYOUT_EVENTS suspended " + layoutEvents);
                }
                TreeNode generalNode = tree.Nodes.Cast<TreeNode>().Single(n => ((TabPage)n.Tag!).Name == "tpGeneral");
                TreeNode notificationNode = tree.Nodes.Cast<TreeNode>().Single(n => ((TabPage)n.Tag!).Name == "tpNotifications");
                for (int i = 0; i < 3; i++)
                {
                    tree.SelectedNode = generalNode; Pump(10); layoutEvents = 0;
                    Measure("诊断/通知切换", "normal-" + i, () => tree.SelectedNode = notificationNode);
                    Console.WriteLine("LAYOUT_EVENTS switch-normal " + layoutEvents);
                    var geometry = Children(notification).ToDictionary(c => c, c => (c.Bounds, c.ClientSize, c.Visible, c.Enabled, c.Text));
                    tree.SelectedNode = generalNode; Pump(10); layoutEvents = 0;
                    var suspended = Children(app).Concat(Children((TabPage)notificationNode.Tag!)).Distinct().ToArray();
                    Measure("诊断/通知切换暂停布局", "suspended-" + i, () =>
                    {
                        foreach (Control c in suspended) c.SuspendLayout();
                        try { tree.SelectedNode = notificationNode; }
                        finally { foreach (Control c in suspended.Reverse()) c.ResumeLayout(false); app.PerformLayout(); }
                    });
                    Console.WriteLine("LAYOUT_EVENTS switch-suspended " + layoutEvents);
                    var mismatches = geometry.Where(pair => pair.Value != (pair.Key.Bounds, pair.Key.ClientSize, pair.Key.Visible, pair.Key.Enabled, pair.Key.Text)).Select(pair => pair.Key.Name).ToArray();
                    Console.WriteLine("GEOMETRY_MISMATCHES " + mismatches.Length + " " + string.Join(",", mismatches));
                    if (mismatches.Length > 0) throw new Exception("Layout diagnostic changed control geometry/state");
                }
            }
            File.WriteAllText(Path.Combine(reportRoot, run + ".json"), JsonSerializer.Serialize(new { Run = run, Mode = mode,
                Dpi = main.DeviceDpi, Framework = Environment.Version.ToString(), Personal = personal, Binary = typeof(MainForm).Assembly.Location, Rows = results }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("COMPLETE " + results.Count);
          }
          catch (Exception error) { Console.WriteLine("FATAL " + error); exitCode = 1; }
          finally
          {
            Call(main, "CloseInlineSettings");
            main.Dispose();
            Application.ExitThread();
          }
        }));
        Application.Run(main);
        return exitCode;
    }
}
