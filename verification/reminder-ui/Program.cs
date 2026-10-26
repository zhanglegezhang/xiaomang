using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Forms;
using ShareX;
class Check {
 static string runtime=Path.GetFullPath("artifacts/Desktop");
 static string output=Path.GetFullPath("test-results/reminder-ui");
 static int count; static void Assert(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 [STAThread] static void Main(){AssemblyLoadContext.Default.Resolving+=(_,n)=>{var p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};Run();}
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]static void Run(){
 Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Directory.CreateDirectory(output);
 var layout=typeof(TodoReminderForm).GetMethod("LayoutContent",BindingFlags.NonPublic|BindingFlags.Instance)!;
 var time=typeof(TodoReminderForm).GetField("timeText",BindingFlags.NonPublic|BindingFlags.Instance)!;
 var durations=new List<double>();
 foreach(var dpi in new[]{96,144,192}){
  var sw=Stopwatch.StartNew();using var f=new TodoReminderForm();int ack=0;
  var item=new TodoItem {Id=Guid.NewGuid(),Text="ABC",ReminderAt=new DateTime(2026,10,26,10,2,0)};
  f.AddReminder(item,TodoReminderStage.Due,()=>ack++);f.Show();Application.DoEvents();layout.Invoke(f,new object[]{dpi});sw.Stop();durations.Add(sw.Elapsed.TotalMilliseconds);
  var text=f.Controls.OfType<TextBox>().Single();Assert(text.Text=="ABC","content");Assert(text.ScrollBars==ScrollBars.None,"short no scroll");Assert(f.Height<=150*dpi/96, $"compact height={f.Height} dpi={dpi}");
  foreach(Control c in f.Controls)Assert(f.ClientRectangle.Contains(c.Bounds),"control bounds");
  using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,f.ClientRectangle);bmp.Save(Path.Combine(output,$"short-{dpi}.png"));}
  var longItem=new TodoItem{Id=Guid.NewGuid(),Text=string.Join("\r\n",Enumerable.Range(1,100).Select(i=>$"第{i}行：完整保留很长的待办内容，支持滚动查看")),ReminderAt=item.ReminderAt};
  f.AddReminder(longItem,TodoReminderStage.OneHour,()=>ack+=10);Application.DoEvents();layout.Invoke(f,new object[]{dpi});
  Assert(text.Text.Contains("第100行"),"long text preserved");Assert(text.Text.Contains("不足 1 小时"),"advance stage");Assert(text.ScrollBars==ScrollBars.Vertical,"long scroll");Assert(f.Height<Screen.FromControl(f).WorkingArea.Height,"fit screen");
  using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,f.ClientRectangle);bmp.Save(Path.Combine(output,$"long-{dpi}.png"));}
  f.RemoveItem(longItem.Id);Assert(ack==0,"remove no ack");f.AddReminder(item,TodoReminderStage.OneHour,()=>ack+=2);Assert(text.Text=="ABC","same id replaces");Assert(((string)time.GetValue(f)!).Contains("不足 1 小时"),"single advance status");
  f.Controls.OfType<Button>().Single(b=>b.Text=="知道了").PerformClick();Assert(ack==2,"ack latest once");
 }
 using(var f=new TodoReminderForm()){int ack=0;var item=new TodoItem{Id=Guid.NewGuid(),Text="测试",ReminderAt=DateTime.Now};f.AddReminder(item,TodoReminderStage.Due,()=>ack++);f.Show();f.Controls.OfType<Button>().Single(b=>b.Text=="×").PerformClick();Assert(ack==1,"X acknowledges");}
 int disposedAck=0;using(var f=new TodoReminderForm()){f.AddReminder(new TodoItem{Id=Guid.NewGuid(),Text="退出保留",ReminderAt=DateTime.Now},TodoReminderStage.Due,()=>disposedAck++);}Assert(disposedAck==0,"dispose no acknowledgement");
 File.WriteAllText(Path.Combine(output,"result.txt"),$"PASS {count} checks. Explicit layout at 96/144/192 DPI, not physical multi-monitor validation. Create+show ms: {string.Join(", ",durations.Select(x=>x.ToString("F1")))}");Console.WriteLine(File.ReadAllText(Path.Combine(output,"result.txt")));
 }
}
