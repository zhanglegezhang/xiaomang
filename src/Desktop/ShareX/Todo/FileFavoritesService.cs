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
                script = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
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
                        DateTime checkpoint = Store.Checkpoint().GetAwaiter().GetResult(), nextCheckpoint = checkpoint;
                        var acceptedRecent = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                        // Only the Windows Recent shortcut directory: no recursion, no drive search.
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
                            } catch (COMException) { }
                            finally { Release(link); }
                            acceptedRecent[info.FullName] = modified;
                            if (modified > nextCheckpoint) nextCheckpoint = modified;
                        }
                        if (recentSeen.Count > 1024) recentSeen.Clear();
                        object windows = null;
                        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
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
                                } catch (COMException) { }
                                finally { Release(window); }
                            }
                        }
                        catch (COMException ex) { DebugHelper.WriteException(ex); }
                        finally { Release(windows); }
                        previousFolders = folders;
                        // At most 32 already-known active local files once per minute.
                        if (++iteration % 4 == 0) foreach (var item in Store.Discovered.Concat(Store.Pinned).Where(x => x.Type == "file").DistinctBy(x => x.Path).Take(32)) {
                            var observed = Observation(item.Path, item.LastUse);
                            if (observed != null && observed.LastWrite != item.LastWrite) observations.Add(observed);
                        }
                        if (!stop.WaitOne(0)) {
                            Store.Observe(observations, nextCheckpoint).GetAwaiter().GetResult();
                            foreach (var accepted in acceptedRecent) recentSeen[accepted.Key] = accepted.Value;
                        }
                    }
                    catch (Exception ex) { Error = "文件使用记录暂时无法更新：" + ex.Message; DebugHelper.WriteException(ex); iteration++; }
                }
            }
            catch (Exception ex) { Error = "文件收藏初始化失败：" + ex.Message; DebugHelper.WriteException(ex); }
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
