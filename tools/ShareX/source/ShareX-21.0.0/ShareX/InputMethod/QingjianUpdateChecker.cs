using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal sealed class QingjianRelease
    {
        public string Version { get; set; }
        public string PageUrl { get; set; }
        public DateTimeOffset PublishedUtc { get; set; }
        public bool IsPrerelease { get; set; }
    }

    internal sealed class QingjianUpdateState
    {
        public int SchemaVersion { get; set; } = 1;
        public QingjianRelease Stable { get; set; }
        public QingjianRelease Preview { get; set; }
        public DateTimeOffset? CheckedUtc { get; set; }
        public DateTimeOffset? AttemptedUtc { get; set; }
        public string LastError { get; set; } = "";
        [JsonIgnore] public bool CacheWriteFailed { get; set; }
    }

    // Release metadata is display-only. Installation continues to use the separately
    // reviewed version, size and checksum in QingjianInputMethodManager.
    internal sealed class QingjianUpdateChecker
    {
        internal const string ReleasesUrl = "https://api.github.com/repos/qingjian-team/qingjian/releases?per_page=100";
        private const string ProjectUrl = "https://github.com/qingjian-team/qingjian";
        private const int MaximumResponseBytes = 2 * 1024 * 1024;
        private static readonly SemaphoreSlim CheckGate = new(1, 1);
        private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromSeconds(12) };
        private static readonly Regex VersionPattern = new(@"^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);
        private readonly string cacheFile;
        private readonly HttpClient client;
        private readonly Func<DateTimeOffset> now;
        private QingjianUpdateState memoryState;

        public QingjianUpdateChecker(string cacheFile, HttpClient client = null, Func<DateTimeOffset> now = null)
        {
            this.cacheFile = cacheFile;
            this.client = client ?? DefaultClient;
            this.now = now ?? (() => DateTimeOffset.UtcNow);
        }

        public QingjianUpdateState ReadState()
        {
            try
            {
                if (File.Exists(cacheFile) && new FileInfo(cacheFile).Length <= 16 * 1024)
                {
                    var state = JsonSerializer.Deserialize<QingjianUpdateState>(File.ReadAllText(cacheFile));
                    if (state != null && state.SchemaVersion == 1 && ValidTime(state.CheckedUtc) && ValidTime(state.AttemptedUtc) &&
                        ValidRelease(state.Stable, false) && ValidRelease(state.Preview, true) && (state.LastError?.Length ?? 0) <= 200)
                        return memoryState = state;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
            return memoryState ?? new QingjianUpdateState();
        }

        private bool ValidTime(DateTimeOffset? value) => !value.HasValue ||
            (value.Value.Year >= 2020 && value.Value <= now().AddMinutes(5));

        private static bool ValidRelease(QingjianRelease release, bool preview) => release == null ||
            (TryVersion(release.Version, out _, out _) && release.IsPrerelease == preview &&
             release.PublishedUtc.Year >= 2020 && IsReleasePage(release.PageUrl));

        private static bool IsReleasePage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
            uri.AbsolutePath.StartsWith("/qingjian-team/qingjian/releases/tag/", StringComparison.Ordinal) &&
            uri.Query.Length == 0 && uri.Fragment.Length == 0;

        public async Task<QingjianUpdateState> CheckAsync(bool force, CancellationToken token)
        {
            await CheckGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var state = ReadState();
                DateTimeOffset attempted = now();
                if (!force && state.AttemptedUtc.HasValue && attempted >= state.AttemptedUtc.Value &&
                    attempted - state.AttemptedUtc.Value < TimeSpan.FromDays(1)) return state;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
                    request.Headers.UserAgent.ParseAdd("xiaomangtongxue-QingjianUpdateCheck/1.0");
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                        throw new HttpRequestException("版本服务器限制了请求次数，请稍后再试。");
                    response.EnsureSuccessStatusCode();
                    Uri finalUri = response.RequestMessage?.RequestUri;
                    if (finalUri == null || finalUri.Scheme != Uri.UriSchemeHttps || finalUri.Host != "api.github.com" || !finalUri.IsDefaultPort)
                        throw new InvalidDataException("版本服务器地址无效。");
                    if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                        throw new InvalidDataException("版本信息超出大小限制。");
                    // HttpClient's headers-only timeout does not cover reading the body.
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(12));
                    await using var stream = await response.Content.ReadAsStreamAsync(readTimeout.Token).ConfigureAwait(false);
                    using var body = new MemoryStream();
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, readTimeout.Token).ConfigureAwait(false)) != 0)
                    {
                        if (body.Length + read > MaximumResponseBytes) throw new InvalidDataException("版本信息超出大小限制。");
                        body.Write(buffer, 0, read);
                    }
                    var fresh = ParseReleases(body.ToArray());
                    token.ThrowIfCancellationRequested();
                    state.Stable = fresh.Stable;
                    state.Preview = fresh.Preview;
                    state.CheckedUtc = now();
                    state.LastError = "";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
                {
                    state.LastError = error is HttpRequestException && error.Message.StartsWith("版本服务器限制", StringComparison.Ordinal)
                        ? error.Message : "检查失败：网络不可用或版本信息异常，请稍后重试。";
                }
                token.ThrowIfCancellationRequested();
                state.AttemptedUtc = attempted;
                memoryState = state;
                SaveState(state);
                return state;
            }
            finally { CheckGate.Release(); }
        }

        private void SaveState(QingjianUpdateState state)
        {
            string temporary = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cacheFile)));
                File.WriteAllText(temporary, JsonSerializer.Serialize(state));
                File.Move(temporary, cacheFile, true);
                state.CacheWriteFailed = false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { state.CacheWriteFailed = true; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }

        internal static QingjianUpdateState ParseReleases(byte[] json)
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("版本列表格式无效。");
            var state = new QingjianUpdateState();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False)
                    continue;
                string tag = String(item, "tag_name");
                string version = tag.StartsWith("windows-v", StringComparison.Ordinal) ? tag[9..] : tag.StartsWith('v') ? tag[1..] : tag;
                if (!TryVersion(version, out _, out string suffix) ||
                    !DateTimeOffset.TryParse(String(item, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ||
                    !IsReleasePage(String(item, "html_url")) || !item.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                    continue;
                string file = "qingjian-" + version + "-windows-x86_64-setup.exe";
                bool windows = assets.EnumerateArray().Any(asset => asset.ValueKind == JsonValueKind.Object &&
                    String(asset, "name").Equals(file, StringComparison.OrdinalIgnoreCase) &&
                    String(asset, "browser_download_url") == ProjectUrl + "/releases/download/" + tag + "/" + String(asset, "name"));
                if (!windows) continue;
                bool preview = suffix.Length != 0 || (item.TryGetProperty("prerelease", out var flag) && flag.ValueKind == JsonValueKind.True);
                var release = new QingjianRelease { Version = version, PageUrl = String(item, "html_url"), PublishedUtc = date, IsPrerelease = preview };
                if (preview)
                {
                    if (state.Preview == null || IsNewer(version, state.Preview.Version)) state.Preview = release;
                }
                else if (state.Stable == null || IsNewer(version, state.Stable.Version)) state.Stable = release;
            }
            if (state.Stable == null && state.Preview == null) throw new InvalidDataException("没有找到 Windows x64 输入法版本。");
            return state;
        }

        private static string String(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

        private static bool TryVersion(string text, out Version number, out string suffix)
        {
            number = null;
            suffix = "";
            if (string.IsNullOrEmpty(text) || text.Length > 80) return false;
            var match = VersionPattern.Match(text);
            if (!match.Success || !Version.TryParse(match.Groups[1].Value + "." + match.Groups[2].Value + "." + match.Groups[3].Value, out number)) return false;
            suffix = match.Groups[4].Value;
            return true;
        }

        internal static bool IsNewer(string candidate, string current)
        {
            if (!TryVersion(candidate, out var next, out string nextSuffix) || !TryVersion(current, out var old, out string oldSuffix)) return false;
            int comparison = next.CompareTo(old);
            if (comparison != 0) return comparison > 0;
            if (nextSuffix == oldSuffix) return false;
            if (nextSuffix.Length == 0) return true;
            if (oldSuffix.Length == 0) return false;
            string[] a = nextSuffix.Split('.'), b = oldSuffix.Split('.');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                bool an = a[i].All(char.IsAsciiDigit), bn = b[i].All(char.IsAsciiDigit);
                string av = an ? a[i].TrimStart('0') : a[i], bv = bn ? b[i].TrimStart('0') : b[i];
                comparison = an && bn ? (av.Length != bv.Length ? av.Length.CompareTo(bv.Length) : string.CompareOrdinal(av, bv)) :
                    an != bn ? (an ? -1 : 1) : string.CompareOrdinal(av, bv);
                if (comparison != 0) return comparison > 0;
            }
            return a.Length > b.Length;
        }
    }
}
