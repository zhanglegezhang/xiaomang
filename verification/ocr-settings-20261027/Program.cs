using System.Reflection;
using System.Runtime.Loader;
var runtime=Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving+=(_,n)=>{var p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
var app=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.dll"));
var method=app.GetType("ShareX.OCRHelper").GetMethod("SelectFallbackLanguage",BindingFlags.Static|BindingFlags.NonPublic);
int count=0;
void Check(string[] values,string expected){var actual=(string)method.Invoke(null,new object[]{values});if(actual!=expected)throw new Exception(actual);count++;}
Check(new[]{"en-US","zh-CN"},"zh-CN");
Check(new[]{"ja-JP","zh-Hans"},"zh-Hans");
Check(new[]{"en-US"},"en-US");
Check(new[]{"ja-JP","en-US"},"ja-JP");
Check(new[]{"ZH-cn","en-US"},"ZH-cn");
try{method.Invoke(null,new object[]{Array.Empty<string>()});throw new Exception("missing failure");}
catch(TargetInvocationException e) when(e.InnerException is InvalidOperationException && e.InnerException.Message.Contains("未安装")){count++;}
Console.WriteLine($"PASS {count} fallback checks");
