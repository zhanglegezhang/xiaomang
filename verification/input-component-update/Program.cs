using ShareX;
using System.Security.Cryptography;
using System.Text.Json;
class Checks {
static async Task Main(string[] args){
string root=Path.GetFullPath("build/input-component-update-tests");Directory.CreateDirectory(root);int count=0;
void Check(bool ok){if(!ok)throw new Exception("Check failed "+count);count++;}
foreach(string mode in new[]{"success","activation-failure","cancel","locked"}){
string dir=Path.Combine(root,mode),stage=Path.Combine(dir,"stage"),dest=Path.Combine(dir,"installed");Directory.CreateDirectory(stage);Directory.CreateDirectory(dest);
File.WriteAllText(Path.Combine(stage,"worker.exe"),"new");File.WriteAllText(Path.Combine(dest,"worker.exe"),"old");File.WriteAllText(Path.Combine(dest,"config.toml"),"my settings");File.WriteAllText(Path.Combine(dest,"learning.db"),"my learning");
var bundle=new ComponentBundle("text","test.zip",1,new string('0',64),[new("worker.exe",3,Convert.ToHexString(SHA256.HashData("new"u8.ToArray())),true)]);
bool restored=false,paused=false;using var cts=new CancellationTokenSource();if(mode=="cancel")cts.Cancel();
using var locked=mode=="locked"?new FileStream(Path.Combine(dest,"worker.exe"),FileMode.Open,FileAccess.Read,FileShare.Read):null;
bool failed=false;try{await InputComponentTransaction.CommitAsync(bundle,stage,dest,()=>{paused=true;return Task.CompletedTask;},()=>mode=="activation-failure"?Task.FromException(new IOException("startup failure")):Task.CompletedTask,()=>{restored=true;return Task.CompletedTask;},cts.Token);}catch(Exception){failed=true;}
Check(failed==(mode!="success"));Check(File.ReadAllText(Path.Combine(dest,"worker.exe"))==(mode=="success"?"new":"old"));Check(File.ReadAllText(Path.Combine(dest,"config.toml"))=="my settings");Check(File.ReadAllText(Path.Combine(dest,"learning.db"))=="my learning");if(mode=="activation-failure")Check(restored);if(mode=="cancel")Check(!paused);
}
if(args.Length>0){
var latest=JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
var bundle=latest.GetProperty("Bundle").Deserialize<ComponentBundle>();
string real=Path.Combine(root,"real-old-upgrade-"+Guid.NewGuid().ToString("N"));
string installed=Path.Combine(real,"installed"),staged=Path.Combine(real,"staged");
System.IO.Compression.ZipFile.ExtractToDirectory("build/github-sync-311-20261029/xiaomang-inputmethod-3.11-20261029-win-x64.zip",installed);
await ComponentBundles.InstallArchiveAsync(bundle,Path.Combine(Path.GetDirectoryName(args[0]),bundle.FileName),staged,default);
File.WriteAllText(Path.Combine(installed,"personal-settings-test.toml"),"keep-settings");File.WriteAllText(Path.Combine(installed,"personal-learning-test.db"),"keep-learning");
await InputComponentTransaction.CommitAsync(bundle,staged,installed,()=>Task.CompletedTask,()=>Task.CompletedTask,()=>Task.CompletedTask,default);
Check(await ComponentBundles.IsValidAsync(bundle,installed,default));
Check(File.ReadAllText(Path.Combine(installed,"personal-settings-test.toml"))=="keep-settings");Check(File.ReadAllText(Path.Combine(installed,"personal-learning-test.db"))=="keep-learning");
Console.WriteLine("REAL_UPGRADE_DIRECTORY="+installed);
}
Console.WriteLine("PASS "+count+" update transaction checks");
File.WriteAllText(Path.Combine(root,"result.json"),JsonSerializer.Serialize(new{passed=true,checks=count}));
}}
namespace ShareX {internal static class Program {public static string ProductVersion=>"3.11";}}

