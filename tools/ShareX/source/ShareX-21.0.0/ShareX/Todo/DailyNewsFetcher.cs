using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ShareX
{
    internal sealed class DailyNewsFetchResult
    {
        public List<DailyNewsItem> Items { get; } = new List<DailyNewsItem>();
        public List<DailyNewsFailure> Failures { get; } = new List<DailyNewsFailure>();
        public List<string> SuccessSources { get; } = new List<string>();
        public int SourcesOk { get; set; }
    }

    internal sealed class DailyNewsFetcher
    {
        private sealed class SourceResult
        {
            public DailyNewsSource Source { get; set; }
            public List<DailyNewsItem> Items { get; set; } = new List<DailyNewsItem>();
            public string Error { get; set; }
        }

        private static readonly Regex HtmlTagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);
        private readonly DailyNewsSettings settings;
        private readonly Func<bool, HttpClient> httpClientFactory;

        public DailyNewsFetcher(DailyNewsSettings settings, Func<bool, HttpClient> httpClientFactory = null)
        {
            this.settings = settings;
            this.httpClientFactory = httpClientFactory;
        }

        public async Task<DailyNewsFetchResult> FetchAsync(DailyNewsRegion region, IProgress<DailyNewsProgress> progress,
            CancellationToken cancellationToken)
        {
            List<DailyNewsSource> sources = GetSources(region);
            ConcurrentBag<SourceResult> results = new ConcurrentBag<SourceResult>();
            using SemaphoreSlim gate = new SemaphoreSlim(settings.Concurrency);
            int completed = 0;

            Task[] tasks = sources.Select(async source =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    List<DailyNewsItem> items = source.DailyHotRoute == null
                        ? await FetchRssAsync(source, cancellationToken)
                        : await FetchDailyHotAsync(source, cancellationToken);
                    results.Add(new SourceResult { Source = source, Items = items });
                }
                catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    results.Add(new SourceResult { Source = source, Error = GetSafeError(e) });
                }
                finally
                {
                    gate.Release();
                    int value = Interlocked.Increment(ref completed);
                    progress?.Report(new DailyNewsProgress
                    {
                        Percent = 5 + value * 25 / Math.Max(1, sources.Count),
                        Message = $"正在获取资讯源（{value}/{sources.Count}）"
                    });
                }
            }).ToArray();

            await Task.WhenAll(tasks);

            DailyNewsFetchResult output = new DailyNewsFetchResult();
            List<SourceResult> ordered = sources.Select(source => results.First(x => x.Source == source)).ToList();
            foreach (SourceResult result in ordered)
            {
                if (result.Error == null)
                {
                    output.SourcesOk++;
                    output.SuccessSources.Add(result.Source.Name);
                }
                else output.Failures.Add(new DailyNewsFailure { Name = result.Source.Name, Reason = result.Error });
            }

            // Round-robin keeps one prolific feed from consuming the entire daily limit.
            HashSet<string> duplicateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; output.Items.Count < settings.MaxTotal; index++)
            {
                bool hasCandidates = false;
                foreach (SourceResult result in ordered)
                {
                    if (result.Error != null || index >= result.Items.Count) continue;
                    hasCandidates = true;
                    DailyNewsItem item = result.Items[index];
                    string duplicateKey = item.SourceName + "\n" + item.TitleOriginal.Trim();
                    if (duplicateKeys.Add(duplicateKey))
                    {
                        output.Items.Add(item);
                        if (output.Items.Count >= settings.MaxTotal) break;
                    }
                }
                if (!hasCandidates) break;
            }

            return output;
        }

        private async Task<List<DailyNewsItem>> FetchRssAsync(DailyNewsSource source, CancellationToken cancellationToken)
        {
            using HttpClient client = CreateHttpClient(source.Region == DailyNewsRegion.International);
            string xml = await client.GetStringAsync(source.Url, cancellationToken);
            XDocument document = XDocument.Parse(xml, LoadOptions.None);
            DateTimeOffset fetchedAt = DateTimeOffset.Now;
            DateTimeOffset cutoff = fetchedAt.AddHours(-settings.RecentHours);
            List<DailyNewsItem> items = new List<DailyNewsItem>();

            IEnumerable<XElement> entries = document.Descendants().Where(x =>
                x.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase) ||
                x.Name.LocalName.Equals("entry", StringComparison.OrdinalIgnoreCase));

            foreach (XElement entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string title = GetElementValue(entry, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;
                string link = GetLink(entry, source.Url);
                if (!IsWebUrl(link)) continue;
                string content = FirstNonEmpty(
                    GetElementValue(entry, "description"),
                    GetElementValue(entry, "summary"),
                    GetElementValue(entry, "content"));
                string publishedText = FirstNonEmpty(
                    GetElementValue(entry, "pubDate"),
                    GetElementValue(entry, "published"),
                    GetElementValue(entry, "updated"),
                    GetElementValue(entry, "date"));
                bool hasTime = TryParseDate(publishedText, out DateTimeOffset publishedAt);
                if (!hasTime && !string.IsNullOrWhiteSpace(publishedText)) continue;
                if (hasTime && publishedAt < cutoff) continue;
                if (!hasTime) publishedAt = fetchedAt;

                string cleanTitle = NormalizeText(title, 240);
                items.Add(CreateItem(source, cleanTitle, NormalizeText(content, settings.MaxSummaryChars), link,
                    publishedAt, fetchedAt, !hasTime));
            }

            return items.OrderByDescending(x => x.PublishedAt).Take(settings.MaxPerSource).ToList();
        }

        private async Task<List<DailyNewsItem>> FetchDailyHotAsync(DailyNewsSource source, CancellationToken cancellationToken)
        {
            using HttpClient client = CreateHttpClient(false);
            string url = settings.DailyHotApi.TrimEnd('/') + "/" + source.DailyHotRoute;
            string json = await client.GetStringAsync(url, cancellationToken);
            JObject root = JObject.Parse(json);
            JArray data = root["data"] as JArray ?? throw new InvalidOperationException("返回内容缺少 data 数组");
            DateTimeOffset fetchedAt = DateTimeOffset.Now;
            List<DailyNewsItem> items = new List<DailyNewsItem>();

            foreach (JToken token in data)
            {
                string title = token.Value<string>("title");
                string link = FirstNonEmpty(token.Value<string>("url"), token.Value<string>("mobileUrl"));
                if (string.IsNullOrWhiteSpace(title) || !IsWebUrl(link)) continue;
                string content = FirstNonEmpty(token.Value<string>("desc"), token.Value<string>("description"));
                items.Add(CreateItem(source, NormalizeText(title, 240), NormalizeText(content, settings.MaxSummaryChars),
                    link, fetchedAt, fetchedAt, true));
                if (items.Count >= settings.MaxPerSource) break;
            }

            return items;
        }

        private HttpClient CreateHttpClient(bool international)
        {
            if (httpClientFactory != null) return httpClientFactory(international);
            HttpClientHandler handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
            if (international && settings.ProxyEnabled && Uri.TryCreate(settings.ProxyUrl, UriKind.Absolute, out Uri proxyUri))
            {
                handler.Proxy = new WebProxy(proxyUri);
                handler.UseProxy = true;
            }
            HttpClient client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
                MaxResponseContentBufferSize = 5 * 1024 * 1024
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("xiaomangtongxue/1.0 (+desktop-news-reader)");
            return client;
        }

        private static DailyNewsItem CreateItem(DailyNewsSource source, string title, string content, string url,
            DateTimeOffset publishedAt, DateTimeOffset fetchedAt, bool estimated)
        {
            return new DailyNewsItem
            {
                Id = CreateId(source.Name, title),
                SourceName = source.Name,
                SourceRegion = source.Region == DailyNewsRegion.Domestic ? "国内" : "国外",
                SourceLevel = source.Level,
                SourceLanguage = source.Language,
                Via = source.DailyHotRoute == null ? null : "dailyhot",
                Category = source.Category,
                TitleOriginal = title,
                TitleChinese = source.Language == "zh" ? title : "",
                SummaryChinese = source.Language == "zh" ? content : "",
                SourceContent = content,
                Url = url,
                PublishedAt = publishedAt,
                FetchedAt = fetchedAt,
                TimeEstimated = estimated
            };
        }

        private static string GetElementValue(XElement entry, string localName)
        {
            return entry.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        internal static bool IsWebUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        private static string GetLink(XElement entry, string sourceUrl)
        {
            foreach (XElement link in entry.Elements().Where(x => x.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase))
                .Where(x => x.Attribute("rel") == null || string.Equals(x.Attribute("rel")?.Value, "alternate", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => string.Equals(x.Attribute("type")?.Value, "text/html", StringComparison.OrdinalIgnoreCase)))
            {
                Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri baseUri);
                foreach (XElement ancestor in link.AncestorsAndSelf().Reverse())
                {
                    string xmlBase = ancestor.Attribute(XNamespace.Xml + "base")?.Value;
                    if (!string.IsNullOrWhiteSpace(xmlBase) && Uri.TryCreate(baseUri, xmlBase, out Uri resolvedBase)) baseUri = resolvedBase;
                }
                string value = link.Attribute("href")?.Value ?? link.Value;
                if (Uri.TryCreate(baseUri, value.Trim(), out Uri resolved) && IsWebUrl(resolved.AbsoluteUri)) return resolved.AbsoluteUri;
            }
            return "";
        }

        private static bool TryParseDate(string value, out DateTimeOffset date)
        {
            var zones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["UT"] = "+0000", ["GMT"] = "+0000", ["EST"] = "-0500", ["EDT"] = "-0400",
                ["CST"] = "-0600", ["CDT"] = "-0500", ["MST"] = "-0700", ["MDT"] = "-0600",
                ["PST"] = "-0800", ["PDT"] = "-0700"
            };
            value = Regex.Replace(value ?? "", @"\b(UT|GMT|EST|EDT|CST|CDT|MST|MDT|PST|PDT)\s*$",
                match => zones[match.Groups[1].Value], RegexOptions.IgnoreCase);
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return true;
            return DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date);
        }

        private static string NormalizeText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string decoded = WebUtility.HtmlDecode(HtmlTagRegex.Replace(value, " "));
            string normalized = Regex.Replace(decoded, @"\s+", " ").Trim();
            return normalized.Length <= maxLength ? normalized : normalized.Substring(0, maxLength);
        }

        private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

        private static string CreateId(string source, string title)
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(source + "\n" + title));
            return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 16);
        }

        private static string GetSafeError(Exception error)
        {
            return error switch
            {
                TaskCanceledException => "连接超时",
                HttpRequestException => "网络请求失败",
                _ => string.IsNullOrWhiteSpace(error.Message) ? "获取失败" : error.Message.Length > 100 ? error.Message.Substring(0, 100) : error.Message
            };
        }

        private static List<DailyNewsSource> GetSources(DailyNewsRegion region)
        {
            if (region == DailyNewsRegion.International)
            {
                return new List<DailyNewsSource>
                {
                    Rss("华尔街日报·世界", region, "财经", "en", "https://feeds.a.dj.com/rss/RSSWorldNews.xml"),
                    Rss("TechCrunch", region, "科技", "en", "https://techcrunch.com/feed/"),
                    Rss("The Verge", region, "科技", "en", "https://www.theverge.com/rss/index.xml"),
                    Rss("Ars Technica", region, "科技", "en", "https://feeds.arstechnica.com/arstechnica/index")
                };
            }

            return new List<DailyNewsSource>
            {
                Rss("中国新闻网·要闻", region, "社会", "zh", "https://www.chinanews.com.cn/rss/importnews.xml"),
                Rss("中国新闻网·国际", region, "国际", "zh", "https://www.chinanews.com.cn/rss/world.xml"),
                Rss("中国新闻网·财经", region, "财经", "zh", "https://www.chinanews.com.cn/rss/finance.xml"),
                Rss("人民网·时政", region, "政治", "zh", "http://www.people.com.cn/rss/politics.xml"),
                Rss("人民网·社会", region, "社会", "zh", "http://www.people.com.cn/rss/society.xml"),
                Rss("36氪", region, "科技", "zh", "https://www.36kr.com/feed"),
                Rss("少数派", region, "科技", "zh", "https://sspai.com/feed"),
                Rss("量子位", region, "AI", "zh", "https://www.qbitai.com/feed"),
                Rss("InfoQ 中文", region, "科技", "zh", "https://www.infoq.cn/feed"),
                DailyHot("微博热搜", "社会", "weibo"),
                DailyHot("知乎热榜", "社会", "zhihu"),
                DailyHot("百度热搜", "社会", "baidu"),
                DailyHot("腾讯新闻", "社会", "qq-news"),
                DailyHot("澎湃热榜", "社会", "thepaper")
            };
        }

        private static DailyNewsSource Rss(string name, DailyNewsRegion region, string category, string language, string url)
        {
            return new DailyNewsSource { Name = name, Region = region, Category = category, Language = language, Url = url, Level = "A" };
        }

        private static DailyNewsSource DailyHot(string name, string category, string route)
        {
            return new DailyNewsSource
            {
                Name = name,
                Region = DailyNewsRegion.Domestic,
                Category = category,
                Language = "zh",
                Url = route,
                DailyHotRoute = route,
                Level = "B"
            };
        }
    }
}
