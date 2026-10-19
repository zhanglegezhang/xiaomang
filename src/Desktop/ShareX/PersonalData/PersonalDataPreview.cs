using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace ShareX
{
    internal static class PersonalDataPreview
    {
        internal static Dictionary<PersonalDataKind, string> Build(PersonalDataPaths paths, JObject app,
            PersonalDataManifest manifest, string archive, PersonalDataKind[] selected, bool inputReady, CancellationToken token)
        {
            var result = new Dictionary<PersonalDataKind, string>();
            using var zip = manifest == null ? null : ZipFile.OpenRead(archive);
            foreach (var kind in Enum.GetValues<PersonalDataKind>()) {
                token.ThrowIfCancellationRequested();
                if (manifest != null && !manifest.Categories.Contains(kind)) { result[kind] = "备份未包含"; continue; }
                if (!selected.Contains(kind)) { result[kind] = "未选择"; continue; }
                long bytes = 0; int count = 0; int? records = null; bool partial = false;
                var watch = Stopwatch.StartNew();
                if (manifest != null) {
                    var entries = manifest.Files.Where(f => f.Path.StartsWith(kind + "/", StringComparison.Ordinal)).ToArray();
                    count = entries.Length; bytes = entries.Sum(f => f.Bytes);
                    if (kind is PersonalDataKind.Todo or PersonalDataKind.Clipboard or PersonalDataKind.Favorites) {
                        var entry = zip.GetEntry(kind + "/items.json");
                        if (entry != null) { using var reader = new StreamReader(entry.Open()); records = (JObject.Parse(reader.ReadToEnd())["Items"] as JArray)?.Count; }
                    }
                } else {
                    void FileSize(string path) { token.ThrowIfCancellationRequested(); if (File.Exists(path)) { PersonalDataArchive.SafePath(path); bytes += new FileInfo(path).Length; count++; } }
                    void Tree(string root, Func<string,bool> include = null) {
                        foreach (string file in PersonalDataArchive.Files(root, token:token)) {
                            if (count >= 100000 || watch.ElapsedMilliseconds > 2500) { partial = true; break; }
                            if (include == null || include(file)) FileSize(file);
                        }
                    }
                    if (kind is PersonalDataKind.Todo or PersonalDataKind.Clipboard or PersonalDataKind.Favorites) {
                        string field = kind == PersonalDataKind.Todo ? "Items" : kind == PersonalDataKind.Clipboard ? "ClipboardItems" : "FavoriteItems";
                        var items = app["TodoSettings"]?[field] as JArray ?? new JArray(); records = items.Count;
                        bytes += System.Text.Encoding.UTF8.GetByteCount(new JObject { ["Items"] = items.DeepClone() }.ToString()); count++;
                        var logKind = kind == PersonalDataKind.Todo ? NoteLogKind.Todo : kind == PersonalDataKind.Clipboard ? NoteLogKind.Clipboard : NoteLogKind.Favorites;
                        Tree(Path.Combine(paths.Personal, "便签日志", NoteLogStore.GetDisplayName(logKind)));
                        Tree(Path.Combine(paths.Personal, "便签日志", "Legacy", NoteLogStore.GetDisplayName(logKind)));
                        if (kind == PersonalDataKind.Todo) Tree(Path.Combine(paths.Personal,"VoiceNotes"));
                        if (kind == PersonalDataKind.Clipboard) Tree(Path.Combine(paths.Personal,"剪切板归档"));
                        if (kind == PersonalDataKind.Favorites) foreach(var name in FileFavoritesStore.FileNames) FileSize(Path.Combine(paths.Personal,"data",name));
                    } else if (kind == PersonalDataKind.Diary) {
                        foreach(var dir in new[]{"notes","attachments","history","trash"}) Tree(Path.Combine(paths.Diary,dir));
                        foreach(var name in PersonalDataArchive.DiaryFiles) FileSize(Path.Combine(paths.Diary,name));
                    } else if (kind == PersonalDataKind.Temporary) Tree(Path.Combine(paths.Personal,"临时文件夹"), f=>!Path.GetFileName(f).StartsWith('.')&&!f.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase));
                    else if (kind == PersonalDataKind.Media) Tree(paths.Media,PersonalDataArchive.IsMedia);
                    else if (kind == PersonalDataKind.InputMethod) {
                        foreach(var name in PersonalDataArchive.InputFiles.Concat(PersonalDataArchive.GlossaryFiles).Append("config.toml")) FileSize(Path.Combine(paths.Input,name));
                        foreach(var dir in new[]{"dicts","codes"}) Tree(Path.Combine(paths.Input,dir));
                    } else if(kind == PersonalDataKind.Preferences) { bytes=System.Text.Encoding.UTF8.GetByteCount(PersonalDataPreferences.Export(app).ToString()); count=1; }
                    else if(File.Exists(paths.Hotkeys)) { bytes=System.Text.Encoding.UTF8.GetByteCount(PersonalDataPreferences.Hotkeys(PersonalDataArchive.ReadJson(paths.Hotkeys,true)).ToString()); count=1; }
                }
                string size = bytes >= 1048576 ? $"{bytes/1048576d:0.##} MiB" : $"{bytes/1024d:0.##} KiB";
                result[kind] = (records.HasValue ? $"当前 {records} 条 · " : "") + $"{count} 个文件 · {size}" + (manifest == null ? "（估算）" : "") + (partial ? " · 尚未统计完" : "");
                if(kind==PersonalDataKind.InputMethod) {
                    int pending = PersonalDataDeferredInput.List(paths.Personal).Length;
                    result[kind] += manifest != null && !inputReady ? " · 导入后保留待恢复" : pending>0 ? $" · 本机 {pending} 份待恢复" : " · 无待恢复数据";
                }
            }
            return result;
        }
    }
}
