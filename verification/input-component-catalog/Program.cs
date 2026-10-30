using ShareX;
using System.Text.Json;
var bundles=JsonSerializer.Deserialize<ComponentBundle[]>(File.ReadAllText("tools/ShareX/source/ShareX-21.0.0/ShareX/Assets/ComponentBundles.json"));
var release=new InputComponentRelease(1,"3.12","3.11",bundles.Single(b=>b.Id=="text"));
InputComponentCatalog.Validate(release,"3.11");
int count=1;
void Reject(InputComponentRelease r,string app="3.11") { try { InputComponentCatalog.Validate(r,app); } catch(Exception){count++;return;} throw new Exception("Invalid manifest accepted"); }
Reject(release with {Schema=2});
Reject(release,"3.10");
Reject(release with {Bundle=release.Bundle with {ReleaseTag="../evil"}});
Reject(release with {Bundle=release.Bundle with {Sha256="bad"}});
Reject(release with {Bundle=release.Bundle with {Files=[]}});
foreach(var path in new[]{"../config.json","C:/config.json","/config.json","a\\b","a//b"})
Reject(release with {Bundle=release.Bundle with {Files=release.Bundle.Files.Append(new ComponentFile(path,1,new string('0',64),true)).ToArray()}});
Reject(release with {Bundle=release.Bundle with {Files=release.Bundle.Files.Append(release.Bundle.Files[0]).ToArray()}});
Console.WriteLine($"PASS {count} catalog checks");
namespace ShareX {
internal sealed record ComponentFile(string Path,long Length,string Sha256,bool Required);
internal sealed record ComponentBundle(string Id,string FileName,long Length,string Sha256,ComponentFile[] Files,string ReleaseTag="v1.0");
internal static class ComponentBundles { internal static ValueTask<int> ReadDownloadAsync(Stream s,Memory<byte> b,CancellationToken t)=>s.ReadAsync(b,t); }
}
