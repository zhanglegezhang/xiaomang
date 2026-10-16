using System;
using System.Collections.Generic;
using System.IO;

namespace ShareX
{
    // Bounded content cache; metadata is checked on every search. Watcher events also
    // invalidate same-size writes whose timestamps were preserved by an external editor.
    internal static class UnifiedSearchCache
    {
        private sealed record Entry(long Length, DateTime Modified, string Text);
        private static readonly object sync = new();
        private static readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
        private const long MaximumBytes = 64L * 1024 * 1024;
        private static long bytes;
        private static long revision;
        private static string root;
        private static FileSystemWatcher watcher;
        private static bool watcherHealthy;

        internal static void Prepare(string directory)
        {
            lock (sync)
            {
                if (string.Equals(root, directory, StringComparison.OrdinalIgnoreCase) && watcher != null && watcherHealthy) return;
                watcher?.Dispose(); watcher = null; root = directory;
                watcherHealthy = false;
                entries.Clear(); bytes = 0; revision++;
                if (!Directory.Exists(directory)) return;
                try
                {
                    watcher = new FileSystemWatcher(directory)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                    };
                    watcher.Changed += (_, e) => Invalidate(e.FullPath);
                    watcher.Deleted += (_, e) => Invalidate(e.FullPath);
                    watcher.Created += (_, e) => Invalidate(e.FullPath);
                    watcher.Renamed += (_, e) => { Invalidate(e.OldFullPath); Invalidate(e.FullPath); };
                    watcher.Error += (_, _) => { lock (sync) { watcherHealthy = false; entries.Clear(); bytes = 0; revision++; } };
                    watcherHealthy = true;
                    watcher.EnableRaisingEvents = true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                { watcher?.Dispose(); watcher = null; watcherHealthy = false; }
            }
        }

        private static void Invalidate(string path)
        {
            lock (sync)
            {
                revision++;
                if (entries.Remove(path, out var old)) bytes -= old.Text.Length * 2L;
                else
                {
                    // Directory renames/deletions may not produce individual child events.
                    string prefix = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var descendants = new List<string>();
                    foreach (string key in entries.Keys)
                        if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) descendants.Add(key);
                    foreach (string key in descendants)
                    { bytes -= entries[key].Text.Length * 2L; entries.Remove(key); }
                }
            }
        }

        internal static string Read(string path, out bool oversized)
        {
            var info = new FileInfo(path);
            long length = info.Length;
            DateTime modified = info.LastWriteTimeUtc;
            oversized = length > 4 * 1024 * 1024;
            if (oversized) { Invalidate(path); return ""; }
            long readRevision;
            lock (sync)
            {
                readRevision = revision;
                if (watcherHealthy && entries.TryGetValue(path, out var entry) && entry.Length == length && entry.Modified == modified)
                    return entry.Text;
            }
            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > 4 * 1024 * 1024) { oversized = true; return ""; }
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }
            info.Refresh();
            lock (sync)
            {
                if (watcherHealthy && revision == readRevision && info.Exists && info.Length == length && info.LastWriteTimeUtc == modified)
                {
                    if (entries.Remove(path, out var old)) bytes -= old.Text.Length * 2L;
                    long size = text.Length * 2L;
                    // Stop retaining new contents when full; never reduce the search scope.
                    if (entries.Count < 20000 && bytes + size <= MaximumBytes)
                    { entries[path] = new Entry(length, modified, text); bytes += size; }
                }
            }
            return text;
        }
    }
}
