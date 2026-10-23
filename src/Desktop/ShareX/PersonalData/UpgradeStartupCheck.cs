using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace ShareX;

internal static class UpgradeStartupCheck
{
    private static readonly string Request = Environment.GetEnvironmentVariable("XIAOMANG_UPGRADE_CHECK");
    private static JObject expected;
    private static JObject originalSettings;
    private static string originalPinned;
    private static Exception settingsError;
    private static bool settingsChecked;
    // Run synchronously after configuration loading, before clipboard monitoring can
    // append a record or evict the oldest record at the history limit.
    internal static void SettingsLoaded()
    {
        if (expected == null) return;
        try
        {
            if (originalSettings != null)
                ValidateSettings(originalSettings, PersonalDataPreferences.Json(Program.Settings));
            settingsChecked = true;
        }
        catch (Exception error) { settingsError = error; }
    }

    internal static void ValidateSettings(JObject original, JObject loaded)
    {
        // Retired startup options normalize old true values to false. Keep the
        // remaining preference and personal-data retention checks unchanged.
        foreach (string name in new[] { "Language", "DisableHotkeys" })
            if (original[name] != null && !JToken.DeepEquals(original[name], loaded[name]))
                throw new IOException("升级后个人设置未正确加载：" + name);
        foreach (string field in new[] { "Items", "FavoriteItems", "ClipboardItems" })
        {
            JToken previous = original["TodoSettings"]?[field];
            if (previous == null || previous.Type == JTokenType.Null) continue;
            var oldItems = previous as JArray;
            var newItems = loaded["TodoSettings"]?[field] as JArray;
            var available = new Dictionary<(Guid, string), int>();
            if (newItems != null)
                foreach (var item in newItems)
                    if (Guid.TryParse((string)item["Id"], out Guid id))
                    {
                        var key = (id, (string)item["Text"]);
                        available[key] = available.GetValueOrDefault(key) + 1;
                    }
            bool valid = oldItems != null && newItems != null;
            if (valid)
                foreach (var item in oldItems)
                {
                    var key = (Guid.TryParse((string)item["Id"], out Guid id) ? id : Guid.Empty, (string)item["Text"]);
                    if (key.Item1 == Guid.Empty || available.GetValueOrDefault(key) == 0) { valid = false; break; }
                    available[key]--;
                }
            if (!valid)
                throw new IOException($"升级后数据加载校验失败：{field}，原条数 {oldItems?.Count ?? -1}，加载条数 {newItems?.Count ?? -1}。");
        }
    }
    internal static void Schedule()
    {
        if (expected == null) return;
        EventHandler handler = null;
        handler = async (_, _) => { System.Windows.Forms.Application.Idle -= handler; await ReportLoaded(); };
        System.Windows.Forms.Application.Idle += handler;
    }
    internal static void BeforeLoad()
    {
        if (string.IsNullOrEmpty(Request)) return;
        expected = JObject.Parse(File.ReadAllText(Request));
        string personal = Path.GetFullPath(Program.PersonalFolder);
        if (!personal.TrimEnd('\\').Equals(((string)expected["Personal"]).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new IOException("升级后个人数据目录发生变化，停止启动以保护原数据。");
        foreach (var file in expected["Files"])
            if (!File.Exists((string)file["Source"]) || PersonalDataArchive.Hash((string)file["Source"]) != (string)file["Hash"])
                throw new IOException("升级后个人数据校验不一致，停止启动。");
        string config = Path.Combine(personal,"ApplicationConfig.json");
        originalSettings = File.Exists(config) ? JObject.Parse(File.ReadAllText(config)) : null;
        string pinned = Path.Combine(personal,"data","pinned_files.json");
        originalPinned = File.Exists(pinned) ? File.ReadAllText(pinned) : null;
    }

    internal static async Task ReportLoaded()
    {
        if (expected == null) return;
        try
        {
            if (settingsError != null) throw settingsError;
            if (!settingsChecked) throw new IOException("升级后的设置尚未完成加载校验。");
            await Task.Run(SettingManager.WaitHotkeysConfig);
            await FileFavoritesService.Store.Ready;
            if (!string.IsNullOrEmpty(FileFavoritesService.Store.Warning)) throw new IOException("收藏数据加载失败。");
            string pinned = Path.Combine(Program.PersonalFolder, "data", "pinned_files.json");
            if (originalPinned != null)
            {
                var oldItems = FileFavoritesStore.ValidateItems(originalPinned, true);
                if (oldItems.Any(x => !FileFavoritesService.Store.Pinned.Any(y => y.Path.Equals(x.Path,StringComparison.OrdinalIgnoreCase))))
                    throw new IOException("升级后固定文件收藏未完整加载。");
            }
            Program.MainForm.ShowMainMenu("Upgrade completed");
            WriteResult(new { Success = true, Personal = Program.PersonalFolder,
                Version = Program.ProductVersion, ProcessId = Environment.ProcessId });
        }
        catch (Exception error)
        {
            WriteResult(new { Success = false, Error = error.Message });
        }
    }
    private static void WriteResult(object value)
    {
        File.WriteAllText(Request + ".result.tmp", JsonConvert.SerializeObject(value));
        File.Move(Request + ".result.tmp", Request + ".result", true);
    }
}
