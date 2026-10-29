using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX;

internal sealed record InputComponentRelease(int Schema, string Version, string MinimumAppVersion, ComponentBundle Bundle);

internal static class InputComponentCatalog
{
    internal const string Url = "https://github.com/zhanglegezhang/xiaomang_component/releases/download/text-input-channel/latest.json";

    internal static async Task<InputComponentRelease> ReadAsync(string appVersion, CancellationToken token, HttpClient client = null)
    {
        using var owned = client == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : null;
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.UserAgent.ParseAdd("Xiaomang-Component-Updater/1.0");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await (client ?? owned).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var data = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await ComponentBundles.ReadDownloadAsync(stream, buffer, token)) != 0)
        {
            if (data.Length + read > 1024 * 1024) throw new InvalidDataException("组件版本信息过大。");
            data.Write(buffer, 0, read);
        }
        var release = JsonSerializer.Deserialize<InputComponentRelease>(data.ToArray());
        Validate(release, appVersion);
        return release;
    }

    internal static void Validate(InputComponentRelease release, string appVersion)
    {
        if (release == null || release.Schema != 1 || !System.Version.TryParse(release.Version, out _) ||
            !System.Version.TryParse(release.MinimumAppVersion, out var minimum) || !System.Version.TryParse(appVersion, out var current))
            throw new InvalidDataException("组件版本信息无效。");
        if (current < minimum) throw new InvalidOperationException("请先更新小蟒至 " + minimum + " 或更高版本，再更新输入法组件。");
        var bundle = release.Bundle;
        if (bundle == null || bundle.Id != "text" || bundle.Length <= 0 || bundle.Length > 1024L * 1024 * 1024 ||
            !Regex.IsMatch(bundle.FileName ?? "", @"^xiaomang-inputmethod-[A-Za-z0-9._-]+\.zip$") ||
            !Regex.IsMatch(bundle.ReleaseTag ?? "", @"^[A-Za-z0-9][A-Za-z0-9._-]{0,80}$") ||
            !Regex.IsMatch(bundle.Sha256 ?? "", @"\A[0-9a-fA-F]{64}\z") || bundle.Files == null || bundle.Files.Length == 0 || bundle.Files.Length > 10000)
            throw new InvalidDataException("组件清单无效。");
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in bundle.Files)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.Path) || file.Path.Contains('\\') || file.Path.StartsWith('/') || file.Path.Contains(':') ||
                file.Path.Split('/').Any(p => p is "" or "." or "..") || !names.Add(file.Path) || file.Length < 0 ||
                !Regex.IsMatch(file.Sha256 ?? "", @"\A[0-9a-fA-F]{64}\z") ||
                Regex.IsMatch(file.Path, @"(?i)(^|/)(config\.toml|user[^/]*\.(db|sqlite|json)|privatecredentials|logs?)(/|$)")) throw new InvalidDataException("组件文件清单无效。");
        }
        foreach (string required in new[] { "XiaomangInputWorker.exe", "InputMethodSetup.exe", "x86_64/XiaomangInputMethod.dll", "i686/XiaomangInputMethod.dll", "data/generated/dict.qj" })
            if (!bundle.Files.Any(f => f.Required && f.Path == required)) throw new InvalidDataException("组件缺少必需文件。");
    }
}
