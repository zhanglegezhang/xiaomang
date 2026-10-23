using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShareX.HelpersLib;

namespace ShareX
{
    internal sealed class PersonalDataOperation
    {
        public string Root { get; set; }
        public string Relative { get; set; }
        public bool Directory { get; set; }
        public bool Existed { get; set; }
        public bool Started { get; set; }
    }
    internal sealed class PersonalDataJournal
    {
        public string Id { get; set; }
        public string Phase { get; set; } = "prepared";
        public string Input { get; set; }
        public string Media { get; set; }
        public string Hotkeys { get; set; }
        public int ProgressVersion { get; set; }
        public List<PersonalDataOperation> Operations { get; set; } = new();
    }
    // All replacements are prepared beside their target, then renamed on the same volume.
    // A write-ahead journal makes rollback idempotent after a process/power failure.
    internal static class PersonalDataRestore
    {
        internal const int MaxOperations = PersonalDataArchive.MaxFiles * 2 + 64;
        internal const long MaxJournalBytes = 128L * 1024 * 1024;
        internal static string JournalPath(string personal) => Path.Combine(personal, "personal-data-transaction.json");
        internal static string ProgressPath(string personal) => JournalPath(personal) + ".progress";
        private static string Target(string personal, PersonalDataJournal journal, PersonalDataOperation op)
        {
            string root = op.Root switch { "personal" => personal, "input" => journal.Input, "media" => journal.Media, "hotkeys" => Path.GetDirectoryName(journal.Hotkeys), _ => throw new InvalidDataException("恢复日志的根目录无效。") };
            PersonalDataArchive.Relative(op.Relative);
            string target = Path.GetFullPath(Path.Combine(root, op.Relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("恢复日志路径无效。");
            if (op.Root == "hotkeys" && !target.Equals(journal.Hotkeys, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("快捷键恢复路径无效。");
            if (op.Root == "personal" && !(new[] { "ApplicationConfig.json", "临时文件夹", "VoiceNotes", "剪切板归档" }.Contains(op.Relative) || op.Relative.StartsWith("便签日志/") || op.Relative.StartsWith("Diary/") || PersonalDataDeferredInput.IsPendingRelative(op.Relative) || FileFavoritesStore.FileNames.Any(name => op.Relative == "data/" + name))) throw new InvalidDataException("个人数据恢复范围无效。");
            if (op.Root == "input" && !(PersonalDataArchive.InputFiles.Contains(op.Relative) || PersonalDataArchive.GlossaryFiles.Contains(op.Relative) || new[] { "config.toml", "dicts", "codes" }.Contains(op.Relative))) throw new InvalidDataException("输入法恢复范围无效。");
            if (op.Root == "media" && !PersonalDataArchive.IsMedia(target)) throw new InvalidDataException("截图恢复范围无效。");
            PersonalDataArchive.SafePath(target); return target;
        }
        private static string New(string target, string id) => target + ".personal-new-" + id;
        private static string Old(string target, string id) => target + ".personal-before-" + id;
        private static void Move(string source, string target, bool directory) { if (directory) System.IO.Directory.Move(source, target); else File.Move(source, target); }
        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        private static void Persist(string personal, PersonalDataJournal journal)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(journal, Formatting.Indented)) > MaxJournalBytes) throw new IOException("恢复日志超过支持范围，未提交个人数据。");
            PersonalDataArchive.WriteJson(JournalPath(personal), journal);
        }
        private static void PersistProgress(string personal, PersonalDataJournal journal, int count) =>
            PersonalDataArchive.WriteJson(ProgressPath(personal), new { journal.Id, StartedCount = count });
        // A small durable prefix checkpoint replaces rewriting all operations per file.
        // Legacy journals still use the Started flags embedded in each operation.
        private static void LoadProgress(string personal, PersonalDataJournal journal)
        {
            if (journal.Phase is "complete" or "rolledBack" or "preparing") return;
            if (journal.ProgressVersion == 0) return;
            if (!File.Exists(ProgressPath(personal))) {
                if (journal.Phase is "committing" or "rollingBack") throw new InvalidDataException("恢复进度记录缺失，请保留个人数据目录。");
                return;
            }
            var progress = PersonalDataArchive.ReadJson(ProgressPath(personal), maxBytes: 4096);
            if ((string)progress["Id"] != journal.Id || progress["StartedCount"]?.Type != JTokenType.Integer) throw new InvalidDataException("恢复进度记录无效，请保留个人数据目录。");
            int count = (int)progress["StartedCount"];
            if (count < 0 || count > journal.Operations.Count || journal.Phase == "prepared" && count != 0) throw new InvalidDataException("恢复进度超出范围，请保留个人数据目录。");
            for (int i = 0; i < count; i++) journal.Operations[i].Started = true;
        }
        private static void ValidateTargets(IEnumerable<string> targets)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string target in targets) if (!set.Add(target)) throw new IOException("恢复目录重复，未修改个人数据。");
            foreach (string target in set)
                for (string parent = Path.GetDirectoryName(target); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                    if (set.Contains(parent)) throw new IOException("数据目录相互重叠，未修改个人数据。");
        }
        internal static void Recover(string personal)
        {
            string file = JournalPath(personal); if (!File.Exists(file)) return;
            var journal = PersonalDataArchive.ReadJson(file, maxBytes: MaxJournalBytes).ToObject<PersonalDataJournal>(PersonalDataPreferences.Serializer());
            if (journal == null || !Guid.TryParseExact(journal.Id, "N", out _) || journal.Operations == null || journal.Operations.Count > MaxOperations || journal.Operations.Any(op => op == null) || journal.ProgressVersion is < 0 or > 1 || journal.Phase is not ("preparing" or "prepared" or "committing" or "rollingBack" or "rolledBack" or "complete")) throw new InvalidDataException("恢复日志不完整，请保留个人数据目录并联系维护者。");
            // Validate every target before touching any of them.
            ValidateTargets(journal.Operations.Select(op => Target(personal, journal, op)));
            LoadProgress(personal, journal);
            if (journal.Phase is not ("complete" or "rolledBack")) {
                // Preparation has not touched targets. Committing/rollingBack retains its
                // checkpoint until the restored original state is durably recorded.
                if (journal.Phase is "committing" or "rollingBack") { journal.Phase = "rollingBack"; Persist(personal, journal); Rollback(personal, journal); }
                journal.Phase = "rolledBack"; Persist(personal, journal);
            }
            TryCleanup(personal, journal);
        }
        private static void Rollback(string personal, PersonalDataJournal journal)
        {
            foreach (var op in journal.Operations.AsEnumerable().Reverse()) {
                string target = Target(personal, journal, op), old = Old(target, journal.Id);
                if (Exists(old)) { PersonalDataArchive.Remove(target); Move(old, target, op.Directory); }
                else if (op.Started && !op.Existed) PersonalDataArchive.Remove(target);
            }
        }
        private static void Cleanup(string personal, PersonalDataJournal journal)
        {
            foreach (var op in journal.Operations) {
                var target = Target(personal, journal, op);
                PersonalDataArchive.Remove(New(target, journal.Id)); PersonalDataArchive.Remove(Old(target, journal.Id));
            }
            File.Delete(JournalPath(personal));
            File.Delete(ProgressPath(personal));
        }
        private static void TryCleanup(string personal, PersonalDataJournal journal)
        {
            try { Cleanup(personal, journal); }
            catch (IOException error) { DebugHelper.WriteException(error, "Finished personal data cleanup deferred"); }
            catch (UnauthorizedAccessException error) { DebugHelper.WriteException(error, "Finished personal data cleanup deferred"); }
        }
        internal static async Task<string> Apply(PersonalDataPaths paths, string extracted, IReadOnlyCollection<PersonalDataKind> selected, string version, Size display, Action<int> afterCommit = null, PersonalDataTransferControl control = null, IProgress<string> progress = null, bool deferInput = false)
        {
            deferInput = deferInput && selected.Contains(PersonalDataKind.InputMethod);
            if (deferInput) selected = selected.Where(k => k != PersonalDataKind.InputMethod).ToArray();
            var token = control?.Token ?? CancellationToken.None; token.ThrowIfCancellationRequested();
            Recover(paths.Personal);
            if (File.Exists(JournalPath(paths.Personal))) throw new IOException("上次导入的临时文件尚未清理，请关闭相关编辑窗口后重试。");
            string appFile = Path.Combine(paths.Personal, "ApplicationConfig.json");
            var original = PersonalDataArchive.ReadJson(appFile, true);
            var hotkeys = File.Exists(paths.Hotkeys) ? PersonalDataArchive.ReadJson(paths.Hotkeys, true) : PersonalDataPreferences.Json(new HotkeysConfig());
            string Payload(string path) => Path.Combine(extracted, path.Replace('/', Path.DirectorySeparatorChar));
            var manifest = PersonalDataArchive.ReadJson(Payload(PersonalDataArchive.ManifestName)).ToObject<PersonalDataManifest>(PersonalDataPreferences.Serializer());
            if (manifest?.MissingVoiceAttachments == null) throw new InvalidDataException("解压后的备份清单无效。");
            progress?.Report("检查恢复空间…");
            PersonalDataSpace.CheckImport(paths, original, manifest, selected, true, control);
            string backup = Path.Combine(paths.Personal, "PersonalDataBackups", "导入前_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6] + ".xmbackup");
            progress?.Report("保存导入前备份…");
            if (selected.Count > 0) await PersonalDataArchive.Export(paths, original, hotkeys, selected, backup, version, progress, internalBackup: true, control: control);
            else backup = null;
            var journal = new PersonalDataJournal { Id = Guid.NewGuid().ToString("N"), Input = paths.Input, Media = paths.Media, Hotkeys = paths.Hotkeys, ProgressVersion = 1 };
            var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<string>();
            bool registered = false;
            void Add(string root, string relative, string source, bool directory) {
                token.ThrowIfCancellationRequested();
                var op = new PersonalDataOperation { Root = root, Relative = relative, Directory = directory };
                string target = Target(paths.Personal, journal, op);
                if (!targetNames.Add(target)) throw new IOException("恢复目录重叠，请调整本机存储路径后重试。");
                if (journal.Operations.Count >= MaxOperations) throw new IOException("恢复操作数量超过限制，未提交个人数据。");
                op.Existed = Exists(target); journal.Operations.Add(op);
                sources.Add(source);
            }
            try {
                if (deferInput) {
                    PersonalDataDeferredInput.CheckSpace(paths.Personal, manifest, control);
                    string archive = Payload("deferred-input.xmbackup");
                    PersonalDataDeferredInput.Create(extracted, archive);
                    Add("personal", PersonalDataDeferredInput.Folder + "/" + Guid.NewGuid().ToString("N") + ".xmbackup", archive, false);
                }
                var app = (JObject)original.DeepClone();
                if (app["TodoSettings"] is not JObject) app["TodoSettings"] = new JObject();
                var todo = (JObject)app["TodoSettings"];
                foreach (var kind in selected) {
                    token.ThrowIfCancellationRequested();
                    if (kind is PersonalDataKind.Todo or PersonalDataKind.Clipboard or PersonalDataKind.Favorites) {
                        var data = PersonalDataArchive.ReadJson(Payload(kind + "/items.json"));
                        if (data["Items"] is not JArray items || items.Count > 200000) throw new InvalidDataException("记录清单格式无效。");
                        string field = kind == PersonalDataKind.Todo ? "Items" : kind == PersonalDataKind.Clipboard ? "ClipboardItems" : "FavoriteItems";
                        // Deserialize using product types before any target is replaced.
                        object typed = kind == PersonalDataKind.Todo ? items.ToObject<List<TodoItem>>(PersonalDataPreferences.Serializer()) : kind == PersonalDataKind.Clipboard ? items.ToObject<List<ClipboardTextItem>>(PersonalDataPreferences.Serializer()) : items.ToObject<List<FavoriteTextItem>>(PersonalDataPreferences.Serializer());
                        if (items.Any(item => item is not JObject || !Guid.TryParse((string)item["Id"], out var id) || id == Guid.Empty || item["Text"]?.Type != JTokenType.String) || items.Select(item => (string)item["Id"]).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count) throw new InvalidDataException("记录内容或标识无效、重复。");
                        if (kind == PersonalDataKind.Todo) foreach (var item in items.OfType<JObject>()) if ((string)item["VoiceAudioRelativePath"] is string audio && audio.Length > 0) {
                            audio = PersonalDataArchive.Relative(audio.Replace('\\', '/'));
                            if (!audio.StartsWith("VoiceNotes/") || !File.Exists(Payload("Todo/" + audio)) && !manifest.MissingVoiceAttachments.Contains(audio, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("待办录音附件缺失。");
                        }
                        todo[field] = items.DeepClone();
                        var logKind = kind == PersonalDataKind.Todo ? NoteLogKind.Todo : kind == PersonalDataKind.Clipboard ? NoteLogKind.Clipboard : NoteLogKind.Favorites;
                        string log = NoteLogStore.GetDisplayName(logKind);
                        Add("personal", "便签日志/" + log, Payload(kind + "/logs"), true);
                        Add("personal", "便签日志/Legacy/" + log, Payload(kind + "/legacy"), true);
                        if (kind == PersonalDataKind.Todo) Add("personal", "VoiceNotes", Payload("Todo/VoiceNotes"), true);
                        if (kind == PersonalDataKind.Clipboard) Add("personal", "剪切板归档", Payload("Clipboard/archive"), true);
                        if (kind == PersonalDataKind.Favorites) foreach (string name in FileFavoritesStore.FileNames) {
                            string source = Payload("Favorites/data/" + name);
                            // Old backups predate file favorites: preserve this newer category.
                            if (File.Exists(source)) { FileFavoritesStore.ValidateFile(name, source); Add("personal", "data/" + name, source, false); }
                        }
                    }
                    else if (kind == PersonalDataKind.Preferences) PersonalDataPreferences.Merge(app, PersonalDataArchive.ReadJson(Payload("Preferences/settings.json")));
                    else if (kind == PersonalDataKind.Hotkeys) {
                        var clean = PersonalDataPreferences.Hotkeys(PersonalDataArchive.ReadJson(Payload("Hotkeys/settings.json")));
                        // An explicit restore is authoritative, including old backups without migration metadata.
                        clean["InputMethodSwitchHotkeyMigrated"] = true;
                        PersonalDataArchive.WriteJson(Payload("hotkeys-ready.json"), clean);
                        Add("hotkeys", Path.GetFileName(paths.Hotkeys), Payload("hotkeys-ready.json"), false);
                    }
                    else if (kind == PersonalDataKind.Diary) {
                        foreach (var dir in new[] { "notes", "attachments", "history", "trash" }) Add("personal", "Diary/" + dir, Payload("Diary/" + dir), true);
                        foreach (var name in PersonalDataArchive.DiaryFiles) {
                            string source = Payload("Diary/" + name);
                            bool invalid = File.Exists(source) && PersonalDataArchive.InvalidDiaryFile(name, source);
                            // Old backups have no activity file: remove target statistics so
                            // the diary rebuilds them from the newly restored notes.
                            // Malformed optional originals remain in the user's backup.
                            Add("personal", "Diary/" + name, invalid ? null : source, false);
                        }
                    }
                    else if (kind == PersonalDataKind.Temporary) Add("personal", "临时文件夹", Payload("Temporary"), true);
                    else if (kind == PersonalDataKind.InputMethod) {
                        string config = Payload("input-ready.toml");
                        await paths.ConfigCodec("merge", Payload("InputMethod/config.toml"), Path.Combine(paths.Input, "config.toml"), config);
                        Add("input", "config.toml", config, false);
                        foreach (var name in PersonalDataArchive.InputFiles) Add("input", name, Payload("InputMethod/" + name), false);
                        foreach (var name in PersonalDataArchive.GlossaryFiles)
                            if (manifest.AdditionalDataVersion >= 1 || File.Exists(Payload("InputMethod/" + name))) Add("input", name, Payload("InputMethod/" + name), false);
                        foreach (var dir in new[] { "dicts", "codes" }) Add("input", dir, Payload("InputMethod/" + dir), true);
                    }
                    else if (kind == PersonalDataKind.Media) {
                        var existing = PersonalDataArchive.Files(paths.Media, token: token).Where(PersonalDataArchive.IsMedia).Select(file => Path.GetRelativePath(paths.Media, file).Replace('\\', '/'));
                        var incoming = PersonalDataArchive.Files(Payload("Media"), token: token).Select(file => Path.GetRelativePath(Payload("Media"), file).Replace('\\', '/'));
                        foreach (var name in existing.Concat(incoming).Distinct(StringComparer.OrdinalIgnoreCase)) Add("media", name, Payload("Media/" + name), false);
                    }
                }
                if (selected.Any(kind => kind is PersonalDataKind.Todo or PersonalDataKind.Clipboard or PersonalDataKind.Favorites or PersonalDataKind.Preferences)) {
                    app["Language"] = nameof(SupportedLanguage.SimplifiedChinese);
                    if (selected.Contains(PersonalDataKind.Preferences)) PersonalDataPreferences.FitWindows(app, display);
                    _ = app.ToObject<ApplicationConfig>(PersonalDataPreferences.Serializer());
                    PersonalDataArchive.WriteJson(Payload("app-ready.json"), app); Add("personal", "ApplicationConfig.json", Payload("app-ready.json"), false);
                }
                // Reject overlapping custom media paths and normal data paths before committing.
                ValidateTargets(targetNames);
                // Recheck only the remaining allocation after extraction/automatic backup.
                var prepareBudget = new PersonalDataSpace(control);
                prepareBudget.Add(paths.Personal, System.Text.Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(journal, Formatting.Indented)) * 2L + 1024 * 1024);
                for (int i = 0; i < sources.Count; i++) {
                    token.ThrowIfCancellationRequested(); var op = journal.Operations[i]; string source = sources[i];
                    string root = op.Root switch { "input" => paths.Input, "media" => paths.Media, "hotkeys" => Path.GetDirectoryName(paths.Hotkeys), _ => paths.Personal };
                    long bytes = 4096;
                    if (source != null && Exists(source)) bytes = checked(bytes + (op.Directory ? PersonalDataArchive.Files(source, token: token).Sum(file => PersonalDataSpace.Allocated(new FileInfo(file).Length) + 4096) : PersonalDataSpace.Allocated(new FileInfo(source).Length)));
                    prepareBudget.Add(root, bytes);
                }
                prepareBudget.Check(); token.ThrowIfCancellationRequested();
                journal.Phase = "preparing"; Persist(paths.Personal, journal); registered = true;
                for (int i = 0; i < journal.Operations.Count; i++) {
                    token.ThrowIfCancellationRequested(); progress?.Report("准备恢复文件 " + (i + 1) + " / " + journal.Operations.Count);
                    var op = journal.Operations[i]; string target = Target(paths.Personal, journal, op), source = sources[i];
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (source != null && Exists(source)) { if (op.Directory) PersonalDataArchive.CopyTree(source, New(target, journal.Id), token); else PersonalDataArchive.CopyFile(source, New(target, journal.Id), token); }
                }
                journal.Phase = "prepared";
                Persist(paths.Personal, journal); PersistProgress(paths.Personal, journal, 0);
                token.ThrowIfCancellationRequested(); control?.EnterCommit();
                progress?.Report("正在恢复，请稍候…");
                journal.Phase = "committing"; Persist(paths.Personal, journal);
                for (int i = 0; i < journal.Operations.Count; i++) {
                    var op = journal.Operations[i]; string target = Target(paths.Personal, journal, op), next = New(target, journal.Id);
                    op.Started = true; PersistProgress(paths.Personal, journal, i + 1);
                    if (op.Existed) Move(target, Old(target, journal.Id), op.Directory);
                    if (Exists(next)) Move(next, target, op.Directory);
                    afterCommit?.Invoke(i);
                }
                journal.Phase = "complete"; Persist(paths.Personal, journal);
            } catch {
                if (registered) {
                    if (journal.Phase is "committing" or "rollingBack" or "complete") { journal.Phase = "rollingBack"; Persist(paths.Personal, journal); Rollback(paths.Personal, journal); }
                    journal.Phase = "rolledBack"; Persist(paths.Personal, journal); TryCleanup(paths.Personal, journal);
                }
                throw;
            }
            // A cleanup failure after complete must never roll back a successful import.
            TryCleanup(paths.Personal, journal);
            return backup;
        }
    }
}
