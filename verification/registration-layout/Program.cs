using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Forms;
using System.Drawing;
using ShareX;
class Test {
 static BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static IEnumerable<Control> All(Control c){foreach(Control x in c.Controls){yield return x;foreach(var y in All(x))yield return y;}}
 [STAThread]static void Main(){AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.GetFullPath("artifacts/Desktop/"+n.Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};Run();}
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]static void Run(){
 Application.EnableVisualStyles();var program=typeof(ApplicationConfig).Assembly.GetType("ShareX.Program")!;var config=new ApplicationConfig();program.GetProperty("Settings",flags)!.SetValue(null,config);program.GetProperty("DefaultTaskSettings",flags)!.SetValue(null,config.DefaultTaskSettings);program.GetProperty("HotkeysConfig",flags)!.SetValue(null,new HotkeysConfig());program.GetProperty("UploadersConfig",flags)!.SetValue(null,new ShareX.UploadersLib.UploadersConfig());
 using var f=new ApplicationSettingsForm();f.Show();typeof(ApplicationSettingsForm).GetMethod("SelectRegistrationPage",flags)!.Invoke(f,new object[]{"ocr"});Application.DoEvents();
 typeof(ApplicationConfig).Assembly.GetType("ShareX.MainMenuAppearance")!.GetMethod("Apply",flags)!.Invoke(null,new object[]{f});Application.DoEvents();
 var controls=All(f).ToList();var helps=controls.Where(c=>c.Name.EndsWith("StatusHelp")||c.Name=="xiaomangInputStatusHelp").ToArray();if(helps.Length!=5)throw new Exception("help count "+helps.Length);
 foreach(var h in helps)if(string.IsNullOrWhiteSpace(h.AccessibleDescription)||h.AccessibleDescription.EndsWith("。")||h.Cursor!=Cursors.Default)throw new Exception("tooltip text or cursor");
 var message=controls.Single(c=>c.Name=="registrationOperationMessage");if(message.Visible)throw new Exception("idle message visible");message.Text="测试错误提示";if(!message.Visible)throw new Exception("error message hidden");message.Text="";
 if(!controls.Any(c=>c.Text=="鼠标右键菜单"))throw new Exception("title");
 Directory.CreateDirectory("test-results/registration-layout");
 float previous=f.DeviceDpi/96f;foreach(float scale in new[]{1.5f,2f}){f.Scale(new SizeF(scale/previous,scale/previous));previous=scale;foreach(int width in new[]{1000,1500}){f.Width=(int)(width*scale);f.PerformLayout();Application.DoEvents();foreach(var h in helps){if(h.Right>h.Parent!.ClientSize.Width)throw new Exception($"help overflow {h.Name} {h.Bounds} parent={h.Parent.ClientSize} scale={scale} width={width}");}using var bmp=new Bitmap(f.Width,f.Height);f.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save($"test-results/registration-layout/layout-{scale}-{width}.png");}}
 File.WriteAllText("test-results/registration-layout/result.txt","PASS five help descriptions, renamed menu, hidden idle note, visible error message, layout bounds; Form.Scale equivalent checks only.");f.Close();}
}