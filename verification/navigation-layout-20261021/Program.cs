using System.Drawing;
using System.Windows.Forms;
class Check {
[STAThread] static void Main(){Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
using var host=new Form{Size=new Size(800,800),AutoScaleMode=AutoScaleMode.None};
using var panel=new Panel{Dock=DockStyle.Left,Width=336};
using var strip=new ToolStrip{Dock=DockStyle.Fill,AutoSize=false,GripStyle=ToolStripGripStyle.Hidden,LayoutStyle=ToolStripLayoutStyle.VerticalStackWithOverflow,Padding=new Padding(12,16,12,0)};
var item=new ToolStripButton("全局设置"){AutoSize=false,Size=new Size(312,76),Margin=new Padding(0,0,0,6)};strip.Items.Add(item);panel.Controls.Add(strip);host.Controls.Add(panel);host.CreateControl();panel.CreateControl();strip.CreateControl();host.PerformLayout();strip.PerformLayout();
Console.WriteLine($"200% panel={panel.Width} strip={strip.Width} item={item.Bounds}");
panel.Width=252;strip.Padding=new Padding(9,12,9,0);host.PerformLayout();strip.PerformLayout();Console.WriteLine($"150% stale item panel={panel.Width} strip={strip.Width} item={item.Bounds}; icon left={item.Bounds.Left+20}");if(item.Bounds.Left>=0)throw new Exception("Expected reproduction missing");
var watch=System.Diagnostics.Stopwatch.StartNew();
for(int repeat=0;repeat<20;repeat++)foreach(int dpi in new[]{144,192}){
int S(int n)=>(int)Math.Round(n*dpi/96d);
panel.Width=S(168);strip.Width=S(168);strip.Padding=new Padding(S(6),S(8),S(6),0);
item.Size=new Size(S(156),S(38));item.Margin=new Padding(0,0,0,S(3));
foreach(int width in new[]{650,1000,800}){
host.Width=width;host.PerformLayout();strip.PerformLayout();
if(item.Bounds.Left<0 || item.Bounds.Right>strip.ClientSize.Width)throw new Exception("Item clipped");
var icon=new Rectangle(item.Bounds.Left+S(13),item.Bounds.Top+(item.Height-S(18))/2,S(18),S(18));
if(!strip.ClientRectangle.Contains(icon))throw new Exception("Icon clipped");
if(strip.GetItemAt(new Point(icon.Left+icon.Width/2,icon.Top+icon.Height/2))!=item)throw new Exception("Hit target wrong");
}}
Console.WriteLine($"PASS 120 layout/clip/hit cases; elapsed={watch.ElapsedMilliseconds}ms");
}
}

