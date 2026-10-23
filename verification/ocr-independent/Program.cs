using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ShareX;
using ShareX.HelpersLib;
class Preview:OCRForm { public Preview():base(new Bitmap(20,20),new OCROptions()){} protected override void OnShown(EventArgs e){} }
static class Check {
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr h);
 static object Get(object o,string n)=>typeof(OCRForm).GetField(n,F)!.GetValue(o)!;
 static void Set(object o,string n,object v)=>typeof(OCRForm).GetField(n,F)!.SetValue(o,v);
 static object Call(object o,string n,params object[] args)=>typeof(OCRForm).GetMethod(n,F)!.Invoke(o,args)!;
 static void Assert(bool b,string m){if(!b)throw new Exception(m);Console.WriteLine("PASS "+m);}
 static void Wait(Task t){while(!t.IsCompleted){Application.DoEvents();Thread.Sleep(10);}t.GetAwaiter().GetResult();}
 [STAThread]static int Main(){try{Run();return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
 static void Run(){
 Application.SetHighDpiMode(Environment.GetCommandLineArgs().Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
 var p=typeof(ApplicationConfig).Assembly.GetType("ShareX.Program")!;var cfg=new ApplicationConfig{ShowTray=false,DisableHotkeys=true};
 foreach(var pair in new (string,object)[]{("Settings",cfg),("DefaultTaskSettings",cfg.DefaultTaskSettings),("HotkeysConfig",new HotkeysConfig()),("UploadersConfig",new ShareX.UploadersLib.UploadersConfig()),("Sandbox",true),("CustomPersonalPath",Path.GetFullPath("build/ocr-independent/personal"))})p.GetProperty(pair.Item1,F)!.SetValue(null,pair.Item2);
 using var main=new MainForm();p.GetProperty("MainForm",F)!.SetValue(null,main);
 main.HandleCreated-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),main,typeof(MainForm).GetMethod("MainForm_HandleCreated",F)!);
 main.Show();Application.DoEvents();
 using var form=new Preview();Set(form,"busy",false);Set(form,"loaded",true);((Bitmap)Get(form,"bmpSource")).Dispose();Set(form,"bmpSource",null!);
 var cb=(ComboBox)Get(form,"cbLanguages");cb.Items.Add(new OCRLanguage("中文","zh-CN"));cb.Items.Add(new OCRLanguage("English","en-US"));cb.SelectedIndex=0;
 Wait((Task)Call(form,"ToggleLanguageAsync"));Assert(form.Options.Language=="zh-CN","mixed to CN");Wait((Task)Call(form,"ToggleLanguageAsync"));Assert(form.Options.Language=="en-US","CN to EN");Wait((Task)Call(form,"ToggleLanguageAsync"));Assert((bool)Get(form,"mixedSelected"),"EN to mixed");Wait((Task)Call(form,"ToggleLanguageAsync"));Assert(form.Options.Language=="zh-CN" && !(bool)Get(form,"mixedSelected"),"mixed to CN again");
 var editor=(TextBox)Get(form,"txtResult");editor.Text="Retained original";Call(form,"UpdateControls");
 string savePath=Path.GetFullPath("build/ocr-independent/closed.png");
 typeof(TaskHelpers).GetMethod("ShowIndependentOcr",F)!.Invoke(null,new object[]{form,savePath});Application.DoEvents();
 Assert(!form.Modal&&IsWindowEnabled(main.Handle),"independent OCR leaves main enabled");
 form.WindowState=FormWindowState.Minimized;Application.DoEvents();Assert(IsWindowEnabled(main.Handle),"minimized OCR leaves main enabled");
 var tray=(ContextMenuStrip)typeof(MainForm).GetField("cmsTray",F)!.GetValue(main)!;tray.Show(main,new Point(30,30));Application.DoEvents();Assert(tray.Visible&&IsWindowEnabled(tray.Handle),"tray menu opens while OCR minimized");tray.Close();using(var widget=(Form)Activator.CreateInstance(typeof(MainForm).Assembly.GetType("ShareX.TodoWidgetForm")!,new object[]{null!})!){widget.Show();Application.DoEvents();Assert(widget.Visible&&IsWindowEnabled(widget.Handle),"todo widget enabled while OCR minimized");widget.Close();} ((Button)Get(form,"registrationButton")).PerformClick();form.WindowState=FormWindowState.Normal;Application.DoEvents();
 ((Button)Get(form,"btnCopyAll")).PerformClick();Assert(Clipboard.GetText()==editor.Text,"copy original actual clipboard");Assert(((Label)Get(form,"translationStatus")).Text=="已复制原文","copy original feedback");
 Call(form,"EnsureApiPanel");Set(form,"apiMode",true);Call(form,"UpdateApiHeader");
 var header=(FlowLayoutPanel)Get(form,"apiHeader");Assert(header.Controls.Cast<Control>().Select(x=>x.Text).SequenceEqual(new[]{"百度 API 译文","中文","EN","改用网页翻译"}),"API header order");
 foreach(var b in new[]{(Button)Get(form,"btnCopyAll"),(Button)Get(form,"btnOpenServiceLink"),(Button)Get(form,"copyTranslationButton")}){
 using var before=new Bitmap(b.Width,b.Height);b.DrawToBitmap(before,b.ClientRectangle);
 typeof(Control).GetMethod("OnMouseEnter",F)!.Invoke(b,new object[]{EventArgs.Empty});using var hover=new Bitmap(b.Width,b.Height);b.DrawToBitmap(hover,b.ClientRectangle);Assert(before.GetPixel(3,3)!=hover.GetPixel(3,3),"hover changes button");
 typeof(Control).GetMethod("OnMouseDown",F)!.Invoke(b,new object[]{new MouseEventArgs(MouseButtons.Left,1,4,4,0)});using var down=new Bitmap(b.Width,b.Height);b.DrawToBitmap(down,b.ClientRectangle);Assert(down.GetPixel(3,3)!=hover.GetPixel(3,3),"pressed changes button");typeof(Control).GetMethod("OnMouseLeave",F)!.Invoke(b,new object[]{EventArgs.Empty});
 }
 Assert(editor.Text=="Retained original"&&!form.Modal,"original retained in modeless OCR");
 var settings=(Form)typeof(MainForm).GetField("cachedApplicationSettingsForm",F)!.GetValue(main)!;
 Assert(!settings.TopLevel&&settings.Parent!=null&&IsWindowEnabled(main.Handle),"registration is embedded in operable main window");
 foreach(int width in new[]{760,1180}){form.Width=width;Application.DoEvents();foreach(string name in new[]{"btnOpenServiceLink","btnCopyAll","copyTranslationButton"}){var b=(Button)Get(form,name);Assert(b.Image!=null&&b.Visible,"icon present "+name);Assert(b.ImageAlign==ContentAlignment.MiddleCenter&&b.TextAlign==ContentAlignment.MiddleCenter,"centered "+name);var r=form.RectangleToClient(b.RectangleToScreen(b.ClientRectangle));Assert(r.Right<=form.ClientSize.Width,"button fits "+width);} }
 Console.WriteLine("DPI="+form.DeviceDpi);using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.GetFullPath("build/ocr-independent/ui-"+form.DeviceDpi+".png"));} Wait((Task)Call(form,"RefreshRegistrationAsync"));Assert(((Button)Get(form,"registrationButton")).Visible,"missing or invalid component keeps entry");
 string source=Path.GetFullPath("tools/ShareX/app/ThirdParty/MixedOCR/v1"),dest=Path.Combine(AppContext.BaseDirectory,"ThirdParty/MixedOCR/v1");Directory.CreateDirectory(dest);foreach(var f in Directory.GetFiles(source))File.Copy(f,Path.Combine(dest,Path.GetFileName(f)),true);
 Wait((Task)Call(form,"RefreshRegistrationAsync"));Assert(!((Button)Get(form,"registrationButton")).Visible,"valid component hides entry");
 File.WriteAllText(Path.Combine(dest,"keys.txt"),"corrupt test fixture");Wait((Task)Call(form,"RefreshRegistrationAsync"));Assert(((Button)Get(form,"registrationButton")).Visible,"corrupt component restores entry");
 cfg.OcrWindowBounds=form.Bounds;form.Close();Assert(File.ReadAllText(Path.ChangeExtension(savePath,".txt"))=="Retained original","close saves OCR text");main.Hide();Console.WriteLine("PASS all OCR actions");
 }
}

