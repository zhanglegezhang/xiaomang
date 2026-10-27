using System.Reflection;
using System.Runtime.Loader;
var runtime=Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving+=(_,n)=>{var p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
var app=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.dll"));
var type=app.GetType("ShareX.ApplicationConfig",true);
var json=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"Newtonsoft.Json.dll")).GetType("Newtonsoft.Json.JsonConvert");
var deserialize=json.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)});
var config=deserialize.Invoke(null,new object[]{"{\"RotateImageByExifOrientationData\":false,\"BrowserPath\":\"custom-browser.exe\",\"CustomHotkeysConfigPath\":\"saved-hotkeys\",\"CustomScreenshotsPath2\":\"saved-backup\",\"DisableHotkeys\":true,\"DisableHotkeysOnFullscreen\":true,\"HotkeyRepeatLimit\":750,\"PNGStripColorSpaceInformation\":true,\"SaveSettingsAfterTaskCompleted\":true}",type});
int count=0;void Check(string name,object expected){if(!Equals(type.GetProperty(name).GetValue(config),expected))throw new Exception(name);count++;}
Check("RotateImageByExifOrientationData",true);
type.GetProperty("RotateImageByExifOrientationData").SetValue(config,false);Check("RotateImageByExifOrientationData",true);
Check("BrowserPath","custom-browser.exe");Check("CustomHotkeysConfigPath","saved-hotkeys");Check("CustomScreenshotsPath2","saved-backup");Check("DisableHotkeys",true);Check("DisableHotkeysOnFullscreen",true);Check("HotkeyRepeatLimit",750);Check("PNGStripColorSpaceInformation",true);Check("SaveSettingsAfterTaskCompleted",true);
Console.WriteLine($"PASS {count} configuration checks");
