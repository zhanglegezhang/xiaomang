using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX;

internal sealed record BaiduTranslationCredentials(string AppId, string Secret);
internal sealed record BaiduTranslationResult(string Text, string SourceLanguage, string TargetLanguage);

internal static class BaiduTranslationApi
{
    // Deliberately outside portable data, preferences, backups and release staging.
    internal static string CredentialPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Xiaomang", "PrivateCredentials", "baidu-translation.dpapi");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Xiaomang.BaiduTranslation.v1");
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime nextRequest;

    internal static BaiduTranslationCredentials Load()
    {
        if (!File.Exists(CredentialPath)) return null;
        try
        {
            byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(CredentialPath), Entropy, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<BaiduTranslationCredentials>(bytes) ?? throw new InvalidDataException(); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch { throw new InvalidOperationException("无法读取翻译凭据，请在注册页重新配置或清除。"); }
    }

    internal static void Save(string appId, string secret)
    {
        appId = appId.Trim(); secret = secret.Trim();
        if (appId.Length == 0 || !appId.All(char.IsAsciiDigit) || secret.Length == 0)
            throw new InvalidOperationException("请填写完整的 APP ID 和密钥。");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(new BaiduTranslationCredentials(appId, secret));
        try
        {
            byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(CredentialPath));
            string temp = CredentialPath + ".tmp";
            try { File.WriteAllBytes(temp, encrypted); File.Move(temp, CredentialPath, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal static void Clear() { if (File.Exists(CredentialPath)) File.Delete(CredentialPath); }
    internal static string Sign(string id, string text, string salt, string secret) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(id + text + salt + secret))).ToLowerInvariant();

    internal static async Task<string> TranslateAsync(BaiduTranslationCredentials credentials, string text, string target, CancellationToken token)
        => (await TranslateResultAsync(credentials, text, target, token)).Text;

    internal static async Task<BaiduTranslationResult> TranslateResultAsync(BaiduTranslationCredentials credentials, string text, string target, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("没有可翻译的原文。");
        if (Encoding.UTF8.GetByteCount(text) > 6000) throw new InvalidOperationException("原文超过 6000 字节，请分段翻译或改用网页。");
        await Gate.WaitAsync(token);
        try
        {
            var wait = nextRequest - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
            token.ThrowIfCancellationRequested();
            nextRequest = DateTime.UtcNow.AddSeconds(1);
            string salt = Guid.NewGuid().ToString("N");
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = text, ["from"] = "auto", ["to"] = target, ["appid"] = credentials.AppId,
                ["salt"] = salt, ["sign"] = Sign(credentials.AppId, text, salt, credentials.Secret)
            });
            using var response = await Client.PostAsync("https://fanyi-api.baidu.com/api/trans/vip/translate", body, token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("翻译服务连接失败，请稍后重试。");
            return ParseResult(await response.Content.ReadAsStringAsync(token));
        }
        catch (HttpRequestException) { throw new InvalidOperationException("无法连接百度翻译，请检查网络。"); }
        finally { Gate.Release(); }
    }

    internal static string Parse(string json)
        => ParseResult(json).Text;

    internal static BaiduTranslationResult ParseResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("error_code", out var code))
            throw new InvalidOperationException(code.GetString() switch
            {
                "52001" => "百度翻译请求超时，请重试。", "52002" => "百度翻译服务异常，请稍后重试。",
                "52003" => "APP ID 无效或未授权，请检查配置。", "54001" => "签名校验失败，请检查 APP ID 和密钥。",
                "54003" => "请求过于频繁，请稍后重试。", "54004" => "账户余额不足，请检查百度账户。",
                "54005" => "长文本请求频繁，请稍后重试。", "58002" => "通用文本翻译服务尚未开通。",
                "58003" => "账户额度不足或服务受限，请检查百度控制台。", "90107" => "开发者认证未完成或已失效。",
                "58000" => "客户端 IP 未获授权，请检查百度控制台。", _ => "百度翻译返回错误，请检查服务状态及账户配置。"
            });
        if (!doc.RootElement.TryGetProperty("trans_result", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("百度未返回有效译文。");
        string result = string.Join(Environment.NewLine, rows.EnumerateArray().Select(x => x.GetProperty("dst").GetString()));
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("百度未返回有效译文。");
        return new(result,
            doc.RootElement.TryGetProperty("from", out var from) ? from.GetString() : null,
            doc.RootElement.TryGetProperty("to", out var to) ? to.GetString() : null);
    }
}
