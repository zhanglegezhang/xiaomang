using System.Reflection;
using System.Windows.Forms;
using System.Drawing;
using ShareX;
class Check {
[STAThread] static void Main(string[] args) {
Application.SetHighDpiMode(args.Contains("--96")?HighDpiMode.DpiUnaware:HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();
var type=typeof(MainForm).Assembly.GetType("ShareX.MainMenuAppearance")!;
var apply=type.GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic)!;
var menuFont=(Font)type.GetField("MenuFont",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
using var host=new Form{AutoScaleMode=AutoScaleMode.Dpi,Size=new Size(600,400)};
using var strip=new ToolStrip{Font=menuFont};strip.Items.Add("全局设置");
using var tree=new TreeView{Font=menuFont};tree.Nodes.Add("常规");
host.Controls.Add(strip);host.Controls.Add(tree);host.CreateControl();tree.CreateControl();strip.CreateControl();
void Dump(string label)=>Console.WriteLine($"{label} hostDpi={host.DeviceDpi} treeDpi={tree.DeviceDpi} stripDpi={strip.DeviceDpi} treeFont={tree.Font.SizeInPoints} stripFont={strip.Font.SizeInPoints} itemFont={strip.Items[0].Font.SizeInPoints} treeHeight={tree.Font.Height}");
Dump("initial"); apply.Invoke(null,new object[]{tree});Dump("styled");
var dpiField=typeof(Control).GetField("_deviceDpi",BindingFlags.NonPublic|BindingFlags.Instance)!;
var setScaled=typeof(Control).GetMethod("SetScaledFont",BindingFlags.NonPublic|BindingFlags.Instance)!;
int initialDpi=host.DeviceDpi; foreach(var dpi in (args.Contains("--96") ? new[]{96} : new[]{144,192,144})) {
foreach(Control c in new Control[]{host,tree,strip})dpiField.SetValue(c,dpi);
using var button=new Button(); using var grid=new DataGridView(); using var menu=new ContextMenuStrip();
foreach(Control c in new Control[]{button,grid,menu})dpiField.SetValue(c,dpi);
apply.Invoke(null,new object[]{button}); apply.Invoke(null,new object[]{grid});
type.GetMethod("ApplyMenu",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{menu});
var surfaceType=typeof(MainForm).Assembly.GetType("ShareX.SettingsDesignSurface")!;
using var surface=(Control)Activator.CreateInstance(surfaceType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{"test"},null)!;
using var combo=new ComboBox{DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=8};
((System.Collections.Generic.HashSet<ComboBox>)surfaceType.GetField("styledCombos",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(surface)!).Add(combo);
dpiField.SetValue(surface,dpi);
surfaceType.GetMethod("OnDpiChangedAfterParent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(surface,new object[]{EventArgs.Empty});
if(combo.ItemHeight < (int)Math.Round(24*surface.DeviceDpi/96d))throw new Exception("Combo height stale");
float expectedBody=9f*dpi/initialDpi, expectedMenu=9.5f*dpi/initialDpi;
if(Math.Abs(button.Font.SizeInPoints-expectedBody)>0.01||Math.Abs(grid.ColumnHeadersDefaultCellStyle.Font.SizeInPoints-expectedBody)>0.01||Math.Abs(menu.Font.SizeInPoints-expectedMenu)>0.01)throw new Exception("Shared style mismatch");
var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<100;i++)apply.Invoke(null,new object[]{tree});watch.Stop();Console.WriteLine($"100 tree refreshes ms={watch.Elapsed.TotalMilliseconds:F2}");
using var scaled=new Font(menuFont.FontFamily,9.5f*dpi/initialDpi);
setScaled.Invoke(tree,new object[]{scaled,true});setScaled.Invoke(strip,new object[]{scaled,true});
Dump("simulated transition before refresh");apply.Invoke(null,new object[]{tree});Dump("after tree refresh"); if(Math.Abs(tree.Font.SizeInPoints-strip.Font.SizeInPoints)>0.01)throw new Exception("Navigation font mismatch");
}
}
}




