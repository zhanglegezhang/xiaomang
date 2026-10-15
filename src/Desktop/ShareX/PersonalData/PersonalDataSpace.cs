using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShareX
{
    internal sealed class PersonalDataSpace
    {
        internal const long Margin = 32L * 1024 * 1024;
        private readonly PersonalDataTransferControl control;
        private readonly Dictionary<string, (string Volume, long Available)> paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (long Needed, long Available)> volumes = new(StringComparer.OrdinalIgnoreCase);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumePathName(string path, StringBuilder volume, uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetDiskFreeSpaceEx(string path, out ulong available, out ulong total, out ulong free);
        internal PersonalDataSpace(PersonalDataTransferControl control = null) { this.control = control; }
        internal static long Allocated(long bytes) => checked((bytes + 4095) / 4096 * 4096);
        internal void Add(string path, long bytes)
        {
            control?.Token.ThrowIfCancellationRequested();
            if (bytes < 0) throw new InvalidDataException("空间估算无效。");
            path = Path.GetFullPath(path);
            if (!paths.TryGetValue(path, out var disk)) {
                if (control?.SpaceProbe != null) disk = control.SpaceProbe(path);
                else {
                    string parent = path;
                    while (!Directory.Exists(parent) && !File.Exists(parent)) parent = Path.GetDirectoryName(parent) ?? throw new IOException("无法确认数据磁盘。");
                    var volume = new StringBuilder(32768);
                    if (!GetVolumePathName(parent, volume, (uint)volume.Capacity) || !GetDiskFreeSpaceEx(volume.ToString(), out var available, out _, out _))
                        throw new IOException("无法读取磁盘可用空间：" + path + "。请确认磁盘已连接且有访问权限。");
                    disk = (volume.ToString(), checked((long)Math.Min(available, (ulong)long.MaxValue)));
                }
                if (string.IsNullOrEmpty(disk.Volume) || disk.Available < 0) throw new IOException("磁盘空间信息无效。");
                paths.Add(path, disk);
            }
            bool exists = volumes.TryGetValue(disk.Volume, out var budget);
            volumes[disk.Volume] = (checked(budget.Needed + bytes), !exists ? disk.Available : Math.Min(budget.Available, disk.Available));
        }
        internal void Check()
        {
            foreach (var disk in volumes) {
                control?.Token.ThrowIfCancellationRequested();
                long needed = checked(disk.Value.Needed + Margin);
                if (disk.Value.Available < needed) throw new IOException($"磁盘空间不足（{disk.Key}）：预计需要 {needed / 1048576d:F1} MB，剩余 {disk.Value.Available / 1048576d:F1} MB。尚未替换原数据，请释放空间或更换存储位置。");
            }
        }
        internal static long ZipBytes(long bytes, int files, long metadata = 0) => checked(bytes + bytes / 100 + files * 4096L + metadata);
        internal static (long Bytes, int Files) LocalPayload(PersonalDataPaths paths, JObject app, IReadOnlyCollection<PersonalDataKind> kinds, CancellationToken token)
        {
            long bytes = 0; int count = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void FileSize(string file) { token.ThrowIfCancellationRequested(); if (File.Exists(file) && seen.Add(file)) { PersonalDataArchive.SafePath(file); bytes = checked(bytes + new FileInfo(file).Length); count++; } }
            void Tree(string root, Func<string, bool> filter = null) { foreach (string file in PersonalDataArchive.Files(root, token: token)) if (filter == null || filter(file)) FileSize(file); }
            foreach (var kind in kinds) {
                token.ThrowIfCancellationRequested();
                if (kind is PersonalDataKind.Todo or PersonalDataKind.Clipboard or PersonalDataKind.Favorites) {
                    string field = kind == PersonalDataKind.Todo ? "Items" : kind == PersonalDataKind.Clipboard ? "ClipboardItems" : "FavoriteItems";
                    bytes = checked(bytes + Encoding.UTF8.GetByteCount((app["TodoSettings"]?[field] ?? new JArray()).ToString(Formatting.Indented)) + 1024); count++;
                    var logKind = kind == PersonalDataKind.Todo ? NoteLogKind.Todo : kind == PersonalDataKind.Clipboard ? NoteLogKind.Clipboard : NoteLogKind.Favorites;
                    string log = NoteLogStore.GetDisplayName(logKind);
                    Tree(Path.Combine(paths.Personal, "便签日志", log)); Tree(Path.Combine(paths.Personal, "便签日志", "Legacy", log));
                    if (kind == PersonalDataKind.Todo) Tree(Path.Combine(paths.Personal, "VoiceNotes"));
                    if (kind == PersonalDataKind.Clipboard) Tree(Path.Combine(paths.Personal, "剪切板归档"));
                    if (kind == PersonalDataKind.Favorites) foreach (string name in FileFavoritesStore.FileNames) FileSize(Path.Combine(paths.Personal, "data", name));
                } else if (kind == PersonalDataKind.Diary) {
                    foreach (string part in new[] { "notes", "attachments", "history", "trash" }) Tree(Path.Combine(paths.Diary, part));
                    foreach (string name in PersonalDataArchive.DiaryFiles) FileSize(Path.Combine(paths.Diary, name));
                } else if (kind == PersonalDataKind.InputMethod) {
                    FileSize(Path.Combine(paths.Input, "config.toml"));
                    foreach (string name in PersonalDataArchive.InputFiles.Concat(PersonalDataArchive.GlossaryFiles)) FileSize(Path.Combine(paths.Input, name));
                    Tree(Path.Combine(paths.Input, "dicts")); Tree(Path.Combine(paths.Input, "codes"));
                } else if (kind == PersonalDataKind.Media) Tree(paths.Media, PersonalDataArchive.IsMedia);
                else if (kind == PersonalDataKind.Temporary) Tree(Path.Combine(paths.Personal, "临时文件夹"), file => !Path.GetFileName(file).StartsWith('.') && !file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
                else { FileSize(kind == PersonalDataKind.Hotkeys ? paths.Hotkeys : Path.Combine(paths.Personal, "ApplicationConfig.json")); }
            }
            return (bytes, count);
        }
        internal static void CheckImport(PersonalDataPaths paths, JObject app, PersonalDataManifest manifest, IReadOnlyCollection<PersonalDataKind> selected, bool extracted, PersonalDataTransferControl control = null)
        {
            var token = control?.Token ?? CancellationToken.None;
            var budget = new PersonalDataSpace(control);
            long extraction = 0;
            foreach (var file in manifest.Files) {
                token.ThrowIfCancellationRequested();
                var kind = Enum.Parse<PersonalDataKind>(file.Path.Split('/')[0]); if (!selected.Contains(kind)) continue;
                long bytes = Allocated(file.Bytes); extraction = checked(extraction + bytes);
                budget.Add(kind == PersonalDataKind.InputMethod ? paths.Input : kind == PersonalDataKind.Media ? paths.Media : paths.Personal, bytes);
            }
            long directoryBytes = 0;
            foreach (string dir in manifest.Directories) {
                token.ThrowIfCancellationRequested(); var kind = Enum.Parse<PersonalDataKind>(dir.Split('/')[0]); if (!selected.Contains(kind)) continue;
                directoryBytes += 4096;
                budget.Add(kind == PersonalDataKind.InputMethod ? paths.Input : kind == PersonalDataKind.Media ? paths.Media : paths.Personal, 4096);
            }
            budget.Add(paths.Personal, Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(manifest)) + 1024 * 1024);
            if (!extracted) budget.Add(paths.Personal, extraction + directoryBytes);
            var local = LocalPayload(paths, app, selected, token);
            budget.Add(paths.Personal, ZipBytes(local.Bytes, local.Files, Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(manifest))));
            // Export creates small generated manifests/config files under the system temp path.
            budget.Add(Path.GetTempPath(), Math.Max(1024 * 1024, Encoding.UTF8.GetByteCount(app.ToString(Formatting.Indented))));
            budget.Check();
        }
    }
}
