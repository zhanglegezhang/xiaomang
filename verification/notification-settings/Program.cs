using System.Reflection;
using System.Runtime.Loader;
using System.Linq.Expressions;
using System.Drawing;
using System.Windows.Forms;
static class Check
{
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static IEnumerable<Control> All(Control c)=>new[]{c}.Concat(c.Controls.Cast<Control>().SelectMany(All));
 [STAThread] static void Main(string[] args){
 string runtime=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
 AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
 var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.dll"));Type T(string n)=>assembly.GetType("ShareX."+n,true);
 Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
 int count=0;void Assert(bool b,string s){if(!b)throw new Exception(s);count++;}
 var json=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"Newtonsoft.Json.dll")).GetType("Newtonsoft.Json.JsonConvert");
 var deserialize=json.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)});
 var settings=deserialize.Invoke(null,new object[]{"{\"PlaySoundAfterCapture\":true,\"PlaySoundAfterUpload\":true,\"PlaySoundAfterAction\":true,\"UseCustomCaptureSound\":true,\"ToastWindowDuration\":7.5}",T("TaskSettingsGeneral")});
 foreach(string name in new[]{"PlaySoundAfterCapture","PlaySoundAfterUpload","PlaySoundAfterAction","UseCustomCaptureSound","UseCustomTaskCompletedSound","UseCustomActionCompletedSound","UseCustomErrorSound"}){var p=settings.GetType().GetProperty(name);p.SetValue(settings,true);Assert(!(bool)p.GetValue(settings),"silent "+name);}
 Assert((float)settings.GetType().GetField("ToastWindowDuration").GetValue(settings)==7.5f,"other settings retained");
 var legacy=deserialize.Invoke(null,new object[]{"{\"ToastWindowAutoHide\":false,\"DisableNotificationsOnFullscreen\":false}",T("TaskSettingsGeneral")});
 foreach(string name in new[]{"ToastWindowAutoHide","DisableNotificationsOnFullscreen"}){
  var property=legacy.GetType().GetProperty(name);Assert((bool)property.GetValue(legacy),"legacy/import forced on "+name);
  property.SetValue(legacy,false);Assert((bool)property.GetValue(legacy),"cannot disable "+name);
 }
 var func=Expression.Lambda(typeof(Func<>).MakeGenericType(settings.GetType()),Expression.Constant(settings)).Compile();
 using var owner=(Control)Activator.CreateInstance(T("NotificationSettingsControl"),func,true);
 using var surface=(Control)Activator.CreateInstance(T("SettingsDesignSurface"),F,null,new object[]{"截图通知"},null);
 T("ApplicationSettingsForm").GetMethod("DesignNotifications",F).Invoke(null,new object[]{surface,owner});
 using var host=new Form{ClientSize=new Size(1120,800),ShowInTaskbar=false};host.Controls.Add(surface);host.Show();
 surface.GetType().GetMethod("Finish",F).Invoke(surface,null);Application.DoEvents();
 Assert(All(surface).Count(c=>c.Name=="notificationPair")==2,"two rows");
 Assert(!All(surface).Any(c=>c.Visible&&c.Text.Contains("提示音")),"no sound section");
 Assert(!All(surface).Any(c=>c.Text.Contains("截图时自动隐藏通知")||c.Text.Contains("全屏应用中不显示")),"removed policy controls");
 Assert(!All(surface).Any(c=>c.Name=="cbToastWindowLeftClickAction"),"left selector removed");
 var allowed=new[]{0,2,3,4};
 foreach(string side in new[]{"Right","Middle"}){
  var combo=All(surface).OfType<ComboBox>().Single(c=>c.Name=="cbToastWindow"+side+"ClickAction");
  Assert(combo.Items.Count==4&&combo.Items[2].ToString()=="复制截图文件","four labeled actions");
  var property=settings.GetType().GetProperty("ToastWindow"+side+"ClickAction");
  for(int i=0;i<4;i++){combo.SelectedIndex=i;Assert(Convert.ToInt32(property.GetValue(settings))==allowed[i],"selection mapping "+side+i);}
  for(int i=0;i<12;i++){
   var old=deserialize.Invoke(null,new object[]{"{\"ToastWindow"+side+"ClickAction\":"+i+"}",T("TaskSettingsGeneral")});
   Assert(Convert.ToInt32(property.GetValue(old))==(allowed.Contains(i)?i:0),"import action "+side+i);
  }
 }
 var location=All(surface).Single(c=>c.Name=="frame_cbToastWindowPlacement");
 var duration=All(surface).Single(c=>c.Name=="number_nudToastWindowDuration");
 Assert(location.Width==duration.Width,"placement matches duration width");
 foreach(Control pair in All(surface).Where(c=>c.Name=="notificationPair"))foreach(Control field in pair.Controls){Assert(field.Right<=pair.Width&&field.Bottom<=pair.Height,"field bounds");if(field is FlowLayoutPanel flow)foreach(Control child in flow.Controls)Assert(child.Right<=flow.Width,"inline controls fit");}
 var number=All(owner).OfType<NumericUpDown>().First(n=>n.Name=="nudToastWindowDuration");number.Value=8.5m;
 Assert((float)settings.GetType().GetField("ToastWindowDuration").GetValue(settings)==8.5f,"value handler retained");
 using var image=new Bitmap(host.ClientSize.Width,host.ClientSize.Height);surface.DrawToBitmap(image,surface.ClientRectangle);image.Save(Path.Combine(output,"layout.png"));
 Console.WriteLine($"PASS {count}; dpi={host.DeviceDpi}");host.Close();
 }
}
