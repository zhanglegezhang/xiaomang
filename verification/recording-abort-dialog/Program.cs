using System.Reflection;
using System.Runtime.Loader;
using System.Drawing;
using System.Windows.Forms;
class Check {
[STAThread] static void Main(string[] args){
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
var runtime=Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving+=(_,n)=>{var p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
var a=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.ScreenCaptureLib.dll"));
var t=a.GetType("ShareX.ScreenCaptureLib.RecordingAbortConfirmationForm",true);
var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
int count=0;void Assert(bool ok,string name){if(!ok)throw new Exception(name);count++;}
foreach(var area in new[]{new Rectangle(0,0,1920,1040),new Rectangle(-2560,0,2560,1400)})
foreach(float scale in new[]{1.5f,2f})
foreach(var anchor in new[]{new Rectangle(area.Left,area.Top,300,60),new Rectangle(area.Right-300,area.Bottom-60,300,60),new Rectangle(area.Left+400,area.Top+600,300,60)}){
var size=new Size((int)(350*scale),(int)(160*scale));int gap=(int)(8*scale);
var p=(Point)t.GetMethod("Place",flags).Invoke(null,new object[]{anchor,size,area,gap});
Assert(area.Contains(new Rectangle(p,size)),"same screen bounds");
Assert(anchor.Top-gap-size.Height>=area.Top?p.Y==anchor.Top-gap-size.Height:p.Y==anchor.Bottom+gap,"above/below");
}
using var owner=new Form{Bounds=new Rectangle(300,650,350,55),ShowInTaskbar=false};owner.Show();
foreach(string action in new[]{"continue","escape","close","discard"}){
using var dialog=(Form)Activator.CreateInstance(t,flags,null,new object[]{owner},null);
using var timer=new System.Windows.Forms.Timer{Interval=200};
timer.Tick+=(_,_)=>{timer.Stop();
Assert(dialog.ActiveControl?.Text=="继续录制","default focus");
Assert(((Button)dialog.AcceptButton).Text=="继续录制","enter safe");
if(action=="continue"){
using var bitmap=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(bitmap,dialog.ClientRectangle);bitmap.Save(Path.Combine(args[1],"dialog.png"));
((Button)dialog.CancelButton).PerformClick();
} else if(action=="escape") typeof(Form).GetMethod("ProcessDialogKey",flags).Invoke(dialog,new object[]{Keys.Escape});
else dialog.Controls.OfType<Button>().Single(b=>b.Text==(action=="close"?"×":"放弃录制")).PerformClick();};
timer.Start();var result=dialog.ShowDialog(owner);Assert(result==(action=="discard"?DialogResult.OK:DialogResult.Cancel),action+" result");
}
Console.WriteLine("PASS "+count+" placement and modal interaction checks");
}
}
