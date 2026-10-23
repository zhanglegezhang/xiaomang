using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using ShareX.HelpersLib;
using System.Drawing;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace ShareX
{
    // Explicit projections avoid migrating credentials, device paths and executable actions.
    internal static class PersonalDataPreferences
    {
        private static readonly string[] AppFields = { "Language", "TrayIconProgressEnabled", "TaskbarProgressEnabled", "RememberMainFormSize", "MainFormSize", "Themes", "SelectedTheme", "ScratchMarkdownWindowSize", "ScratchTextWindowSize", "ScratchMarkdownSplitRatio", "ImageCollageWindowSize", "TextCompareWindowSize", "DisableHotkeys", "DisableHotkeysOnFullscreen", "SaveImageSubFolderPattern", "SaveImageSubFolderPatternWindow" };
        private static readonly string[] TodoFields = { "WidgetWidth", "WidgetHeight", "VisibleModules", "ClipboardPreviewLines", "NoteLogWindowSize", "NoteLogWindowSizeVersion" };
        private static readonly string[] NewsFields = { "Enabled", "MaxPerSource", "MaxTotal", "RecentHours", "MaxSummaryChars", "Concurrency", "TimeoutSeconds", "EnableDigest", "EnableTopicGroup", "AutoRun", "AutoRunHour", "DomesticWindowSize", "InternationalWindowSize", "WindowSizeVersion" };
        private static readonly string[] TaskFields = { "Description", "Job", "UseDefaultAfterCaptureJob", "AfterCaptureJob", "UseDefaultGeneralSettings", "GeneralSettings", "UseDefaultImageSettings", "ImageSettings", "UseDefaultCaptureSettings", "CaptureSettings", "UseDefaultToolsSettings", "ToolsSettings", "UseDefaultAdvancedSettings", "AdvancedSettings" };
        internal static JObject Json(object value) => JObject.Parse(JsonHelpers.SerializeToString(value));
        private sealed class ImageBinder : Newtonsoft.Json.Serialization.ISerializationBinder
        {
            private static readonly Type[] Types = new ShareX.ImageEffectsLib.ImageEffectsSerializationBinder().KnownTypes.ToArray();
            public Type BindToType(string assemblyName, string typeName) => Types.SingleOrDefault(type => (type.Name == typeName || type.FullName == typeName) && (assemblyName == null || assemblyName.Split(',')[0] == type.Assembly.GetName().Name)) ?? throw new JsonSerializationException("不支持的图片特效类型。");
            public void BindToName(Type type, out string assemblyName, out string typeName) { assemblyName = null; typeName = type.Name; }
        }
        internal static JsonSerializer Serializer() => JsonSerializer.Create(new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace, TypeNameHandling = TypeNameHandling.None, SerializationBinder = new ImageBinder(), Converters = { new StringEnumConverter() } });
        internal static JObject Pick(JObject value, IEnumerable<string> names) { var result = new JObject(); foreach (var name in names) if (value?[name] is JToken token) result[name] = token.DeepClone(); return result; }
        private static JObject Shape(JObject value, JObject schema)
        {
            var result = new JObject();
            foreach (var property in schema.Properties())
            {
                if (value?[property.Name] is not JToken token || token.Type == JTokenType.Null) continue;
                if (property.Name.StartsWith('$')) continue;
                if (new[] { "AIOptions", "TextCustom", "OpenURLFormat" }.Contains(property.Name) || new[] { "Password", "ApiKey", "Token", "Secret", "Credential", "Authorization", "Command", "Argument", "CLI" }.Any(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase))) continue;
                // Polymorphic image effects can contain files and custom payloads. Keep target
                // effects, rather than deserializing executable type metadata from a backup.
                if (token is JContainer container && container.DescendantsAndSelf().OfType<JProperty>().Any(p => p.Name == "$type")) continue;
                if (token is JObject nested && property.Value is JObject child) result[property.Name] = Shape(nested, child);
                else if (token.Type == JTokenType.String && (property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Folder", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Device", StringComparison.OrdinalIgnoreCase) || property.Name.EndsWith("Source") || property.Name.EndsWith("FileName"))) continue;
                else if (token is JObject || token is JArray array && array.Any(item => item is JContainer)) continue;
                else result[property.Name] = token.DeepClone();
            }
            return result;
        }
        internal static JObject Task(JObject value)
        {
            var clean = Shape(Pick(value, TaskFields), Json(new TaskSettings()));
            if (clean["ImageSettings"] is JObject image) image.Remove("SelectedImageEffectPreset");
            if (clean["GeneralSettings"] is JObject general)
                foreach (var key in new[] { "UseCustomCaptureSound", "UseCustomTaskCompletedSound", "UseCustomActionCompletedSound", "UseCustomErrorSound" }) general[key] = false;
            // Device names, CLI paths and external actions must be configured on the target machine.
            if (clean["CaptureSettings"] is JObject capture && capture["FFmpegOptions"] is JObject ffmpeg) ffmpeg["OverrideCLIPath"] = false;
            return clean;
        }
        internal static JObject Export(JObject app)
        {
            var output = Pick(app, AppFields);
            output["Language"] = nameof(SupportedLanguage.SimplifiedChinese);
            output["TodoSettings"] = Pick(app["TodoSettings"] as JObject, TodoFields);
            output["DailyNewsSettings"] = Pick(app["DailyNewsSettings"] as JObject, NewsFields);
            output["DefaultTaskSettings"] = Task(app["DefaultTaskSettings"] as JObject);
            return output;
        }
        internal static void Merge(JObject target, JObject source)
        {
            var portable = Export(source);
            foreach (var property in portable.Properties())
            {
                if (property.Value is JObject nested) { var current = target[property.Name] as JObject ?? new JObject(); current.Merge(nested, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace }); target[property.Name] = current; }
                else target[property.Name] = property.Value.DeepClone();
            }
        }
        internal static JObject Hotkeys(JObject source)
        {
            if (source["Hotkeys"] is not JArray items || items.Count > 500) throw new InvalidDataException("快捷键备份格式无效或数量过多。");
            var output = new JArray();
            foreach (var item in items.OfType<JObject>())
            {
                if (item["HotkeyInfo"] is not JObject info || item["TaskSettings"] is not JObject task) throw new InvalidDataException("快捷键条目不完整。");
                if (info["Hotkey"] == null) throw new InvalidDataException("快捷键缺少按键。");
                var value = new JObject { ["HotkeyInfo"] = Pick(info, new[] { "Win", "Hotkey" }), ["TaskSettings"] = Task(task) };
                var typed = value.ToObject<HotkeySettings>(Serializer());
                if (typed?.TaskSettings == null || !Enum.IsDefined(typed.TaskSettings.Job)) throw new InvalidDataException("备份包含当前版本不支持的快捷键功能。");
                output.Add(value);
            }
            if (output.Count != items.Count) throw new InvalidDataException("快捷键条目格式无效。");
            JToken migrated = source["InputMethodSwitchHotkeyMigrated"];
            if (migrated != null && migrated.Type != JTokenType.Boolean)
                throw new InvalidDataException("快捷键迁移状态格式无效。");
            return new JObject {
                ["Hotkeys"] = output,
                ["InputMethodSwitchHotkeyMigrated"] = migrated?.Value<bool>() ?? true
            };
        }
        internal static void FitWindows(JObject app, Size area)
        {
            void Fit(JObject holder, string key, int minWidth, int minHeight) {
                if (holder?[key] == null) return;
                Size size = holder[key].ToObject<Size>(Serializer()); if (size.IsEmpty) return;
                holder[key] = JToken.FromObject(new Size(Math.Clamp(size.Width, minWidth, Math.Max(minWidth, area.Width)), Math.Clamp(size.Height, minHeight, Math.Max(minHeight, area.Height))), Serializer());
            }
            foreach (var key in new[] { "MainFormSize", "ScratchMarkdownWindowSize", "ScratchTextWindowSize", "ImageCollageWindowSize", "TextCompareWindowSize" })
                Fit(app, key, 300, 200);
            Fit(app["TodoSettings"] as JObject, "NoteLogWindowSize", 300, 200);
            Fit(app["DailyNewsSettings"] as JObject, "DomesticWindowSize", 300, 200);
            Fit(app["DailyNewsSettings"] as JObject, "InternationalWindowSize", 300, 200);
            if (app["TodoSettings"] is JObject todo) { todo["WidgetY"] = -1; if (todo["WidgetWidth"] != null) todo["WidgetWidth"] = Math.Clamp((int)todo["WidgetWidth"], 0, area.Width); if (todo["WidgetHeight"] != null) todo["WidgetHeight"] = Math.Clamp((int)todo["WidgetHeight"], 0, area.Height); }
        }
    }
}
