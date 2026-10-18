using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    internal static class FileFavoritesService
    {
        internal static FileFavoritesStore Store { get; private set; }
        internal static string Error { get; private set; }
        internal static event Action StatusChanged;
        private static void SetError(string message)
        {
            if (Error == message) return;
            Error = message;
            DebugHelper.WriteLine("File discovery: " + (message ?? "recovered"));
            StatusChanged?.Invoke();
        }
        private static readonly ManualResetEvent stop = new(false);
        private static Thread worker;
        private static readonly ConcurrentQueue<FavoriteFile> opened = new();
        private static void PathOpened(string path, bool folder)
        {
            if (!stop.WaitOne(0) && opened.Count < 256) opened.Enqueue(new FavoriteFile { Path = path, Type = folder ? "folder" : "file", LastUse = DateTime.UtcNow });
        }
        internal static void Start(string recentOverride = null)
        {
            if (Store != null) return;
            Store = new FileFavoritesStore(Program.PersonalFolder);
            FileHelpers.PathOpened += PathOpened;
            worker = new Thread(() => Run(recentOverride)) { IsBackground = true, Name = "小蟒文件使用记录" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }
        internal static void Stop() {
            stop.Set(); FileHelpers.PathOpened -= PathOpened; Store?.Close(); worker?.Join(1000);
            try { Store?.Flush().GetAwaiter().GetResult(); } catch (Exception ex) { DebugHelper.WriteException(ex); }
        }
        private static void Release(object value) {
            try { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
            catch (COMException) { } catch (ArgumentException) { }
        }
        private static FavoriteFile Observation(string path, DateTime used)
        {
            try
            {
                path = FileFavoritesStore.Normalize(path);
                // Do not wake disconnected network drives or touch UNC paths during discovery.
                if (path.StartsWith(@"\\") || new DriveInfo(Path.GetPathRoot(path)).DriveType != DriveType.Fixed) return null;
                bool folder = Directory.Exists(path);
                if (!folder && !File.Exists(path)) return null;
                return new FavoriteFile { Path = path, Type = folder ? "folder" : "file", LastUse = used,
                    LastWrite = folder ? default : File.GetLastWriteTimeUtc(path) };
            }
            catch { return null; }
        }
        private static void Run(string recentOverride)
        {
            object shell = null, script = null;
            try
            {
                Store.Ready.GetAwaiter().GetResult();
                var previousFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var recentSeen = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                string recent = recentOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                int iteration = 0;
                while (!stop.WaitOne(iteration == 0 ? 1500 : 15000))
                {
                    try
                    {
                        var observations = new List<FavoriteFile>();
                        while (opened.TryDequeue(out var inApp)) observations.Add(inApp);
                        // Persist app events before touching optional Windows discovery sources.
                        if (observations.Count > 0)
                        {
                            try { Store.Observe(observations).GetAwaiter().GetResult(); }
                            catch { foreach (var item in observations) if (opened.Count < 256) opened.Enqueue(item); throw; }
                            observations.Clear();
                        }
                        var failures = new List<string>();
                        DateTime checkpoint = Store.Checkpoint().GetAwaiter().GetResult(), nextCheckpoint = checkpoint;
                        var acceptedRecent = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                        // Only the Windows Recent shortcut directory: no recursion, no drive search.
                        try
                        {
                        script ??= Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                        foreach (var info in new DirectoryInfo(recent).EnumerateFiles("*.lnk").OrderByDescending(x => x.LastWriteTimeUtc).Take(512))
                        {
                            if (stop.WaitOne(0)) break;
                            DateTime modified = info.LastWriteTimeUtc;
                            if (modified < checkpoint || recentSeen.TryGetValue(info.FullName, out var seen) && seen == modified) continue;
                            object link = null;
                            try {
                                link = ((dynamic)script).CreateShortcut(info.FullName);
                                string target = ((dynamic)link).TargetPath;
                                var observed = Observation(target, modified);
                                if (observed != null) observations.Add(observed);
                            } catch (COMException) { continue; }
                            finally { Release(link); }
                            acceptedRecent[info.FullName] = modified;
                            if (modified > nextCheckpoint) nextCheckpoint = modified;
                        }
                        }
                        catch (Exception ex)
                        {
                            failures.Add("最近文件读取失败，将自动重试");
                            if (Error == null || !Error.Contains("最近文件")) DebugHelper.WriteException(ex);
                            Release(script); script = null;
                        }
                        if (recentSeen.Count > 1024) recentSeen.Clear();
                        object windows = null;
                        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                            windows = ((dynamic)shell).Windows();
                            int count = Math.Min(32, (int)((dynamic)windows).Count);
                            for (int i = 0; i < count; i++)
                            {
                                object window = null;
                                try {
                                    window = ((dynamic)windows).Item(i);
                                    string url = ((dynamic)window).LocationURL;
                                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile && folders.Add(uri.LocalPath) && !previousFolders.Contains(uri.LocalPath)) {
                                        var observed = Observation(uri.LocalPath, DateTime.UtcNow);
                                        if (observed != null) observations.Add(observed);
                                    }
                                } catch (COMException ex) { failures.Add("部分目录读取失败，将自动重试"); if (Error == null || !Error.Contains("目录读取")) DebugHelper.WriteException(ex); }
                                finally { Release(window); }
                            }
                        }
                        catch (Exception ex) {
                            failures.Add("目录读取失败，将自动重试");
                            if (Error == null || !Error.Contains("目录读取")) DebugHelper.WriteException(ex);
                            Release(shell); shell = null;
                        }
                        finally { Release(windows); }
                        // At most 32 already-known active local files once per minute.
                        if (++iteration % 4 == 0) foreach (var item in Store.Discovered.Concat(Store.Pinned).Where(x => x.Type == "file").DistinctBy(x => x.Path).Take(32)) {
                            var observed = Observation(item.Path, item.LastUse);
                            if (observed != null && observed.LastWrite != item.LastWrite) observations.Add(observed);
                        }
                        if (!stop.WaitOne(0)) {
                            Store.Observe(observations, nextCheckpoint).GetAwaiter().GetResult();
                            previousFolders = folders;
                            foreach (var accepted in acceptedRecent) recentSeen[accepted.Key] = accepted.Value;
                            SetError(failures.Count == 0 ? null : string.Join("；", failures.Distinct()));
                        }
                    }
                    catch (Exception ex) { SetError("文件使用记录暂时无法更新，将自动重试：" + ex.Message); DebugHelper.WriteException(ex); iteration++; }
                }
            }
            catch (Exception ex) { SetError("文件收藏数据读取失败，请检查日志或恢复备份：" + ex.Message); DebugHelper.WriteException(ex); }
            finally { Release(shell); Release(script); }
        }
        internal static async Task Open(FavoriteFile item)
        {
            await Task.Run(() => {
                string path = FileFavoritesStore.Normalize(item.Path);
                if (!File.Exists(path) && !Directory.Exists(path)) throw new IOException("文件或目录不存在，或磁盘暂不可用。" + (item.Pinned ? "可右键选择“更改位置”。" : "可右键选择“不再推荐”。"));
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            });
            await Store.Observe(new[] { new FavoriteFile { Path = item.Path, Type = item.Type, LastUse = DateTime.UtcNow } });
        }
    }
}
