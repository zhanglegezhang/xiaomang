using System;
using System.IO;
using System.Text.Json;

namespace ShareX;

internal static class UpgradeDataSnapshot
{
    // Capture authoritative running paths. The installer backs up and verifies the final
    // files after normal shutdown and before replacing anything, including exit-time saves.
    internal static void Create(PersonalDataPaths paths, string destination, IProgress<string> progress)
    {
        foreach (string path in new[] { paths.Personal, paths.Input, paths.Media, paths.Hotkeys })
            PersonalDataArchive.SafePath(path);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(destination), "data-snapshot.json"),
            JsonSerializer.Serialize(new { Personal = paths.Personal, Input = paths.Input, Media = paths.Media,
                Hotkeys = paths.Hotkeys, Backup = destination, Files = Array.Empty<object>() }));
        progress?.Report("已保存数据；更新助手将在退出后原样备份并校验，再替换程序。");
    }
}
