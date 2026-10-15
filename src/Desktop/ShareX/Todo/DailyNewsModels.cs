using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace ShareX
{
    public enum DailyNewsRegion
    {
        Domestic,
        International
    }

    public sealed class DailyNewsItem
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("source_name")]
        public string SourceName { get; set; } = "";

        [JsonProperty("source_region")]
        public string SourceRegion { get; set; } = "";

        [JsonProperty("source_level")]
        public string SourceLevel { get; set; } = "A";

        [JsonProperty("source_lang")]
        public string SourceLanguage { get; set; } = "zh";

        [JsonProperty("via", NullValueHandling = NullValueHandling.Ignore)]
        public string Via { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; } = "社会";

        [JsonProperty("title_zh")]
        public string TitleChinese { get; set; } = "";

        [JsonProperty("title_original")]
        public string TitleOriginal { get; set; } = "";

        [JsonProperty("summary_zh")]
        public string SummaryChinese { get; set; } = "";

        [JsonProperty("source_content", NullValueHandling = NullValueHandling.Ignore)]
        public string SourceContent { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new List<string>();

        [JsonProperty("key_entities")]
        public List<string> KeyEntities { get; set; } = new List<string>();

        [JsonProperty("confidence")]
        public double Confidence { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; } = "";

        [JsonProperty("published_at")]
        public DateTimeOffset PublishedAt { get; set; }

        [JsonProperty("fetched_at")]
        public DateTimeOffset FetchedAt { get; set; }

        [JsonProperty("time_estimated")]
        public bool TimeEstimated { get; set; }

        [JsonProperty("group_id", NullValueHandling = NullValueHandling.Include)]
        public string GroupId { get; set; }

        [JsonProperty("ai_failed")]
        public bool AiFailed { get; set; }
    }

    public sealed class DailyNewsFailure
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("reason")]
        public string Reason { get; set; } = "";
    }

    public sealed class DailyNewsGroup
    {
        [JsonProperty("group_id")]
        public string GroupId { get; set; } = "";

        [JsonProperty("topic")]
        public string Topic { get; set; } = "";

        [JsonProperty("member_ids")]
        public List<string> MemberIds { get; set; } = new List<string>();
    }

    public sealed class DailyNewsDigest
    {
        [JsonProperty("headline")]
        public string Headline { get; set; } = "";

        [JsonProperty("key_items")]
        public List<string> KeyItems { get; set; } = new List<string>();

        [JsonProperty("category_brief")]
        public Dictionary<string, string> CategoryBrief { get; set; } = new Dictionary<string, string>();
    }

    public sealed class DailyNewsStats
    {
        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("by_category")]
        public Dictionary<string, int> ByCategory { get; set; } = new Dictionary<string, int>();

        [JsonProperty("sources_ok")]
        public int SourcesOk { get; set; }

        [JsonProperty("sources_failed")]
        public int SourcesFailed { get; set; }
    }

    public sealed class DailyNewsDocument
    {
        [JsonProperty("date")]
        public string Date { get; set; } = "";

        [JsonProperty("region")]
        public string Region { get; set; } = "";

        [JsonProperty("generated_at")]
        public DateTimeOffset GeneratedAt { get; set; }

        [JsonProperty("stats")]
        public DailyNewsStats Stats { get; set; } = new DailyNewsStats();

        [JsonProperty("digest")]
        public DailyNewsDigest Digest { get; set; } = new DailyNewsDigest();

        [JsonProperty("sources_failed")]
        public List<DailyNewsFailure> SourcesFailed { get; set; } = new List<DailyNewsFailure>();

        [JsonProperty("sources_succeeded")]
        public List<string> SourcesSucceeded { get; set; } = new List<string>();

        [JsonProperty("groups")]
        public List<DailyNewsGroup> Groups { get; set; } = new List<DailyNewsGroup>();

        [JsonProperty("items")]
        public List<DailyNewsItem> Items { get; set; } = new List<DailyNewsItem>();
    }

    public sealed class DailyNewsProgress
    {
        public int Percent { get; set; }
        public string Message { get; set; } = "";
    }

    internal sealed class DailyNewsSource
    {
        public string Name { get; set; } = "";
        public DailyNewsRegion Region { get; set; }
        public string Level { get; set; } = "A";
        public string Language { get; set; } = "zh";
        public string Category { get; set; } = "社会";
        public string Url { get; set; } = "";
        public string DailyHotRoute { get; set; }
    }
}
