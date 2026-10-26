using System.Reflection;
using System.Runtime.Loader;
var runtime=Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving+=(_,n)=>{var p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
var a=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.ScreenCaptureLib.dll"));
var t=a.GetType("ShareX.ScreenCaptureLib.RegionCaptureOptions",true);
var p=t.GetProperty("RegionCaptureActionRightClick");
var j=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"Newtonsoft.Json.dll")).GetType("Newtonsoft.Json.JsonConvert");
var d=j.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)});
int count=0;
foreach(int v in new[]{-1,0,1,2,3,4,5,6,7,999}){
var obj=d.Invoke(null,new object[]{"{\"RegionCaptureActionRightClick\":"+v+"}",t});
int expected=v>=0&&v<=3?v:2;
if(Convert.ToInt32(p.GetValue(obj))!=expected)throw new Exception("Import "+v);
count++;
p.SetValue(obj,Enum.ToObject(p.PropertyType,v));
if(Convert.ToInt32(p.GetValue(obj))!=expected)throw new Exception("Setter "+v);
count++;
}
Console.WriteLine("PASS "+count+" legacy/import and assignment checks");
