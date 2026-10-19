using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    // Painting/expanding only consults memory. At most two OS probes may remain
    // blocked by an offline share; a timeout never starts replacement probes.
    internal static class FavoritePathStatus
    {
        private sealed class Entry { internal int State; internal long Checked; }
        private static readonly ConcurrentDictionary<string, Entry> cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim slots = new(2, 2);
        internal static int Get(string path)
        {
            if (cache.TryGetValue(path, out var old) && (old.State == 0 || Environment.TickCount64 - old.Checked < 60000)) return old.State;
            if (!slots.Wait(0)) return 3;
            var entry = new Entry();
            if (cache.Count > 5000) cache.Clear();
            cache[path] = entry;
            _ = Check(path, entry);
            return 0;
        }
        private static async Task Check(string path, Entry entry)
        {
            var probe = Task.Run(() => {
                try { File.GetAttributes(path); return 1; }
                catch (FileNotFoundException) { return MissingState(path); }
                catch (DirectoryNotFoundException) { return MissingState(path); }
                catch { return 3; }
            });
            if (await Task.WhenAny(probe, Task.Delay(800)).ConfigureAwait(false) != probe)
            { entry.Checked = Environment.TickCount64; Volatile.Write(ref entry.State, 3); }
            try { int state = await probe.ConfigureAwait(false); entry.Checked = Environment.TickCount64; Volatile.Write(ref entry.State, state); }
            finally { slots.Release(); }
        }
        private static int MissingState(string path)
        {
            if (path.StartsWith(@"\\")) return 3;
            try { var drive = new DriveInfo(Path.GetPathRoot(path)); return drive.DriveType != DriveType.Network && drive.IsReady ? 2 : 3; }
            catch { return 3; }
        }
        internal static string Label(int state) => state == 2 ? "失效" : state == 3 ? "暂无法访问" : state == 0 ? "检查中" : "";
    }
}
