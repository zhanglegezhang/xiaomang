using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal static class RecordingComponent
    {
        internal const string DownloadUrl="https://github.com/zhanglegezhang/xiaomang_component/releases/download/v1.0/ffmpeg-8.1-win-x64.zip";
        internal const string ArchiveHash="1838932e32f01e0e7b65fde5ca0ebb2ab131cb6a9ff1a84025ee5b784075576c";
        internal static string Path => System.IO.Path.Combine(AppContext.BaseDirectory,"ffmpeg.exe");
        internal static bool IsInstalled { get { try { return new FileInfo(Path).Length == 202259968; } catch { return false; } } }
        internal static Task<bool> IsValidAsync(CancellationToken token) => IsExecutableValidAsync(Path, token);
        private static async Task<bool> IsExecutableValidAsync(string path, CancellationToken token)
        {
            if(!File.Exists(path) || new FileInfo(path).Length != 202259968)return false;
            await using var file=File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(file,token).ConfigureAwait(false))=="958AEAA0EA3E74E5E273157E6F045C94804BB72913261D8EF6C3D772743D13DF";
        }
        private static readonly SemaphoreSlim downloadGate=new(1,1);
        internal static Task RemoveAsync(CancellationToken token) => Task.Run(async () =>
        {
            if (!await downloadGate.WaitAsync(0, token)) throw new IOException("录屏组件正在下载或移除，请稍后重试。");
            try
            {
                if (ScreenRecordManager.IsRecording) throw new IOException("正在录屏，请结束录屏后再移除组件。");
                ComponentBundles.SafePath(AppContext.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(Path)) return;
                using var lease = new FileStream(Path + ".component-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.DeleteOnClose);
                try { ManagedComponentRemoval.RemoveFiles(AppContext.BaseDirectory, new[] { "ffmpeg.exe" }, token); }
                catch (IOException error) when (File.Exists(Path))
                { throw new IOException("无法移除录屏组件：可能正在录屏、转换或被其他程序占用。原文件已保留。", error); }
            }
            finally { downloadGate.Release(); }
        }, token);
        internal static async Task DownloadAsync(IProgress<int> progress,CancellationToken cancellation)
        {
            if(!await downloadGate.WaitAsync(0,cancellation)) throw new IOException("录屏组件正在下载，请稍候。");
            string cache=System.IO.Path.Combine(Program.PersonalFolder,"Cache","RecordingComponent");
            string archive=System.IO.Path.Combine(cache,Guid.NewGuid().ToString("N")+".zip");
            string temporary=Path+".download-"+Guid.NewGuid().ToString("N");
            try
            {
                if(await IsValidAsync(cancellation).ConfigureAwait(false)) { progress?.Report(100); return; }
                Directory.CreateDirectory(cache);
                using var client=new HttpClient { Timeout=TimeSpan.FromMinutes(10) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Xiaomang/2.3");
                using var response=await client.GetAsync(DownloadUrl,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                long? length=response.Content.Headers.ContentLength;
                if(length>100*1024*1024) throw new IOException("下载文件大小异常。");
                await using(var input=await response.Content.ReadAsStreamAsync(cancellation))
                await using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
                {
                    byte[] buffer=new byte[81920];long total=0;int previous=-1;
                    int read;
                    while((read=await input.ReadAsync(buffer,cancellation))>0)
                    {
                        total+=read;if(total>100*1024*1024) throw new IOException("下载文件过大。");
                        await output.WriteAsync(buffer.AsMemory(0,read),cancellation);
                        int percent=length>0?(int)(total*90/length.Value):0;
                        if(percent!=previous){progress?.Report(percent);previous=percent;}
                    }
                }
                cancellation.ThrowIfCancellationRequested();
                await InstallArchive(archive,temporary,Path,cancellation).ConfigureAwait(false);
                progress?.Report(100);
            }
            finally
            {
                foreach(string path in new[]{archive,temporary}) try { if(File.Exists(path)) File.Delete(path); } catch { }
                downloadGate.Release();
            }
        }

        internal static async Task InstallArchive(string archive,string temporary,string destination,CancellationToken cancellation)
        {
            await using(var file=File.OpenRead(archive))
            {
                string hash=Convert.ToHexString(await SHA256.HashDataAsync(file,cancellation).ConfigureAwait(false));
                if(!hash.Equals(ArchiveHash,StringComparison.OrdinalIgnoreCase)) throw new IOException("录屏组件校验失败，请重新下载。");
            }
            if(await IsExecutableValidAsync(destination, cancellation).ConfigureAwait(false)) return;
            using var zip=ZipFile.OpenRead(archive);
            ZipArchiveEntry executable=null;
            foreach(var entry in zip.Entries)
                if(System.IO.Path.GetFileName(entry.FullName).Equals("ffmpeg.exe",StringComparison.OrdinalIgnoreCase))
                { if(executable!=null)throw new IOException("录屏组件存在重复文件。");executable=entry; }
            if(executable==null || executable.Length>500*1024*1024 || executable.Length<1024) throw new IOException("录屏组件内容无效。");
            try
            {
                await using(var input=executable.Open())
                await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
                {
                    await input.CopyToAsync(output,cancellation).ConfigureAwait(false);
                    await output.FlushAsync(cancellation).ConfigureAwait(false);output.Flush(true);
                }
                cancellation.ThrowIfCancellationRequested();
                // The fully verified replacement is on the same volume; a locked target stays intact.
                using var lease = new FileStream(destination + ".component-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.DeleteOnClose);
                File.Move(temporary,destination,true);
            }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
        }
    }
}
