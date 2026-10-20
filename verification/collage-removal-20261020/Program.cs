using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

static class Check
{
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr handle, int index);
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Assembly app = null!;
    static string output = null!;
    static int passes;
    static object Get(object o, string n) => o.GetType().GetField(n, F)?.GetValue(o) ?? (o.GetType().GetProperty(n, F | BindingFlags.DeclaredOnly) ?? o.GetType().GetProperty(n, F))!.GetValue(o)!;
    static void Select(Control list, int index) => list.GetType().GetProperty("SelectedIndex", F)!.SetValue(list, index);
    static object? Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, F)!.Invoke(o, a);
    static void Assert(bool value, string label) { if (!value) throw new Exception(label); passes++; Console.WriteLine("PASS " + label); }
    static void Pump(int milliseconds = 50) { var w = Stopwatch.StartNew(); while (w.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(2); } }
    static void Ready(Form form)
    {
        var watch = Stopwatch.StartNew();
        do { Pump(15); if (watch.ElapsedMilliseconds > 20000) throw new TimeoutException("Preview timeout"); }
        while ((bool)Get(form, "working") || (bool)Get(form, "pending"));
    }
    static void Accessible(Control list, List<AccessibleObject> held)
    {
        var root = list.AccessibilityObject;
        for (int i = 0; i < root.GetChildCount(); i++)
        {
            var child = root.GetChild(i);
            if (child == null) continue;
            held.Add(child); _ = child.Name; _ = child.Bounds; _ = child.State;
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            string runtime = Path.GetFullPath(args[0]); output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            AssemblyLoadContext.Default.Resolving += (_, n) => { string p = Path.Combine(runtime, n.Name + ".dll"); return File.Exists(p) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(p) : null; };
            app = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime, "ShareX.dll"));
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => { Console.Error.WriteLine("UI_THREAD_EXCEPTION " + e.Exception); Console.Error.Flush(); Environment.Exit(2); };
            Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
            string[] paths = Enumerable.Range(0, 3).Select(i => Path.Combine(output, "fixture-" + i + ".png")).ToArray();
            for (int i = 0; i < paths.Length; i++) { using var b = new Bitmap(120 + 20 * i, 80 + 10 * i); using (var g = Graphics.FromImage(b)) g.Clear(new[] { Color.Red, Color.Blue, Color.Green }[i]); b.Save(paths[i], ImageFormat.Png); }
            var original = paths.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
            var openWatch = Stopwatch.StartNew();
            var initial = args.Contains("--layout") ? Enumerable.Range(0, 50).Select(i => paths[i % 3]) : paths.Take(args.Contains("--three") ? 3 : 2);
            using var form = (Form)Activator.CreateInstance(app.GetType("ShareX.ImageCollageForm", true)!, F, null, new object[] { initial }, null)!;
            form.Text = "BUG-12 独立拼图回归 " + Environment.ProcessId;
            form.Show(); Ready(form);
            Console.WriteLine("OPEN_PREVIEW_MS " + openWatch.Elapsed.TotalMilliseconds + " DPI " + form.DeviceDpi);
            if (args.Contains("--layout"))
            {
                var list = (ListView)Get(form, "files"); Select(list, 49);
                form.Size = new Size(form.MinimumSize.Width, form.MinimumSize.Height + 100); Pump(300);
                Console.WriteLine("LAYOUT column=" + list.Columns[0].Width + " client=" + list.ClientSize.Width + " style=" + GetWindowLong(list.Handle, -16).ToString("X"));
                Assert((GetWindowLong(list.Handle, -16) & 0x100000) == 0, "native horizontal scrollbar absent");
                form.Close(); return 0;
            }
            if (args.Contains("--narrow")) { form.Size = new Size(form.MinimumSize.Width, form.MinimumSize.Height + 100); Pump(); }
            ((NumericUpDown)Get(form, "gap")).Value = 13;
            form.GetType().GetField("background", F)!.SetValue(form, Color.Purple); Call(form, "Changed"); Ready(form);
            using (var saved = (Image)((PictureBox)Get(form, "preview")).Image!.Clone()) saved.Save(Path.Combine(output, "saved-preview.png"), ImageFormat.Png);
            var start = new Button { Text = "运行移除回归", Dock = DockStyle.Bottom, Height = 40 };
            form.Controls.Add(start); start.BringToFront();
            int result = 0;
            start.Click += (_, _) =>
            {
                start.Enabled = false;
                try { Run(form, paths, original, args.Contains("--accessible")); }
                catch (Exception e) { Console.Error.WriteLine(e); result = 1; }
                if (!args.Contains("--keep")) form.Close();
            };
            Console.WriteLine("READY " + Environment.ProcessId); Console.Out.Flush();
            if (!args.Contains("--manual")) form.BeginInvoke(() => start.PerformClick());
            Application.Run(form);
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passes, result, dpi96 = args.Contains("--dpi96"), accessible = args.Contains("--accessible"), runtimeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(runtime, "ShareX.dll")))) }));
            return result;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Run(Form form, string[] paths, string[] original, bool accessible)
    {
        var list = (Control)Get(form, "files");
        var items = (System.Collections.IList)Get(list, "Items");
        var held = new List<AccessibleObject>();
        if (accessible) Accessible(list, held);
        Console.WriteLine("BEFORE_FIRST_REMOVE"); Console.Out.Flush();
        Select(list, 0); Call(form, "RemoveSelected"); Ready(form);
        Assert(items.Count == 1, "remove after spacing/background/save");
        Call(form, "RemoveSelected"); Ready(form); Assert(items.Count == 0, "remove last item");
        Call(form, "RemoveSelected"); Assert(items.Count == 0, "remove empty list no-op");
        var durations = new List<double>();
        for (int cycle = 0; cycle < 50; cycle++)
        {
            Call(form, "AddFiles", (object)paths); Ready(form);
            if (accessible) Accessible(list, held);
            Select(list, 1);
            Call(form, "MoveSelected", -1); Assert((int)Get(list, "SelectedIndex") == 0 && items[0]!.ToString() == Path.GetFileName(paths[1]), "move up " + cycle);
            if (accessible) Accessible(list, held);
            Call(form, "MoveSelected", 1); Assert((int)Get(list, "SelectedIndex") == 1, "move down " + cycle);
            Select(list, 1);
            var watch = Stopwatch.StartNew(); Call(form, "RemoveSelected"); durations.Add(watch.Elapsed.TotalMilliseconds);
            Assert(items.Count == 2, "remove middle " + cycle);
            if (accessible) Accessible(list, held);
            Select(list, 1); Call(form, "RemoveSelected"); Select(list, 0); Call(form, "RemoveSelected"); Ready(form);
            Assert(items.Count == 0, "remove all " + cycle);
            if (cycle % 5 == 0) { held.Clear(); GC.Collect(); GC.WaitForPendingFinalizers(); Pump(); }
        }
        Assert(paths.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).SequenceEqual(original), "source image hashes unchanged");
        Assert(((PictureBox)Get(form, "preview")).Image == null, "empty preview detached");
        Call(form, "AddFiles", (object)paths); Ready(form);
        EventHandler reentrant = (_, _) => Call(form, "RemoveSelected");
        var selection = list.GetType().GetEvent("SelectedIndexChanged", F)!;
        selection.AddEventHandler(list, reentrant);
        Call(form, "RemoveSelected");
        selection.RemoveEventHandler(list, reentrant);
        Assert(items.Count == 2, "selection callback cannot recursively remove two images"); Ready(form);
        Call(list, "OnKeyDown", new KeyEventArgs(Keys.Delete)); Ready(form);
        Assert(items.Count == 1, "Delete key removes one selected image");
        Call(form, "RemoveSelected"); Ready(form);
        Call(form, "AddFiles", (object)paths); Ready(form);
        Select(list, 1); Call(list, "OnKeyDown", new KeyEventArgs(Keys.Alt | Keys.Up));
        Assert(items[0]!.ToString() == Path.GetFileName(paths[1]), "Alt+Up changes order");
        Call(list, "OnKeyDown", new KeyEventArgs(Keys.Alt | Keys.Down));
        Assert(items[1]!.ToString() == Path.GetFileName(paths[1]), "Alt+Down changes order"); Ready(form);
        string corrupt = Path.Combine(output, "corrupt-after-preview.png"); File.Copy(paths[0], corrupt, true);
        Call(form, "AddFiles", (object)new[] { corrupt }); Ready(form);
        File.WriteAllText(corrupt, "not an image"); Call(form, "Changed"); Ready(form);
        Assert(items.Count == 3 && !items.Cast<object>().Any(x => x.ToString() == Path.GetFileName(corrupt)), "invalid image removed safely after preview");
        Call(form, "AddFiles", (object)Enumerable.Repeat(paths[0], 47).ToArray()); Ready(form);
        Assert(items.Count == 50, "50-image limit retained");
        Select(list, 49); Pump();
        Assert(((ListView)list).TopItem!.Index > 0 && ((ListView)list).Items[49].Bounds.IntersectsWith(list.ClientRectangle), "select last item scrolls into view");
        form.Size = new Size(form.MinimumSize.Width, form.MinimumSize.Height + 100); Pump();
        Console.WriteLine("NARROW_WIDTH column=" + ((ListView)list).Columns[0].Width + " client=" + list.ClientSize.Width);
        Assert(((ListView)list).Columns[0].Width <= list.ClientSize.Width, "narrow list does not require horizontal scrolling");
        Assert((GetWindowLong(list.Handle, -16) & 0x100000) == 0, "native horizontal scrollbar absent after resize");
        Assert(((ListView)list).Items[49].Bounds.IntersectsWith(list.ClientRectangle), "narrow list keeps selected last image visible");
        using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(output, "narrow.png"), ImageFormat.Png); }
        Assert(((Control)Get(form, "remove")).Enabled, "narrow layout retains selected-item actions");
        Select(list, 0); Call(form, "RemoveSelected");
        form.Close(); Pump(400);
        Assert(form.IsDisposed, "close with pending preview completes without disposed-image callbacks");
        Console.WriteLine("REMOVE_MEDIAN_MS " + durations.OrderBy(x => x).ElementAt(durations.Count / 2));
        Console.WriteLine("RESULT " + passes); Console.Out.Flush();
    }
}
