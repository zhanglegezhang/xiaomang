using ShareX;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

static class Check
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int count;
    static readonly string Output = Path.GetFullPath("test-results/registration-popup-ui/evidence");
    static Type DialogType => typeof(OCRForm).Assembly.GetType("ShareX.BaiduTranslationSettingsForm", true)!;
    static IEnumerable<Control> All(Control c) => new[] { c }.Concat(c.Controls.Cast<Control>().SelectMany(All));
    static T Find<T>(Control root, string name) where T : Control => (T)All(root).Single(c => c.Name == name);
    static void Assert(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); count++; }
    static void Pump() { Application.DoEvents(); Thread.Sleep(20); Application.DoEvents(); }
    static void Until(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew(); while (!ready() && clock.ElapsedMilliseconds < 3000) Pump();
        if (!ready()) throw new Exception("Timed out waiting for UI");
    }
    static void Shot(Control form, string name)
    {
        using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(Path.Combine(Output, name + ".png"));
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles(); Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Directory.CreateDirectory(Output);
            var program = typeof(OCRForm).Assembly.GetType("ShareX.Program", true)!;
            var config = new ApplicationConfig { ShowTray = false, DisableHotkeys = true };
            foreach (var entry in new (string, object)[] {
                ("Settings", config), ("Sandbox", true), ("CustomPersonalPath", Path.Combine(Output, "profile")),
                ("DefaultTaskSettings", config.DefaultTaskSettings), ("HotkeysConfig", new HotkeysConfig()),
                ("UploadersConfig", new ShareX.UploadersLib.UploadersConfig()) }) program.GetProperty(entry.Item1, F)!.SetValue(null, entry.Item2);
            DialogChecks(); MenuChecks(); Console.WriteLine($"RESULT {count} passed"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void DialogChecks()
    {
        var clock = Stopwatch.StartNew();
        (string AppId, string Secret) saved = ("1234567890", "synthetic-not-a-real-key");
        int writes = 0, clears = 0, opens = 0, tests = 0;
        bool failSave = false, failClear = false, failOpen = false;
        TaskCompletionSource pending = null;
        var load = (Func<(string, string)>)(() => saved);
        var save = (Action<string, string>)((id, secret) => { if (failSave) throw new IOException(); saved = (id, secret); writes++; });
        var clear = (Action)(() => { if (failClear) throw new IOException(); saved = (null, null); clears++; });
        var test = (Func<string, string, CancellationToken, Task>)((id, secret, token) => { tests++; return pending?.Task ?? Task.CompletedTask; });
        var open = (Action)(() => { if (failOpen) throw new IOException(); opens++; });
        using var dialog = (Form)Activator.CreateInstance(DialogType, F, null, new object[] { load, save, clear, test, open }, null)!;
        dialog.Show(); Pump(); clock.Stop(); Console.WriteLine($"DIALOG_OPEN_MS={clock.Elapsed.TotalMilliseconds:F1}; DPI={dialog.DeviceDpi}");
        var idInput = Find<TextBox>(dialog, "baiduAppId"); var secretInput = Find<TextBox>(dialog, "baiduSecret");
        var status = Find<Label>(dialog, "baiduConfigurationStatus");
        Assert(dialog.Text == "百度翻译 API 配置" && dialog.Icon != null, "dialog title and Xiaomang icon");
        Assert(secretInput.UseSystemPasswordChar && idInput.Text == saved.AppId, "existing credentials loaded with secret masked");
        Assert(!dialog.Controls.OfType<GroupBox>().Any(), "no nested form cards");
        foreach (int width in new[] { 480, 560 })
        {
            dialog.ClientSize = new Size(width * dialog.DeviceDpi / 96, 438 * dialog.DeviceDpi / 96); Pump();
            foreach (string name in new[] { "baiduAppId", "baiduSecret", "baiduRevealSecret", "baiduOpenCertification", "baiduClearCredentials", "baiduTestConnection", "baiduSaveCredentials", "baiduClose" })
            {
                var control = Find<Control>(dialog, name); var bounds = dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                Assert(control.Visible && bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= dialog.ClientSize.Width && bounds.Bottom <= dialog.ClientSize.Height,
                    $"{name} fits {width}px at {dialog.DeviceDpi} DPI");
                if (control is Button button && button.Text.Length > 0)
                {
                    int textWidth = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    int icon = (string)button.GetType().GetProperty("Glyph", F)!.GetValue(button) == null ? 0 : 26 * button.DeviceDpi / 96;
                    Assert(textWidth + icon < button.Width, name + " text and icon fit");
                }
            }
            var hint = Find<Label>(dialog, "baiduConfigurationHint");
            int textHeight = TextRenderer.MeasureText(hint.Text, hint.Font, new Size(hint.Width, 0), TextFormatFlags.WordBreak).Height;
            Assert(textHeight <= hint.Height, "short instructions fit at " + width);
            Shot(dialog, $"baidu-dialog-{dialog.DeviceDpi}-{width}");
        }
        var reveal = Find<Button>(dialog, "baiduRevealSecret"); reveal.PerformClick(); Assert(!secretInput.UseSystemPasswordChar, "secret can be revealed on request");
        DialogType.GetMethod("OnDeactivate", F)!.Invoke(dialog, new object[] { EventArgs.Empty });
        Assert(secretInput.UseSystemPasswordChar, "secret remasked when leaving dialog");
        Find<Button>(dialog, "baiduOpenCertification").PerformClick(); Assert(opens == 1 && writes == 0, "certification action does not save credentials");
        Assert((string)DialogType.GetField("CertificationUrl", F)!.GetRawConstantValue()! == "https://fanyi-api.baidu.com/manage/developer", "certification uses verified official developer page");
        failOpen = true; Find<Button>(dialog, "baiduOpenCertification").PerformClick(); Assert(status.Text.Contains("无法打开"), "browser failure displayed"); failOpen = false;
        idInput.Clear(); Find<Button>(dialog, "baiduSaveCredentials").PerformClick(); Assert(writes == 0 && status.Text.Contains("APP ID"), "blank credentials rejected locally");
        idInput.Text = "not-a-number"; Find<Button>(dialog, "baiduTestConnection").PerformClick(); Assert(tests == 0, "invalid ID never sends test request");
        idInput.Text = " 12345 "; secretInput.Text = " synthetic-key ";
        Find<Button>(dialog, "baiduSaveCredentials").PerformClick(); Assert(saved == ("12345", "synthetic-key") && writes == 1 && status.Text.Contains("已保存"), "save uses trimmed credentials");
        failSave = true; Find<Button>(dialog, "baiduSaveCredentials").PerformClick(); Assert(writes == 1 && status.Text.Contains("保存失败"), "failed save leaves stored credentials unchanged"); failSave = false;
        pending = new TaskCompletionSource(); Find<Button>(dialog, "baiduTestConnection").PerformClick();
        Assert(tests == 1 && !idInput.Enabled && !Find<Button>(dialog, "baiduClearCredentials").Enabled, "test prevents concurrent credential changes");
        pending.SetResult(); Until(() => Find<Button>(dialog, "baiduTestConnection").Enabled);
        Assert(status.Text.Contains("测试成功") && writes == 1, "test succeeds without silently saving"); pending = null;
        failClear = true; Find<Button>(dialog, "baiduClearCredentials").PerformClick(); Assert(clears == 0 && idInput.Text.Length > 0 && status.Text.Contains("清空失败"), "failed clear preserves visible and stored credentials"); failClear = false;
        Find<Button>(dialog, "baiduClearCredentials").PerformClick(); Assert(clears == 1 && saved == (null, null) && idInput.Text == "" && secretInput.Text == "" && status.Text.Contains("网页"), "clear removes local credentials and selects web fallback");
        Assert((bool)DialogType.GetMethod("ProcessDialogKey", F)!.Invoke(dialog, new object[] { Keys.Escape })! && !dialog.Visible, "Escape closes dialog");

        CancellationToken observed = default;
        using var pendingDialog = (Form)Activator.CreateInstance(DialogType, F, null, new object[] {
            (Func<(string,string)>)(()=>("12345","synthetic-key")), save, clear,
            (Func<string,string,CancellationToken,Task>)((_,_,token)=>{observed=token;return Task.Delay(Timeout.Infinite,token);}), open }, null)!;
        pendingDialog.Show(); Find<Button>(pendingDialog, "baiduTestConnection").PerformClick(); pendingDialog.Close(); Pump();
        Assert(observed.IsCancellationRequested && pendingDialog.IsDisposed, "closing during test cancels request without keeping dialog alive");
    }
    static void MenuChecks()
    {
        using var settings = new ApplicationSettingsForm();
        typeof(ApplicationSettingsForm).GetMethod("PrepareRegistrationDialog", F)!.Invoke(settings, new object[] { "ocr" });
        settings.Show(); Pump();
        var expected = new Dictionary<string, string[]> {
            ["OCR 识别组件Maintenance"] = new[]{"configureBaiduTranslation"},
            ["语音输入Maintenance"] = new[]{"uninstallVoiceInputComponent"},
            ["文本输入Maintenance"] = new[]{"retryXiaomangInputWorker","openXiaomangInputSetupLogFolder","uninstallXiaomangInputMethod"},
            ["外层菜单Maintenance"] = new[]{"removeModernFileMenu"}
        };
        foreach (int width in new[]{600,900})
        {
            settings.ClientSize = new Size(width*settings.DeviceDpi/96,560*settings.DeviceDpi/96);
            var area = Screen.FromControl(settings).WorkingArea; settings.Location=new Point(area.Right-settings.Width,area.Top); Pump();
            foreach(var entry in expected)
            {
                var more=Find<Button>(settings,entry.Key);var menu=more.ContextMenuStrip!;
                var clock=Stopwatch.StartNew();more.PerformClick();Pump();clock.Stop();
                Assert(menu.Visible && menu.Renderer.GetType().DeclaringType!.Name=="RegistrationMaintenanceMenu", "shared maintenance menu style: "+entry.Key);
                Assert(menu.Items.OfType<ToolStripMenuItem>().Select(item=>item.Name).SequenceEqual(entry.Value),"actions preserved in approved order: "+entry.Key);
                Assert(area.Contains(menu.Bounds),"popup remains inside screen at right edge: "+entry.Key);
                foreach(ToolStripMenuItem item in menu.Items.OfType<ToolStripMenuItem>())
                    Assert(TextRenderer.MeasureText(item.Text,menu.Font,Size.Empty,TextFormatFlags.NoPadding).Width+48*menu.DeviceDpi/96<=item.Width,"menu text fits: "+item.Name);
                menu.Items.OfType<ToolStripMenuItem>().First().Select();menu.Refresh();
                Shot(menu,$"menu-{entry.Key.Split('M')[0]}-{menu.DeviceDpi}-{width}");
                Console.WriteLine($"MENU_OPEN_MS={clock.Elapsed.TotalMilliseconds:F1}; MENU={entry.Key}; DPI={menu.DeviceDpi}");
                menu.Close();Pump();more.PerformClick();Pump();Assert(menu.Visible&&!menu.IsDisposed,"menu can reopen without disposed-object error");menu.Close();
            }
        }
        // Exercise the real settings -> API-dialog route, without reading or changing real credential values.
        Exception error = null; bool routePassed=false;
        using var timer=new System.Windows.Forms.Timer{Interval=80};
        timer.Tick+=(_,_)=>
        {
            var dialog=Application.OpenForms.Cast<Form>().SingleOrDefault(form=>form.Name=="baiduTranslationSettings");if(dialog==null)return;
            timer.Stop();
            try
            {
                Assert(!(bool)typeof(ApplicationSettingsForm).GetField("registrationBusy",F)!.GetValue(settings)!,"API configuration does not start component registration");
                Assert(!Find<ProgressBar>(settings,"registrationOverallProgress").Visible,"API configuration shows no installation progress");
                Assert(dialog.Owner==settings && dialog.Visible,"OCR menu opens owned API dialog");routePassed=true;
            }
            catch(Exception failure){error=failure;}
            finally{dialog.Close();}
        };
        var button=Find<Button>(settings,"OCR 识别组件Maintenance");button.PerformClick();Pump();timer.Start();
        button.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single().PerformClick();timer.Stop();
        if(error!=null)throw error;Assert(routePassed,"real API menu click route completed");
        Assert(button.ContextMenuStrip!=null&&!button.ContextMenuStrip.IsDisposed,"menu remains alive after opening API dialog");
        settings.Close();
    }
}
