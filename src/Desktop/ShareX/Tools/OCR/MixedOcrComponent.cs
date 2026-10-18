using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace ShareX;
internal static class MixedOcrComponent
{
    internal sealed record FileSpec(string Entry,string Name,long Length,string Sha256);
    internal sealed record DownloadSpec(string Url,long Length,string Sha256,FileSpec[] Files);
    internal static readonly string DirectoryPath=Path.Combine(AppContext.BaseDirectory,"ThirdParty","MixedOCR","v1");
    private static readonly SemaphoreSlim gate=new(1,1);
    internal static DownloadSpec[] Manifest { get {using var s=typeof(MixedOcrComponent).Assembly.GetManifestResourceStream("ShareX.MixedOcrManifest.json");return JsonSerializer.Deserialize<DownloadSpec[]>(s);} }
    internal static bool IsPresent {get {try{return File.Exists(Path.Combine(DirectoryPath,"ready")) && Manifest.SelectMany(x=>x.Files).All(f=>new FileInfo(Path.Combine(DirectoryPath,f.Name)).Length==f.Length);}catch{return false;}}}
    internal static async Task ValidateAsync(CancellationToken token)
    {
        foreach(var file in Manifest.SelectMany(x=>x.Files))await Verify(Path.Combine(DirectoryPath,file.Name),file.Length,file.Sha256,token);
        using var keys=typeof(MixedOcrComponent).Assembly.GetManifestResourceStream("ShareX.MixedOcrKeys.txt");
        await Verify(Path.Combine(DirectoryPath,"keys.txt"),keys.Length,Convert.ToHexString(await SHA256.HashDataAsync(keys,token)),token);
    }
    internal static async Task<bool> IsValidAsync(CancellationToken token)
    {if(!IsPresent)return false;try{await ValidateAsync(token);return true;}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}}
    private static async Task Verify(string path,long length,string hash,CancellationToken token)
    {await using var input=File.OpenRead(path);if(input.Length!=length || !Convert.ToHexString(await SHA256.HashDataAsync(input,token)).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("中英识别组件校验失败，请重新下载。");}
    internal static Task DownloadAsync(IProgress<int> progress,CancellationToken token) => Task.Run(()=>DownloadCoreAsync(progress,token),token);
    private static async Task DownloadCoreAsync(IProgress<int> progress,CancellationToken token)
    {
        if(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture!=System.Runtime.InteropServices.Architecture.X64)throw new PlatformNotSupportedException("中英识别组件目前支持 Windows x64，请继续使用 Windows OCR。");
        if(!await gate.WaitAsync(0,token))throw new IOException("识别组件正在下载。");
        string stage=DirectoryPath+".install-"+Guid.NewGuid().ToString("N"),backup=DirectoryPath+".backup-"+Guid.NewGuid().ToString("N");
        bool committed=false;
        try
        {
            ComponentBundles.SafePath(AppContext.BaseDirectory,Path.GetRelativePath(AppContext.BaseDirectory,DirectoryPath));
            Directory.CreateDirectory(Path.GetDirectoryName(DirectoryPath));
            using var lease=new FileStream(DirectoryPath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);
            if(IsPresent){try{await ValidateAsync(token);progress?.Report(100);return;}catch(IOException){} }
            Directory.CreateDirectory(stage);
            using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(15)};
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Xiaomang/2.9");
            string cache=Path.Combine(Path.GetDirectoryName(DirectoryPath),"download-cache");Directory.CreateDirectory(cache);
            long total=Manifest.Sum(x=>x.Length),done=0;
            foreach(var item in Manifest)
            {
                string archive=ComponentBundles.SafePath(cache,item.Sha256+".download");
                bool cached=false;
                try{if(File.Exists(archive)){await Verify(archive,item.Length,item.Sha256,token);cached=true;}}catch(IOException){}
                if(!cached)using(var response=await client.GetAsync(item.Url,HttpCompletionOption.ResponseHeadersRead,token))
                {
                    response.EnsureSuccessStatusCode();
                    await using var input=await response.Content.ReadAsStreamAsync(token);
                    await using var output=File.Create(archive);byte[] buffer=new byte[81920];long count=0;int n;
                    while((n=await input.ReadAsync(buffer,token))>0){count+=n;if(count>item.Length)throw new InvalidDataException("下载大小异常。");await output.WriteAsync(buffer.AsMemory(0,n),token);progress?.Report((int)((done+count)*90/total));}
                }
                await Verify(archive,item.Length,item.Sha256,token);
                if(item.Files.Length==1 && item.Files[0].Entry=="")File.Copy(archive,Path.Combine(stage,item.Files[0].Name));
                else
                {
                    using(var zip=ZipFile.OpenRead(archive))foreach(var f in item.Files){var entry=zip.GetEntry(f.Entry)??throw new InvalidDataException("组件内容缺失。");if(entry.Length!=f.Length)throw new InvalidDataException("组件大小不符。");entry.ExtractToFile(ComponentBundles.SafePath(stage,f.Name));}
                }
                foreach(var f in item.Files)await Verify(ComponentBundles.SafePath(stage,f.Name),f.Length,f.Sha256,token);
                done+=item.Length;
            }
            using(var keys=typeof(MixedOcrComponent).Assembly.GetManifestResourceStream("ShareX.MixedOcrKeys.txt"))
            using(var target=File.Create(Path.Combine(stage,"keys.txt")))await keys.CopyToAsync(target,token);
            foreach(string name in new[]{"MixedOcrLicense.txt","MixedOcrNotice.txt"})
            {using var source=typeof(MixedOcrComponent).Assembly.GetManifestResourceStream("ShareX."+name);using var target=File.Create(Path.Combine(stage,name));await source.CopyToAsync(target,token);}
            await File.WriteAllTextAsync(Path.Combine(stage,"ready"),"v1",token);
            token.ThrowIfCancellationRequested();
            await MixedOcrClient.StopAsync();
            if(Directory.Exists(DirectoryPath))Directory.Move(DirectoryPath,backup);
            try{Directory.Move(stage,DirectoryPath);}catch{if(Directory.Exists(backup))Directory.Move(backup,DirectoryPath);throw;}
            committed=true;
            foreach(var item in Manifest)try{File.Delete(Path.Combine(cache,item.Sha256+".download"));}catch{}
            progress?.Report(100);
        }
        finally
        {
            // Only installer-owned, random sibling staging directories.
            foreach(string path in committed?new[]{stage,backup}:new[]{stage})try{if(Directory.Exists(path))Directory.Delete(path,true);}catch{}
            gate.Release();
        }
    }
}
