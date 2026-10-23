using ShareX;
class Broken : Exception { public override string ToString()=>throw new OutOfMemoryException(); }
class BadMessage : Exception { public override string Message=>throw new InvalidOperationException(); }
class Oom : OutOfMemoryException { public override string ToString()=>throw new Exception("must not format"); }
class Test {
static int passed;
static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);passed++;}
static void Main(){
var logs=new List<string>();int full=0,fallback=0;
Action<string,string> display=(m,d)=>full++;
Action<string> minimal=m=>fallback++;
ErrorReporting.Handle(new Exception("original"),"test",logs.Add,display,minimal);
Check(full==1&&fallback==0&&logs[0].Contains("original")&&logs.Any(x=>x.Contains("privateBytes")),"normal error and resource record");
logs.Clear();full=fallback=0;
ErrorReporting.Handle(new Broken(),"test",logs.Add,display,minimal);
Check(full==0&&fallback==1&&logs[0].Contains("Broken"),"format OOM keeps original identity and falls back");
full=fallback=0;
ErrorReporting.Handle(new Oom(),"test",logs.Add,display,minimal);
Check(full==0&&fallback==1,"direct OOM skips detailed dialog");
full=fallback=0;
ErrorReporting.Handle(new Exception(),"test",_=>throw new IOException(),display,minimal);
Check(full==1,"logging failure does not prevent notification");
full=fallback=0;
ErrorReporting.Handle(new Exception(),"test",logs.Add,(m,d)=>throw new OutOfMemoryException(),minimal);
Check(fallback==1,"dialog allocation failure falls back");
ErrorReporting.Handle(new Exception(),"test",_=>throw new IOException(),(m,d)=>throw new Exception(),_=>throw new Exception());
full=0;ErrorReporting.Handle(new Exception(),"test",logs.Add,display,minimal);
Check(full==1,"all reporting failures swallowed and gate released");
full=fallback=0;
ErrorReporting.Handle(new BadMessage(),"test",logs.Add,display,minimal);
Check(full==0&&fallback==1,"throwing exception message protected");
full=0;
ErrorReporting.Handle(new Exception(),"test",logs.Add,(m,d)=>{full++;ErrorReporting.Handle(new Exception(),"nested",logs.Add,display,minimal);},minimal);
Check(full==1,"modal reentrancy suppressed");
using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
full=0;
var task=Task.Run(()=>ErrorReporting.Handle(new Exception(),"test",_=>{},(m,d)=>{Interlocked.Increment(ref full);entered.Set();release.Wait();},minimal));
if(!entered.Wait(5000))throw new Exception("timeout");
Parallel.For(0,20,_=>ErrorReporting.Handle(new Exception(),"test",_=>{},display,minimal));
release.Set();task.Wait();
Check(full==1,"concurrent errors produce only one dialog");
ErrorReporting.Handle(new Exception(),"test",logs.Add,display,minimal);
Check(full==2,"next independent error still displayed");
Console.WriteLine("RESULT "+passed+" passed");
}}
