using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal sealed class DailyNewsAuthenticationException : Exception
    {
        public DailyNewsAuthenticationException() : base("DeepSeek API Key 无效或无权访问。")
        {
        }
    }

    internal sealed class DailyNewsAiClient : IDisposable
    {
        private const string EditorPrompt = @"你是资深新闻编辑，负责把多语种新闻加工成中文资讯条目。

【铁律】
1. 只做翻译、摘要、分类打标。不评论、不引申、不补充原文没有的信息。
2. 输出必须是合法 JSON，不带 markdown 代码块标记，不带任何解释文字。
3. 信息缺失时填 null，绝不编造。

【翻译要求】
- 标题翻成简体中文，简洁有力，控制在40字以内。
- 专有名词首次出现时保留英文原名和中文注释；人名保留原文。
- 数字、日期、单位必须准确。直译优先，不调整原文立场。

【摘要要求】
- 2到3句，60到120字；只陈述原文事实。
- 信息不足时不凑字数。

【分类】
从以下选且仅选一个：AI | 科技 | 财经 | 社会 | 国际 | 政治 | 体育 | 娱乐 | 健康 | 军事

【标签】从以下选0到2个，宁缺勿滥：权威、官方通报、传言、未证实、辟谣、转发、独家。

【输出 JSON 格式】
{
  ""title_zh"": ""字符串"",
  ""summary_zh"": ""字符串"",
  ""category"": ""字符串"",
  ""tags"": [""字符串""],
  ""key_entities"": [""最多5个""],
  ""confidence"": 0.0,
  ""language"": ""语言代码""
}";

        private const string DigestPrompt = @"你是新闻简报编辑。根据今日标题清单生成中文导读。
只客观陈述，不评论、不预测、不煽动。总长度300字以内。
输出必须是合法 JSON：
{
  ""headline"": ""一句话点出核心事项"",
  ""key_items"": [""要闻1"", ""要闻2"", ""要闻3""],
  ""category_brief"": {""分类"": ""1到2句速览""}
}";

        private const string GroupPrompt = @"你是新闻编辑，负责识别报道同一事件的不同条目。只分组，不删除或改写任何条目。
同一事件必须核心主体和动作一致；仅主题相近不算同一事件。
输出必须是合法 JSON：
{
  ""groups"": [
    {""main_id"": ""条目id"", ""member_ids"": [""id1"", ""id2""], ""topic"": ""10字以内主题""}
  ]
}
不属于任何组的条目不要出现在结果中。";

        private static readonly HashSet<string> Categories = new HashSet<string>
        {
            "AI", "科技", "财经", "社会", "国际", "政治", "体育", "娱乐", "健康", "军事"
        };

        private static readonly HashSet<string> AllowedTags = new HashSet<string>
        {
            "权威", "官方通报", "传言", "未证实", "辟谣", "转发", "独家"
        };

        private readonly DailyNewsSettings settings;
        private readonly HttpClient client;

        public DailyNewsAiClient(DailyNewsSettings settings, string apiKey)
        {
            this.settings = settings;
            client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(30, settings.TimeoutSeconds * 3)) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("xiaomangtongxue/1.0");
        }

        public async Task EnrichItemsAsync(List<DailyNewsItem> items, IProgress<DailyNewsProgress> progress,
            CancellationToken cancellationToken)
        {
            using SemaphoreSlim gate = new SemaphoreSlim(settings.Concurrency);
            int completed = 0;
            DailyNewsAuthenticationException authenticationError = null;

            Task[] tasks = items.Select(async item =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    if (authenticationError != null) return;
                    await EnrichItemAsync(item, cancellationToken);
                }
                catch (DailyNewsAuthenticationException e)
                {
                    authenticationError = e;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    ApplyAiFailure(item);
                }
                finally
                {
                    gate.Release();
                    int value = Interlocked.Increment(ref completed);
                    progress?.Report(new DailyNewsProgress
                    {
                        Percent = 30 + value * 52 / Math.Max(1, items.Count),
                        Message = $"正在翻译和整理（{value}/{items.Count}）"
                    });
                }
            }).ToArray();

            await Task.WhenAll(tasks);
            cancellationToken.ThrowIfCancellationRequested();
            if (authenticationError != null) throw authenticationError;
            if (items.Count > 0 && items.All(item => item.AiFailed))
                throw new InvalidOperationException("AI 处理全部失败，请检查账户余额和服务连接；已保留上次成功资讯。");
        }

        public async Task<DailyNewsDigest> CreateDigestAsync(List<DailyNewsItem> items, CancellationToken cancellationToken)
        {
            if (!settings.EnableDigest || items.Count == 0) return CreateFallbackDigest(items);
            try
            {
                string titles = string.Join("\n", items.GroupBy(x => x.Category).Select(group =>
                    "【" + group.Key + "】\n" + string.Join("\n", group.Select(x => "- " + x.TitleChinese))));
                JObject json = await SendJsonAsync(DigestPrompt, $"日期：{DateTime.Today:yyyy-MM-dd}\n{titles}", 0.4, 700, cancellationToken);
                DailyNewsDigest digest = json.ToObject<DailyNewsDigest>() ?? CreateFallbackDigest(items);
                digest.KeyItems ??= new List<string>();
                digest.CategoryBrief ??= new Dictionary<string, string>();
                return digest;
            }
            catch (DailyNewsAuthenticationException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                return CreateFallbackDigest(items);
            }
        }

        public async Task<List<DailyNewsGroup>> CreateGroupsAsync(List<DailyNewsItem> items, CancellationToken cancellationToken)
        {
            List<DailyNewsGroup> output = new List<DailyNewsGroup>();
            if (!settings.EnableTopicGroup || items.Count < 2) return output;
            HashSet<string> validIds = items.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            HashSet<string> assignedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DailyNewsItem item in items) item.GroupId = null;
            int groupNumber = 1;

            foreach (IEnumerable<DailyNewsItem> batch in items.GroupBy(x => x.Category).SelectMany(group => group.Chunk(40)))
            {
                string list = string.Join("\n", batch.Select(x => $"{x.Id} | {x.TitleChinese} | {x.SourceName}"));
                JObject json;
                try
                {
                    json = await SendJsonAsync(GroupPrompt, list, 0.1, 2000, cancellationToken);
                }
                catch (DailyNewsAuthenticationException)
                {
                    throw;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    continue;
                }

                foreach (JToken token in json["groups"] as JArray ?? new JArray())
                {
                    if (token is not JObject groupToken || groupToken["member_ids"] is not JArray members) continue;
                    HashSet<string> batchIds = batch.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                    List<string> ids = members.Where(member => member.Type == JTokenType.String).Values<string>()
                        .Where(id => validIds.Contains(id) && batchIds.Contains(id) && !assignedIds.Contains(id)).Distinct().ToList();
                    if (ids.Count < 2) continue;
                    DailyNewsGroup group = new DailyNewsGroup
                    {
                        GroupId = $"g_{DateTime.Today:yyyyMMdd}_{groupNumber++:000}",
                        Topic = groupToken["topic"]?.Type == JTokenType.String ? (string)groupToken["topic"] : "同一事件",
                        MemberIds = ids
                    };
                    output.Add(group);
                    assignedIds.UnionWith(ids);
                    foreach (DailyNewsItem item in items.Where(x => ids.Contains(x.Id))) item.GroupId = group.GroupId;
                }
            }

            return output;
        }

        private async Task EnrichItemAsync(DailyNewsItem item, CancellationToken cancellationToken)
        {
            string content = item.SourceContent ?? "";
            if (content.Length > settings.MaxSummaryChars) content = content.Substring(0, settings.MaxSummaryChars);
            string userPrompt = $"来源：{item.SourceName}（{item.SourceRegion}）\n" +
                $"原文发布时间：{item.PublishedAt:O}\n原文标题：{item.TitleOriginal}\n原文正文：{content}\n\n请按系统要求输出 JSON。";
            JObject json = await SendJsonAsync(EditorPrompt, userPrompt, 0.2, 800, cancellationToken);

            string title = json.Value<string>("title_zh")?.Trim();
            if (string.IsNullOrWhiteSpace(title)) throw new JsonException("title_zh 缺失");
            item.TitleChinese = title.Length > 80 ? title.Substring(0, 80) : title;
            item.SummaryChinese = json.Value<string>("summary_zh")?.Trim() ?? "";
            string category = json.Value<string>("category")?.Trim();
            if (Categories.Contains(category)) item.Category = category;
            item.Tags = (json["tags"] as JArray)?.Values<string>().Where(AllowedTags.Contains).Distinct().Take(2).ToList()
                ?? new List<string>();
            item.KeyEntities = (json["key_entities"] as JArray)?.Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Take(5).ToList()
                ?? new List<string>();
            item.Confidence = Math.Clamp(json.Value<double?>("confidence") ?? 0, 0, 1);
            item.SourceLanguage = json.Value<string>("language")?.Trim() ?? item.SourceLanguage;
            item.AiFailed = false;
        }

        private async Task<JObject> SendJsonAsync(string systemPrompt, string userPrompt, double temperature, int maxTokens,
            CancellationToken cancellationToken)
        {
            JObject request = new JObject
            {
                ["model"] = settings.Model,
                ["temperature"] = temperature,
                ["max_tokens"] = maxTokens,
                ["response_format"] = new JObject { ["type"] = "json_object" },
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = systemPrompt },
                    new JObject { ["role"] = "user", ["content"] = userPrompt }
                }
            };

            string endpoint = settings.ApiBase.TrimEnd('/') + "/chat/completions";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(request.ToString(Formatting.None), Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new DailyNewsAuthenticationException();
                }
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    if (attempt < 2)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken);
                        continue;
                    }
                }
                response.EnsureSuccessStatusCode();
                string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                JObject envelope = JObject.Parse(responseText);
                string content = envelope["choices"]?[0]?["message"]?["content"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(content)) throw new JsonException("AI 返回内容为空");
                return JObject.Parse(StripCodeFence(content));
            }
            throw new HttpRequestException("DeepSeek 服务暂时不可用");
        }

        private static string StripCodeFence(string text)
        {
            string value = text.Trim();
            if (value.StartsWith("```", StringComparison.Ordinal))
            {
                int firstLine = value.IndexOf('\n');
                if (firstLine >= 0) value = value.Substring(firstLine + 1);
                int ending = value.LastIndexOf("```", StringComparison.Ordinal);
                if (ending >= 0) value = value.Substring(0, ending);
            }
            return value.Trim();
        }

        private static void ApplyAiFailure(DailyNewsItem item)
        {
            item.AiFailed = true;
            if (string.IsNullOrWhiteSpace(item.TitleChinese)) item.TitleChinese = item.TitleOriginal;
            if (string.IsNullOrWhiteSpace(item.SummaryChinese)) item.SummaryChinese = "AI 处理失败，请通过原文链接查看内容。";
            if (!item.Tags.Contains("AI处理失败")) item.Tags.Add("AI处理失败");
        }

        private static DailyNewsDigest CreateFallbackDigest(List<DailyNewsItem> items)
        {
            return new DailyNewsDigest
            {
                Headline = items.Count == 0 ? "今日暂无资讯" : $"今日共整理 {items.Count} 条资讯",
                KeyItems = items.Take(3).Select(x => x.TitleChinese).ToList(),
                CategoryBrief = items.GroupBy(x => x.Category).ToDictionary(x => x.Key, x => $"本分类共 {x.Count()} 条资讯。")
            };
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
