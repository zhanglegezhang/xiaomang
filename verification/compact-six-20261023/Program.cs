using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows.Forms;

static class Check
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly product;
    static int checks;
    static Type T(string name) => product.GetType("ShareX." + name, true);
    static object Get(object target, string name) => target.GetType().GetField(name, F)?.GetValue(target) ?? target.GetType().GetProperty(name, F)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, F).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, F).Invoke(target, args);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static void Until(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done()) { if (watch.ElapsedMilliseconds > 2000) throw new TimeoutException("Menu did not close"); Application.DoEvents(); Thread.Sleep(1); }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            string stage = Path.GetFullPath(args[0]), report = Path.GetFullPath(args[1]); Directory.CreateDirectory(report);
            string daily = args.Contains("--daily-dependencies") ? Path.GetFullPath("artifacts/Desktop") : stage;
            AssemblyLoadContext.Default.Resolving += (_, name) => { string path = Path.Combine(daily, name.Name + ".dll"); return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null; };
            product = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(stage, "ShareX.dll"));
            Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles(); Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            var config = Activator.CreateInstance(T("ApplicationConfig"));
            T("Program").GetProperty("Settings", F).SetValue(null, config);
            T("Program").GetProperty("DefaultTaskSettings", F).SetValue(null, Get(config, "DefaultTaskSettings"));
            T("Program").GetProperty("CustomPersonalPath", F).SetValue(null, Path.Combine(report, "isolated-personal"));
            var store = Activator.CreateInstance(T("NoteLogStore"), Path.Combine(report, "isolated-notes"));
            using var widget = (Form)Activator.CreateInstance(T("TodoWidgetForm"), store);
            Set(Get(widget, "todoPage"), "choosingSchedule", true);
            widget.Opacity = 0; widget.ShowInTaskbar = false; widget.Show();
            Call(widget, "SetExpanded", true);
            var durations = new List<double>();
            ContextMenuStrip Open()
            {
                var watch = Stopwatch.StartNew(); Call(widget, "ShowSearchMenu"); watch.Stop(); durations.Add(watch.Elapsed.TotalMilliseconds);
                var menu = (ContextMenuStrip)Get(widget, "searchMenu");
                Assert(menu.Visible && menu.Items.Count == 3, "actual widget menu opens with original commands");
                Assert((IntPtr)Get(widget, "searchMenuMouseHookHandle") != IntPtr.Zero && ((System.Windows.Forms.Timer)Get(widget, "searchMenuOutsideClickTimer")).Enabled, "native hook and UI watcher run while menu is visible");
                return menu;
            }
            void Stopped()
            {
                Assert((IntPtr)Get(widget, "searchMenuMouseHookHandle") == IntPtr.Zero && !((System.Windows.Forms.Timer)Get(widget, "searchMenuOutsideClickTimer")).Enabled, "closed menu releases hook and stops UI timer");
            }
            void Mouse(ContextMenuStrip menu, int message, bool inside)
            {
                Rectangle bounds = menu.RectangleToScreen(menu.ClientRectangle);
                IntPtr data = Marshal.AllocHGlobal(32);
                try
                {
                    for (int offset = 0; offset < 32; offset += 4) Marshal.WriteInt32(data, offset, 0);
                    Marshal.WriteInt32(data, 0, inside ? bounds.Left + bounds.Width / 2 : bounds.Left - 30);
                    Marshal.WriteInt32(data, 4, inside ? bounds.Top + bounds.Height / 2 : bounds.Top - 30);
                    Call(widget, "SearchMenuMouseHookProc", 0, new IntPtr(message), data);
                }
                finally { Marshal.FreeHGlobal(data); }
            }
            Assert(Get(widget, "searchMenu") == null, "no menu watcher is created before first use");
            foreach (int message in new[] { 0x201, 0x204, 0x207, 0x20B })
            {
                var menu = Open(); Mouse(menu, 0x200, false);
                Assert(!(bool)Get(widget, "searchMenuOutsideClickPending"), "moving outside does not dismiss menu");
                Mouse(menu, message, true);
                Assert(menu.Visible && !(bool)Get(widget, "searchMenuOutsideClickPending"), "click inside is left to normal menu handling");
                Mouse(menu, message, false);
                Assert((bool)Get(widget, "searchMenuOutsideClickPending"), "outside button-down schedules dismissal");
                var watch = Stopwatch.StartNew(); Until(() => !menu.Visible); watch.Stop();
                Console.WriteLine($"DISMISS {message:X}: {watch.Elapsed.TotalMilliseconds:F2} ms"); Stopped();
            }
            var escapeMenu = Open();
            Call(escapeMenu, "ProcessDialogKey", Keys.Escape);
            Assert(!escapeMenu.Visible, "Escape closes menu"); Stopped();
            var commandMenu = Open(); commandMenu.Items[0].PerformClick();
            var search = (Form)T("UnifiedSearchForm").GetField("current", F).GetValue(null);
            Assert(search?.Visible == true && !commandMenu.Visible, "original search command executes after closing menu"); Stopped(); search.Close(); Application.DoEvents(); widget.Activate(); Application.DoEvents();
            var reopened = Open();
            Assert(ReferenceEquals(reopened, commandMenu), "same menu remains reusable after command execution");
            Set(widget, "searchMenuOutsideClickPending", true); reopened.Close();
            var fresh = Open();
            Assert(!(bool)Get(widget, "searchMenuOutsideClickPending"), "reopening clears pending click from prior lifetime");
            var themes = (ToolStripMenuItem)fresh.Items[2];
            Assert(themes.Text == "主题" && themes.DropDownItems.Count == 7, "seven theme choices");
            themes.DropDown.Closing += (_, e) => Console.WriteLine("SUBMENU CLOSED " + e.CloseReason); fresh.Closing += (_, e) => Console.WriteLine("ROOT CLOSED " + e.CloseReason); themes.Select(); themes.ShowDropDown(); Console.WriteLine("submenu before pump="+themes.DropDown.Visible); Application.DoEvents();
            var drop = themes.DropDown;
            Assert(drop.Visible, "theme submenu opens");
            Assert(((Rectangle[])Get(widget, "searchMenuScreenBounds")).Any(r => r.Contains(drop.PointToScreen(new Point(drop.Width/2, drop.Height/2)))), "submenu is inside click bounds");
            Mouse((ContextMenuStrip)drop, 0x201, true);
            Assert(!(bool)Get(widget, "searchMenuOutsideClickPending"), "submenu click not treated as outside");
            for(int theme=0;theme<7;theme++) {
                themes.DropDownItems[theme].PerformClick();
                Assert((int)Get(Get(widget,"Settings"),"CompactTheme")==theme && (int)Get(Get(widget,"compactLabel"),"Theme")==theme,"choice applied " + theme);
            }
            var settings=Get(widget,"Settings");
            var jsonType=Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            string serialized=(string)jsonType.GetMethod("SerializeObject", new[]{typeof(object)}).Invoke(null,new[]{settings});
            object restored=jsonType.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{serialized,settings.GetType()});
                        Assert((int)Get(restored,"CompactTheme")==6,"theme survives JSON round trip");
            Set(config,"TodoSettings",restored);
            using (var restoredWidget=(Form)Activator.CreateInstance(T("TodoWidgetForm"),store)) {
                Assert((int)Get(Get(restoredWidget,"compactLabel"),"Theme")==6,"new widget restores saved theme");
            }
            Assert((int)Get(Activator.CreateInstance(settings.GetType()),"CompactTheme")==0,"existing configs default to original");
                        Set(restored,"CompactThemeRevision",0);
            Set(restored,"CompactTheme",2);
            using(var legacy=(Form)Activator.CreateInstance(T("TodoWidgetForm"),store))
                Assert((int)Get(Get(legacy,"compactLabel"),"Theme")==0,"old theme revision falls back to default");
                        var label=Get(widget,"compactLabel");
            string Fingerprint(bool recording,int theme) {
                label.GetType().GetProperty("Theme",F).SetValue(label,theme);
                label.GetType().GetProperty("IsRecording",F).SetValue(label,recording);
                using var bitmap=new Bitmap(96,96);using var graphics=Graphics.FromImage(bitmap);
                Call(label,"DrawContent",graphics,bitmap.Size);
                using var ms=new MemoryStream();bitmap.Save(ms,System.Drawing.Imaging.ImageFormat.Png);
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ms.ToArray()));
            }
            var logos=new HashSet<string>();
            string recordingBase=Fingerprint(true,0);
            for(int theme=0;theme<7;theme++) {
                Assert(logos.Add(Fingerprint(false,theme)),"actual label uses distinct robot "+theme);
                Assert(Fingerprint(true,theme)==recordingBase,"recording icon unchanged "+theme);
            }
            var render=T("TodoWidgetForm").GetMethod("DrawCompactBackground",F);
            var renderMs=new List<double>();
            using var board=new Bitmap(1120,480);using(var bg=Graphics.FromImage(board)) {
              bg.Clear(Color.FromArgb(230,235,240));
              foreach(int dpi in new[]{96,144,192}) for(int theme=0;theme<7;theme++) {
                int size=48*dpi/96;using var bmp=new Bitmap(size,size);using var g=Graphics.FromImage(bmp);
                var watch=Stopwatch.StartNew();render.Invoke(null,new object[]{g,bmp.Size,dpi,false,theme});watch.Stop();renderMs.Add(watch.Elapsed.TotalMilliseconds);
                Assert(bmp.GetPixel(size/2,size/2).A==255,"render opaque center " + theme + "/"+dpi);
                Assert(bmp.GetPixel(0,0).A==0,"render clipped corner " + theme + "/"+dpi);
                using var stream=product.GetManifestResourceStream("ShareX.Assets.XiaomangCompactRobot.png");using var logo=Image.FromStream(stream);
                var themed=(Image)T("CompactWidgetThemes").GetMethod("GetLogo",F).Invoke(null,new object[]{theme,logo});
                Assert(theme==0 ? ReferenceEquals(themed,logo) : !ReferenceEquals(themed,logo),"theme logo resolved "+theme);
                g.DrawImage(themed,new Rectangle(size/6,size/6,size*2/3,size*2/3));
                bg.DrawImage(bmp,new Rectangle(theme*160+20,(dpi/48-2)*140+35,size,size));
                bg.DrawString(new[]{"默认","绯红","赤雪","青瓷","酒红","霜雪","薄荷"}[theme],SystemFonts.DefaultFont,Brushes.Black,theme*160+20,(dpi/48-2)*140+10);
              }
            }
            board.Save(Path.Combine(report,"themes.png"));
            using(var b=new Bitmap(48,48))using(var g=Graphics.FromImage(b)) {
                render.Invoke(null,new object[]{g,b.Size,96,false,0});int expected=b.GetPixel(24,24).ToArgb();
                render.Invoke(null,new object[]{g,b.Size,96,false,999});Assert(b.GetPixel(24,24).ToArgb()==expected,"unknown theme falls back");
                var watch=Stopwatch.StartNew();for(int i=0;i<100;i++)render.Invoke(null,new object[]{g,b.Size,96,true,2});watch.Stop();
                Console.WriteLine("cached render average ms="+watch.Elapsed.TotalMilliseconds/100);
            }
            Console.WriteLine("first render timings ms="+string.Join(",",renderMs));
            widget.Close();
            Assert(fresh.IsDisposed && (IntPtr)Get(widget, "searchMenuMouseHookHandle") == IntPtr.Zero && Get(widget, "searchMenuOutsideClickTimer") == null, "closing widget disposes menu, timer and native hook");
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(stage, "ShareX.dll"))));
            File.WriteAllText(Path.Combine(report, "result.json"), JsonSerializer.Serialize(new { Checks = checks, Hash = hash, OpenMs = durations, Scope = "Actual compiled widget and menu, installed Windows hook, simulated hook input and real UI timer; no OS mouse input injected" }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"RESULT {checks} menu-dismiss checks passed"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}




