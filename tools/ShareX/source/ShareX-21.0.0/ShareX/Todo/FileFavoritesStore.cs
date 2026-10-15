using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal sealed class FavoriteFile
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("pinned")] public bool Pinned { get; set; }
        [JsonProperty("pinned_at")] public DateTime PinnedAt { get; set; }
        [JsonProperty("open_count")] public long OpenCount { get; set; }
        [JsonProperty("last_use")] public DateTime LastUse { get; set; }
        [JsonProperty("modify_count")] public long ModifyCount { get; set; }
        [JsonProperty("last_write")] public DateTime LastWrite { get; set; }
        [JsonProperty("score")] public double Score { get; set; }
        internal FavoriteFile Copy() => (FavoriteFile)MemberwiseClone();
        public override string ToString() => Name ?? Path ?? "";
    }

    internal sealed class FileFavoritePreferences
    {
        [JsonProperty("excluded_paths")] public List<string> ExcludedPaths { get; set; } = new();
        [JsonProperty("recent_checkpoint")] public DateTime RecentCheckpoint { get; set; }
    }

    // All IO and mutation is serialized away from the UI thread. Published arrays are snapshots.
    internal sealed class FileFavoritesStore
    {
        internal static readonly string[] FileNames = { "pinned_files.json", "usage_stats.json", "settings.json" };
        private readonly string directory;
        private readonly SemaphoreSlim gate = new(1, 1);
        private List<FavoriteFile> pinned = new(), usage = new();
        private FileFavoritePreferences preferences = new();
        private FavoriteFile[] pinnedView = Array.Empty<FavoriteFile>(), usageView = Array.Empty<FavoriteFile>();
        internal FavoriteFile[] Pinned => Volatile.Read(ref pinnedView);
        internal FavoriteFile[] Discovered => Volatile.Read(ref usageView);
        internal event Action Changed;
        internal string Warning { get; private set; }
        private volatile bool closed;
        private volatile bool heldForTransfer;
        internal void Close() => closed = true;
        internal Task Ready { get; }
        internal FileFavoritesStore(string personal)
        {
            directory = System.IO.Path.Combine(personal, "data");
            Ready = Task.Run(Load);
        }

        internal static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) || path.Length > 32700 ||
                path.IndexOfAny(new[] { '\0', '"', '\r', '\n' }) >= 0 || path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\"))
                throw new IOException("文件路径无效。");
            string full = System.IO.Path.GetFullPath(path);
            if (full.Substring(2).Contains(':')) throw new IOException("不支持此文件路径。");
            return System.IO.Path.TrimEndingDirectorySeparator(full);
        }
        internal static List<FavoriteFile> ValidateItems(string json, bool isPinned)
        {
            var items = JsonConvert.DeserializeObject<List<FavoriteFile>>(json) ?? throw new IOException("文件收藏清单为空。");
            if (items.Count > 20000) throw new IOException("文件收藏清单过大。");
            foreach (var item in items)
            {
                if (item == null || item.Type != "folder" && item.Type != "file" || item.OpenCount < 0 || item.ModifyCount < 0)
                    throw new IOException("文件收藏记录无效。");
                item.Path = Normalize(item.Path);
                item.Name = System.IO.Path.GetFileName(item.Path);
                if (string.IsNullOrEmpty(item.Name)) item.Name = item.Path;
                item.Pinned = isPinned;
            }
            return items.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }
        internal static FileFavoritePreferences ValidatePreferences(string json)
        {
            var value = JsonConvert.DeserializeObject<FileFavoritePreferences>(json) ?? throw new IOException("文件收藏设置无效。");
            if (value.ExcludedPaths == null || value.ExcludedPaths.Count > 20000) throw new IOException("排除清单无效。");
            value.ExcludedPaths = value.ExcludedPaths.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return value;
        }
        internal static void ValidateFile(string name, string path)
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new IOException("文件收藏清单过大。");
            string json = File.ReadAllText(path);
            if (name == "settings.json") ValidatePreferences(json);
            else ValidateItems(json, name == "pinned_files.json");
        }
        private T Read<T>(string name, Func<string, T> parse, T empty)
        {
            string path = System.IO.Path.Combine(directory, name);
            if (!File.Exists(path)) return empty;
            try { ValidateFile(name, path); return parse(File.ReadAllText(path)); }
            catch (Exception)
            {
                // Preserve damaged originals. Do not silently replace them with an empty list.
                string backup = path + ".bak";
                if (File.Exists(backup))
                {
                    ValidateFile(name, backup);
                    var recovered = parse(File.ReadAllText(backup));
                    File.Copy(path, path + ".damaged-" + DateTime.UtcNow.Ticks);
                    File.Copy(backup, path, true);
                    Warning = "已从备份恢复文件收藏数据。";
                    return recovered;
                }
                throw new IOException("文件收藏数据损坏，请从个人数据备份恢复：" + name);
            }
        }
        private void Load()
        {
            pinned = Read("pinned_files.json", x => ValidateItems(x, true), new List<FavoriteFile>());
            usage = Read("usage_stats.json", x => ValidateItems(x, false), new List<FavoriteFile>());
            preferences = Read("settings.json", ValidatePreferences, new FileFavoritePreferences());
            if (pinned.Any(x => x.PinnedAt == default))
            {
                // Legacy files stored newest first without a timestamp. Persist this
                // one-time inferred order in the same atomic file as the timestamps.
                var legacy = pinned.Where(x => x.PinnedAt == default).Reverse().ToList();
                var dated = pinned.Where(x => x.PinnedAt != default).OrderBy(x => x.PinnedAt).ToList();
                DateTime start = (dated.Count > 0 ? dated[0].PinnedAt : DateTime.UtcNow).AddTicks(-legacy.Count);
                for (int i = 0; i < legacy.Count; i++) legacy[i].PinnedAt = start.AddTicks(i);
                legacy.AddRange(dated);
                Save("pinned_files.json", legacy); pinned = legacy;
            }
            usage = usage.OrderByDescending(x => Heat(x, DateTime.UtcNow)).Take(2000).ToList();
            if (!File.Exists(System.IO.Path.Combine(directory, "pinned_files.json"))) Save("pinned_files.json", pinned);
            if (!File.Exists(System.IO.Path.Combine(directory, "usage_stats.json"))) Save("usage_stats.json", usage);
            if (!File.Exists(System.IO.Path.Combine(directory, "settings.json"))) Save("settings.json", preferences);
            Publish();
        }
        private void Save(string name, object value)
        {
            Directory.CreateDirectory(directory);
            string target = System.IO.Path.Combine(directory, name), temp = target + ".new-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                {
                    writer.Write(JsonConvert.SerializeObject(value, Formatting.Indented));
                    writer.Flush(); stream.Flush(true);
                }
                if (File.Exists(target)) File.Replace(temp, target, target + ".bak", true);
                else File.Move(temp, target);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private void Publish()
        {
            var used = new HashSet<string>(pinned.Select(x => x.Path).Concat(preferences.ExcludedPaths), StringComparer.OrdinalIgnoreCase);
            Volatile.Write(ref pinnedView, pinned.Select(x => x.Copy()).ToArray());
            Volatile.Write(ref usageView, usage.Where(x => !used.Contains(x.Path)).Select(x => {
                var result = x.Copy(); result.Score = Heat(result, DateTime.UtcNow); return result;
            }).OrderByDescending(x => x.Score).ThenByDescending(x => x.LastUse).Take(100).ToArray());
            Changed?.Invoke();
        }
        internal static double Heat(FavoriteFile item, DateTime now) =>
            (Math.Log2(1 + Math.Min(item.OpenCount, 1000000)) * 12 + Math.Log2(1 + Math.Min(item.ModifyCount, 1000000)) * 3 + 20) /
            (1 + Math.Max(0, (now - item.LastUse.ToUniversalTime()).TotalDays) / 7);
        private async Task Change(Action action)
        {
            await Ready.ConfigureAwait(false);
            await gate.WaitAsync().ConfigureAwait(false);
            try { if (closed) return; await Task.Run(action).ConfigureAwait(false); Publish(); }
            finally { gate.Release(); }
        }
        internal Task Pin(string path) => PinWithResult(path);
        internal async Task<bool> PinWithResult(string path)
        {
            bool added=false;
            await Change(() => {
            path = Normalize(path);
            bool folder = Directory.Exists(path);
            if (!folder && !File.Exists(path)) throw new IOException("文件或目录不存在，或当前无法访问。");
            if (pinned.Any(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
            var next = pinned.Select(x => x.Copy()).ToList();
            if (next.Count >= 20000) throw new IOException("固定数量已达上限。");
            next.Add(new FavoriteFile { Name = System.IO.Path.GetFileName(path), Path = path, Type = folder ? "folder" : "file", Pinned = true, PinnedAt = DateTime.UtcNow });
            if (string.IsNullOrEmpty(next[^1].Name)) next[^1].Name = path;
            Save("pinned_files.json", next); pinned = next;added=true;
            });
            return added;
        }
        internal Task Unpin(string path) => Change(() => { var next = pinned.Where(x => !x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList(); Save("pinned_files.json", next); pinned = next; });
        internal Task Exclude(string path) => Change(() =>
        {
            var next = new FileFavoritePreferences { RecentCheckpoint = preferences.RecentCheckpoint,
                ExcludedPaths = preferences.ExcludedPaths.Append(Normalize(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
            if (next.ExcludedPaths.Count > 20000) throw new IOException("排除数量已达上限。");
            Save("settings.json", next); preferences = next;
        });
        internal Task Repoint(string oldPath, string path) => Change(() =>
        {
            path = Normalize(path); bool folder = Directory.Exists(path);
            if (!folder && !File.Exists(path)) throw new IOException("新位置无法访问。");
            var next = pinned.Select(x => x.Copy()).ToList();
            var item = next.FirstOrDefault(x => x.Path.Equals(oldPath, StringComparison.OrdinalIgnoreCase));
            if (item == null) return;
            next.RemoveAll(x => !ReferenceEquals(x, item) && x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            item.Path = path; item.Name = System.IO.Path.GetFileName(path); item.Type = folder ? "folder" : "file";
            if (string.IsNullOrEmpty(item.Name)) item.Name = path;
            Save("pinned_files.json", next); pinned = next;
        });
        internal Task Observe(IEnumerable<FavoriteFile> observations, DateTime checkpoint = default) => Change(() =>
        {
            var next = usage.Select(x => x.Copy()).ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var input in observations)
            {
                string path; try { path = Normalize(input.Path); } catch { continue; }
                if (preferences.ExcludedPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
                if (!next.TryGetValue(path, out var item)) next[path] = item = new FavoriteFile { Path = path, Name = System.IO.Path.GetFileName(path), Type = input.Type };
                if (string.IsNullOrEmpty(item.Name)) item.Name = path;
                // Coalesce shell/Recent/in-app signals for the same visit. It is an observed count, not an audit log.
                if ((input.LastUse - item.LastUse).TotalSeconds > 5) { item.OpenCount = Math.Min(long.MaxValue - 1, item.OpenCount) + 1; item.LastUse = input.LastUse; }
                if (input.LastWrite != default && input.LastWrite != item.LastWrite) {
                    if (item.LastWrite != default) item.ModifyCount = Math.Min(long.MaxValue - 1, item.ModifyCount) + 1;
                    item.LastWrite = input.LastWrite;
                }
                item.Score = Heat(item, DateTime.UtcNow);
            }
            var bounded = next.Values.OrderByDescending(x => Heat(x, DateTime.UtcNow)).Take(2000).ToList();
            if (observations.Any()) { Save("usage_stats.json", bounded); usage = bounded; }
            if (checkpoint > preferences.RecentCheckpoint) {
                var nextPreferences = new FileFavoritePreferences { ExcludedPaths = preferences.ExcludedPaths, RecentCheckpoint = checkpoint };
                Save("settings.json", nextPreferences); preferences = nextPreferences;
            }
        });
        internal async Task<DateTime> Checkpoint() { await Ready; await gate.WaitAsync(); try { return preferences.RecentCheckpoint; } finally { gate.Release(); } }
        internal async Task Flush() { await Ready; if (heldForTransfer) return; await gate.WaitAsync(); gate.Release(); }
        internal async Task<IDisposable> Hold() {
            try { await Ready; } catch { /* A valid backup must still be restorable after corruption. */ }
            await gate.WaitAsync(); heldForTransfer = true;
            return new Lease(() => { heldForTransfer = false; gate.Release(); });
        }
        private sealed class Lease : IDisposable
        {
            private Action release;
            internal Lease(Action release) { this.release = release; }
            public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
        }
    }
}
