using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal sealed record ComponentFile(string Path, long Length, string Sha256, bool Required);
    internal sealed record ComponentBundle(string Id, string FileName, long Length, string Sha256, ComponentFile[] Files);

    internal static class ComponentBundles
    {
        internal const string ReleaseUrl = "https://github.com/zhanglegezhang/xiaomang_component/releases/download/v1.0/";
        private static readonly SemaphoreSlim gate = new(1, 1);
        private static readonly ComponentBundle[] bundles = Load();
        private static ComponentBundle[] Load()
        {
            using var stream = typeof(ComponentBundles).Assembly.GetManifestResourceStream("ShareX.Assets.ComponentBundles.json");
            return JsonSerializer.Deserialize<ComponentBundle[]>(stream);
        }
        internal static ComponentBundle Voice => bundles.Single(b => b.Id == "voice");
        internal static ComponentBundle Text => bundles.Single(b => b.Id == "text");

        // Cheap status checks never hash a large model on the UI refresh timer.
        internal static bool IsPresent(ComponentBundle bundle, string destination)
        {
            try { return bundle.Files.Where(f => f.Required).All(f => new FileInfo(Path.Combine(destination, f.Path)).Length == f.Length); }
            catch { return false; }
        }
        internal static async Task<bool> IsValidAsync(ComponentBundle bundle, string destination, CancellationToken token)
        {
            if (!IsPresent(bundle, destination)) return false;
            foreach (var file in bundle.Files.Where(f => f.Required))
                if (!await MatchesAsync(SafePath(destination, file.Path), file, token).ConfigureAwait(false)) return false;
            return true;
        }
        private static async Task<bool> MatchesAsync(string path, ComponentFile file, CancellationToken token)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length != file.Length) return false;
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        internal static string SafePath(string root, string relative)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("组件路径无效。");
            string result = Path.GetFullPath(Path.Combine(root, relative));
            if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("组件路径越界。");
            // Do not traverse junctions or symlinks in an existing destination.
            for (string current = result; current != null; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("组件目录包含链接，请使用普通目录。");
            return result;
        }
        internal static async Task EnsureAsync(ComponentBundle bundle, string destination, string cache, IProgress<int> progress,
            CancellationToken token, HttpClient client = null)
        {
            if (!await gate.WaitAsync(0, token).ConfigureAwait(false)) throw new IOException("组件正在准备，请稍后重试。");
            string archive = null;
            try
            {
                if (await IsValidAsync(bundle, destination, token).ConfigureAwait(false)) { progress?.Report(100); return; }
                Directory.CreateDirectory(cache);
                archive = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".zip");
                using var ownedClient = client == null ? new HttpClient { Timeout = TimeSpan.FromMinutes(20) } : null;
                var http = client ?? ownedClient;
                using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseUrl + bundle.FileName);
                request.Headers.UserAgent.ParseAdd("Xiaomang/2.3");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length && length != bundle.Length) throw new IOException("组件下载大小不符，请重试。");
                await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920]; long total = 0; int last = -1, read;
                    while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > bundle.Length) throw new IOException("组件下载大小异常。");
                        await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        int percent = (int)(total * 90 / bundle.Length);
                        if (percent != last) { last = percent; progress?.Report(percent); }
                    }
                    if (total != bundle.Length) throw new IOException("组件下载不完整，请重试。");
                }
                await InstallArchiveAsync(bundle, archive, destination, token).ConfigureAwait(false);
                progress?.Report(100);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("组件下载超时，请检查网络后重试。"); }
            finally { if (archive != null) TryDeleteFile(archive); gate.Release(); }
        }
        internal static async Task InstallArchiveAsync(ComponentBundle bundle, string archive, string destination, CancellationToken token)
        {
            await using (var input = File.OpenRead(archive))
                if (input.Length != bundle.Length || !Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)).Equals(bundle.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("组件校验失败，请重新下载。");
            destination = Path.GetFullPath(destination);
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            SafePath(parent, Path.GetFileName(destination));
            // Cross-process exclusive lease; only a temporary sibling directory is deleted.
            await using var lease = new FileStream(destination + ".install-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            string stage = destination + ".install-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stage);
            var changed = new List<(string Target, string Backup)>();
            bool preserveRecovery = false;
            try
            {
                var expected = bundle.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var zip = ZipFile.OpenRead(archive))
                {
                    foreach (var entry in zip.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        if (entry.FullName.EndsWith('/')) continue;
                        if (!expected.TryGetValue(entry.FullName, out var file) || !seen.Add(entry.FullName) || entry.Length != file.Length)
                            throw new InvalidDataException("组件文件清单不符。");
                        string target = SafePath(Path.Combine(stage, "new"), file.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        await using (var source = entry.Open())
                        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                            await source.CopyToAsync(output, token).ConfigureAwait(false);
                        if (!await MatchesAsync(target, file, token).ConfigureAwait(false)) throw new InvalidDataException("组件文件校验失败。");
                    }
                }
                if (seen.Count != expected.Count) throw new InvalidDataException("组件文件缺失。");
                var pending = new List<ComponentFile>();
                foreach (var file in bundle.Files)
                    if (!await MatchesAsync(SafePath(destination, file.Path), file, token).ConfigureAwait(false)) pending.Add(file);
                token.ThrowIfCancellationRequested();
                // Cancellation stops before commit. Finish or roll back the short commit as a unit.
                foreach (var file in pending)
                {
                    string target = SafePath(destination, file.Path), backup = null;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target))
                    {
                        backup = SafePath(Path.Combine(stage, "old"), file.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));
                        File.Move(target, backup);
                    }
                    changed.Add((target, backup));
                    File.Move(SafePath(Path.Combine(stage, "new"), file.Path), target);
                }
            }
            catch (Exception error)
            {
                var rollbackErrors = new List<Exception>();
                foreach (var change in changed.AsEnumerable().Reverse())
                    try { if (File.Exists(change.Target)) File.Delete(change.Target); if (change.Backup != null) File.Move(change.Backup, change.Target); }
                    catch (Exception rollback) { rollbackErrors.Add(rollback); }
                if (rollbackErrors.Count > 0)
                {
                    preserveRecovery = true;
                    throw new IOException("组件安装失败，部分文件无法恢复；恢复副本保留在：" + stage, new AggregateException(new[] { error }.Concat(rollbackErrors)));
                }
                throw;
            }
            finally { if (!preserveRecovery) try { Directory.Delete(stage, true); } catch { } }
        }
        private static void TryDeleteFile(string path) { try { File.Delete(path); } catch { } }
    }
}
