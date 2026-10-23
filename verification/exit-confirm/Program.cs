using System;
using System.Linq;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
class Check {
[STAThread] static void Main(string[] args){
Application.SetHighDpiMode(args.Length > 0 ? HighDpiMode.PerMonitorV2 : HighDpiMode.DpiUnaware);
var t=typeof(ShareX.MainForm).Assembly.GetType("ShareX.ExitConfirmationForm")!;
foreach(var action in new[]{"取消","×","退出小蟒","Escape","Enter"}){
using var f=(Form)Activator.CreateInstance(t,true)!;
Exception failure=null;
f.Shown+=(_,_)=>f.BeginInvoke(new Action(()=>{try{
if(f.ActiveControl?.Text!="取消")throw new Exception("Default focus");
if(action=="Escape"||action=="Enter")typeof(Form).GetMethod("ProcessDialogKey",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(f,new object[]{action=="Escape"?Keys.Escape:Keys.Enter});
else f.Controls.OfType<Button>().Single(b=>b.Text==action).PerformClick();
}catch(Exception e){failure=e;f.Close();}}));
var result=f.ShowDialog();if(failure!=null)throw failure;
if(result!=(action=="退出小蟒"?DialogResult.OK:DialogResult.Cancel))throw new Exception(action+result);
Console.WriteLine("PASS "+action);
}
foreach(float scale in new[]{1f,1.5f,2f}){
using var f=(Form)Activator.CreateInstance(t,true)!;f.Scale(new SizeF(scale,scale));f.Show();Application.DoEvents();
foreach(Control c in f.Controls)if(!f.ClientRectangle.Contains(c.Bounds))throw new Exception("Bounds "+c.Text);
foreach(Control c in f.Controls.OfType<Control>().Where(c=>c is Label || c is Button)){var size=TextRenderer.MeasureText(c.Text,c.Font,Size.Empty,TextFormatFlags.SingleLine); if(size.Width>c.Width || size.Height>c.Height)throw new Exception("Text clipping "+c.Text+" "+size+" "+c.Size);} Console.WriteLine("PASS text fit dpi="+f.DeviceDpi); using var bmp=new Bitmap(f.Width,f.Height);f.DrawToBitmap(bmp,f.ClientRectangle);bmp.Save("./dialog-"+scale+".png");Console.WriteLine("PASS bounds "+scale);
}
}}
