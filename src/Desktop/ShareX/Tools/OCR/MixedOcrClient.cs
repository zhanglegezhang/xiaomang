using ShareX.HelpersLib;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace ShareX;
internal static class MixedOcrClient
{
    private static readonly SemaphoreSlim gate=new(1,1);
    private static Process child;
    private static NamedPipeServerStream pipe;
    private static StreamReader reader;
    private static StreamWriter writer;
    internal sealed record Result(string Text,string Notice);
    private static void Stop()
    {try{if(child!=null&&!child.HasExited){child.Kill();child.WaitForExit(3000);}}catch{} child?.Dispose();child=null;try{reader?.Dispose();}catch{}reader=null;try{writer?.Dispose();}catch{}writer=null;try{pipe?.Dispose();}catch{}pipe=null;}
    internal static async Task StopAsync(){await gate.WaitAsync();try{Stop();}finally{gate.Release();}}
    internal static async Task WithIdleWorkerAsync(Action action, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token)) throw new IOException("OCR 正在识别，请结束识别后再管理组件。");
        try { Stop(); token.ThrowIfCancellationRequested(); action(); }
        finally { gate.Release(); }
    }
    internal static async Task<Result> RecognizeAsync(Bitmap image,string fallbackLanguage,CancellationToken token)
    {
        string failure="未安装中英识别组件，使用 Windows OCR";
        if(MixedOcrComponent.IsPresent)
        {
            await gate.WaitAsync(token);
            string path=Path.Combine(Path.GetTempPath(),"XiaomangOCR-"+Guid.NewGuid().ToString("N")+".png");
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
                if(child==null||child.HasExited||pipe==null||!pipe.IsConnected)
                {
                    Stop();await MixedOcrComponent.ValidateAsync(timeout.Token);
                    string name="XiaomangOcr-"+Guid.NewGuid().ToString("N");
                    pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                    child=Process.Start(new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,ArgumentList={"-MixedOcrWorker",name}});
                    await pipe.WaitForConnectionAsync(timeout.Token);
                    reader=new StreamReader(pipe);writer=new StreamWriter(pipe){AutoFlush=true};
                    if(await reader.ReadLineAsync(timeout.Token)!="ready")throw new IOException("识别进程初始化失败。");
                }
                await Task.Run(()=>image.Save(path,ImageFormat.Png),timeout.Token);
                await writer.WriteLineAsync(JsonSerializer.Serialize(path).AsMemory(),timeout.Token);
                var line=await reader.ReadLineAsync(timeout.Token)??throw new IOException("识别进程已退出。");
                var reply=JsonSerializer.Deserialize<MixedOcrWorker.Reply>(line);
                if(reply.Error!=null)throw new IOException(reply.Error);
                return new Result(reply.Text,"中英混合 · 本地识别");
            }
            catch(Exception ex)
            {
                Stop();token.ThrowIfCancellationRequested();DebugHelper.WriteException(ex);
                failure=ex is OperationCanceledException?"中英识别超时，已回退 Windows OCR":"中英识别失败，已回退 Windows OCR";
            }
            finally{try{File.Delete(path);}catch{} gate.Release();}
        }
        token.ThrowIfCancellationRequested();
        string text=await OCRHelper.OCR(image,OCRHelper.GetFallbackLanguage(),2,false,false);
        token.ThrowIfCancellationRequested();return new Result(text,failure);
    }
}
