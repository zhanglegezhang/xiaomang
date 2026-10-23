using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Newtonsoft.Json;
using ShareX;
using ShareX.HelpersLib;

internal static class Check
{
    const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static readonly Type ProgramType = typeof(HotkeyManager).Assembly.GetType("ShareX.Program")!;
    static readonly Type MonitorType = typeof(HotkeyManager).Assembly.GetType("ShareX.ForegroundFullscreenMonitor")!;
    static bool Fullscreen() => (bool)MonitorType.GetMethod("IsForegroundFullscreen", All)!.Invoke(null, null)!;
    static bool Suspended(HotkeyManager manager) => (bool)typeof(HotkeyManager).GetProperty("FullscreenSuspended", All)!.GetValue(manager)!;
    static bool GlobalAllowed(HotkeyManager manager) => (bool)typeof(HotkeyManager).GetProperty("CanRunGlobalActions", All)!.GetValue(manager)!;
    static void Refresh(HotkeyManager manager) => typeof(HotkeyManager).GetMethod("RefreshFullscreenPolicy", All)!.Invoke(manager, null);
    static object Field(object owner, string name) => owner.GetType().GetField(name, All)!.GetValue(owner)!;
    static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static void Pump(int milliseconds = 80) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(2); } }
    static double Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 1600) Pump(2);
        Assert(condition(), "window event transition within 1.6 seconds");
        return watch.Elapsed.TotalMilliseconds;
    }
    static void Focus(Form form)
    {
        uint current = GetCurrentThreadId(), foreground = GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        bool attached = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
        try { form.Show(); form.Activate(); NativeMethods.SetForegroundWindow(form.Handle); BringWindowToTop(form.Handle); }
        finally { if (attached) AttachThreadInput(current, foreground, false); }
    }
    static void Activate(Form form) { Focus(form); Pump(); Assert(NativeMethods.GetForegroundWindow() == form.Handle, "test window foreground"); }
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint one, uint two, bool attach);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    static void SendChord(Keys key)
    {
        byte[] keys = { 0x11, 0x10, 0x12, (byte)(key & Keys.KeyCode) };
        try { foreach (byte item in keys) keybd_event(item, 0, 0, UIntPtr.Zero); }
        finally { foreach (byte item in keys.Reverse()) keybd_event(item, 0, 2, UIntPtr.Zero); }
        Pump();
    }
    static void Benchmark(ApplicationConfig config, bool enabled)
    {
        config.DisableHotkeysOnFullscreen = enabled;
        using var host = new HotkeyForm { Text = "小蟒后台监听耗时验证", Size = new Size(400, 180), ShowInTaskbar = false };
        using var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        var process = Process.GetCurrentProcess(); TimeSpan cpu = default;
        var clock = new Stopwatch(); HotkeyManager manager = null!;
        host.Shown += (_, _) =>
        {
            manager = new HotkeyManager(host); manager.UpdateHotkeys(new(), false); timer.Start();
        };
        timer.Tick += (_, _) =>
        {
            if (!clock.IsRunning) { cpu = process.TotalProcessorTime; clock.Start(); timer.Interval = 6000; }
            else
            {
                timer.Stop();
                Console.WriteLine($"monitorEnabled={enabled}; singleCoreCpuPercent={(process.TotalProcessorTime - cpu).TotalMilliseconds / clock.Elapsed.TotalMilliseconds * 100:F4}; wallMs={clock.Elapsed.TotalMilliseconds:F1}");
                host.Close();
            }
        };
        Application.Run(host);
    }
    static HotkeySettings Binding(HotkeyType type, Keys key) => new(type, Keys.Control | Keys.Shift | Keys.Alt | key);
    static void Recording(bool value) => typeof(ScreenRecordManager).GetProperty("IsRecording", All)!.SetValue(null, value);

    [STAThread]
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void Run(string[] args)
    {
        Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var config = new ApplicationConfig { ShowTray = false };
        ProgramType.GetProperty("Settings", All)!.SetValue(null, config);
        ProgramType.GetProperty("DefaultTaskSettings", All)!.SetValue(null, config.DefaultTaskSettings);
        ProgramType.GetProperty("Sandbox", All)!.SetValue(null, true);
        if (args.Contains("--perf-enabled") || args.Contains("--perf-disabled")) { Benchmark(config, args.Contains("--perf-enabled")); return; }
        Assert(!config.DisableHotkeysOnFullscreen, "new installation default off");
        config.DisableHotkeysOnFullscreen = true;
        var restored = JsonConvert.DeserializeObject<ApplicationConfig>(JsonHelpers.SerializeToString(config))!;
        Assert(restored.DisableHotkeysOnFullscreen, "setting survives JSON save/load");
        Assert(!JsonConvert.DeserializeObject<ApplicationConfig>("{}")!.DisableHotkeysOnFullscreen, "old settings without field retain default off");
        config.DisableHotkeysOnFullscreen = false;
        using var host = new HotkeyForm { ShowInTaskbar = false, Size = new Size(420, 250), Text = "小蟒快捷键隔离验证" };
        Activate(host);
        var manager = new HotkeyManager(host);
        ProgramType.GetProperty("HotkeyManager", All)!.SetValue(null, manager);
        var capture = Binding(HotkeyType.RectangleRegion, Keys.F20);
        var record = Binding(HotkeyType.ScreenRecorder, Keys.F21);
        var pause = Binding(HotkeyType.PauseScreenRecording, Keys.F22);
        var stop = Binding(HotkeyType.StopScreenRecording, Keys.F23);
        var input = Binding(HotkeyType.SwitchToXiaomangInputMethod, Keys.F24);
        var entries = new List<HotkeySettings> { capture, record, pause, stop, input };
        manager.UpdateHotkeys(entries, false);
        Assert(entries.All(x => x.HotkeyInfo.Status == HotkeyStatus.Registered), "isolated bindings registered");
        Assert(!Fullscreen(), "normal window not fullscreen");
        host.WindowState = FormWindowState.Maximized; Pump();
        Assert(!Fullscreen(), "ordinary maximized captioned window not fullscreen");
        host.WindowState = FormWindowState.Normal; Pump();
        using var fullscreen = new Form { Text = "小蟒全屏隔离验证", FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, BackColor = Color.FromArgb(30, 40, 50), Bounds = Screen.FromControl(host).Bounds };
        Activate(fullscreen);
        Assert(Fullscreen(), "borderless foreground covering display is fullscreen");
        Refresh(manager);
        Assert(!Suspended(manager) && entries.All(x => x.HotkeyInfo.Status == HotkeyStatus.Registered), "off setting keeps hotkeys in fullscreen");
        config.DisableHotkeysOnFullscreen = true;
        Refresh(manager);
        Assert(Suspended(manager), "enable while already fullscreen takes effect immediately");
        Assert(!GlobalAllowed(manager), "voice typing and input switching share fullscreen guard");
        var voiceType = ProgramType.Assembly.GetType("ShareX.VoiceTypingController")!;
        using (var voice = (IDisposable)Activator.CreateInstance(voiceType, All, null,
            new object[] { host, (Func<bool>)(() => GlobalAllowed(manager)), (Func<TodoWidgetForm>)(() => null!) }, null)!)
        {
            var armType = ProgramType.Assembly.GetType("ShareX.VoiceTypingKeyAction")!;
            voiceType.GetMethod("OnKey", All)!.Invoke(voice, new[] { Enum.Parse(armType, "Arm") });
            Pump();
            Assert((long)Field(voice, "armedAt") == 0 && voiceType.GetField("session", All)!.GetValue(voice) == null,
                "actual voice controller does not arm or start microphone in fullscreen");
        }
        Assert(entries.All(x => x.HotkeyInfo.Status != HotkeyStatus.Registered), "all ordinary registrations released in fullscreen");
        using var other = new HotkeyForm { ShowInTaskbar = false };
        _ = other.Handle;
        var takeover = new HotkeyInfo(capture.HotkeyInfo.Hotkey);
        other.RegisterHotkey(takeover);
        Assert(takeover.Status == HotkeyStatus.Registered, "another application can register released key");
        int delivered = 0;
        other.HotkeyRepeatLimit = 0; other.HotkeyPress += (_, _, _) => delivered++;
        SendChord(capture.HotkeyInfo.Hotkey);
        Assert(delivered == 1, "Windows delivers released keyboard chord to the other application");
        other.UnregisterHotkey(takeover);
        var measure = Stopwatch.StartNew();
        Focus(host);
        double restoreMs = Until(() => !Suspended(manager) && entries.All(x => x.HotkeyInfo.Status == HotkeyStatus.Registered));
        Console.WriteLine($"restoreMs={restoreMs:F3}");
        Assert(GlobalAllowed(manager), "voice and input switching become available after leaving fullscreen");
        Focus(fullscreen);
        double suspendMs = Until(() => Suspended(manager) && entries.All(x => x.HotkeyInfo.Status != HotkeyStatus.Registered));
        Console.WriteLine($"suspendMs={suspendMs:F3}");
        Assert(config.DisableHotkeys == false, "automatic suspension does not change manual disabled setting");
        Recording(true); Refresh(manager);
        Assert(capture.HotkeyInfo.Status != HotkeyStatus.Registered && input.HotkeyInfo.Status != HotkeyStatus.Registered, "recording keeps screenshot and input switch suspended");
        Assert(record.HotkeyInfo.Status == HotkeyStatus.Registered && pause.HotkeyInfo.Status == HotkeyStatus.Registered && stop.HotkeyInfo.Status == HotkeyStatus.Registered, "active recording stop toggle pause preserved");
        int called = 0; manager.HotkeyTrigger += _ => called++;
        var dispatch = typeof(HotkeyManager).GetMethod("HotkeyForm_HotkeyPress", All)!;
        dispatch.Invoke(manager, new object[] { record.HotkeyInfo.ID, Keys.F21, record.HotkeyInfo.ModifiersEnum });
        Assert(called == 1, "recording stop action executes while fullscreen");
        manager.IgnoreHotkeys = true;
        dispatch.Invoke(manager, new object[] { stop.HotkeyInfo.ID, Keys.F23, stop.HotkeyInfo.ModifiersEnum });
        Assert(called == 1, "personal data and editing guard still blocks actions");
        manager.IgnoreHotkeys = false;
        Recording(false); Refresh(manager);
        Assert(entries.All(x => x.HotkeyInfo.Status != HotkeyStatus.Registered), "recording completion releases recording hotkeys again");
        var added = Binding(HotkeyType.ActiveWindowTopMost, Keys.F19);
        manager.RegisterHotkey(added);
        Assert(added.HotkeyInfo.Status != HotkeyStatus.Registered, "new binding added during fullscreen waits for restore");
        manager.UnregisterHotkey(added);
        Assert(!entries.Contains(added), "removed suspended binding does not return");
        config.DisableHotkeys = true; manager.ToggleHotkeys(true);
        Activate(host); Refresh(manager);
        Assert(entries.All(x => x.HotkeyInfo.Status != HotkeyStatus.Registered) && config.DisableHotkeys, "exit fullscreen retains manual disabled state");
        config.DisableHotkeys = false; manager.ToggleHotkeys(false);
        Assert(entries.All(x => x.HotkeyInfo.Status == HotkeyStatus.Registered), "manual enable still works");
        Activate(fullscreen); Refresh(manager);
        config.DisableHotkeysOnFullscreen = false; Refresh(manager);
        Assert(entries.All(x => x.HotkeyInfo.Status == HotkeyStatus.Registered) && !Suspended(manager), "turning off while fullscreen restores bindings");
        var monitor = Field(manager, "fullscreenMonitor");
        Assert(!((System.Windows.Forms.Timer)Field(monitor, "timer")).Enabled && (IntPtr)Field(monitor, "foregroundHook") == IntPtr.Zero, "option off stops polling and releases event hooks");
        config.DisableHotkeysOnFullscreen = true; Refresh(manager);
        other.RegisterHotkey(takeover);
        Assert(takeover.Status == HotkeyStatus.Registered, "conflict fixture acquired released key");
        Activate(host); Refresh(manager);
        Assert(capture.HotkeyInfo.Status == HotkeyStatus.Failed, "restore conflict is visible as registration failure");
        other.UnregisterHotkey(takeover); manager.RegisterFailedHotkeys();
        Assert(capture.HotkeyInfo.Status == HotkeyStatus.Registered, "conflict can be repaired without losing binding");
        // Verify foreground-based policy, including switching away while the full window remains open.
        foreach (var screen in Screen.AllScreens)
        {
            // Finish the monitor's DPI transition before assigning fullscreen
            // bounds; otherwise WM_DPICHANGED scales that first rectangle.
            fullscreen.Location = screen.Bounds.Location; Pump();
            fullscreen.Bounds = screen.Bounds; Activate(fullscreen); Refresh(manager);
            Assert(Fullscreen() && Suspended(manager), "fullscreen on monitor " + screen.DeviceName);
            Activate(host); Refresh(manager);
            Assert(!Suspended(manager), "background fullscreen does not disable foreground normal window");
        }
        var times = new List<double>();
        for (int index = 0; index < 1000; index++) { var clock = Stopwatch.StartNew(); Fullscreen(); times.Add(clock.Elapsed.TotalMilliseconds); }
        times.Sort(); Console.WriteLine($"detectionMedianMs={times[500]:F4}; detectionP95Ms={times[950]:F4}; displays={Screen.AllScreens.Length}");
        var constructClock = Stopwatch.StartNew();
        using var page = new HotkeySettingsForm(manager);
        Console.WriteLine($"pageConstructionMs={constructClock.Elapsed.TotalMilliseconds:F3}");
        page.ShowInTaskbar = false; page.Show(); Activate(page);
        int S(int x) => (int)Math.Round(x * page.DeviceDpi / 96f);
        var toggle = (CheckBox)Field(page, "fullscreenToggle");
        var label = (Label)Field(page, "fullscreenToggleLabel");
        var reset = (Button)Field(page, "btnReset");
        var list = (FlowLayoutPanel)Field(page, "flpHotkeys");
        var pageClock = Stopwatch.StartNew();
        foreach (int width in new[] { 560, 760, 1000 })
        {
            page.ClientSize = new Size(S(width), S(450)); Pump();
            Assert(page.ClientRectangle.Contains(toggle.Bounds) && page.ClientRectangle.Contains(label.Bounds), "toggle fits width " + width);
            Assert(!toggle.Bounds.IntersectsWith(reset.Bounds) && !label.Bounds.IntersectsWith(reset.Bounds), "toolbar does not overlap width " + width);
            Assert(list.Top > toggle.Bottom && list.Width > 0, "hotkey list below toolbar width " + width);
        }
        Console.WriteLine($"layoutThreeSizesMs={pageClock.Elapsed.TotalMilliseconds:F3}; dpi={page.DeviceDpi}");
        toggle.Checked = false;
        Assert(!config.DisableHotkeysOnFullscreen, "UI switch updates settings immediately");
        toggle.Checked = true;
        Assert(config.DisableHotkeysOnFullscreen, "UI switch enables persisted setting");
        config.DisableHotkeysOnFullscreen = false;
        typeof(HotkeySettingsForm).GetMethod("RefreshForActivation", All)!.Invoke(page, null);
        Assert(!toggle.Checked, "reopening page reflects advanced setting changes");
        string output = Path.GetFullPath("build/fullscreen-hotkeys-20261021");
        page.ClientSize = new Size(S(1000), S(450)); Pump();
        using (var image = new Bitmap(page.Width, page.Height)) { page.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, $"hotkeys-{page.DeviceDpi}.png")); }
        page.Close();
        config.DisableHotkeysOnFullscreen = true; Refresh(manager);
        manager.UnregisterAllHotkeys(false);
        host.Close();
        Assert((bool)Field(monitor, "disposed") && !((System.Windows.Forms.Timer)Field(monitor, "timer")).Enabled, "closing owner disposes hooks and timer");
        Recording(false);
        Console.WriteLine($"PASS {checks} checks");
    }
}
