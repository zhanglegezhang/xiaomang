using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace ShareX
{
    internal enum PersonalDataKind { Todo, Clipboard, Favorites, Diary, Hotkeys, Temporary, Preferences, InputMethod, Media }
    internal sealed class PersonalDataPaths
    {
        internal string Personal, Input, Media, Hotkeys;
        internal Func<string, string, string, string, Task> ConfigCodec;
        internal string Diary => Path.Combine(Personal, "Diary");
    }
    internal sealed class PersonalDataFile
    {
        public string Path { get; set; }
        public long Bytes { get; set; }
        public string Sha256 { get; set; }
        public DateTime? LastWriteUtc { get; set; }
    }
    internal sealed class PersonalDataManifest
    {
        public string Kind { get; set; } = "xiaomang-personal-data";
        public int Format { get; set; } = 1;
        public string ProductVersion { get; set; }
        public DateTime CreatedUtc { get; set; }
        public List<PersonalDataKind> Categories { get; set; } = new();
        public List<PersonalDataFile> Files { get; set; } = new();
        public List<string> Directories { get; set; } = new();
        public int AdditionalDataVersion { get; set; }
        public List<string> MissingVoiceAttachments { get; set; } = new();
        public List<string> InvalidDiaryFiles { get; set; } = new();
    }
    internal static class PersonalDataArchive
    {
        internal const long MaxBytes = 20L * 1024 * 1024 * 1024;
        internal const int MaxFiles = 100000;
        internal const string ManifestName = "personal-data.json";
        internal static readonly PersonalDataKind[] Defaults = Enum.GetValues<PersonalDataKind>().Where(kind => kind != PersonalDataKind.Media).ToArray();
        internal static string Label(PersonalDataKind kind) => kind switch { PersonalDataKind.Todo => "待办", PersonalDataKind.Clipboard => "剪贴板", PersonalDataKind.Favorites => "收藏夹", PersonalDataKind.Diary => "本地日记", PersonalDataKind.Hotkeys => "快捷键设置", PersonalDataKind.Temporary => "临时文档", PersonalDataKind.Preferences => "全局设置偏好", PersonalDataKind.InputMethod => "输入法个人数据", _ => "截图和录屏文件" };
        internal static readonly string[] InputFiles = { "user.tsv", "user-words.tsv", "user-ngram.tsv", "user-choices.tsv", "user-english.tsv", "user-typos.tsv", "user-vocab.tsv" };
        internal static readonly string[] GlossaryFiles = { "user-glossary-en.tsv", "user-glossary-ja.tsv", "user-glossary-es.tsv" };
        internal static readonly string[] DiaryFiles = { "item-colors.json", "window-state.json", "activity-v1.json" };
        internal static readonly string[] MediaExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".avif", ".heic", ".mp4", ".webm", ".avi", ".mkv", ".mov", ".wmv" };
        internal static bool IsMedia(string name) => MediaExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());
        internal static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        internal static void SafePath(string path)
        {
            for (var check = Path.GetFullPath(path); !string.IsNullOrEmpty(check); check = Path.GetDirectoryName(check))
                if ((File.Exists(check) || Directory.Exists(check)) && (File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) throw new IOException("数据路径含目录联接或符号链接，未修改数据。");
        }
        internal static string Relative(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Contains('\\') || value.StartsWith('/') || value.Length > 2000) throw new InvalidDataException("备份包含无效路径。");
            foreach (var segment in value.Split('/'))
                if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.Split('.')[0].ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" || System.Text.RegularExpressions.Regex.IsMatch(segment, "^(COM|LPT)[1-9](\\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new InvalidDataException("备份包含无效路径。");
            return value;
        }
        internal static IEnumerable<string> Files(string root, int depth = 0, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            SafePath(root);
            if (!Directory.Exists(root)) yield break;
            if (depth > 80) throw new IOException("数据目录层级超过支持范围。");
            foreach (var file in Directory.EnumerateFiles(root)) { token.ThrowIfCancellationRequested(); SafePath(file); yield return file; }
            foreach (var directory in Directory.EnumerateDirectories(root)) foreach (var file in Files(directory, depth + 1, token)) yield return file;
        }
        internal static void WriteJson(string path, object value)
        {
            SafePath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".writing-" + Guid.NewGuid().ToString("N");
            try { using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { var bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented)); stream.Write(bytes); stream.Flush(true); } File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static JObject ReadJson(string path, bool trustedLocal = false, long maxBytes = 32L * 1024 * 1024)
        {
            if (new FileInfo(path).Length > maxBytes) throw new InvalidDataException("配置文件过大。");
            using var reader = new JsonTextReader(new StreamReader(path, new UTF8Encoding(false, true))) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
            var value = JObject.Load(reader);
            if (reader.Read() || (!trustedLocal && value.DescendantsAndSelf().OfType<JProperty>().Any(property => new[] { "$type", "$ref", "$id", "$values" }.Contains(property.Name)))) throw new InvalidDataException("配置包含不支持的数据类型。");
            return value;
        }
        internal static long DiaryLimit(string name) => name == "activity-v1.json" ? 8L * 1024 * 1024 : name == "item-colors.json" ? 16L * 1024 * 1024 : 32L * 1024 * 1024;
        private static void ValidateDiaryData(string name, JObject value)
        {
            void ValidPath(string path) {
                Relative(path);
                if (path.Split('/').Any(part => part.StartsWith('.'))) throw new InvalidDataException("日记辅助记录路径无效。");
            }
            if (name == "activity-v1.json") {
                if (value["version"]?.Type != JTokenType.Integer || (int)value["version"] != 1 || value["entries"] is not JArray entries || entries.Count > 20000) throw new InvalidDataException("日记使用统计格式无效。");
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var token in entries) {
                    if (token is not JObject entry || entry["path"]?.Type != JTokenType.String || entry["folder"]?.Type != JTokenType.Boolean) throw new InvalidDataException("日记使用统计条目无效。");
                    string path = (string)entry["path"]; ValidPath(path);
                    if (!paths.Add(path)) throw new InvalidDataException("日记使用统计路径重复。");
                    foreach (string key in new[] { "edited", "opens", "last_open" })
                        if (entry[key]?.Type != JTokenType.Integer || !ulong.TryParse(entry[key].ToString(), out _)) throw new InvalidDataException("日记使用统计数值无效。");
                }
            } else if (name == "item-colors.json") {
                var colors = new HashSet<string>(new[] {
                    "#c74747", "#b96523", "#967519", "#368057", "#258387", "#3976b8", "#8f3030", "#884414", "#685312", "#24553a", "#1b5c61", "#284f7d", "#8755ad", "#b34f85", "#ce6473", "#8d6246", "#73808d", "#303841", "#a381c4", "#cb7aaa", "#729ac4", "#73a486", "#ce996c", "#acb5be", "#ef4444", "#f97316", "#eab308", "#16a34a", "#06b6d4", "#2563eb", "#7c3aed", "#d946ef", "#ec4899", "#a16207", "#64748b", "#111827", "#ff2020", "#ff7a00", "#ffcc00", "#00c853", "#00cfee", "#0066ff", "#8338ff", "#ed00e6", "#ff3b91", "#c07800"
                });
                if (value.Count > 20000) throw new InvalidDataException("日记图标颜色记录过多。");
                foreach (var property in value.Properties()) {
                    ValidPath(property.Name);
                    string color; bool solid = false;
                    if (property.Value.Type == JTokenType.String) color = (string)property.Value;
                    else if (property.Value is JObject appearance && appearance.Count == 2 && appearance["color"]?.Type == JTokenType.String && appearance["solid"]?.Type == JTokenType.Boolean) { color = (string)appearance["color"]; solid = (bool)appearance["solid"]; }
                    else throw new InvalidDataException("日记图标颜色格式无效。");
                    if (!colors.Contains(color) && !(color.Length == 0 && solid)) throw new InvalidDataException("日记图标颜色无效。");
                }
            } else if (name == "window-state.json") {
                foreach (string key in new[] { "width", "height" }) {
                    if (value[key]?.Type is not (JTokenType.Integer or JTokenType.Float)) throw new InvalidDataException("日记窗口尺寸格式无效。");
                    double size = (double)value[key];
                    if (!double.IsFinite(size) || size < 200 || size > 10000) throw new InvalidDataException("日记窗口尺寸无效。");
                }
                if (value["maximized"]?.Type != JTokenType.Boolean) throw new InvalidDataException("日记窗口状态无效。");
            } else throw new InvalidDataException("未知日记辅助文件。");
        }
        // Only malformed optional metadata is recoverable; unreadable files and corrupt ZIPs
        // remain fatal. Original bytes stay in the archive, never become live configuration.
        private static bool InvalidDiaryStream(string name, Stream stream, long length)
        {
            try {
                if (length > DiaryLimit(name)) return true;
                using var text = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, leaveOpen: true);
                using var reader = new JsonTextReader(text) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
                var value = JObject.Load(reader);
                if (reader.Read() || value.DescendantsAndSelf().OfType<JProperty>().Any(p => new[] { "$type", "$ref", "$id", "$values" }.Contains(p.Name))) return true;
                ValidateDiaryData(name, value); return false;
            } catch (Exception error) when (error is JsonException or InvalidDataException or DecoderFallbackException or OverflowException or FormatException) { return true; }
        }
        internal static bool InvalidDiaryFile(string name, string path)
        {
            SafePath(path); using var stream = File.OpenRead(path);
            return InvalidDiaryStream(name, stream, stream.Length);
        }
        internal static string DiaryWarning(IEnumerable<string> names)
        {
            var files = names.Distinct().ToArray();
            return files.Length == 0 ? "" : "日记辅助数据异常（" + string.Join("、", files) + "）。原件保留在备份中供核对；正文和附件可恢复，损坏的统计将重新生成，损坏的图标颜色和窗口状态使用默认值。";
        }
        private static bool Allowed(string path, IReadOnlyCollection<PersonalDataKind> selected)
        {
            var parts = Relative(path).Split('/');
            if (!Enum.TryParse<PersonalDataKind>(parts[0], out var kind) || parts[0] != kind.ToString() || !selected.Contains(kind) || parts.Length < 2) return false;
            var tail = string.Join('/', parts.Skip(1));
            return kind switch {
                PersonalDataKind.Todo => tail == "items.json" || tail.StartsWith("logs/") || tail.StartsWith("legacy/") || tail.StartsWith("VoiceNotes/"),
                PersonalDataKind.Clipboard => tail == "items.json" || tail.StartsWith("logs/") || tail.StartsWith("legacy/") || tail.StartsWith("archive/"),
                PersonalDataKind.Favorites => tail == "items.json" || tail.StartsWith("logs/") || tail.StartsWith("legacy/") || FileFavoritesStore.FileNames.Any(name => tail == "data/" + name),
                PersonalDataKind.Diary => new[] { "notes/", "attachments/", "history/", "trash/" }.Any(tail.StartsWith) || DiaryFiles.Contains(tail),
                PersonalDataKind.Hotkeys => tail == "settings.json",
                PersonalDataKind.Preferences => tail == "settings.json",
                PersonalDataKind.InputMethod => InputFiles.Contains(tail) || GlossaryFiles.Contains(tail) || tail == "config.toml" || tail.StartsWith("dicts/") || tail.StartsWith("codes/"),
                PersonalDataKind.Media => IsMedia(tail),
                _ => true
            };
        }
        internal static async Task Export(PersonalDataPaths paths, JObject app, JObject hotkeys, IReadOnlyCollection<PersonalDataKind> selected, string destination, string version, IProgress<string> progress = null, bool internalBackup = false, PersonalDataTransferControl control = null)
        {
            var token = control?.Token ?? CancellationToken.None; token.ThrowIfCancellationRequested();
            if (selected.Count == 0 || selected.Any(kind => !Enum.IsDefined(kind))) throw new InvalidDataException("请至少选择一类数据。");
            SafePath(destination); destination = Path.GetFullPath(destination);
            bool trustedBackup = internalBackup && Path.GetDirectoryName(destination).Equals(Path.GetFullPath(Path.Combine(paths.Personal, "PersonalDataBackups")), StringComparison.OrdinalIgnoreCase);
            foreach (var root in new[] { paths.Personal, paths.Input, paths.Media })
                if (!trustedBackup)
                if (destination.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择个人数据目录之外的备份位置。");
            var stage = Path.Combine(Path.GetTempPath(), "xiaomang-data-" + Guid.NewGuid().ToString("N"));
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingAudio = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string name, string source) { token.ThrowIfCancellationRequested(); name = Relative(name); SafePath(source); if (!sources.TryAdd(name, source)) throw new IOException("数据名称冲突。"); }
            void AddTree(string prefix, string root, Func<string, bool> filter = null) {
                foreach (var file in Files(root, token: token)) if (filter == null || filter(file)) Add(prefix + "/" + Path.GetRelativePath(root, file).Replace('\\', '/'), file);
                void Visit(string dir, int depth) {
                    token.ThrowIfCancellationRequested(); SafePath(dir); if (!Directory.Exists(dir)) return;
                    if (depth > 80 || directories.Count >= MaxFiles) throw new IOException("数据目录数量或层级超过限制。");
                    directories.Add(Relative(prefix + (dir == root ? "" : "/" + Path.GetRelativePath(root, dir).Replace('\\', '/'))));
                    foreach (var child in Directory.EnumerateDirectories(dir)) Visit(child, depth + 1);
                }
                if (filter == null) Visit(root, 0);
            }
            void JsonFile(string name, object value) {
                token.ThrowIfCancellationRequested();
                long size = Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(value, Formatting.Indented));
                if (size > 32 * 1024 * 1024) throw new IOException("单类记录清单超过 32 MB，当前不支持完整迁移该类别。");
                var budget = new PersonalDataSpace(control); budget.Add(stage, PersonalDataSpace.Allocated(size)); budget.Check();
                var file = Path.Combine(stage, name.Replace('/', Path.DirectorySeparatorChar)); WriteJson(file, value); Add(name, file);
            }
            try
            {
                Directory.CreateDirectory(stage);
                foreach (var kind in selected)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report("读取" + Label(kind) + "…");
                    if (kind is PersonalDataKind.Todo or PersonalDataKind.Clipboard or PersonalDataKind.Favorites)
                    {
                        var field = kind == PersonalDataKind.Todo ? "Items" : kind == PersonalDataKind.Clipboard ? "ClipboardItems" : "FavoriteItems";
                        var array = app["TodoSettings"]?[field] as JArray ?? new JArray();
                        JsonFile(kind + "/items.json", new JObject { ["Items"] = array.DeepClone() });
                        var logKind = kind == PersonalDataKind.Todo ? NoteLogKind.Todo : kind == PersonalDataKind.Clipboard ? NoteLogKind.Clipboard : NoteLogKind.Favorites;
                        AddTree(kind + "/logs", Path.Combine(paths.Personal, "便签日志", NoteLogStore.GetDisplayName(logKind)));
                        AddTree(kind + "/legacy", Path.Combine(paths.Personal, "便签日志", "Legacy", NoteLogStore.GetDisplayName(logKind)));
                        if (kind == PersonalDataKind.Todo) {
                            AddTree("Todo/VoiceNotes", Path.Combine(paths.Personal, "VoiceNotes"));
                            foreach (var item in array.OfType<JObject>()) if ((string)item["VoiceAudioRelativePath"] is string audio && audio.Length > 0) {
                                audio = Relative(audio.Replace('\\', '/'));
                                if (!audio.StartsWith("VoiceNotes/")) throw new IOException("待办录音路径无效，请先检查该待办。");
                                if (!sources.ContainsKey("Todo/" + audio)) {
                                    if (!trustedBackup) throw new IOException("待办存在缺失的录音附件，请先检查该待办。");
                                    missingAudio.Add(audio);
                                }
                            }
                        }
                        if (kind == PersonalDataKind.Clipboard) AddTree("Clipboard/archive", Path.Combine(paths.Personal, "剪切板归档"));
                        if (kind == PersonalDataKind.Favorites) foreach (string name in FileFavoritesStore.FileNames) {
                            string file = Path.Combine(paths.Personal, "data", name);
                            if (File.Exists(file)) { if (!trustedBackup) FileFavoritesStore.ValidateFile(name, file); Add("Favorites/data/" + name, file); }
                        }
                    }
                    else if (kind == PersonalDataKind.Diary) {
                        foreach (var dir in new[] { "notes", "attachments", "history", "trash" }) AddTree("Diary/" + dir, Path.Combine(paths.Diary, dir));
                        foreach (var name in DiaryFiles) if (File.Exists(Path.Combine(paths.Diary, name))) Add("Diary/" + name, Path.Combine(paths.Diary, name));
                    }
                    else if (kind == PersonalDataKind.Hotkeys) JsonFile("Hotkeys/settings.json", PersonalDataPreferences.Hotkeys(hotkeys));
                    else if (kind == PersonalDataKind.Preferences) JsonFile("Preferences/settings.json", PersonalDataPreferences.Export(app));
                    else if (kind == PersonalDataKind.Temporary) AddTree("Temporary", Path.Combine(paths.Personal, "临时文件夹"), file => !Path.GetFileName(file).StartsWith('.') && !file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
                    else if (kind == PersonalDataKind.Media) AddTree("Media", paths.Media, IsMedia);
                    else {
                        var config = Path.Combine(stage, "config.toml");
                        await paths.ConfigCodec("export", Path.Combine(paths.Input, "config.toml"), null, config);
                        Add("InputMethod/config.toml", config);
                        foreach (var name in InputFiles.Concat(GlossaryFiles)) if (File.Exists(Path.Combine(paths.Input, name))) Add("InputMethod/" + name, Path.Combine(paths.Input, name));
                        foreach (var dir in new[] { "dicts", "codes" }) AddTree("InputMethod/" + dir, Path.Combine(paths.Input, dir));
                    }
                }
                if (sources.Count > MaxFiles) throw new IOException("文件数量超过 100000 个，备份未创建。");
                var manifest = new PersonalDataManifest { ProductVersion = version, CreatedUtc = DateTime.UtcNow, Categories = selected.Distinct().ToList(), Directories = directories.ToList(), AdditionalDataVersion = 1, MissingVoiceAttachments = missingAudio.ToList() };
                progress?.Report("检查备份空间…"); token.ThrowIfCancellationRequested();
                long estimate = 0, metadata = Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(manifest));
                foreach (var source in sources) { token.ThrowIfCancellationRequested(); estimate = checked(estimate + new FileInfo(source.Value).Length); metadata = checked(metadata + Encoding.UTF8.GetByteCount(source.Key) * 2L + 256); }
                var exportBudget = new PersonalDataSpace(control); exportBudget.Add(destination, PersonalDataSpace.ZipBytes(estimate, sources.Count, metadata)); exportBudget.Check();
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                var output = destination + ".partial-" + Guid.NewGuid().ToString("N");
                try {
                    long bytes = 0;
                    using (var zip = ZipFile.Open(output, ZipArchiveMode.Create)) {
                        foreach (var source in sources) {
                            token.ThrowIfCancellationRequested();
                            progress?.Report("备份文件 " + (manifest.Files.Count + 1) + " / " + sources.Count);
                            using var input = new FileStream(source.Value, FileMode.Open, FileAccess.Read, FileShare.Read);
                            var length = input.Length; bytes += length; if (bytes > MaxBytes) throw new IOException("个人备份超过 20 GB，请分批选择数据。");
                            if (source.Key.StartsWith("Diary/") && DiaryFiles.Contains(source.Key[6..])) {
                                if (InvalidDiaryStream(source.Key[6..], input, length)) manifest.InvalidDiaryFiles.Add(source.Key[6..]);
                                input.Position = 0;
                            }
                            DateTime modified = File.GetLastWriteTimeUtc(source.Value);
                            var entry = zip.CreateEntry(source.Key, CompressionLevel.Fastest);
                            entry.LastWriteTime = new DateTimeOffset(modified.Year is >= 1980 and <= 2107 ? modified : new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                            using var target = entry.Open(); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                            var buffer = new byte[81920]; int count; while ((count = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); target.Write(buffer, 0, count); }
                            if (input.Position != length) throw new IOException("文件在备份时发生变化，请重新备份。");
                            manifest.Files.Add(new PersonalDataFile { Path = source.Key, Bytes = length, Sha256 = Convert.ToHexString(hash.GetHashAndReset()), LastWriteUtc = modified });
                        }
                        var listing = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                        if (Encoding.UTF8.GetByteCount(listing) > 32 * 1024 * 1024) throw new IOException("备份清单超过大小限制，请分批选择数据。");
                        using var writer = new StreamWriter(zip.CreateEntry(ManifestName).Open(), new UTF8Encoding(false)); writer.Write(listing);
                    }
                    token.ThrowIfCancellationRequested();
                    if (!internalBackup) control?.EnterCommit();
                    File.Move(output, destination, false);
                } finally { if (File.Exists(output)) File.Delete(output); }
            } finally { Remove(stage); }
        }
        internal static PersonalDataManifest Inspect(string source)
        {
            SafePath(source); using var zip = ZipFile.OpenRead(source); var manifest = Validate(zip);
            // Detect old backups as well as newly annotated ones. Never trust an annotation
            // to bypass validation or hashes during extraction.
            manifest.InvalidDiaryFiles.Clear();
            foreach (string name in DiaryFiles) if (zip.GetEntry("Diary/" + name) is ZipArchiveEntry entry) {
                using var stream = entry.Open();
                if (InvalidDiaryStream(name, stream, entry.Length)) manifest.InvalidDiaryFiles.Add(name);
            }
            return manifest;
        }
        private static PersonalDataManifest Validate(ZipArchive zip)
        {
            if (zip.Entries.Count > MaxFiles + 1) throw new InvalidDataException("备份文件数量超过限制。");
            var manifestEntry = zip.GetEntry(ManifestName) ?? throw new InvalidDataException("不是小蟒个人数据备份。");
            if (manifestEntry.Length > 32 * 1024 * 1024) throw new InvalidDataException("备份清单过大。");
            PersonalDataManifest manifest; using (var reader = new StreamReader(manifestEntry.Open(), new UTF8Encoding(false, true))) using (var json = new JsonTextReader(reader) { MaxDepth = 64 }) { manifest = JsonSerializer.CreateDefault().Deserialize<PersonalDataManifest>(json); if (json.Read()) throw new InvalidDataException("备份清单格式无效。"); }
            if (manifest?.Kind != "xiaomang-personal-data" || manifest.Format != 1 || manifest.Categories == null || manifest.Files == null || manifest.Categories.Count == 0 || manifest.Categories.Distinct().Count() != manifest.Categories.Count || manifest.Categories.Any(kind => !Enum.IsDefined(kind))) throw new InvalidDataException("备份版本或类别不受支持。");
            if (manifest.AdditionalDataVersion is < 0 or > 1 || manifest.MissingVoiceAttachments == null || manifest.MissingVoiceAttachments.Count > MaxFiles || manifest.MissingVoiceAttachments.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.MissingVoiceAttachments.Count) throw new InvalidDataException("备份扩展信息无效。");
            if (manifest.InvalidDiaryFiles == null || manifest.InvalidDiaryFiles.Count > DiaryFiles.Length || manifest.InvalidDiaryFiles.Distinct().Count() != manifest.InvalidDiaryFiles.Count || manifest.InvalidDiaryFiles.Any(name => !DiaryFiles.Contains(name) || !manifest.Categories.Contains(PersonalDataKind.Diary) || !manifest.Files.Any(file => file?.Path == "Diary/" + name))) throw new InvalidDataException("日记异常记录无效。");
            var expected = new Dictionary<string, PersonalDataFile>(StringComparer.OrdinalIgnoreCase);
            if (manifest.Directories == null || manifest.Directories.Count > MaxFiles || manifest.Directories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Directories.Count) throw new InvalidDataException("备份目录无效。");
            foreach (var dir in manifest.Directories) if (!AllowedDirectory(dir, manifest.Categories)) throw new InvalidDataException("备份目录超出类别范围。");
            long bytes = 0;
            foreach (var file in manifest.Files) { if (file == null || !Allowed(file.Path, manifest.Categories) || file.Bytes < 0 || file.Sha256?.Length != 64 || !file.Sha256.All(Uri.IsHexDigit) || !expected.TryAdd(file.Path, file)) throw new InvalidDataException("备份清单含无效或重复文件。"); bytes = checked(bytes + file.Bytes); if (bytes > MaxBytes) throw new InvalidDataException("备份超过 20 GB 限制。"); }
            foreach (var file in manifest.Files) if (file.LastWriteUtc is DateTime modified && modified.ToUniversalTime().Year < 1601) throw new InvalidDataException("备份文件时间无效。");
            foreach (string audio in manifest.MissingVoiceAttachments) if (manifest.AdditionalDataVersion != 1 || !manifest.Categories.Contains(PersonalDataKind.Todo) || !Relative(audio).StartsWith("VoiceNotes/") || expected.ContainsKey("Todo/" + audio)) throw new InvalidDataException("备份录音缺失记录无效。");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries) {
                Relative(entry.FullName); if (!seen.Add(entry.FullName)) throw new InvalidDataException("备份存在重复路径。");
                if (entry.FullName == ManifestName) continue;
                if (!expected.TryGetValue(entry.FullName, out var file) || entry.Length != file.Bytes || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("备份清单与文件不一致。");
            }
            if (seen.Count != expected.Count + 1) throw new InvalidDataException("备份存在缺失文件。");
            foreach (var kind in manifest.Categories.Where(kind => kind is PersonalDataKind.Todo or PersonalDataKind.Clipboard or PersonalDataKind.Favorites)) if (!expected.ContainsKey(kind + "/items.json")) throw new InvalidDataException("备份缺少记录清单。");
            foreach (var kind in manifest.Categories.Where(kind => kind is PersonalDataKind.Hotkeys or PersonalDataKind.Preferences)) if (!expected.ContainsKey(kind + "/settings.json")) throw new InvalidDataException("备份缺少配置清单。");
            if (manifest.Categories.Contains(PersonalDataKind.InputMethod) && !expected.ContainsKey("InputMethod/config.toml")) throw new InvalidDataException("备份缺少输入法配置。");
            return manifest;
        }
        internal static PersonalDataManifest Extract(string source, string destination, IReadOnlyCollection<PersonalDataKind> selected, IProgress<string> progress = null, PersonalDataTransferControl control = null)
        {
            var token = control?.Token ?? CancellationToken.None; token.ThrowIfCancellationRequested();
            SafePath(source);
            SafePath(destination); if (Directory.Exists(destination)) throw new IOException("恢复暂存目录已存在。");
            using var zip = ZipFile.OpenRead(source); var manifest = Validate(zip);
            if (selected.Count == 0 || selected.Any(kind => !manifest.Categories.Contains(kind))) throw new InvalidDataException("请选择备份内的有效类别。");
            var extractBudget = new PersonalDataSpace(control);
            extractBudget.Add(destination, manifest.Files.Where(file => selected.Contains(Enum.Parse<PersonalDataKind>(file.Path.Split('/')[0]))).Sum(file => PersonalDataSpace.Allocated(file.Bytes)) + manifest.Directories.Count * 4096L + Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(manifest)));
            extractBudget.Check(); token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destination);
            try {
                manifest.InvalidDiaryFiles.Clear();
                foreach (var dir in manifest.Directories.Where(dir => selected.Contains(Enum.Parse<PersonalDataKind>(dir.Split('/')[0])))) { token.ThrowIfCancellationRequested(); Directory.CreateDirectory(Path.Combine(destination, dir.Replace('/', Path.DirectorySeparatorChar))); }
                var files = manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries.Where(entry => entry.FullName != ManifestName)) {
                    token.ThrowIfCancellationRequested();
                    progress?.Report("校验 " + Label(Enum.Parse<PersonalDataKind>(entry.FullName.Split('/')[0])) + "…");
                    bool chosen = selected.Contains(Enum.Parse<PersonalDataKind>(entry.FullName.Split('/')[0]));
                    string output = Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    if (chosen) Directory.CreateDirectory(Path.GetDirectoryName(output));
                    using var target = chosen ? new FileStream(output, FileMode.CreateNew, FileAccess.Write) : null;
                    using var input = entry.Open(); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920]; long bytes = 0; int count;
                    while ((count = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); bytes += count; if (bytes > files[entry.FullName].Bytes) throw new InvalidDataException("备份解压长度异常。"); hash.AppendData(buffer, 0, count); target?.Write(buffer, 0, count); }
                    if (bytes != files[entry.FullName].Bytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(files[entry.FullName].Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份校验失败，未恢复数据。");
                    if (chosen) {
                        target.Dispose();
                        File.SetLastWriteTimeUtc(output, files[entry.FullName].LastWriteUtc?.ToUniversalTime() ?? entry.LastWriteTime.UtcDateTime);
                        if (entry.FullName.StartsWith("Diary/") && DiaryFiles.Contains(entry.FullName[6..]) && InvalidDiaryFile(entry.FullName[6..], output)) manifest.InvalidDiaryFiles.Add(entry.FullName[6..]);
                    }
                }
                token.ThrowIfCancellationRequested(); WriteJson(Path.Combine(destination, ManifestName), manifest); return manifest;
            } catch { Remove(destination); throw; }
        }
        private static bool AllowedDirectory(string name, IReadOnlyCollection<PersonalDataKind> selected)
        {
            var parts = Relative(name).Split('/');
            if (!Enum.TryParse<PersonalDataKind>(parts[0], out var kind) || parts[0] != kind.ToString() || !selected.Contains(kind)) return false;
            if (kind == PersonalDataKind.Temporary || kind == PersonalDataKind.Media) return true;
            if (parts.Length < 2) return false;
            return kind switch {
                PersonalDataKind.Diary => new[] { "notes", "attachments", "history", "trash" }.Contains(parts[1]),
                PersonalDataKind.InputMethod => new[] { "dicts", "codes" }.Contains(parts[1]),
                PersonalDataKind.Todo => new[] { "logs", "legacy", "VoiceNotes" }.Contains(parts[1]),
                PersonalDataKind.Clipboard => new[] { "logs", "legacy", "archive" }.Contains(parts[1]),
                PersonalDataKind.Favorites => new[] { "logs", "legacy" }.Contains(parts[1]), _ => false
            };
        }
        internal static void CopyTree(string source, string target, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(target); foreach (var file in Files(source, token: token)) { var next = Path.Combine(target, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(next)); CopyFile(file, next, token); }
            // Keep empty directories too, especially empty diary folders.
            if (Directory.Exists(source)) foreach (var directory in Directory.EnumerateDirectories(source)) CopyTreeEmpty(directory, Path.Combine(target, Path.GetFileName(directory)), token);
        }
        internal static void CopyFile(string source, string target, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); SafePath(source); SafePath(target);
            // Native copies retain timestamps without reopening every tiny diary/config file.
            // Their short bounded write completes before honoring a pending cancellation;
            // registered preparation cleanup still removes that copy on cancellation.
            if (!token.CanBeCanceled || new FileInfo(source).Length <= 81920) {
                File.Copy(source, target, false); token.ThrowIfCancellationRequested(); return;
            }
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                var buffer = new byte[81920]; int count;
                while ((count = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
                token.ThrowIfCancellationRequested();
            }
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        }
        private static void CopyTreeEmpty(string source, string target, CancellationToken token) { token.ThrowIfCancellationRequested(); SafePath(source); Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source)) CopyTreeEmpty(directory, Path.Combine(target, Path.GetFileName(directory)), token); }
        internal static void Remove(string path) { SafePath(path); if (Directory.Exists(path)) { _ = Files(path).Count(); Directory.Delete(path, true); } else if (File.Exists(path)) File.Delete(path); }
    }
}
