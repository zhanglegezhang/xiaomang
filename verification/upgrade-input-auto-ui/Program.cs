using System.Text.Json;
using ShareX;

internal static class Checks
{
 static int count;
 static void Check(bool value,string label){if(!value)throw new Exception(label);count++;}
 static async Task Until(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(5);while(!done()){if(DateTime.UtcNow>end)throw new TimeoutException();await Task.Delay(20);}}
 [STAThread] static void Main()
 {
  ApplicationConfiguration.Initialize();
  using var main=new Form{Text="小蟒升级测试 · 独立界面",Width=350,Height=160};
  main.Shown+=async(_,_)=>{try{await Run(main);Console.WriteLine($"PASS {count} modeless/retry/cancel/safe-exit UI checks");}catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;}finally{main.Close();}};
  Application.Run(main);
 }
 static async Task Run(Form main)
 {
  string root=Path.Combine(AppContext.BaseDirectory,"fixtures");Directory.CreateDirectory(root);
  XiaomangInputMethodManager.BundleDirectory=root;
  File.WriteAllText(Path.Combine(root,"XiaomangInputWorker.exe"),"fixture");
  string receipt=Path.Combine(root,"result.json");
  string Outcome()=>JsonDocument.Parse(File.ReadAllText(receipt)).RootElement.GetProperty("Outcome").GetString();
  void Open(){if(File.Exists(receipt))File.Delete(receipt);UpgradeInputComponentForm.OpenAfterUpgrade(receipt);}
  UpgradeInputComponentForm Window()=>Application.OpenForms.OfType<UpgradeInputComponentForm>().Single();
  XiaomangInputMethodManager.IsRegistered=false;Open();
  Check(Outcome()=="Skipped"&&!UpgradeInputComponentForm.IsBusy,"missing skips UI and catalog");
  XiaomangInputMethodManager.IsRegistered=true;
  string marker=Path.Combine(AppContext.BaseDirectory,".xiaomang-update.json");
  File.WriteAllText(marker,"fixture");
  try{
   Open();await Task.Delay(100);
   Check(InputComponentCatalog.Calls==0,"waits for main helper");
   Check(main.Enabled&&!Window().Modal,"main remains enabled and form modeless");
   Window().WindowState=FormWindowState.Minimized;
   Check(main.Enabled,"minimizing keeps main enabled");
   await UpgradeInputComponentForm.StopForExitAsync();
   Check(Outcome()=="Cancelled"&&!UpgradeInputComponentForm.IsBusy,"cancel while waiting exits safely");
  }finally{File.Delete(marker);}
  InputComponentCatalog.Fail=true;Open();await Until(()=>File.Exists(receipt)&&!UpgradeInputComponentForm.IsBusy);
  Check(Outcome()=="Failed","network failure reported separately");
  var button=Window().Controls.OfType<Panel>().Single().Controls.OfType<Button>().Single();
  Check(button.Text=="重试"&&button.Enabled,"retry offered");
  InputComponentCatalog.Fail=false;File.Delete(receipt);button.PerformClick();await Until(()=>File.Exists(receipt)&&!UpgradeInputComponentForm.IsBusy);
  Check(Outcome()=="Current","retry finishes");Window().Close();
  InputComponentUpdater.Commit=new(TaskCreationOptions.RunContinuationsAsynchronously);InputComponentUpdater.Entered=false;Open();
  await Until(()=>InputComponentUpdater.Entered);
  Task exit=UpgradeInputComponentForm.StopForExitAsync();await Task.Delay(80);
  Check(!exit.IsCompleted,"exit waits for atomic commit");
  Check(main.Enabled,"commit does not lock main window");
  InputComponentUpdater.Commit.SetResult(true);await exit;
  Check(Outcome()=="Updated"&&!UpgradeInputComponentForm.IsBusy,"commit finishes before exit");
  Check(!Application.OpenForms.OfType<UpgradeInputComponentForm>().Any(),"progress window disposed");
 }
}
namespace ShareX
{
 internal static class Program{internal static string ProductVersion=>"3.14";internal static string PersonalFolder=>AppContext.BaseDirectory;}
 internal class ProductUpdateButton:Button{}
 internal static class ProductUpdateStyle{internal static Color Background=>Color.FromArgb(25,34,40);internal static Color Text=>Color.White;}
 internal static class XiaomangInputMethodManager{internal static bool IsRegistered;internal static string BundleDirectory;}
 internal static class PersonalDataManager{internal static bool Busy=>false;}
 internal record Release(string Version);
 internal static class InputComponentCatalog
 {
  internal static int Calls;internal static bool Fail;
  internal static async Task<Release> ReadAsync(string version,CancellationToken token){Calls++;await Task.Delay(25,token);if(Fail)throw new IOException("offline");return new("3.14.0");}
 }
 internal static class InputComponentUpdater
 {
  internal static TaskCompletionSource<bool> Commit;internal static bool Entered;
  internal static Task<bool> UpdateAsync(Release r,string cache,IProgress<int> p,CancellationToken token){Entered=true;p.Report(100);return Commit?.Task??Task.FromResult(false);}
 }
}
namespace ShareX.HelpersLib
{
 internal static class ShareXResources{internal static Icon Icon=>SystemIcons.Application;}
 internal static class DebugHelper{internal static void WriteException(Exception e,string context)=>Console.WriteLine(context+": "+e.Message);}
}
