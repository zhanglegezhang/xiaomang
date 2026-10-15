using ShareX.HelpersLib;
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;

namespace ShareX
{
    internal static class DailyNewsStorageMaintenance
    {
        private const string CacheMarker = ".clear-cache-on-startup";
        private static readonly EnumerationOptions NoLinks = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        private static readonly string[] TransactionFiles =
            { "old.json", "old.html", "new.json", "new.html", "prepared", "committed" };

        public static string NewsFolder(string personalFolder) => Path.Combine(personalFolder, "daily_news");
        public static string CacheFolder(string personalFolder) => Path.Combine(personalFolder, "DailyNewsWebView");

        public static long GetFolderSize(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                long size = 0;
                if (!Directory.Exists(path)) return size;
                foreach (string file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    }))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    size += new FileInfo(file).Length;
                }
                return size;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return 0;
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024d * 1024 * 1024)).ToString("0.##") + " GB";
            if (bytes >= 1024L * 1024) return (bytes / (1024d * 1024)).ToString("0.##") + " MB";
            if (bytes >= 1024L) return (bytes / 1024d).ToString("0.##") + " KB";
            return bytes + " B";
        }

        public static void ScheduleCacheCleanup(string personalFolder)
        {
            string folder = CacheFolder(personalFolder);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, CacheMarker), DateTime.Now.ToString("O"));
        }

        public static void ApplyPendingCacheCleanup(string personalFolder)
        {
            string folder = CacheFolder(personalFolder);
            string marker = Path.Combine(folder, CacheMarker);
            if (!File.Exists(marker)) return;
            try
            {
                foreach (string child in Directory.EnumerateFileSystemEntries(folder).ToArray())
                {
                    if (string.Equals(child, marker, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.Exists(child)) Directory.Delete(child, true);
                    else File.Delete(child);
                }
                File.Delete(marker);
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "资讯网页缓存清理失败。下次启动将重试。");
            }
        }

        public static int ClearSavedNews(string personalFolder)
        {
            string folder = NewsFolder(personalFolder);
            if (!Directory.Exists(folder)) return 0;
            int count = 0;
            // News documents are stored at the root. Never recurse into a junction or
            // a user-created subdirectory when deleting files.
            foreach (string file in Directory.EnumerateFiles(folder, "*", NoLinks).ToArray())
            {
                string name = Path.GetFileName(file);
                if (!IsGeneratedNewsName(name, ".html") && !IsGeneratedNewsName(name, ".json")) continue;
                File.Delete(file);
                count++;
            }
            // Remove only our known interrupted-write files; unknown files and linked
            // directories must remain untouched.
            foreach (string directory in Directory.EnumerateDirectories(folder, "*.transaction", NoLinks).ToArray())
            {
                if (!IsGeneratedNewsName(Path.GetFileName(directory), ".transaction")) continue;
                foreach (string file in Directory.EnumerateFiles(directory, "*", NoLinks).ToArray())
                {
                    if (!TransactionFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal)) continue;
                    File.Delete(file);
                    count++;
                }
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            return count;
        }

        private static bool IsGeneratedNewsName(string name, string extension)
        {
            if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return false;
            string stem = name[..^extension.Length];
            if (!stem.StartsWith("国内资讯_", StringComparison.Ordinal) &&
                !stem.StartsWith("国际资讯_", StringComparison.Ordinal)) return false;
            return DateTime.TryParseExact(stem[5..], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _);
        }
    }
}
