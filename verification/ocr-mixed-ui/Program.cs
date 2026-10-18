using ShareX;
using System.Reflection;
using System.IO;
class Preview:OCRForm{public Preview(Bitmap b):base(b,new OCROptions()){}protected override void OnShown(EventArgs e){}}
class Check
{
 static IEnumerable<Control> All(Control c)=>new[]{c}.Concat(c.Controls.Cast<Control>().SelectMany(All));
 [STAThread]static void Main(){
 Application.SetHighDpiMode(HighDpiMode.DpiUnaware);Application.EnableVisualStyles();
 typeof(OCRForm).Assembly.GetType("ShareX.Program").GetProperty("Settings",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).SetValue(null,new ApplicationConfig());
 Directory.CreateDirectory("build/ocr-integration/ui");
 foreach(float scale in new[]{1f,2f}){
 using var form=new Preview(new Bitmap(20,20));form.Show();if(scale!=1)form.Scale(new SizeF(scale,scale));
 foreach(int width in new[]{760,1180}){
 form.ClientSize=new Size((int)(width*scale),(int)(740*scale));form.PerformLayout();Application.DoEvents();
 foreach(var name in new[]{"ocrMixed","ocrChinese","ocrEnglish","ocrMixedDownload","btnCopyAll","btnOpenServiceLink"}){
 var c=All(form).Single(x=>x.Name==name);if(!c.Visible || c.Right>c.Parent.ClientSize.Width+1 || c.Bottom>c.Parent.ClientSize.Height+1)throw new Exception("Clipped: "+name);
 }
 var editor=All(form).OfType<TextBox>().Single(x=>x.Name=="txtResult");editor.Text="MIT 剑桥 University";
 if(form.Result!=editor.Text)throw new Exception("Editing regression");
 using var bmp=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save($"build/ocr-integration/ui/{scale}-{width}.png");
 Console.WriteLine($"PASS {scale*100}% {width}px toolbar/editing");
 }form.Close();}
 }
}
