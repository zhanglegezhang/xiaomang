#nullable enable
using System.IO;
using System.Reflection;
using System.Diagnostics;
using Newtonsoft.Json;
using ShareX;

class Preview : OCRForm
{
    public Preview(OCROptions options) : base(new Bitmap(20,20), options) { }
    protected override void OnShown(EventArgs e) { }
}
static class Check
{
    const BindingFlags F = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static int checks;
    static Type Api = typeof(OCRForm).Assembly.GetType("ShareX.BaiduTranslationApi")!;
    static object Call(Type t, string name, object? instance, params object?[] args)
    {
        // This harness pumps messages without Application.Run; retain the UI context at async entry.
        if (instance is OCRForm) SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        return t.GetMethod(name,F)!.Invoke(instance,args)!;
    }
    static T Field<T>(object form,string name) => (T)typeof(OCRForm).GetField(name,F)!.GetValue(form)!;
    static void Assert(bool value,string label) { if(!value)throw new Exception(label); checks++; Console.WriteLine("PASS "+label); }
    static void Wait(Task task) { var timer=Stopwatch.StartNew(); while(!task.IsCompleted){if(timer.Elapsed.TotalSeconds>30)throw new Exception("test timeout");Application.DoEvents();Thread.Sleep(10);}task.GetAwaiter().GetResult(); }
    static string ReadClipboard() { for(int i=0;;i++){try{return Clipboard.GetText();}catch(System.Runtime.InteropServices.ExternalException) when(i<20){Thread.Sleep(50);}} }
    static IEnumerable<Control> All(Control root) => new[]{root}.Concat(root.Controls.Cast<Control>().SelectMany(All));
    [STAThread] static int Main(string[] args)
    {
        try { Run(args); Console.WriteLine($"PASS {checks} checks"); return 0; }
        catch(Exception e) { Console.WriteLine("FAIL "+(e.InnerException??e).Message); return 1; }
    }
    static void Run(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var program=typeof(OCRForm).Assembly.GetType("ShareX.Program")!;
        program.GetProperty("Settings",F)!.SetValue(null,new ApplicationConfig());
        program.GetProperty("CustomPersonalPath",F)!.SetValue(null,Path.GetFullPath("build/translation-copy-20261027/test-profile"));
        if(args.Contains("--ui"))
        {
            var cfg=new ApplicationConfig{ShowTray=false,DisableHotkeys=true}; cfg.DefaultTaskSettings.CaptureSettings.OCROptions.AutoCopy=true;
            foreach(var pair in new (string,object)[]{("Settings",cfg),("Sandbox",true),("DefaultTaskSettings",cfg.DefaultTaskSettings),
                ("HotkeysConfig",new HotkeysConfig()),("UploadersConfig",new ShareX.UploadersLib.UploadersConfig())})
                program.GetProperty(pair.Item1,F)!.SetValue(null,pair.Item2);
            using var main=new ApplicationSettingsForm();
            var tabs=(TabControl)typeof(ApplicationSettingsForm).GetField("tcSettings",F)!.GetValue(main)!;
            var page=tabs.TabPages.Cast<TabPage>().Single(p=>p.Name=="tpOCR");
            Assert((bool)Call(typeof(ApplicationSettingsForm),"EnsureSettingsPage",main,page),"main OCR page loads");
            if(args.Contains("--interactive"))
            {
                main.Text="OCR radio verification";
                main.Show(); tabs.SelectedTab=page;
                main.ClientSize=new Size(1200,800);
                Application.Run(main);
                return;
            }
            // Render the actual designed page independently of sidebar navigation.
            tabs.Controls.Remove(page);using var host=new Form{ClientSize=new Size(850,600)};
            var view=new TabControl{Dock=DockStyle.Fill};view.TabPages.Add(page);host.Controls.Add(view);host.Show();Application.DoEvents();
            var radios=All(page).OfType<RadioButton>().ToArray();
            Assert(radios.Length==3,"main page includes three choices");
            foreach(var size in new[]{new Size(850,600),new Size(600,600)})
            {
                host.ClientSize=size;Application.DoEvents();
                Assert(radios.All(r=>r.Visible&&r.Bounds.Right<=r.Parent!.ClientSize.Width),"choices not clipped "+size.Width);
                using var bitmap=new Bitmap(host.Width,host.Height);host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));
                bitmap.Save("build/translation-copy-20261027/ui-"+size.Width+".png");
            }
            Assert(All(page).Any(c=>c.Text.Contains("仅支持 API 自动复制")),"API scope is visible");
            return;
        }
        var composer=typeof(OCRForm).Assembly.GetType("ShareX.TranslationClipboardContent")!;
        object Parse(string json)=>Call(Api,"ParseResult",null,json);
        string? Compose(string original,object result,TranslationCopyContent content)=>(string?)Call(composer,"Compose",null,original,result,content);
        foreach(bool toEnglish in new[]{true,false})
        {
            string original=toEnglish?"中文 123，API！":"English 123, API!";
            string translation=toEnglish?"English 123, API!":"中文 123，API！";
            var parsed=Parse(JsonConvert.SerializeObject(new{from=toEnglish?"zh":"en",to=toEnglish?"en":"zh",trans_result=new[]{new{dst=translation}}}));
            foreach(var choice in Enum.GetValues<TranslationCopyContent>())
            {
                string expected=choice==TranslationCopyContent.Chinese?"中文 123，API！":choice==TranslationCopyContent.English?"English 123, API!":"中文 123，API！"+Environment.NewLine+Environment.NewLine+"English 123, API!";
                Assert(Compose(original,parsed,choice)==expected,$"language direction {toEnglish} / {choice}");
            }
        }
        var missing=Parse("{\"trans_result\":[{\"dst\":\"你好\"}]}");
        Assert(Compose("Hello",missing,TranslationCopyContent.ChineseAndEnglish)==null,"missing metadata does not guess language");
        var french=Parse("{\"from\":\"fra\",\"to\":\"en\",\"trans_result\":[{\"dst\":\"Hello\"}]}");
        Assert(Compose("Bonjour",french,TranslationCopyContent.ChineseAndEnglish)==null,"missing Chinese does not mislabel original");
        Assert(Compose("Bonjour",french,TranslationCopyContent.English)=="Hello","available target is usable");
        Assert(Compose(" ",french,TranslationCopyContent.English)==null,"empty original protected");
        foreach(var code in new[]{"54001","54003","58003","99999"})
        {bool failed=false;try{Parse("{\"error_code\":\""+code+"\"}");}catch(TargetInvocationException){failed=true;}Assert(failed,"API error does not become result "+code);}
        foreach(bool enabled in new[]{true,false})
        {
            var old=JsonConvert.DeserializeObject<OCROptions>("{\"AutoCopy\":"+enabled.ToString().ToLowerInvariant()+"}")!;
            Assert(old.AutoCopy==enabled&&old.TranslationCopyContent==TranslationCopyContent.ChineseAndEnglish,"old switch and new bilingual default "+enabled);
        }
        foreach(var choice in Enum.GetValues<TranslationCopyContent>())
        {
            var opts=new OCROptions{AutoCopy=true,TranslationCopyContent=choice};
            var loaded=JsonConvert.DeserializeObject<OCROptions>(JsonConvert.SerializeObject(opts))!;
            Assert(loaded.TranslationCopyContent==choice&&loaded.AutoCopy,"configuration roundtrip "+choice);
        }
        var settings=new TaskSettings();settings.CaptureSettings.OCROptions.AutoCopy=true;
        using(var form=new TaskSettingsForm(settings,true,true))
        {
            var page=(TabPage)Call(typeof(TaskSettingsForm),"LoadDefaultSettingsPage",form,"tpOCR",96);
            var choices=All(page).OfType<FlowLayoutPanel>().Single(c=>c.Name=="translationCopyChoices");
            var radios=choices.Controls.OfType<RadioButton>().ToArray();
            Assert(radios.Length==3&&radios[2].Checked,"three exclusive choices, bilingual selected");
            foreach(int i in new[]{0,1,2}){radios[i].Checked=true;Assert(radios.Count(r=>r.Checked)==1&&(int)settings.CaptureSettings.OCROptions.TranslationCopyContent==i,"choice writes setting "+i);}
            var toggle=All(page).OfType<CheckBox>().Single(c=>c.Name=="cbCaptureOCRAutoCopy");
            toggle.Checked=false;Assert(!choices.Enabled,"disabled when auto-copy off");toggle.Checked=true;
            var silent=All(page).OfType<CheckBox>().Single(c=>c.Name=="cbCaptureOCRSilent");
            silent.Checked=true;Assert(!choices.Enabled&&!toggle.Enabled,"silent OCR disables translation choices");silent.Checked=false;
            Assert(choices.Enabled&&toggle.Enabled,"settings reenabled after silent OCR");
        }
        if(!args.Contains("--live"))return;
        // Never read, alter or log the user's API credential file; production Load is used by the form.
        IDataObject? oldClipboard=Clipboard.GetDataObject();string? lastWritten=null;
        try
        {
            var options=new OCROptions{AutoCopy=true};using var form=new Preview(options);form.Show();
            var editor=Field<TextBox>(form,"txtResult");
            foreach(bool toEnglish in new[]{true,false})foreach(var choice in Enum.GetValues<TranslationCopyContent>())
            {
                options.TranslationCopyContent=choice;editor.Text=toEnglish?"你好，世界！":"Hello, world!";
                typeof(OCRForm).GetField("apiTarget",F)!.SetValue(form,toEnglish?"en":"zh");
                var timer=Stopwatch.StartNew();Wait((Task)Call(typeof(OCRForm),"ShowTranslationAsync",form));
                string translated=Field<TextBox>(form,"apiResult").Text;
                Assert(!string.IsNullOrWhiteSpace(translated),"live translation returned");
                string chinese=toEnglish?editor.Text:translated,english=toEnglish?translated:editor.Text;
                string expected=choice==TranslationCopyContent.Chinese?chinese:choice==TranslationCopyContent.English?english:chinese+Environment.NewLine+Environment.NewLine+english;
                Thread.Sleep(250);lastWritten=ReadClipboard();
                if(lastWritten!=expected)Console.WriteLine("DETAIL "+JsonConvert.SerializeObject(new { expected,actual=lastWritten,feedback=Field<Label>(form,"apiFeedback").Text }));
                Assert(lastWritten==expected,$"live clipboard {toEnglish}/{choice}");
                Console.WriteLine("REQUEST_MS="+timer.ElapsedMilliseconds);
            }
            options.AutoCopy=false;editor.Text="Hello";Wait((Task)Call(typeof(OCRForm),"ShowTranslationAsync",form));
            Assert(ReadClipboard()==lastWritten,"off leaves clipboard untouched");
            options.AutoCopy=true;editor.Text="Cancellation test";
            var pending=(Task)Call(typeof(OCRForm),"ShowTranslationAsync",form);editor.Text="Replacement text";Wait(pending);
            Assert(ReadClipboard()==lastWritten&&Field<TextBox>(form,"apiResult").Text=="","stale request cannot copy or display");
            options.Silent=true;editor.Text="Hello";Wait((Task)Call(typeof(OCRForm),"ShowTranslationAsync",form));
            Assert(ReadClipboard()==lastWritten,"silent setting leaves API clipboard untouched");
            options.Silent=false;Wait((Task)Call(typeof(OCRForm),"CopyTranslationAsync",form));lastWritten=ReadClipboard();
            Assert(lastWritten==Field<TextBox>(form,"apiResult").Text,"manual translation copy remains translation only");
            Assert(typeof(OCRForm).GetField("translationBrowser",F)!.GetValue(form)==null,"API does not initialize web browser");
        }
        finally { if(lastWritten!=null&&ReadClipboard()==lastWritten){if(oldClipboard!=null)Clipboard.SetDataObject(oldClipboard,true);else Clipboard.Clear();} }
    }
}
