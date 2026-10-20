using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.Loader;

string runtime=Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving+=(_,n)=>{var p=Path.Combine(runtime,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
var app=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime,"ShareX.dll"));
const BindingFlags F=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
var read=app.GetType("ShareX.ComponentBundles",true)!.GetMethod("ReadDownloadAsync",F)!;
Task<int> Read(Stream stream, CancellationToken token, int timeout=60)=>((ValueTask<int>)read.Invoke(null,new object[]{stream,new Memory<byte>(new byte[8]),token,TimeSpan.FromMilliseconds(timeout)})!).AsTask();
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
using(var slow=new DelayedStream(20)){for(int i=0;i<6;i++)Check(await Read(slow,CancellationToken.None,80)==1,"slow continued progress does not time out "+i);}
using(var stalled=new DelayedStream(5000)){try{await Read(stalled,CancellationToken.None);throw new Exception("Missing timeout");}catch(IOException e){Check(e.Message.Contains("长时间"),"stalled network produces actionable error");}}
using(var cancelled=new DelayedStream(5000))using(var stop=new CancellationTokenSource(20)){try{await Read(cancelled,stop.Token,1000);throw new Exception("Missing cancellation");}catch(OperationCanceledException){Check(true,"user cancellation remains cancellation");}}
var manager=app.GetType("ShareX.XiaomangInputMethodManager",true)!;var gate=manager.GetField("gate",F)!.GetValue(null)!;
using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
var holder=Task.Run(()=>{lock(gate){entered.Set();release.Wait();}});entered.Wait();
try{
 var watch=Stopwatch.StartNew();var status=(string)manager.GetProperty("Status",F)!.GetValue(null)!;Check(status.Contains("检查中")&&watch.ElapsedMilliseconds<500,"input status does not block on busy lifecycle");
 watch.Restart();manager.GetMethod("Start",F)!.Invoke(null,null);Check(watch.ElapsedMilliseconds<500,"start returns promptly while lifecycle is busy");
 Check(manager.GetField("monitor",F)!.GetValue(null)==null,"busy start does not launch a background worker");
}finally{release.Set();await holder;}
Console.WriteLine("RESULT 11 checks passed");

sealed class DelayedStream(int delay):Stream{
 public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
 public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
 public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){await Task.Delay(delay,token);buffer.Span[0]=42;return 1;}
 public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
}
