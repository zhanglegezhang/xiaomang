using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.IO;
static class Check {
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static Assembly app;static int count;
 static Type T(string n)=>app.GetType("ShareX."+n,true);
 static object Get(object o,string n){var t=o as Type??o.GetType();return t.GetField(n,F)?.GetValue(o is Type?null:o)??t.GetProperty(n,F)?.GetValue(o is Type?null:o);}
 static void Set(object o,string n,object v){var t=o as Type??o.GetType();var field=t.GetField(n,F);if(field!=null)field.SetValue(o is Type?null:o,v);else t.GetProperty(n,F).SetValue(o is Type?null:o,v);}
 static object Call(object o,string n,params object[] a)=>(o as Type??o.GetType()).GetMethod(n,F).Invoke(o is Type?null:o,a);
 static object New(string n,params object[] a)=>Activator.CreateInstance(T(n),F,null,a,null);
 static void Assert(bool pass,string name){if(!pass)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 static void Until(Func<bool> done){var t=Stopwatch.StartNew();while(!done()){if(t.ElapsedMilliseconds>15000)throw new TimeoutException();Application.DoEvents();Thread.Sleep(2);}Application.DoEvents();}
 static Array Hits(params object[] items){var array=Array.CreateInstance(T("SearchHit"),items.Length);for(int i=0;i<items.Length;i++)array.SetValue(items[i],i);return array;}
 static object Hit(string source,string title,string text)=>New("SearchHit",source,title,text,Guid.NewGuid(),title);
 static void Mouse(Control c,string method,Point p)=>Call(c,method,new MouseEventArgs(MouseButtons.Left,1,p.X,p.Y,0));
 [STAThread] static int Main(string[] args){try{
  string runtime=Path.GetFullPath(args[0]),dependencies=Path.GetFullPath(Environment.GetEnvironmentVariable("XIAOMANG_TEST_DEPENDENCIES")??runtime),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
  AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(runtime,n.Name+".dll");if(!File.Exists(p))p=Path.Combine(dependencies,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
  app=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.dll"));
  Application.SetHighDpiMode(args.Contains("--dpi96")?HighDpiMode.DpiUnaware:HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
  object config=New("ApplicationConfig");Set(T("Program"),"Settings",config);Set(T("Program"),"DefaultTaskSettings",Get(config,"DefaultTaskSettings"));Set(T("Program"),"CustomPersonalPath",Path.Combine(output,"personal"));
  using var form=new Form {Opacity=0,ShowInTaskbar=false,Size=new Size(950,600)};var view=(Control)New("UnifiedSearchResultsView");view.Dock=DockStyle.Fill;form.Controls.Add(view);form.Show();Application.DoEvents();int D(int n)=>(int)Math.Round(n*view.DeviceDpi/96d);
  object a=Hit("固定文件","__init__.py",@"C:\fixture\__init__.py"),b=Hit("待办","alpha","alpha"),c=Hit("待办","alpha-other","alpha"),d=Hit("本地笔记","alpha","body");
  Call(view,"SetHits",Hits(a),"init");var rows=(IList)Get(view,"rows");var row=rows.Cast<object>().First(x=>Get(x,"Hit")!=null);
  Assert((string)Get(row,"Title")=="__init__.py","B06 raw filename preserved");Assert(((string)Get(row,"Snippet")).Contains("__init__.py"),"B06 raw fixed path preserved");
  Call(view,"SetHits",Hits(b,c,d),"alpha");var sorted=(Array)Get(view,"hits");Assert(ReferenceEquals(sorted.GetValue(1),d),"O02 exact match from another source precedes weak hit");Assert(sorted.Length==3,"O02 no duplicates or omissions");
  Call(view,"SetHits",Hits(a),"init");int invoked=0;view.GetType().GetEvent("ResultInvoked",F).GetAddMethod(true).Invoke(view,new object[]{new EventHandler((_,_)=>invoked++)});
  Point point=new(D(70),D(65)),far=new(D(300),D(65));Mouse(view,"OnMouseDown",point);Mouse(view,"OnMouseUp",point);Assert(invoked==1,"B07 normal click opens once");
  Mouse(view,"OnMouseDown",point);Mouse(view,"OnMouseMove",far);Mouse(view,"OnMouseMove",point);Mouse(view,"OnMouseUp",point);Assert(invoked==1,"B07 drag out and return never opens");
  Mouse(view,"OnMouseDown",point);Mouse(view,"OnMouseUp",far);Assert(invoked==1,"B07 distant release never opens");
  Call(view,"OnKeyDown",new KeyEventArgs(Keys.Enter));Assert(invoked==2,"B07 keyboard Enter still opens");
  using var search=(Form)New("UnifiedSearchForm");search.Opacity=0;search.ShowInTaskbar=false;search.Show();Application.DoEvents();
  var filter=(ComboBox)Get(search,"sourceFilter");var query=(TextBox)Get(search,"query");var clip=(CheckBox)Get(search,"clipboard");var status=(Label)Get(search,"status");
  filter.SelectedIndex=5;query.Text="fixture";Until(()=>status.Text.Contains("尚未开启"));Assert(!clip.Checked,"O01 clipboard remains private by default");Assert((int)Get(Get(search,"results"),"HitCount")==0,"O01 gated source has no stale results");
  object todo=Get(config,"TodoSettings"),item=New("ClipboardTextItem");Set(item,"Text","fixture");((IList)Get(todo,"ClipboardItems")).Add(item);clip.Checked=true;Until(()=>status.Text.StartsWith("显示 "));Assert((int)Get(Get(search,"results"),"HitCount")==1,"O01 explicit opt-in returns clipboard records");search.Close();
  Set(todo,"NoteLogSeedVersion",1);object unfinished=New("TodoItem"),finished=New("TodoItem");Set(unfinished,"Text","unfinished");Set(finished,"Text","finished");Set(finished,"IsCompleted",true);((IList)Get(todo,"Items")).Add(unfinished);((IList)Get(todo,"Items")).Add(finished);
  using(var widget=(Form)New("TodoWidgetForm",New("NoteLogStore",Path.Combine(output,"widget")))) {
   object page=Get(widget,"todoPage");Set(page,"showCompleted",false);
   object TodoHit(object target)=>New("SearchHit","待办",Get(target,"Text"),Get(target,"Text"),Get(target,"Id"),null);
   Call(widget,"LocateSearchResult",TodoHit(unfinished));Assert(!(bool)Get(page,"showCompleted"),"O03 unfinished search retains hide-completed choice");
   Call(widget,"LocateSearchResult",TodoHit(finished));Assert((bool)Get(page,"showCompleted"),"O03 completed target temporarily becomes visible");
   Call(widget,"SelectModule","Clipboard",false);Assert(!(bool)Get(page,"showCompleted"),"O03 changing module restores original visibility");
   Call(widget,"LocateSearchResult",TodoHit(finished));Call(widget,"SetExpanded",false);Assert(!(bool)Get(page,"showCompleted"),"O03 collapse restores original visibility");
  }
  string sourceOcr=Environment.GetEnvironmentVariable("XIAOMANG_OCR_FIXTURE")??Path.Combine(dependencies,"ThirdParty","MixedOCR","v1"),testOcr=Path.Combine(output,"ocr-fixture");Directory.CreateDirectory(testOcr);
  foreach(string source in Directory.GetFiles(sourceOcr))File.Copy(source,Path.Combine(testOcr,Path.GetFileName(source)),true);
  bool Valid()=>((Task<bool>)Call(T("MixedOcrComponent"),"IsValidDirectoryAsync",testOcr,CancellationToken.None)).GetAwaiter().GetResult();
  var watch=Stopwatch.StartNew();Assert(Valid(),"B05 complete OCR validates");Console.WriteLine("OCR_FIRST_MS "+watch.Elapsed.TotalMilliseconds);
  watch.Restart();for(int i=0;i<20;i++)Assert(Valid(),"B05 valid cached check "+i);Console.WriteLine("OCR_CACHE_20_MS "+watch.Elapsed.TotalMilliseconds);
  string keys=Path.Combine(testOcr,"keys.txt");byte[] original=File.ReadAllBytes(keys);File.Delete(keys);Assert(!(bool)Call(T("MixedOcrComponent"),"IsPresentAt",testOcr),"B05 missing dictionary fails quick check");Assert(!Valid(),"B05 missing dictionary fails validity");
  File.WriteAllBytes(keys,original);Assert(Valid(),"B05 restored dictionary validates");original[0]^=1;File.WriteAllBytes(keys,original);File.SetLastWriteTimeUtc(keys,DateTime.UtcNow.AddSeconds(3));Assert((bool)Call(T("MixedOcrComponent"),"IsPresentAt",testOcr),"B05 same-size corruption passes only existence check");Assert(!Valid(),"B05 changed dictionary invalidates cache and fails hash");
  Console.WriteLine("RESULT "+count+" checks passed; dpi="+view.DeviceDpi);return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
