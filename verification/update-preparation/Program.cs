using System.Diagnostics;
using System.Reflection;
using ShareX;
var root=Path.Combine(AppContext.BaseDirectory,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
using var helper=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -Command Start-Sleep -Seconds 100"){UseShellExecute=false,CreateNoWindow=true})!;
try {
var messages=new List<string>();
var progress=new Progress<string>(x=>{lock(messages)messages.Add(x);});
var wait=(Task)typeof(ProductUpdateInstaller).GetMethod("WaitForPreparation",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{root,helper,progress})!;
await Task.Delay(35000);File.WriteAllText(Path.Combine(root,"progress.txt"),"10 / 20");
await Task.Delay(30000);if(wait.IsCompleted)throw new Exception("Progress did not extend preparation beyond 60 seconds");
File.WriteAllText(Path.Combine(root,"ready"),"ready");await wait.WaitAsync(TimeSpan.FromSeconds(3));
if(File.Exists(Path.Combine(root,"cancel")))throw new Exception("Unexpected cancellation");
Console.WriteLine("PASS 65-second first preparation with progress, no retry and no cancellation");
} finally {if(!helper.HasExited){helper.Kill();await helper.WaitForExitAsync();}}
