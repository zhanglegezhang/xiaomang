using ShareX;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class Checks
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [DllImport("user32.dll")] static extern int GetWindowLongW(IntPtr h, int i);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int z, uint f);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, All)!.Invoke(target, args);
    static object Field(object target, string name) => target.GetType().GetField(name, All)!.GetValue(target);
    static int failures, passed;
    static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (ok) passed++; else failures++; }
    [STAThread] static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var root = Path.GetFullPath("build/widget-position-20261029/fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var program = typeof(ApplicationConfig).Assembly.GetType("ShareX.Program")!;
        program.GetProperty("CustomPersonalPath", All)!.SetValue(null, root);
        var settings = new ApplicationConfig();
        program.GetProperty("Settings", All)!.SetValue(null, settings);
        program.GetProperty("DefaultTaskSettings", All)!.SetValue(null, settings.DefaultTaskSettings);
        settings.TodoSettings.Items.Clear(); settings.TodoSettings.ClipboardItems.Clear(); settings.TodoSettings.FavoriteItems.Clear();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        settings.TodoSettings.WidgetWidth = 700;
        settings.TodoSettings.WidgetHeight = area.Height - 40;
        using var form = new TodoWidgetForm(new NoteLogStore(root));
        form.Show(); Application.DoEvents();
        foreach (double fraction in new[] { .3, .5, .8 })
        foreach (string module in new[] { "Todo", "Clipboard", "Favorites", "News", "Diary" })
        {
            Call(form, "SetExpanded", false);
            int anchor = area.Top + (int)(area.Height * fraction);
            settings.TodoSettings.WidgetY = anchor;
            Call(form, "MoveToScreenEdge");
            Call(form, "SelectModule", module, true);
            Call(form, "SetExpanded", false);
            Check(form.Top == anchor && settings.TodoSettings.WidgetY == anchor, module + " returns to anchor " + anchor);
            Call(form, "SetCompactIdle", true);
            Check(form.Top == anchor, module + " half width anchor");
        }
        if (!args.Contains("--baseline"))
        {
            var overlay = (Form)Field(form, "compactOverlay");
            using var other = new Form { Text = "普通窗口置顶回归", Bounds = area };
            other.Show(); other.Activate(); Application.DoEvents();
            Check((GetWindowLongW(overlay.Handle, -20) & 8) != 0, "normal window activation keeps overlay topmost");
            SetWindowPos(form.Handle, new IntPtr(-2), 0, 0, 0, 0, 0x13);
            SetWindowPos(overlay.Handle, new IntPtr(-2), 0, 0, 0, 0, 0x13);
            var foreground = GetForegroundWindow();
            var bounds = form.Bounds;
            Call(form, "EnsureWidgetTopMost");
            Check((GetWindowLongW(form.Handle, -20) & 8) != 0 && (GetWindowLongW(overlay.Handle, -20) & 8) != 0, "recover native topmost loss on owner and overlay");
            Check(GetForegroundWindow() == foreground && form.Bounds == bounds, "repair preserves focus and bounds");
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++) Call(form, "EnsureWidgetTopMost");
            Console.WriteLine("Topmost check ms/call (includes reflection): " + watch.Elapsed.TotalMilliseconds / 10000);
            Call(form, "SetExpanded", true);
            Call(form, "EnsureWidgetTopMost");
            Check((GetWindowLongW(form.Handle, -20) & 8) != 0, "expanded owner topmost");
            Call(form, "SetExpanded", false);
        }
        form.Close();
        Console.WriteLine($"RESULT passed={passed} failed={failures}; fixture={root}; DPI={form.DeviceDpi}");
        return failures == 0 ? 0 : 1;
    }
}
