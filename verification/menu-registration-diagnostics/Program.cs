using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics;
using System.ComponentModel;
const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
string runtime=Path.GetFullPath(args[0]), root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
var asm=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.dll"));
var type=asm.GetType("ShareX.ModernFileFavoritesMenu",true)!;
object Call(string name,params object[] values)=>type.GetMethod(name,flags)!.Invoke(null,values);
int count=0;void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
string daily=Path.GetFullPath(args[2]);var clock=Stopwatch.StartNew();Call("ValidateFiles",daily);clock.Stop();Console.WriteLine($"Existing runtime preflight: {clock.Elapsed.TotalMilliseconds:F2} ms");
string[] names={"xiaomang.exe","resources.pri","ThirdParty/FileFavoritesMenu/Xiaomang.FileFavoritesMenu.msix","ThirdParty/FileFavoritesMenu/XiaomangFileFavoritesMenu.dll","ThirdParty/FileFavoritesMenu/XiaomangMenuPublisher.cer","ThirdParty/FileFavoritesMenu/Assets/Logo.png","ThirdParty/FileFavoritesMenu/Assets/Square44x44Logo.scale-100.png","ThirdParty/FileFavoritesMenu/Assets/Square150x150Logo.scale-100.png"};
foreach(string name in names){string p=Path.GetFullPath(Path.Combine(root,name));Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.Copy(Path.Combine(daily,name),p,true);}
Call("ValidateFiles",root);Check(true,"Complete fixture accepted");
foreach(string name in names){string p=Path.GetFullPath(Path.Combine(root,name));File.Move(p,p+".test-held");try{Call("ValidateFiles",root);throw new Exception("Missing file was accepted");}catch(TargetInvocationException e){var message=(string)Call("FormatFailure","检查菜单文件",root,e.InnerException!)!;Check(message.Contains(p)&&message.Contains("0x80070002"),"Missing file reports exact path: "+name);}finally{File.Move(p+".test-held",p);}}
string empty=Path.Combine(root,"resources.pri");File.WriteAllBytes(empty,Array.Empty<byte>());try{Call("ValidateFiles",root);throw new Exception("Empty file accepted");}catch(TargetInvocationException e){Check(e.InnerException!.Message.Contains(empty),"Empty resource has exact path");}File.Copy(Path.Combine(daily,"resources.pri"),empty,true);
using(var locked=File.Open(empty,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){try{Call("ValidateFiles",root);throw new Exception("Locked file accepted");}catch(TargetInvocationException e){Check(e.InnerException is IOException,"Locked resource preserves IO failure");}}
string native=(string)Call("FormatFailure","启动证书授权",daily,new Win32Exception(2))!;Check(native.Contains("系统错误 2")&&native.Contains("启动证书授权"),"Native error and stage retained");
string nested=(string)Call("FormatFailure","注册外层菜单",daily,new IOException("部署失败",new FileNotFoundException("missing","test.msix")))!;Check(nested.Contains("0x80070002")&&nested.Contains("test.msix"),"Nested native cause retained");
var program=asm.GetType("ShareX.Program",true)!;program.GetProperty("CustomPersonalPath",flags)!.SetValue(null,Path.Combine(root,"personal"));
string before=(string)Call("Status")!;
try{await (Task)Call("ChangeAsync",true,false)!;throw new Exception("Expected preflight failure");}catch(IOException e){Check(e.Message.Contains("步骤：检查菜单文件")&&e.Message.Contains("诊断日志："),"Real registration entry returns actionable preflight failure");}
Check(before==(string)Call("Status")!,"Failed preflight preserves actual registration");
string log=Directory.GetFiles(Path.Combine(root,"personal","Logs")).Single();string text=File.ReadAllText(log);Check(text.Contains("FileNotFoundException")&&text.Contains("版本：")&&text.Contains("程序目录："),"Diagnostic log includes full exception and environment");
Console.WriteLine($"RESULT {count} checks passed");
