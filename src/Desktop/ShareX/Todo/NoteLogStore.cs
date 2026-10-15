using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    public enum NoteLogKind { Todo, Clipboard, Favorites }

    public sealed class NoteLogEntry
    {
        public NoteLogKind Kind { get; set; }
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// <summary>Daily human-readable archives for the three desktop note modules.</summary>
    public sealed class NoteLogStore
    {
        public const int RetentionDays = 100;
        private const string Separator = "────────────────────────────────────────";

        private sealed class StoredEntry
        {
            public DateTime Timestamp { get; set; }
            public string Action { get; set; } = "";
            public string Text { get; set; } = "";
        }

        private static readonly ConcurrentDictionary<string, object> CategoryLocks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex DateInFileName = new Regex(@"(?<date>\d{4}-\d{2}-\d{2})", RegexOptions.Compiled);
        private static readonly Regex LegacyClipboardHeader = new Regex(@"^\[(?<time>\d{2}:\d{2}:\d{2})\]$", RegexOptions.Compiled);
        private static readonly Regex ReadableHeader = new Regex(@"^\[(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]\s+(?<action>.+)$", RegexOptions.Compiled);
        private readonly string personalFolder;
        private readonly string rootFolder;
        private readonly ConcurrentDictionary<NoteLogKind, List<NoteLogEntry>> cache = new();
        private readonly ConcurrentDictionary<NoteLogKind, string> cacheFingerprints = new();
        private readonly ThreadLocal<bool> incompleteRead = new();
        private readonly object queueGate = new();
        private readonly Queue<NoteLogEntry> pendingWrites = new();
        private Task writeWorker;
        private Exception writeFailure;
        public event Action<Exception> WriteFailed;
        private object CategoryLock(NoteLogKind kind) => CategoryLocks.GetOrAdd(GetFolder(kind), _ => new object());

        public NoteLogStore(string personalFolder)
        {
            this.personalFolder = personalFolder ?? throw new ArgumentNullException(nameof(personalFolder));
            rootFolder = Path.Combine(personalFolder, "便签日志");
            TryReadOrMigrate(() => Directory.CreateDirectory(rootFolder));
        }

        public string RootFolder => rootFolder;
        public string GetFolder(NoteLogKind kind) => Path.Combine(rootFolder, GetDisplayName(kind));
        public static string GetDisplayName(NoteLogKind kind) => kind switch
        {
            NoteLogKind.Todo => "待办日志",
            NoteLogKind.Clipboard => "剪贴板日志",
            _ => "收藏夹日志"
        };
        private static string GetFilePrefix(NoteLogKind kind) => kind switch
        {
            NoteLogKind.Todo => "待办-",
            NoteLogKind.Clipboard => "剪贴板-",
            _ => "收藏夹-"
        };

        public void Append(NoteLogKind kind, string action, string text, DateTime? timestamp = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            DateTime time = timestamp ?? DateTime.Now;
            NoteLogEntry entry = new NoteLogEntry
            {
                Kind = kind,
                Timestamp = time,
                Action = string.IsNullOrWhiteSpace(action) ? "记录" : action.Trim(),
                Text = NormalizeText(text)
            };
            if (entry.Text.Length == 0) return;

            AppendEntry(entry);
        }

        public void EnqueueAppend(NoteLogKind kind, string action, string text, DateTime? timestamp = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            NoteLogEntry entry = new() { Kind = kind, Timestamp = timestamp ?? DateTime.Now,
                Action = string.IsNullOrWhiteSpace(action) ? "记录" : action.Trim(), Text = NormalizeText(text) };
            lock (queueGate)
            {
                pendingWrites.Enqueue(entry);
                if (writeWorker == null) writeWorker = Task.Run(DrainWrites);
            }
        }

        private void DrainWrites()
        {
            while (true)
            {
                NoteLogEntry entry;
                lock (queueGate)
                {
                    if (pendingWrites.Count == 0) { writeWorker = null; writeFailure = null; return; }
                    entry = pendingWrites.Peek();
                }
                try { AppendEntry(entry); }
                catch (Exception error)
                {
                    lock (queueGate) { writeFailure = error; writeWorker = null; }
                    WriteFailed?.Invoke(error);
                    return; // The failed head and everything after it remain available for retry.
                }
                lock (queueGate) { pendingWrites.Dequeue(); writeFailure = null; }
            }
        }

        public async Task FlushAsync()
        {
            Task worker;
            lock (queueGate)
            {
                if (pendingWrites.Count > 0 && writeWorker == null) writeWorker = Task.Run(DrainWrites);
                worker = writeWorker;
            }
            while (worker != null)
            {
                await worker.ConfigureAwait(false);
                lock (queueGate)
                {
                    if (writeFailure != null) throw new IOException("便签日志尚未保存成功，内容仍保留，请重试。", writeFailure);
                    worker = writeWorker;
                }
            }
        }

        private void AppendEntry(NoteLogEntry entry)
        {
            NoteLogKind kind = entry.Kind;
            DateTime time = entry.Timestamp;
            lock (CategoryLock(kind))
            {
                string folder = GetFolder(kind);
                Directory.CreateDirectory(folder);
                string filePath = Path.Combine(folder, GetFilePrefix(kind) + time.ToString("yyyy-MM-dd") + ".txt");
                EnsureReadableFile(filePath, kind);
                byte[] bytes = new UTF8Encoding(false).GetBytes(SerializeReadable(entry));
                using (FileStream stream = new(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                {
                    long length = stream.Length;
                    stream.Position = length;
                    try { stream.Write(bytes); stream.Flush(true); }
                    catch { stream.SetLength(length); stream.Flush(true); throw; }
                }
                // Never stamp an old cache with a new disk fingerprint, including external writes.
                cache.TryRemove(kind, out _);
                cacheFingerprints.TryRemove(kind, out _);
                CleanupExpiredCore(kind, time.Date);
            }
        }

        public IReadOnlyList<NoteLogEntry> Read(NoteLogKind kind)
        {
            return ReadSnapshot(kind).entries;
        }

        internal (List<NoteLogEntry> entries, string fingerprint) ReadSnapshot(NoteLogKind kind)
        {
            List<NoteLogEntry> entries = null;
            string fingerprint = "";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                lock (CategoryLock(kind))
                {
                    CleanupExpiredCore(kind, DateTime.Today);
                    fingerprint = GetFingerprintCore(kind);
                    if (cache.TryGetValue(kind, out entries) && cacheFingerprints.TryGetValue(kind, out string cached) && cached == fingerprint)
                        return (entries.ToList(), fingerprint);
                }
                // Disk parsing and sorting must not block appends or other categories.
                incompleteRead.Value = false;
                entries = LoadEntries(kind).OrderByDescending(x => x.Timestamp).ToList();
                lock (CategoryLock(kind))
                {
                    if (fingerprint != GetFingerprintCore(kind)) continue;
                    if (!incompleteRead.Value) { cache[kind] = entries; cacheFingerprints[kind] = fingerprint; }
                    return (entries.ToList(), fingerprint);
                }
            }
            return (entries ?? new List<NoteLogEntry>(), fingerprint);
        }

        public string GetFingerprint(NoteLogKind kind)
        {
            return GetFingerprintCore(kind);
        }

        public void Invalidate(NoteLogKind kind)
        {
            cache.TryRemove(kind, out _);
            cacheFingerprints.TryRemove(kind, out _);
        }

        public void CleanupExpired()
        {
            foreach (NoteLogKind kind in Enum.GetValues<NoteLogKind>())
                lock (CategoryLock(kind)) CleanupExpiredCore(kind, DateTime.Today);
        }

        private IEnumerable<NoteLogEntry> LoadEntries(NoteLogKind kind)
        {
            List<NoteLogEntry> entries = new List<NoteLogEntry>();
            string folder = GetFolder(kind);
            if (Directory.Exists(folder))
            {
                foreach (string file in GetFilesSafely(folder, "*.txt")) TryReadOrMigrate(() => ReadCurrentFile(file, kind, entries));
            }
            if (kind == NoteLogKind.Clipboard)
            {
                string legacyFolder = Path.Combine(personalFolder, "剪切板归档");
                if (Directory.Exists(legacyFolder))
                {
                    foreach (string file in GetFilesSafely(legacyFolder, "*.txt")) TryReadOrMigrate(() => ReadLegacyClipboardFile(file, entries));
                }
            }
            return entries;
        }

        private void CleanupExpiredCore(NoteLogKind kind, DateTime today)
        {
            DateTime cutoff = today.AddDays(-(RetentionDays - 1));
            bool removed = DeleteExpiredFiles(GetFolder(kind), cutoff);
            removed |= DeleteExpiredFiles(Path.Combine(rootFolder, "Legacy", GetDisplayName(kind)), cutoff, "*.jsonl");
            if (kind == NoteLogKind.Clipboard) removed |= DeleteExpiredFiles(Path.Combine(personalFolder, "剪切板归档"), cutoff);
            if (removed)
            {
                cache.TryRemove(kind, out _);
                cacheFingerprints.TryRemove(kind, out _);
            }
        }

        private string GetFingerprintCore(NoteLogKind kind)
        {
            StringBuilder builder = new StringBuilder();
            AppendFolderFingerprint(builder, GetFolder(kind));
            if (kind == NoteLogKind.Clipboard) AppendFolderFingerprint(builder, Path.Combine(personalFolder, "剪切板归档"));
            return builder.ToString();
        }

        private static void AppendFolderFingerprint(StringBuilder builder, string folder)
        {
            builder.Append(folder).Append('|');
            if (!Directory.Exists(folder))
            {
                builder.Append("missing;");
                return;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*.txt").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        FileInfo info = new FileInfo(file);
                        builder.Append(info.Name).Append(':').Append(info.Length).Append(':')
                            .Append(info.LastWriteTimeUtc.Ticks).Append(';');
                    }
                    catch (IOException)
                    {
                        builder.Append(Path.GetFileName(file)).Append(":changing;");
                    }
                }
            }
            catch (IOException)
            {
                builder.Append("changing;");
            }
            catch (UnauthorizedAccessException)
            {
                builder.Append("unavailable;");
            }
        }

        private bool DeleteExpiredFiles(string folder, DateTime cutoff, string pattern = "*.txt")
        {
            if (!Directory.Exists(folder)) return false;
            bool removed = false;
            foreach (string file in GetFilesSafely(folder, pattern))
            {
                Match match = DateInFileName.Match(Path.GetFileName(file));
                if (match.Success && DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) && date.Date < cutoff)
                {
                    TryReadOrMigrate(() => { File.Delete(file); removed = true; });
                }
            }
            return removed;
        }

        private void MigrateJsonLogsCore()
        {
            foreach (NoteLogKind kind in Enum.GetValues<NoteLogKind>())
            {
                string folder = GetFolder(kind);
                if (!Directory.Exists(folder)) continue;
                foreach (string file in GetFilesSafely(folder, "*.txt")) TryReadOrMigrate(() => EnsureReadableFile(file, kind));
            }
        }

        private string[] GetFilesSafely(string folder, string pattern)
        {
            string[] files = Array.Empty<string>();
            TryReadOrMigrate(() => files = Directory.GetFiles(folder, pattern));
            return files;
        }

        private void TryReadOrMigrate(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                incompleteRead.Value = true;
                System.Diagnostics.Trace.TraceWarning("Note log operation deferred: " + error.Message);
            }
        }

        private void EnsureReadableFile(string file, NoteLogKind kind)
        {
            if (!File.Exists(file)) return;
            string first = File.ReadLines(file, Encoding.UTF8).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
            if (first == null || !first.TrimStart().StartsWith("{", StringComparison.Ordinal)) return;

            List<NoteLogEntry> entries = new List<NoteLogEntry>();
            ReadJsonFile(file, kind, entries);
            string legacyFolder = Path.Combine(rootFolder, "Legacy", GetDisplayName(kind));
            Directory.CreateDirectory(legacyFolder);
            string legacyPath = Path.Combine(legacyFolder, Path.GetFileNameWithoutExtension(file) + ".jsonl");
            if (!File.Exists(legacyPath)) File.Copy(file, legacyPath);
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, string.Concat(entries.OrderBy(x => x.Timestamp).Select(SerializeReadable)), new UTF8Encoding(false));
            File.Move(temporary, file, true);
            cache.TryRemove(kind, out _);
            cacheFingerprints.TryRemove(kind, out _);
        }

        private static string SerializeReadable(NoteLogEntry entry)
        {
            string text = string.Join(Environment.NewLine, entry.Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(line => line == Separator || IsEscapedSeparator(line) ? "\\" + line : line));
            return $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Action}{Environment.NewLine}{text}{Environment.NewLine}{Separator}{Environment.NewLine}";
        }

        private static bool IsEscapedSeparator(string line) => line.StartsWith("\\", StringComparison.Ordinal) &&
            line.TrimStart('\\').StartsWith(Separator, StringComparison.Ordinal);

        private static void ReadCurrentFile(string file, NoteLogKind kind, List<NoteLogEntry> entries)
        {
            string first = ReadLinesShared(file).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
            if (first != null && first.TrimStart().StartsWith("{", StringComparison.Ordinal)) ReadJsonFile(file, kind, entries);
            else ReadReadableFile(file, kind, entries);
        }

        private static void ReadJsonFile(string file, NoteLogKind kind, List<NoteLogEntry> entries)
        {
            foreach (string line in ReadLinesShared(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    StoredEntry stored = JsonSerializer.Deserialize<StoredEntry>(line);
                    if (stored != null && !string.IsNullOrWhiteSpace(stored.Text))
                        entries.Add(new NoteLogEntry { Kind = kind, Timestamp = stored.Timestamp, Action = stored.Action, Text = stored.Text });
                }
                catch (JsonException) { }
            }
        }

        private static void ReadReadableFile(string file, NoteLogKind kind, List<NoteLogEntry> entries)
        {
            using IEnumerator<string> lines = ReadLinesShared(file).GetEnumerator();
            while (lines.MoveNext())
            {
                Match header = ReadableHeader.Match(lines.Current);
                if (!header.Success) continue;
                List<string> text = new List<string>();
                while (lines.MoveNext() && lines.Current != Separator)
                {
                    string line = lines.Current;
                    if (IsEscapedSeparator(line)) line = line.Substring(1);
                    text.Add(line);
                }
                if (DateTime.TryParseExact(header.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out DateTime time))
                {
                    string value = NormalizeText(string.Join(Environment.NewLine, text));
                    if (value.Length > 0) entries.Add(new NoteLogEntry { Kind = kind, Timestamp = time, Action = header.Groups["action"].Value, Text = value });
                }
            }
        }

        private static void ReadLegacyClipboardFile(string file, List<NoteLogEntry> entries)
        {
            if (!TryGetFileDate(file, out DateTime fileDate)) return;
            using IEnumerator<string> lines = ReadLinesShared(file).GetEnumerator();
            while (lines.MoveNext())
            {
                Match header = LegacyClipboardHeader.Match(lines.Current);
                if (!header.Success) continue;
                TimeSpan.TryParseExact(header.Groups["time"].Value, "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out TimeSpan time);
                List<string> textLines = new List<string>();
                while (lines.MoveNext() && !IsLegacySeparator(lines.Current)) textLines.Add(lines.Current);
                string text = NormalizeText(string.Join(Environment.NewLine, textLines));
                if (text.Length > 0) entries.Add(new NoteLogEntry { Kind = NoteLogKind.Clipboard, Timestamp = fileDate.Date.Add(time), Action = "记录", Text = text });
            }
        }

        private static IEnumerable<string> ReadLinesShared(string file)
        {
            using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Ignore bytes appended after opening this snapshot; a subsequent fingerprint check refreshes them.
            using StreamReader reader = new(new SnapshotReadStream(stream), Encoding.UTF8, true);
            while (reader.ReadLine() is string line) yield return line;
        }

        private sealed class SnapshotReadStream : Stream
        {
            private readonly Stream source;
            private long remaining;
            internal SnapshotReadStream(Stream source) { this.source = source; remaining = source.Length; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = source.Read(buffer, offset, (int)Math.Min(count, remaining));
                remaining -= read;
                return read;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static bool IsLegacySeparator(string line) => line.Length >= 20 && line.All(c => c == '-');
        private static bool TryGetFileDate(string file, out DateTime date)
        {
            date = default;
            Match match = DateInFileName.Match(Path.GetFileName(file));
            return match.Success && DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
        private static string NormalizeText(string text)
        {
            string normalized = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            int end = lines.Length;
            while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
            return string.Join(Environment.NewLine, lines.Take(end));
        }
    }
}
