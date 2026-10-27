using System.Reflection;
using System.Runtime.Loader;
using System.Linq.Expressions;
using System.Drawing;
using System.Windows.Forms;
class Check
{
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 public static object Current;
 static IEnumerable<Control> All(Control c)=>new[]{c}.Concat(c.Controls.Cast<Control>().SelectMany(All));
 [STAThread] static void Main(string[] args)
 {
  string runtime=Path.GetFullPath(args[0]), output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
  AssemblyLoadContext.Default.Resolving+=(_,n)=>{var p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
  var app=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.dll"));
  var capture=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.ScreenCaptureLib.dll"));
  var type=capture.GetType("ShareX.ScreenCaptureLib.FFmpegOptions",true);
  var audioProp=type.GetProperty("AudioSource");var codecProp=type.GetProperty("VideoCodec");
  Current=Activator.CreateInstance(type);
  int count=0;void Assert(bool b,string s){if(!b)throw new Exception(s);count++;}
  string mixed="xiaomang-system-and-microphone";
  Assert((string)audioProp.GetValue(Current)==mixed,"new default mixed");
  var func=Expression.Lambda(typeof(Func<>).MakeGenericType(type),Expression.Convert(Expression.Field(null,typeof(Check).GetField("Current")),type)).Compile();
  Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
  using var surface=(Control)Activator.CreateInstance(app.GetType("ShareX.SettingsDesignSurface"),F,null,new object[]{"屏幕录制"},null);
  var advanced=new Button(); bool reset=false;
  advanced.Click+=(_,_)=>{if(reset)Current=Activator.CreateInstance(type);};
  app.GetType("ShareX.ApplicationSettingsForm").GetMethod("DesignRecordingOptions",F).Invoke(null,new object[]{surface,func,advanced});
  using var host=new Form {ClientSize=new Size(1300,700),ShowInTaskbar=false};host.Controls.Add(surface);host.Show();
  surface.GetType().GetMethod("Finish",F).Invoke(surface,null);Application.DoEvents();
  var audio=All(surface).OfType<ComboBox>().Single(c=>c.Name=="recordingAudioSource");
  var codec=All(surface).OfType<ComboBox>().Single(c=>c.Name=="recordingVideoCodec");
  Assert(audio.Items.Count==2&&audio.SelectedIndex==1,"audio choices and selected default");
  Assert(codec.Items.Count==3&&codec.SelectedIndex==0,"codec choices");
  Assert(advanced.Text=="更多参数…"&&All(surface).Any(c=>c.Text=="高级选项"),"captions");
  Assert(audio.Parent.Parent.Width > codec.Parent.Parent.Width,"content-sized widths");
  foreach(var combo in new[]{audio,codec}) Assert(Math.Abs(combo.Top*2+combo.Height-combo.Parent.Height)<=1,"vertical center");
  audio.SelectedIndex=0;Assert((string)audioProp.GetValue(Current)=="","None stored");
  codec.SelectedIndex=2;Assert(!audio.Enabled&&!(bool)type.GetProperty("IsAudioSourceSelected").GetValue(Current),"GIF no audio");
  codec.SelectedIndex=1;Assert(audio.Enabled&&audio.SelectedIndex==0&&codecProp.GetValue(Current).ToString()=="libx265","restore None after GIF");
  audio.SelectedIndex=1;codec.SelectedIndex=2;codec.SelectedIndex=0;
  Assert(audio.SelectedIndex==1&&audio.Enabled,"restore mixed after GIF");
  audio.GetType().GetMethod("OnMouseWheel",F).Invoke(audio,new object[]{new HandledMouseEventArgs(MouseButtons.None,0,0,0,120)});
  Assert(audio.SelectedIndex==1,"wheel no change");
  audio.SelectedIndex=0;codec.SelectedIndex=1;reset=true;advanced.PerformClick();
  Assert(audio.SelectedIndex==1&&codec.SelectedIndex==0,"reset replacement reflected");reset=false;
  audioProp.SetValue(Current,"existing-device");codecProp.SetValue(Current,Enum.Parse(codecProp.PropertyType,"h264_nvenc"));advanced.PerformClick();
  Assert(audio.SelectedIndex==-1&&codec.SelectedIndex==-1&&(string)audioProp.GetValue(Current)=="existing-device","advanced legacy values preserved");
  audioProp.SetValue(Current,mixed);codecProp.SetValue(Current,Enum.Parse(codecProp.PropertyType,"libx264"));advanced.PerformClick();
  using var bmp=new Bitmap(host.ClientSize.Width,host.ClientSize.Height);host.DrawToBitmap(bmp,host.ClientRectangle);bmp.Save(Path.Combine(output,"preview.png"));
  Console.WriteLine($"PASS {count} checks");
 }
}
