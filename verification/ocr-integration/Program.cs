using System.Reflection;
using System.IO;
using System.Diagnostics;
using ShareX;
class Check
{
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
 static Type T(string n)=>typeof(OCRHelper).Assembly.GetType("ShareX."+n,true);
 static object Call(string t,string n,params object[] a)=>T(t).GetMethod(n,F).Invoke(null,a);
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static async Task<object> Recognize(Bitmap image,CancellationToken token=default){var task=(Task)Call("MixedOcrClient","RecognizeAsync",image,"zh-CN",token);await task;return task.GetType().GetProperty("Result").GetValue(task);}
 static string Value(object result,string name)=>(string)result.GetType().GetProperty(name).GetValue(result);
 static async Task Main(string[] args)
 {
  if((bool)Call("MixedOcrWorker","TryRun",(object)args))return;
  using var image=new Bitmap(args[0]);
  string root=(string)T("MixedOcrComponent").GetField("DirectoryPath",F).GetValue(null);
  if(!Directory.Exists(root)){var fallback=await Recognize(image);Assert(Value(fallback,"Notice").Contains("未安装")&&Value(fallback,"Text").Length>0,"missing component falls back to Windows");}
  if(!Directory.Exists(root))
  {
   using var downloadCancel=new CancellationTokenSource(30);bool aborted=false;
   try{await (Task)Call("MixedOcrComponent","DownloadAsync",null,downloadCancel.Token);}catch(OperationCanceledException){aborted=true;}
   Assert(aborted&&!Directory.Exists(root),"cancelled first download leaves no partial installation");
  }
  var sw=Stopwatch.StartNew();int last=-1;
  await (Task)Call("MixedOcrComponent","DownloadAsync",new Progress<int>(x=>{if(x/10!=last){last=x/10;Console.WriteLine("DOWNLOAD "+x);}}),CancellationToken.None);
  Console.WriteLine("DOWNLOAD_SECONDS "+sw.Elapsed.TotalSeconds);
  await (Task)Call("MixedOcrComponent","ValidateAsync",CancellationToken.None);
  var installedTime=File.GetLastWriteTimeUtc(Path.Combine(root,"ready"));
  await (Task)Call("MixedOcrComponent","DownloadAsync",null,CancellationToken.None);
  Assert(File.GetLastWriteTimeUtc(Path.Combine(root,"ready"))==installedTime,"valid installation reused without replacement");
  var first=await Recognize(image);Console.WriteLine(Value(first,"Notice"));Assert(Value(first,"Notice").StartsWith("中英混合")&&Value(first,"Text").Contains("MIT")&&Value(first,"Text").Contains("剑桥"),"downloaded component recognizes mixed original");
  var child=(Process)T("MixedOcrClient").GetField("child",F).GetValue(null);int id=child.Id;
  sw.Restart();await Recognize(image);Console.WriteLine("WARM_MS "+sw.Elapsed.TotalMilliseconds);
  Assert(((Process)T("MixedOcrClient").GetField("child",F).GetValue(null)).Id==id,"consecutive recognition reuses process");
  Console.WriteLine("WAIT_IDLE_62_SECONDS");await Task.Delay(62000);Assert(child.HasExited,"worker exits after 60 seconds idle");
  Assert(Value(await Recognize(image),"Notice").StartsWith("中英混合"),"next request reloads worker");
  await (Task)Call("MixedOcrClient","StopAsync");
  string model=Path.Combine(root,"ch_PP-OCRv5_det_mobile.onnx");byte[] original=File.ReadAllBytes(model);
  try{byte[] corrupt=(byte[])original.Clone();corrupt[0]^=255;File.WriteAllBytes(model,corrupt);var fallback=await Recognize(image);Assert(Value(fallback,"Notice").Contains("回退")&&Value(fallback,"Text").Length>0,"corrupt component falls back without loading");}
  finally{File.WriteAllBytes(model,original);}
  using var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;
  try{await Recognize(image,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
  Assert(cancelled,"cancelled request does not start recognition");
  using var activeCancel=new CancellationTokenSource(10);cancelled=false;
  try{await Recognize(image,activeCancel.Token);}catch(OperationCanceledException){cancelled=true;}
  Assert(cancelled,"active cancellation aborts loading or recognition without fallback");
  Assert(Value(await Recognize(image),"Notice").StartsWith("中英混合"),"restored model recovers");
  await (Task)Call("MixedOcrClient","StopAsync");Console.WriteLine("ALL PASS");
 }
}
