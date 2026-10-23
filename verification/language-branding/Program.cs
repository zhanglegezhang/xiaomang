using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.Loader;
var dir=Path.GetFullPath("artifacts/Desktop");
AssemblyLoadContext.Default.Resolving+=(_,n)=>File.Exists(Path.Combine(dir,n.Name+".dll"))?Assembly.LoadFrom(Path.Combine(dir,n.Name+".dll")):null;
var asm=Assembly.LoadFrom(Path.Combine(dir,"ShareX.dll"));
var replace=asm.GetType("ShareX.VisibleBranding")!.GetMethod("Replace")!;
var rm=new ResourceManager("ShareX.Properties.Resources",asm);
var cultures=Directory.GetDirectories(dir).Select(Path.GetFileName).Where(n=>File.Exists(Path.Combine(dir,n!,"ShareX.resources.dll"))).Append("").Distinct();
var lines=new List<string>();int count=0;
foreach(var name in cultures){
 var culture=CultureInfo.GetCultureInfo(name!);
 foreach(var key in new[]{"ApplicationSettingsForm_cbLanguage_SelectedIndexChanged_Language_Restart","ShareXConfirmation"}){
  string before=rm.GetString(key,culture)??throw new Exception("Missing resource "+name+key);
  string after=(string)replace.Invoke(null,new object[]{before})!;
  if(after.Contains("ShareX",StringComparison.OrdinalIgnoreCase)||(before.Contains("ShareX",StringComparison.OrdinalIgnoreCase)&&!after.Contains("小蟒")))throw new Exception("Brand residue "+name+key);
  lines.Add($"{name}: {key}: {after.Replace("\r","").Replace("\n"," / ")}");count++;
 }
}
foreach(var original in new[]{@"D:\ShareX\test.png","https://getsharex.com",@"\\server\ShareX\file"}){
 if((string)replace.Invoke(null,new object[]{original})! != original)throw new Exception("Data changed"); count++;
}
lines.Insert(0,$"PASS {count} checks; compiled language resources and protected paths/links.");
File.WriteAllLines("test-results/language-branding/check-result.txt",lines);
Console.WriteLine(lines[0]);
