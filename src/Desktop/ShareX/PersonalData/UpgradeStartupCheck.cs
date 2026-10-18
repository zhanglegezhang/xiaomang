using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace ShareX;

internal static class UpgradeStartupCheck
{
    private static readonly string Request = Environment.GetEnvironmentVariable("XIAOMANG_UPGRADE_CHECK");
    private static JObject expected;
    private static JObject originalSettings;
    private static string originalPinned;
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
            await Task.Run(SettingManager.WaitHotkeysConfig);
            await FileFavoritesService.Store.Ready;
            if (!string.IsNullOrEmpty(FileFavoritesService.Store.Warning)) throw new IOException("收藏数据加载失败。");
            string config = Path.Combine(Program.PersonalFolder, "ApplicationConfig.json");
            if (originalSettings != null)
            {
                var original = originalSettings;
                var loaded = PersonalDataPreferences.Json(Program.Settings);
                foreach (string name in new[] { "Language", "ShowTray", "SilentRun", "DisableHotkeys" })
                    if (original[name] != null && !JToken.DeepEquals(original[name], loaded[name]))
                        throw new IOException("升级后个人设置未正确加载：" + name);
                foreach (string field in new[] { "Items", "FavoriteItems", "ClipboardItems" })
                {
                    var oldItems = original["TodoSettings"]?[field] as JArray;
                    var newItems = loaded["TodoSettings"]?[field] as JArray;
                    if (oldItems != null && (newItems == null || oldItems.Count != newItems.Count ||
                        oldItems.Any(x => !newItems.Any(y => (string)y["Id"] == (string)x["Id"] && (string)y["Text"] == (string)x["Text"]))))
                        throw new IOException("升级后待办或收藏内容未完整加载。");
                }
            }
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
