using System.IO;
using System.Reflection;
using System.Diagnostics;
using ShareX;
class Preview:OCRForm{public Preview():base(new Bitmap(20,20),new OCROptions()){}protected override void OnShown(EventArgs e){}}
static class Check{
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Type Api=typeof(OCRForm).Assembly.GetType("ShareX.BaiduTranslationApi")!;
 static object Call(string n,params object[] a)=>Api.GetMethod(n,F)!.Invoke(null,a)!;
 static void Assert(bool b,string n){if(!b)throw new Exception(n);Console.WriteLine("PASS "+n);}
 static void Wait(Task t){while(!t.IsCompleted){Application.DoEvents();Thread.Sleep(10);}t.GetAwaiter().GetResult();}
 [STAThread]static int Main(string[] args){try{Run(args);return 0;}catch(Exception e){Console.WriteLine("FAIL "+e.GetType().Name+" "+(e is InvalidOperationException ? e.Message : ""));return 1;}}
 static void Run(string[] args){
 Application.EnableVisualStyles();var p=typeof(OCRForm).Assembly.GetType("ShareX.Program")!;p.GetProperty("Settings",F)!.SetValue(null,new ApplicationConfig());p.GetProperty("CustomPersonalPath",F)!.SetValue(null,Path.GetFullPath("build/baidu-api/profile"));
 string path=(string)Api.GetProperty("CredentialPath",F)!.GetValue(null)!;
 if(args.Contains("--live")){
 var credentials=Call("Load");var watch=Stopwatch.StartNew();var task=(Task<string>)Call("TranslateAsync",credentials,"Hello","zh",CancellationToken.None);Wait(task);Assert(!string.IsNullOrWhiteSpace(task.Result),"authorized short API request");Console.WriteLine("API_MS="+watch.ElapsedMilliseconds);
 using var form=new Preview();form.Show();var editor=(TextBox)typeof(OCRForm).GetField("txtResult",F)!.GetValue(form)!;editor.Text="Hello";
 Wait((Task)typeof(OCRForm).GetMethod("ShowTranslationAsync",F)!.Invoke(form,null)!);
 var result=(TextBox)typeof(OCRForm).GetField("apiResult",F)!.GetValue(form)!;Assert(!string.IsNullOrWhiteSpace(result.Text),"API result in OCR window");Assert(typeof(OCRForm).GetField("translationBrowser",F)!.GetValue(form)==null,"API does not initialize browser");
 Wait((Task)typeof(OCRForm).GetMethod("CopyTranslationAsync",F)!.Invoke(form,null)!);Assert(Clipboard.GetText()==result.Text,"API clipboard matches");
 editor.Text="New source";Assert(result.Text=="","source change clears old API result");
var pending=(Task)typeof(OCRForm).GetMethod("ShowTranslationAsync",F)!.Invoke(form,null)!;editor.Text="Changed immediately";Wait(pending);Assert(result.Text=="","cancelled request cannot overwrite new source");
foreach(int width in new[]{760,1180}){form.Width=width;Application.DoEvents();Assert(result.Width>150,"API result fits "+width);}
using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.GetFullPath("build/baidu-api/api-window.png"));}
Wait((Task)typeof(OCRForm).GetMethod("ShowWebTranslationAsync",F)!.Invoke(form,null)!);Assert(!(bool)typeof(OCRForm).GetField("apiMode",F)!.GetValue(form)!,"manual web fallback");return;
 }
 Assert(!File.Exists(path),"fresh local credentials absent");
 Assert(Call("Load")==null,"unconfigured defaults to web");
 try{
 Call("Save","12345","synthetic-private-test");Assert(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("synthetic-private-test"),"DPAPI ciphertext");Assert(Call("Load")!=null,"credential roundtrip");
 File.WriteAllBytes(path,new byte[]{1,2,3});bool rejected=false;try{Call("Load");}catch{rejected=true;}Assert(rejected,"corrupt credentials rejected");
 }finally{Call("Clear");}
 Assert((string)Call("Parse","{\"trans_result\":[{\"dst\":\"你好\"},{\"dst\":\"世界\"}]}")=="你好"+Environment.NewLine+"世界","ordered multiline response");
 foreach(string code in new[]{"54001","54003","54004","58002"}){bool rejected=false;try{Call("Parse","{\"error_code\":\""+code+"\"}");}catch{rejected=true;}Assert(rejected,"service error "+code);}
 using var dialog=(Form)Activator.CreateInstance(typeof(OCRForm).Assembly.GetType("ShareX.BaiduTranslationSettingsForm")!,true)!;dialog.Show();Application.DoEvents();Assert(dialog.Controls.Count>0,"configuration dialog");dialog.Close();
 Console.WriteLine("PASS offline checks");
 }
}

