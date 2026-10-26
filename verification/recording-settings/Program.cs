using System.Reflection;
using System.Runtime.Loader;
var runtime=Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving+=(_,n)=>{var p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
var a=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.dll"));
var t=a.GetType("ShareX.TaskSettingsCapture",true);
var j=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"Newtonsoft.Json.dll")).GetType("Newtonsoft.Json.JsonConvert");
var d=j.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)});
int count=0;
foreach(bool v in new[]{false,true}){
var obj=d.Invoke(null,new object[]{"{\"ScreenRecordTwoPassEncoding\":"+v.ToString().ToLower()+",\"ScreenRecordTransparentRegion\":"+v.ToString().ToLower()+",\"ScreenRecordAskConfirmationOnAbort\":"+v.ToString().ToLower()+",\"ScreenRecordFPS\":42}",t});
foreach(var name in new[]{"ScreenRecordTwoPassEncoding","ScreenRecordTransparentRegion","ScreenRecordAskConfirmationOnAbort"}){
var p=t.GetProperty(name);bool expected=name=="ScreenRecordAskConfirmationOnAbort";
if((bool)p.GetValue(obj)!=expected)throw new Exception("Import "+name);count++;
p.SetValue(obj,!expected);if((bool)p.GetValue(obj)!=expected)throw new Exception("Setter "+name);count++;
}
if((int)t.GetField("ScreenRecordFPS").GetValue(obj)!=42)throw new Exception("FPS changed");count++;
}
Console.WriteLine("PASS "+count+" configuration checks");
