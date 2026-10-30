using System.Reflection;
using System.IO;
using System.Text.Json;
using ShareX;
const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
var assembly=typeof(ApplicationConfig).Assembly;
string root=Path.GetFullPath("build/upgrade-retention/plan-test");Directory.CreateDirectory(root);
string input=Path.Combine(root,"input");Directory.CreateDirectory(input);File.WriteAllText(Path.Combine(input,"config.toml"),"# retained input configuration");
var kinds=Enum.GetValues(assembly.GetType("ShareX.PersonalDataKind")!);
bool blocked=false;
try{assembly.GetType("ShareX.PersonalDataInputPreflight")!.GetMethod("ForExport",F)!.Invoke(null,new object[]{input,kinds,Path.Combine(root,"missing-component"),CancellationToken.None});}catch(TargetInvocationException e)when(e.InnerException is IOException){blocked=true;}
if(!blocked)throw new Exception("Expected manual-export converter requirement was not reproduced");
var type=assembly.GetType("ShareX.PersonalDataPaths")!;var paths=Activator.CreateInstance(type,true)!;
foreach(var item in new[]{("Personal",root),("Input",input),("Media",Path.Combine(root,"media")),("Hotkeys",Path.Combine(root,"HotkeysConfig.json"))})type.GetField(item.Item1,F)!.SetValue(paths,item.Item2);
assembly.GetType("ShareX.UpgradeDataSnapshot")!.GetMethod("Create",F)!.Invoke(null,new object?[]{paths,Path.Combine(root,"backup.zip"),null,null});
using var plan=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"data-snapshot.json")));
if(plan.RootElement.GetProperty("Input").GetString()!=input||plan.RootElement.GetProperty("Personal").GetString()!=root)throw new Exception("Actual paths not retained");
if(File.ReadAllText(Path.Combine(input,"config.toml"))!="# retained input configuration")throw new Exception("Input modified");
Console.WriteLine("PASS upgrade records actual paths without requiring converter; residual input retained; manual export behavior unchanged");
var startup=assembly.GetType("ShareX.UpgradeStartupCheck")!;
foreach(bool? enabled in new bool?[]{true,false,null})
{
 assembly.GetType("ShareX.UpgradeDataSnapshot")!.GetMethod("Create",F)!.Invoke(null,new object?[]{paths,Path.Combine(root,"backup.zip"),null,enabled});
 var snapshot=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(root,"data-snapshot.json")));
 if((bool?)snapshot["InputWasEnabled"]!=enabled)throw new Exception("Snapshot lost input pause state");
 startup.GetField("expected",F)!.SetValue(null,snapshot);
 if((bool)startup.GetProperty("StartInputAfterUpgrade",F)!.GetValue(null)! != (enabled!=false))throw new Exception("Unexpected upgrade input start policy");
}
startup.GetField("expected",F)!.SetValue(null,null);
if(!(bool)startup.GetProperty("StartInputAfterUpgrade",F)!.GetValue(null)!)throw new Exception("Normal startup behavior changed");
Console.WriteLine("PASS input enabled/paused/legacy snapshot roundtrip and ordinary startup policy");
