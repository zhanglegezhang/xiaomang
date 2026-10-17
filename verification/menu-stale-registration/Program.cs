using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using Windows.Management.Deployment;
const BindingFlags F=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
string runtime=Path.GetFullPath(args[0]),root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
var asm=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.dll"));var type=asm.GetType("ShareX.ModernFileFavoritesMenu",true)!;
object Call(string name,params object[] values)=>type.GetMethod(name,F)!.Invoke(null,values)!;
asm.GetType("ShareX.Program",true)!.GetProperty("CustomPersonalPath",F)!.SetValue(null,Path.Combine(root,"personal"));
if(args.Length>3 && args[2]=="--register")
{
 AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",Path.GetFullPath(args[3])+Path.DirectorySeparatorChar);
 await (Task)Call("ChangeAsync",true,false);
 string status=(string)Call("Status");if(!status.StartsWith("外层菜单已注册"))throw new Exception(status);
 Console.WriteLine("PASS native registration: "+Path.GetFullPath(args[3]));return;
}
int count=0;void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
string old=Path.Combine(root,"old"),current=Path.Combine(root,"current");Directory.CreateDirectory(old);Directory.CreateDirectory(current);
int removed=0;var added=new List<string>();var stages=new List<string>();
Task Repair(Func<string> read,Func<string,Task> install)=> (Task)Call("RepairConflictAsync",read,current,(Func<Task>)(()=>{removed++;return Task.CompletedTask;}),install,(Action<string,string>)((s,p)=>stages.Add(s)));
Task Add(string location){added.Add(location);return Task.CompletedTask;}
await Repair(()=>current,Add);Check(removed==0&&added.Count==0,"Repeat registration does not remove a valid current entry");
await Repair(()=>old,Add);Check(removed==1&&added.SequenceEqual(new[]{current}),"Existing valid different location repaired");
foreach(int code in new[]{unchecked((int)0x80070002),unchecked((int)0x80070003)}){removed=0;added.Clear();await Repair(()=>throw new COMException("missing",code),Add);Check(removed==1&&added.SequenceEqual(new[]{current}),"Confirmed missing path repairs: "+code.ToString("X8"));}
removed=0;added.Clear();await Repair(()=>Path.Combine(root,"absent"),Add);Check(removed==1&&added.Count==1,"Returned nonexistent directory repairs");
foreach(var error in new Exception[]{new UnauthorizedAccessException("denied"),new COMException("other",unchecked((int)0x80004005))}){removed=0;added.Clear();try{await Repair(()=>throw error,Add);throw new Exception("Expected rejection");}catch(Exception e)when(e==error){}Check(removed==0&&added.Count==0,"Unknown/access errors preserve registration");}
removed=0;try{await Repair(()=>null!,Add);throw new Exception("Expected rejection");}catch(IOException){}Check(removed==0,"Empty external location does not trigger removal");
removed=0;added.Clear();try{await Repair(()=>old,p=>{added.Add(p);if(p==current)throw new IOException("injected add failure");return Task.CompletedTask;});throw new Exception("Expected failure");}catch(IOException e){Check(e.Message.Contains("已恢复"),"Valid old registration restored after replacement failure");}Check(added.SequenceEqual(new[]{current,old}),"Rollback uses original valid location");
removed=0;added.Clear();try{await Repair(()=>throw new FileNotFoundException(),p=>{added.Add(p);throw new IOException("injected add failure");});throw new Exception("Expected failure");}catch(IOException e){Check(e.Message.Contains("失效")&&e.Message.Contains("收藏数据不受影响"),"Stale repair failure explains state");}Check(added.Count==1,"No rollback to an unknown missing location");
Check(stages.Contains("读取旧菜单注册目录")&&stages.Contains("清理小蟒失效菜单入口"),"Diagnostic steps distinguish stale-registration repair");
Console.WriteLine($"RESULT {count} fault-injection checks passed");
if(!args.Contains("--native"))return;
const string name="Xiaomang.FileFavoritesMenu",publisher="CN=Xiaomang Local Components";
var manager=new PackageManager();var existing=manager.FindPackagesForUser("",name,publisher).Single();string original=existing.EffectiveExternalPath;
string a=Path.Combine(root,"native-a"),b=Path.Combine(root,"native-b"),held=Path.Combine(root,"native-b-held");
void Fixture(string destination){Directory.CreateDirectory(destination);foreach(string file in new[]{"xiaomang.exe","resources.pri"})File.Copy(Path.Combine(original,file),Path.Combine(destination,file));string bundle=Path.Combine(original,"ThirdParty","FileFavoritesMenu");foreach(string file in Directory.GetFiles(bundle,"*",SearchOption.AllDirectories)){string target=Path.Combine(destination,"ThirdParty","FileFavoritesMenu",Path.GetRelativePath(bundle,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);}}
Fixture(a);Fixture(b);
async Task Change(string directory,bool install){AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",directory+Path.DirectorySeparatorChar);await (Task)Call("ChangeAsync",install,false);}
string Location()=>manager.FindPackagesForUser("",name,publisher).Single().EffectiveExternalPath;
bool restored=false;
try{
 await Change(original,false);await Change(a,true);Check(Location()==a,"Native first registration");
 await Change(a,true);Check(Location()==a,"Native repeated registration");
 await Change(b,true);Check(Location()==b,"Native relocation with valid previous folder");
 if(!Path.GetFullPath(b).StartsWith(root+Path.DirectorySeparatorChar)||!Path.GetFullPath(held).StartsWith(root+Path.DirectorySeparatorChar))throw new Exception("Fixture move outside test root");
 Directory.Move(b,held);
 AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",a+Path.DirectorySeparatorChar);
 Check(((string)Call("Status")).Contains("失效"),"Native deleted/moved old folder reports stale registration");
 await Change(a,true);Check(Location()==a,"Native stale old folder repairs successfully");
}finally{
 await Change(original,true);restored=Location()==original;Check(restored,"Original daily menu location restored");
 File.WriteAllText(Path.Combine(root,"native-restoration.txt"),original+"\nRestored="+restored);
}
Console.WriteLine($"RESULT {count} total checks passed; native restoration={restored}");
