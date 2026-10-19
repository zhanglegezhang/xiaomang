using System.Reflection;
using System.IO;
using Newtonsoft.Json.Linq;
using ShareX;
var method=typeof(ApplicationConfig).Assembly.GetType("ShareX.UpgradeStartupCheck")!.GetMethod("ValidateSettings",BindingFlags.Static|BindingFlags.NonPublic)!;
int count=0;
void Check(JObject old, JObject current, bool pass) {
 bool ok=true;try{method.Invoke(null,new object[]{old,current});}catch(TargetInvocationException e)when(e.InnerException is IOException){ok=false;}
 if(ok!=pass)throw new Exception("Unexpected validation result "+count);count++;
}
JObject Config(int n) {var c=new JObject();var todo=new JObject();c["TodoSettings"]=todo;foreach(var f in new[]{"Items","FavoriteItems","ClipboardItems"}){var a=new JArray();todo[f]=a;for(int i=0;i<n;i++)a.Add(new JObject{["Id"]=Guid.NewGuid().ToString(),["Text"]="fixture "+i});}return c;}
foreach(int size in new[]{0,1,100}){
 var old=Config(size);Check(old,(JObject)old.DeepClone(),true);
 foreach(var field in new[]{"Items","FavoriteItems","ClipboardItems"}){
  var copy=(JObject)old.DeepClone();((JArray)copy["TodoSettings"]![field]!).Add(new JObject{["Id"]=Guid.NewGuid().ToString(),["Text"]="new"});Check(old,copy,true);
  copy=(JObject)old.DeepClone();copy["TodoSettings"]![field]=null;Check(old,copy,false);
  if(size>0){copy=(JObject)old.DeepClone();((JArray)copy["TodoSettings"]![field]!).RemoveAt(0);Check(old,copy,false);copy=(JObject)old.DeepClone();copy["TodoSettings"]![field]![0]!["Text"]="changed";Check(old,copy,false);}
 }
}
var duplicate=Config(1);((JArray)duplicate["TodoSettings"]!["Items"]!).Add(duplicate["TodoSettings"]!["Items"]![0]!.DeepClone());var missing=(JObject)duplicate.DeepClone();((JArray)missing["TodoSettings"]!["Items"]!).RemoveAt(0);Check(duplicate,missing,false);
Console.WriteLine($"PASS {count} validation cases: empty/addition/loss/change/null/duplicate");
