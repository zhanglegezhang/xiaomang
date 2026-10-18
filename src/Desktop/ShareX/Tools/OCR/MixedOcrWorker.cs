using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX;
internal static class MixedOcrWorker
{
    private sealed class Context(string root) : AssemblyLoadContext
    {
        protected override Assembly Load(AssemblyName name)
        { string path=Path.Combine(root,name.Name+".dll"); return File.Exists(path)?LoadFromAssemblyPath(path):null; }
        protected override IntPtr LoadUnmanagedDll(string name)
        { string path=Path.Combine(root,name.EndsWith(".dll")?name:name+".dll"); return File.Exists(path)?LoadUnmanagedDllFromPath(path):IntPtr.Zero; }
    }
    internal static bool TryRun(string[] args)
    {
        if(args.Length==0 || args[0]!="-MixedOcrWorker")return false;
        try { if(args.Length!=2 || !args[1].StartsWith("XiaomangOcr-",StringComparison.Ordinal))return true; Run(args[1]).GetAwaiter().GetResult(); }
        catch { /* The parent reports the failure and falls back; never open a child error dialog. */ }
        return true;
    }
    private static async Task Run(string name)
    {
        using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(10000);
        using var reader=new StreamReader(pipe);using var writer=new StreamWriter(pipe){AutoFlush=true};
        using var resource=typeof(MixedOcrWorker).Assembly.GetManifestResourceStream("ShareX.OcrBridge.dll");
        var context=new Context(MixedOcrComponent.DirectoryPath);
        var type=context.LoadFromStream(resource).GetType("Xiaomang.OcrBridge.Engine",true);
        using var engine=(IDisposable)Activator.CreateInstance(type,MixedOcrComponent.DirectoryPath);
        await writer.WriteLineAsync("ready");
        while(true)
        {
            using var idle=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            string line=await reader.ReadLineAsync(idle.Token);if(line==null)return;
            try
            {
                string path=JsonSerializer.Deserialize<string>(line);
                string text=(string)type.GetMethod("Recognize").Invoke(engine,new object[]{path});
                await writer.WriteLineAsync(JsonSerializer.Serialize(new Reply(text,null)));
            }
            catch(Exception ex){await writer.WriteLineAsync(JsonSerializer.Serialize(new Reply(null,ex.GetBaseException().Message)));}
        }
    }
    internal sealed record Reply(string Text,string Error);
}
