using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Globalization;
using System.Runtime.Loader;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using ShareX;
using ShareX.HelpersLib;
using ShareX.HistoryLib;
using ShareX.UploadersLib;
using ShareX.ImageEffectsLib;
using Application=System.Windows.Forms.Application;
public class Settings { public int Count {get;set;}=7; public string Text {get;set;}="原设置"; }
public class Broken { public string Value {get=>throw new IOException("隔离导出错误");set{}} }
class Test {
 static string root=Path.GetFullPath("test-results/popup-fixes");
 static BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static int checks,dialogs;static string current="";static List<string> lines=new();static Exception? timerError;
 static object? Call(object o,string n,params object?[] a)=>o.GetType().GetMethod(n,flags)!.Invoke(o,a);
 static Control Field(object o,string n)=>(Control)o.GetType().GetField(n,flags)!.GetValue(o)!;
 static void Check(bool v,string m){if(!v)throw new Exception(m);checks++;lines.Add("PASS "+m);}
 [STAThread]static void Main(){Directory.CreateDirectory(root);AssemblyLoadContext.Default.Resolving+=(_,n)=>{
  foreach(var d in new[]{Path.GetFullPath("artifacts/Desktop"),Path.GetFullPath("artifacts/Desktop")}){var p=Path.Combine(d,n.Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
  CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo("zh-CN");Thread.CurrentThread.CurrentUICulture=CultureInfo.GetCultureInfo("zh-CN");
  Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();try{Run();File.WriteAllLines(Path.Combine(root,"regression.txt"),lines.Append($"PASS {checks} assertions, {dialogs} actual handled dialogs"));}catch(Exception e){File.WriteAllText(Path.Combine(root,"regression-failed.txt"),e.ToString());Environment.ExitCode=1;}}
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] static void Run(){
  using var host=new Form{Text="异常修复回归（隔离）",Width=400,Height=100};
  using var timer=new System.Windows.Forms.Timer{Interval=100};
  timer.Tick+=(_,_)=>{foreach(var f in Application.OpenForms.Cast<Form>().OfType<ErrorForm>().ToArray()){
   try{Check(f.Text=="小蟒 - 错误",current+" title");var details=(TextBox)Field(f,"txtException");Check(details.Multiline&&details.ScrollBars!=ScrollBars.None,current+" scrollable details");Check(f.Bounds.Height<=Screen.FromControl(f).WorkingArea.Height,current+" fits screen");Check(Field(f,"btnOK").Visible,current+" OK visible");
    using var bmp=new Bitmap(f.Width,f.Height);f.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(root,current+".png"));
    Check(!Field(f,"lblErrorMessage").Text.Contains("ShareX"),current+" summary brand");dialogs++;
   }catch(Exception e){timerError=e;}finally{f.Close();}
  }};
  void Case(string name,Action action,int count=1){current=name;int before=dialogs;action();if(timerError!=null)throw timerError;Check(dialogs-before==count,name+" dialog count");}
  host.Shown+=(_,_)=>host.BeginInvoke(new Action(()=>{try{
   timer.Start();string p=Path.Combine(root,"fixtures");Directory.CreateDirectory(p);
   Case("01-save-directory",()=>{using var d=new ImageData{ImageStream=new MemoryStream(new byte[]{1,2,3})};Check(!d.Write(p),"save failed false");});
   Case("02-create-directory",()=>{var f=Path.Combine(p,"occupied");File.WriteAllText(f,"original");FileHelpers.CreateDirectory(f);Check(File.ReadAllText(f)=="original","create keeps original");});
   Case("03-rename",()=>{var a=Path.Combine(p,"a");var b=Path.Combine(p,"b");File.WriteAllText(a,"a");File.WriteAllText(b,"b");FileHelpers.RenameFile(a,"b");Check(File.ReadAllText(a)=="a"&&File.ReadAllText(b)=="b","rename preserves both files");});
   foreach(var ext in new[]{"json","xml"})Case("04-history-"+ext,()=>{var f=Path.Combine(p,"history."+ext);var s=ext=="json"?"{broken":"<HistoryItem><broken>";File.WriteAllText(f,s);HistoryManager m=ext=="json"?new HistoryManagerJSON(f):new HistoryManagerXML(f);Check(m.GetHistoryItems().Count==0,"history returns safely "+ext);Check(File.ReadAllText(f)==s,"history source unchanged "+ext);});
   Case("05-export",()=>{using var c=new ExportImportControl();Check(Call(c,"Serialize",new Broken())==null,"writable getter exception fails export");});
   using(var c=new ExportImportControl{ObjectType=typeof(Settings)}){
    int imported=0,completed=0;Settings value=new();c.ImportRequested+=o=>{value=(Settings)o;imported++;};c.ImportCompleted+=()=>completed++;
    int i=0;foreach(var s in new[]{"{broken","null","{\"Count\":\"bad\"}","{\"Count\":2} {\"Count\":3}","[1,2]"," "})Case("06-import-"+(++i),()=>Call(c,"ImportJson",s));
    Check(imported==0&&completed==0&&value.Count==7,"invalid input never applies or completes");
    Case("07-valid-import",()=>Call(c,"ImportJson","{\"Count\":3,\"UnknownFutureField\":true}"),0);
    Check(imported==1&&completed==1&&value.Count==3,"valid and unknown-field compatibility");
    Case("08-import-missing-file",()=>Check((bool)Call(c,"ImportFile",Path.Combine(p,"missing.json"))! == false,"missing file false"));
   }
   using(var c=new ExportImportControl{ObjectType=typeof(Settings)}){int completed=0;c.ImportRequested+=_=>throw new IOException("应用失败");c.ImportCompleted+=()=>completed++;Case("09-apply-failure",()=>Call(c,"ImportJson","{}"));Check(completed==0,"apply failure no completed event");}
   foreach(bool xml in new[]{false,true})Case(xml?"10-custom-xml":"10-custom-json",()=>{using var f=new CustomUploaderSettingsForm(new UploadersConfig());var box=Field(f,"rtbData");box.Text=xml?"<broken>":"{broken";string original=box.Text;if(xml)Call(f,"CustomUploaderFormatXMLData");else Call(f,"CustomUploaderFormatJsonData",Newtonsoft.Json.Formatting.Indented);Check(box.Text==original,"formatter preserves original "+xml);});
   Case("10-custom-export",()=>{using var f=new CustomUploaderSettingsForm(new UploadersConfig());var item=CustomUploaderItem.Init();item.Name="test";Directory.CreateDirectory(Path.Combine(p,item.GetFileName()));Call(f,"CustomUploaderSerialize",item,p);});
   Case("11-generic",()=>new IOException("D:\\ShareX\\测试文件.txt").ShowError());
   Case("12-image-helper-save",()=>{using var image=new Bitmap(8,8);Check(!ImageHelpers.SaveImage(image,p),"image helper directory target fails");});
   var effectsType=Assembly.LoadFrom(Path.GetFullPath("artifacts/Desktop/ShareX.ImageEffectsLib.dll")).GetType("ShareX.ImageEffectsLib.ImageEffectsForm")!;
   foreach(var json in new[]{"{broken", "", "null", "{}", "{\"Effects\":[null]}", "{\"Effects\":[{\"$type\":\"UnknownEffect\"}]}", "{\"Effects\":[{\"$type\":\"Canvas\",\"Enabled\":\"bad\"}]}", "{\"Effects\":[{\"$type\":\"Canvas\"}]} {}"})
    Case("13-image-effect-"+checks,()=>{using var form=(ImageEffectsForm)Activator.CreateInstance(effectsType,new object?[]{null,new List<ImageEffectPreset>(),0})!;int before=form.Presets.Count;Check(!form.ImportImageEffect(json),"invalid effect returns false");Check(form.Presets.Count==before,"invalid effect preserves presets");});
   HelpersOptions.ShareXSpecialFolders["ShareXImageEffects"]=Path.Combine(p,"effect-ui-assets");
   using(var form=new ImageEffectsForm(null,new List<ImageEffectPreset>(),0)){
    int before=form.Presets.Count;
    Case("14-valid-effect",()=>Check(form.ImportImageEffect("{\"Name\":\"有效配置\",\"Effects\":[{\"$type\":\"Canvas\"}]}"),"valid effect returns true"),0);
    Check(form.Presets.Count==before+1,"valid effect adds one preset");
    string badPackage=Path.Combine(p,"broken-effect.sxie");File.WriteAllText(badPackage,"not zip");
    Case("15-broken-effect-package",()=>form.ImportImageEffectFile(badPackage));
    Check(form.Presets.Count==before+1,"broken package preserves presets");
   }
   var media=Assembly.LoadFrom(Path.GetFullPath("artifacts/Desktop/ShareX.MediaLib.dll"));
   Case("16-split-corrupt-image",()=>{using var f=(Form)Activator.CreateInstance(media.GetType("ShareX.MediaLib.ImageSplitterForm")!)!;string corrupt=Path.Combine(p,"corrupt.png");File.WriteAllText(corrupt,"not image");Field(f,"txtImageFilePath").Text=corrupt;Field(f,"txtOutputFolder").Text=p;((NumericUpDown)Field(f,"nudRowCount")).Value=2;Call(f,"BtnSplitImage_Click",null,EventArgs.Empty);var until=DateTime.UtcNow.AddSeconds(10);while((bool)f.GetType().GetProperty("IsBusy",flags)!.GetValue(f)!&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}Check(!(bool)f.GetType().GetProperty("IsBusy",flags)!.GetValue(f)!,"split failure clears busy");},0);lines.Add("OBS: corrupt image split returns silently; no error dialog.");
   Case("17-effect-package-write-failure",()=>{using var f=new ImageEffectPackagerForm("{}","test",p);Field(f,"txtAssetsFolderPath").Text="";Field(f,"txtPackageFilePath").Text=p;Call(f,"btnPackage_Click",null,EventArgs.Empty);});
   Case("16b-split-output-failure",()=>{using var f=(Form)Activator.CreateInstance(media.GetType("ShareX.MediaLib.ImageSplitterForm")!)!;string validImage=Path.Combine(p,"split-valid.png");using(var bmp=new Bitmap(8,8))bmp.Save(validImage,System.Drawing.Imaging.ImageFormat.Png);Directory.CreateDirectory(Path.Combine(p,"split-valid1.png"));Field(f,"txtImageFilePath").Text=validImage;Field(f,"txtOutputFolder").Text=p;((NumericUpDown)Field(f,"nudRowCount")).Value=2;Call(f,"BtnSplitImage_Click",null,EventArgs.Empty);var until=DateTime.UtcNow.AddSeconds(10);while((bool)f.GetType().GetProperty("IsBusy",flags)!.GetValue(f)!&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}Check(!(bool)f.GetType().GetProperty("IsBusy",flags)!.GetValue(f)!,"split write failure clears busy");});
   EffectPackages(p);
   var method=typeof(ImageData).GetMethod("GetWriteErrorSummary",flags)!;
   foreach(var pair in new[]{(new UnauthorizedAccessException() as Exception,"权限"),(new DirectoryNotFoundException(),"不存在"),(new IOException("locked",unchecked((int)0x80070020)),"占用"),(new IOException("other"),"磁盘")})Check(((string)method.Invoke(null,new object[]{pair.Item1,Path.Combine(p,"not-present")})!).Contains(pair.Item2),"save classification "+pair.Item2);
   timer.Stop();
   foreach(float scale in new[]{1.5f,2f}){
    using var form=new ErrorForm("历史记录读取失败。请保留原文件，以便检查或恢复。",new string('测',20000),null,null,false);
    form.Show();Application.DoEvents();float factor=scale*96f/form.DeviceDpi;form.Scale(new SizeF(factor,factor));form.PerformLayout();Application.DoEvents();
    var detail=Field(form,"txtException");var button=Field(form,"btnOK");
    Check(detail.Height>100&&detail.Bottom<=form.ClientSize.Height,"DPI "+scale+" details bounded");
    Check(button.Parent!.Bottom<=form.ClientSize.Height,"DPI "+scale+" buttons bounded");
    Check(Field(form,"lblErrorMessage").Bottom<=detail.Top,"DPI "+scale+" summary no overlap");
    using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(root,"layout-"+scale+".png"));
   }
   var watch=Stopwatch.StartNew();using(var c=new ExportImportControl{ObjectType=typeof(Settings)}){c.ImportRequested+=_=>{};for(int j=0;j<1000;j++)Call(c,"ImportJson","{\"Count\":2}");}watch.Stop();lines.Add("1000 valid imports ms="+watch.Elapsed.TotalMilliseconds.ToString("F2"));
  }catch(Exception e){timerError=e;}finally{timer.Stop();host.Close();}}));Application.Run(host);if(timerError!=null)throw timerError;
 }
 static void EffectPackages(string fixture){
  string p=Path.Combine(fixture,"effects-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);string dest=Path.Combine(p,"assets");Directory.CreateDirectory(dest);
  string valid="{\"Name\":\"测试\",\"UnknownFutureField\":true,\"Effects\":[{\"$type\":\"Canvas\",\"Enabled\":true}]}";
  var parse=typeof(ImageEffectPreset).Assembly.GetType("ShareX.ImageEffectsLib.ImageEffectImport")!.GetMethod("Parse",flags)!;
  Check(((ImageEffectPreset)parse.Invoke(null,new object[]{valid})!).Effects.Count==1,"valid effect and unknown-property compatibility");
  string Zip(params (string name,string text)[] entries){string path=Path.Combine(p,Guid.NewGuid()+".sxie");using var zip=ZipFile.Open(path,ZipArchiveMode.Create);foreach(var e in entries){using var writer=new StreamWriter(zip.CreateEntry(e.name).Open());writer.Write(e.text);}return path;}
  void Reject(string path){bool rejected=false;try{ImageEffectPackager.ExtractPackage(path,dest);}catch{rejected=true;}Check(rejected,"reject invalid package");}
  string old=Path.Combine(dest,"old.png");File.WriteAllText(old,"ORIGINAL");
  Reject(Zip(("old.png","REPLACE"),("Config.json","{broken")));Check(File.ReadAllText(old)=="ORIGINAL","invalid config cannot overwrite assets");
  Reject(Zip(("old.png","REPLACE")));Check(File.ReadAllText(old)=="ORIGINAL","missing config cannot overwrite assets");
  Reject(Zip(("Config.json",valid),("config.json",valid)));Reject(Zip(("Config.json",valid),("../outside.png","bad")));Check(!File.Exists(Path.Combine(p,"outside.png")),"traversal prevented");
  Reject(Zip(("Config.json",valid),("old.png","a"),("OLD.PNG","b")));Check(File.ReadAllText(old)=="ORIGINAL","duplicate asset rejected before write");
  Directory.CreateDirectory(Path.Combine(dest,"blocked.png"));Reject(Zip(("Config.json",valid),("old.png","NEW"),("blocked.png","fail")));Check(File.ReadAllText(old)=="ORIGINAL","later write failure rolls earlier asset back");
  string ok=Zip(("Config.json",valid),("old.png","NEW"),("sub/new.png","NEW2"));var watch=Stopwatch.StartNew();string result=ImageEffectPackager.ExtractPackage(ok,dest);watch.Stop();Check(result==valid&&File.ReadAllText(old)=="NEW"&&File.ReadAllText(Path.Combine(dest,"sub/new.png"))=="NEW2","valid package commits assets");lines.Add("small package validate-stage-commit ms="+watch.Elapsed.TotalMilliseconds.ToString("F2"));
 }
}


