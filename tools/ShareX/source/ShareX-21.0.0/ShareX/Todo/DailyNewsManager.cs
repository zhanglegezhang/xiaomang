using Newtonsoft.Json;
using ShareX.HelpersLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal sealed class DailyNewsManager
    {
        private readonly ConcurrentDictionary<DailyNewsRegion, SemaphoreSlim> updateLocks = new();
        private static readonly object storageGate = new object();
        public event Action<DailyNewsRegion> UpdateSucceeded;

        public string OutputFolder { get; }

        private DailyNewsSettings Settings
        {
            get
            {
                Program.Settings.DailyNewsSettings ??= new DailyNewsSettings();
                Program.Settings.DailyNewsSettings.Normalize();
                return Program.Settings.DailyNewsSettings;
            }
        }

        public DailyNewsManager(string personalFolder)
        {
            OutputFolder = Path.Combine(personalFolder, "daily_news");
            Directory.CreateDirectory(OutputFolder);
            lock (storageGate)
            {
                foreach (string transaction in Directory.GetDirectories(OutputFolder, "*.transaction"))
                {
                    try { RecoverTransaction(transaction); }
                    catch (Exception error) { DebugHelper.WriteException(error, "News transaction recovery deferred"); }
                }
            }
            CleanupTemporaryFiles();
        }

        public bool HasApiKey => !string.IsNullOrWhiteSpace(Settings.GetApiKey());

        public bool IsUpdateInProgress(DailyNewsRegion region)
        {
            return updateLocks.TryGetValue(region, out SemaphoreSlim updateLock) && updateLock.CurrentCount == 0;
        }

        public string GetTodayHtmlPath(DailyNewsRegion region) =>
            Path.Combine(OutputFolder, GetPrefix(region) + "_" + DateTime.Today.ToString("yyyy-MM-dd") + ".html");

        public string GetLatestHtmlPath(DailyNewsRegion region)
        {
            return Directory.EnumerateFiles(OutputFolder, GetPrefix(region) + "_*.html", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        public string RefreshLatestHtmlFromJson(DailyNewsRegion region)
        {
            lock (storageGate) return RefreshLatestHtmlCore(region);
        }

        private string RefreshLatestHtmlCore(DailyNewsRegion region)
        {
            try
            {
                string prefix = GetPrefix(region) + "_";
                string jsonPath = Directory.EnumerateFiles(OutputFolder, prefix + "*.json", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(jsonPath)) return GetLatestHtmlPath(region);
                RecoverTransaction(Path.ChangeExtension(jsonPath, null) + ".transaction");

                DailyNewsDocument document = JsonConvert.DeserializeObject<DailyNewsDocument>(File.ReadAllText(jsonPath));
                if (document == null) return GetLatestHtmlPath(region);
                document.Items ??= new List<DailyNewsItem>();
                document.SourcesFailed ??= new List<DailyNewsFailure>();
                document.SourcesSucceeded ??= new List<string>();
                if (document.SourcesSucceeded.Count == 0)
                {
                    HashSet<string> failedNames = document.SourcesFailed.Select(x => x.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    document.SourcesSucceeded = document.Items.Select(x => x.SourceName)
                        .Where(x => !string.IsNullOrWhiteSpace(x) && !failedNames.Contains(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                }

                string htmlPath = Path.ChangeExtension(jsonPath, ".html");
                string temporaryPath = htmlPath + ".tmp";
                try
                {
                    File.WriteAllText(temporaryPath, DailyNewsHtmlRenderer.Render(document), new UTF8Encoding(false));
                    File.Move(temporaryPath, htmlPath, true);
                }
                finally
                {
                    TryDeleteTemporaryFile(temporaryPath);
                }
                return htmlPath;
            }
            catch (Exception error)
            {
                DebugHelper.WriteLine("Daily news saved page refresh failed ({0}): {1}", region, error.Message);
                return GetLatestHtmlPath(region);
            }
        }

        public DateTime? GetLastSuccess(DailyNewsRegion region) =>
            region == DailyNewsRegion.Domestic ? Settings.LastDomesticSuccess : Settings.LastInternationalSuccess;

        public async Task<DailyNewsDocument> UpdateAsync(DailyNewsRegion region,
            IProgress<DailyNewsProgress> progress = null, CancellationToken cancellationToken = default)
        {
            SemaphoreSlim updateLock = updateLocks.GetOrAdd(region, _ => new SemaphoreSlim(1, 1));
            if (updateLock.CurrentCount == 0)
            {
                progress?.Report(new DailyNewsProgress { Percent = 0, Message = "正在等待当前更新完成…" });
            }
            await updateLock.WaitAsync(cancellationToken);
            try
            {
                DailyNewsSettings settings = Settings;
                string apiKey = settings.GetApiKey();
                if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("尚未配置 DeepSeek API Key。");

                progress?.Report(new DailyNewsProgress { Percent = 2, Message = "正在准备资讯源…" });
                DailyNewsFetcher fetcher = new DailyNewsFetcher(settings);
                DailyNewsFetchResult fetched = await fetcher.FetchAsync(region, progress, cancellationToken);
                if (fetched.Items.Count == 0)
                {
                    throw new InvalidOperationException("本次没有获取到可用资讯，已保留上次成功内容。");
                }

                DailyNewsDigest digest;
                List<DailyNewsGroup> groups;
                using (DailyNewsAiClient ai = new DailyNewsAiClient(settings, apiKey))
                {
                    await ai.EnrichItemsAsync(fetched.Items, progress, cancellationToken);
                    progress?.Report(new DailyNewsProgress { Percent = 84, Message = "正在生成今日导读…" });
                    digest = await ai.CreateDigestAsync(fetched.Items, cancellationToken);
                    progress?.Report(new DailyNewsProgress { Percent = 90, Message = "正在合并同一事件…" });
                    groups = await ai.CreateGroupsAsync(fetched.Items, cancellationToken);
                }

                DailyNewsDocument document = new DailyNewsDocument
                {
                    Date = DateTime.Today.ToString("yyyy-MM-dd"),
                    Region = region == DailyNewsRegion.Domestic ? "国内" : "国际",
                    GeneratedAt = DateTimeOffset.Now,
                    Digest = digest,
                    Groups = groups,
                    SourcesFailed = fetched.Failures,
                    SourcesSucceeded = fetched.SuccessSources,
                    Items = fetched.Items,
                    Stats = new DailyNewsStats
                    {
                        Total = fetched.Items.Count,
                        ByCategory = fetched.Items.GroupBy(x => x.Category)
                            .OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count()),
                        SourcesOk = fetched.SourcesOk,
                        SourcesFailed = fetched.Failures.Count
                    }
                };

                progress?.Report(new DailyNewsProgress { Percent = 95, Message = "正在保存资讯…" });
                await SaveDocumentAsync(region, document, cancellationToken);
                if (region == DailyNewsRegion.Domestic) settings.LastDomesticSuccess = DateTime.Now;
                else settings.LastInternationalSuccess = DateTime.Now;
                SettingManager.SaveApplicationConfigAsync();
                UpdateSucceeded?.Invoke(region);
                progress?.Report(new DailyNewsProgress { Percent = 100, Message = "更新完成" });
                return document;
            }
            finally
            {
                updateLock.Release();
            }
        }

        private async Task SaveDocumentAsync(DailyNewsRegion region, DailyNewsDocument document, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(OutputFolder);
            string stem = Path.Combine(OutputFolder, GetPrefix(region) + "_" + document.Date);
            string json = JsonConvert.SerializeObject(document, Formatting.Indented);
            string html = DailyNewsHtmlRenderer.Render(document);
            await Task.Run(() =>
            {
                lock (storageGate)
                {
                    string transaction = stem + ".transaction";
                    RecoverTransaction(transaction);
                    Directory.CreateDirectory(transaction);
                    try
                    {
                        foreach (string extension in new[] { ".json", ".html" })
                        {
                            string path = stem + extension;
                            if (File.Exists(path)) File.Copy(path, Path.Combine(transaction, "old" + extension));
                            File.WriteAllText(Path.Combine(transaction, "new" + extension), extension == ".json" ? json : html, new UTF8Encoding(false));
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        File.WriteAllText(Path.Combine(transaction, "prepared"), "1");
                        foreach (string extension in new[] { ".html", ".json" })
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            File.Move(Path.Combine(transaction, "new" + extension), stem + extension, true);
                        }
                        File.WriteAllText(Path.Combine(transaction, "committed"), "1");
                    }
                    finally { RecoverTransaction(transaction); }
                }
            }, cancellationToken);
        }

        private static void RecoverTransaction(string transaction)
        {
            if (!Directory.Exists(transaction)) return;
            string stem = transaction[..^".transaction".Length];
            string name = Path.GetFileName(stem);
            if (!(name.StartsWith("国内资讯_", StringComparison.Ordinal) || name.StartsWith("国际资讯_", StringComparison.Ordinal)) ||
                !DateTime.TryParseExact(name[5..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _)) return;
            if (File.Exists(Path.Combine(transaction, "prepared")) && !File.Exists(Path.Combine(transaction, "committed")))
            {
                foreach (string extension in new[] { ".html", ".json" })
                {
                    // A remaining staged file means this component was never committed.
                    if (File.Exists(Path.Combine(transaction, "new" + extension))) continue;
                    string old = Path.Combine(transaction, "old" + extension);
                    if (File.Exists(old)) File.Copy(old, stem + extension, true);
                    else if (File.Exists(stem + extension)) File.Delete(stem + extension);
                }
                File.WriteAllText(Path.Combine(transaction, "committed"), "rolled back");
            }
            foreach (string file in new[] { "old.json", "old.html", "new.json", "new.html", "prepared", "committed" })
                File.Delete(Path.Combine(transaction, file));
            Directory.Delete(transaction, false);
        }

        private static async Task WriteAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
        {
            string temporaryPath = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken);
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }

        private void CleanupTemporaryFiles()
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles(OutputFolder, "*.tmp", SearchOption.TopDirectoryOnly))
                {
                    TryDeleteTemporaryFile(path);
                }
            }
            catch (Exception error)
            {
                DebugHelper.WriteLine("Daily news temporary file cleanup failed: " + error.Message);
            }
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception error)
            {
                DebugHelper.WriteLine("Daily news temporary file could not be removed ({0}): {1}", path, error.Message);
            }
        }

        private static string GetPrefix(DailyNewsRegion region) =>
            region == DailyNewsRegion.Domestic ? "国内资讯" : "国际资讯";
    }
}
