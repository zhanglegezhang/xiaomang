using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShareX
{
    internal static class LocalDiaryManager
    {
        internal const int ShortcutLimit = 6;
        internal const string ProjectUrl = "https://github.com/codexu/note-gen";
        internal static string DataFolder => Path.Combine(Program.PersonalFolder, "Diary");

        internal sealed class DiaryEntry
        {
            public string path { get; set; }
            public bool folder { get; set; }
            public ulong edited { get; set; }
            public ulong opens { get; set; }
            public ulong last_open { get; set; }
            internal string DisplayName => folder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        }
        private sealed class ActivityStore
        {
            public int version { get; set; }
            public List<DiaryEntry> entries { get; set; }
        }
        internal sealed record Shortcuts(DiaryEntry[] Recent, DiaryEntry[] Frequent);
        internal static string ActivityPath => Path.Combine(DataFolder, "activity-v1.json");
        internal static Shortcuts ReadShortcuts(string dataFolder)
        {
            string notes = Path.GetFullPath(Path.Combine(dataFolder, "notes"));
            if (!Directory.Exists(notes)) return new Shortcuts([], []);
            string activity = Path.Combine(dataFolder, "activity-v1.json");
            List<DiaryEntry> entries;
            if (File.Exists(activity))
            {
                if ((File.GetAttributes(activity) & FileAttributes.ReparsePoint) != 0) throw new IOException("统计文件不能是链接");
                using FileStream stream = new(activity, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 8 * 1024 * 1024) throw new IOException("日记统计文件过大");
                ActivityStore store = JsonSerializer.Deserialize<ActivityStore>(stream);
                if (store?.version != 1 || store.entries == null || store.entries.Count > 20000) throw new IOException("日记统计格式无效");
                entries = store.entries;
            }
            else
            {
                // One-time compatibility fallback for existing diaries before their first updated launch.
                // Metadata only; never load note bodies or initialize personal storage.
                entries = new List<DiaryEntry>();
                Scan(notes, "", 0);
                void Scan(string directory, string relative, int depth)
                {
                    if (depth > 64 || entries.Count >= 20000 || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                    foreach (string item in Directory.EnumerateFileSystemEntries(directory))
                    {
                        if (entries.Count >= 20000) break;
                        string name = Path.GetFileName(item);
                        if (name.StartsWith('.')) continue;
                        FileAttributes attributes = File.GetAttributes(item);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        bool folder = (attributes & FileAttributes.Directory) != 0;
                        if (!folder && !IsNote(item)) continue;
                        string path = string.IsNullOrEmpty(relative) ? name : relative + "/" + name;
                        string content = folder ? Path.Combine(item, ".xiaomang-folder.md") : item;
                        ulong edited = File.Exists(content) && (File.GetAttributes(content) & FileAttributes.ReparsePoint) == 0
                            ? (ulong)Math.Max(0, new DateTimeOffset(File.GetLastWriteTimeUtc(content)).ToUnixTimeMilliseconds()) : 0;
                        entries.Add(new DiaryEntry { path = path, folder = folder, edited = edited });
                        if (folder) Scan(item, path, depth + 1);
                    }
                }
            }
            return SelectShortcuts(entries.Where(entry => Available(notes, entry)));
        }
        internal static Shortcuts SelectShortcuts(IEnumerable<DiaryEntry> source)
        {
            var entries = source.Where(e => e != null && !string.IsNullOrWhiteSpace(e.path))
                .DistinctBy(e => e.path, StringComparer.OrdinalIgnoreCase).ToArray();
            DiaryEntry[] recent = entries.Where(e => e.edited > 0).OrderByDescending(e => e.edited)
                .ThenBy(e => e.path, StringComparer.OrdinalIgnoreCase).Take(ShortcutLimit).ToArray();
            var selected = recent.Select(e => e.path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            DiaryEntry[] frequent = entries.Where(e => e.opens > 0 && !selected.Contains(e.path)).OrderByDescending(e => e.opens)
                .ThenByDescending(e => e.last_open).ThenBy(e => e.path, StringComparer.OrdinalIgnoreCase).Take(ShortcutLimit).ToArray();
            return new Shortcuts(recent, frequent);
        }
        private static bool IsNote(string path) => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase);
        private static bool Available(string notes, DiaryEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.path) || entry.path.Contains('\\')) return false;
            string[] parts = entry.path.Split('/');
            if (parts.Length > 65 || parts.Any(p => string.IsNullOrWhiteSpace(p) || p.StartsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return false;
            string item = notes;
            if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) return false;
            foreach (string part in parts)
            {
                item = Path.Combine(item, part);
                if (!Path.Exists(item) || (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) return false;
            }
            return entry.folder ? Directory.Exists(item) : File.Exists(item) && IsNote(item);
        }

        internal static async Task OpenAsync(string notePath = null)
        {
            string executable = Path.Combine(AppContext.BaseDirectory, "ThirdParty", "LocalDiary", "xiaomang-diary.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("未找到本地日记程序，请检查程序文件是否完整。", executable);
            Directory.CreateDirectory(DataFolder);
            ProcessStartInfo start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)
            };
            start.ArgumentList.Add("--data-dir");
            start.ArgumentList.Add(Path.GetFullPath(DataFolder));
            if (!string.IsNullOrEmpty(notePath))
            {
                if (!Available(Path.Combine(DataFolder, "notes"), new DiaryEntry { path = notePath, folder = Directory.Exists(Path.Combine(DataFolder, "notes", notePath)) }))
                    throw new IOException("日记项目已不存在，请刷新后重试。");
                start.ArgumentList.Add("--open-note");
                start.ArgumentList.Add(notePath);
            }
            // NoteGen's single-instance handler restores and focuses an existing diary window.
            // The independent diary process is deliberately not bound to Xiaomang's lifetime.
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("本地日记启动失败。");
            await Task.Delay(700);
            if (process.HasExited && process.ExitCode != 0)
                throw new InvalidOperationException("本地日记启动失败。请确认已安装 Microsoft Edge WebView2 运行环境。");
        }
    }
}
