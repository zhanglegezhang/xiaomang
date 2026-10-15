using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace ShareX
{
    internal static class DailyNewsHtmlRenderer
    {
        public static string Render(DailyNewsDocument document)
        {
            string title = document.Region + "资讯 · " + document.Date;
            StringBuilder html = new StringBuilder(64 * 1024);
            html.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">")
                .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
                .Append("<title>").Append(E(title)).Append("</title><style>")
                .Append(Css).Append("</style></head><body><main>");

            bool domestic = string.Equals(document.Region, "国内", StringComparison.Ordinal);
            string switchTarget = domestic ? "international" : "domestic";
            string switchText = domestic ? ">> 国际资讯" : "<< 国内资讯";
            html.Append("<header><div><h1>").Append(E(title)).Append("</h1><p class=\"meta\">生成于 ")
                .Append(E(document.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")))
                .Append(" · 共 ").Append(document.Stats.Total).Append(" 条</p></div><a class=\"switch-region\" href=\"xiaomangtongxue-news://")
                .Append(switchTarget).Append("\">").Append(E(switchText)).Append("</a></header>");

            AppendDigest(html, document.Digest);
            AppendSourceStatus(html, document.SourcesSucceeded, document.SourcesFailed);
            AppendFilters(html, document.Items);
            AppendItems(html, document);

            html.Append("</main><script>").Append(Script).Append("</script></body></html>");
            return html.ToString();
        }

        public static string RenderMessage(string title, string message)
        {
            return "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><style>" + Css +
                "</style></head><body><main><section class=\"empty\"><h1>" + E(title) + "</h1><p>" + E(message) +
                "</p></section></main></body></html>";
        }

        private static void AppendDigest(StringBuilder html, DailyNewsDigest digest)
        {
            if (digest == null) return;
            html.Append("<section class=\"digest\"><h2>").Append(E(digest.Headline)).Append("</h2>");
            if (digest.KeyItems?.Count > 0)
            {
                html.Append("<ol>");
                foreach (string item in digest.KeyItems.Where(x => !string.IsNullOrWhiteSpace(x)))
                    html.Append("<li>").Append(E(item)).Append("</li>");
                html.Append("</ol>");
            }
            if (digest.CategoryBrief?.Count > 0)
            {
                html.Append("<div class=\"briefs\">");
                foreach (KeyValuePair<string, string> item in digest.CategoryBrief)
                    html.Append("<p><b>").Append(E(item.Key)).Append("：</b>").Append(E(item.Value)).Append("</p>");
                html.Append("</div>");
            }
            html.Append("</section>");
        }

        private static void AppendSourceStatus(StringBuilder html, List<string> successes, List<DailyNewsFailure> failures)
        {
            successes ??= new List<string>();
            failures ??= new List<DailyNewsFailure>();
            if (successes.Count == 0 && failures.Count == 0) return;
            html.Append("<section class=\"sources\">");
            if (successes.Count > 0)
            {
                html.Append("<p class=\"source-ok\"><b>成功：</b>")
                    .Append(E(string.Join("、", successes))).Append("</p>");
            }
            if (failures.Count > 0)
            {
                html.Append("<p class=\"source-failed\"><b>失败：</b>");
                for (int index = 0; index < failures.Count; index++)
                {
                    if (index > 0) html.Append("；");
                    DailyNewsFailure failure = failures[index];
                    html.Append(E(failure.Name)).Append("（").Append(E(failure.Reason)).Append("）");
                }
                html.Append("</p>");
            }
            html.Append("</section>");
        }

        private static void AppendFilters(StringBuilder html, List<DailyNewsItem> items)
        {
            string[] categories = items.Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct().OrderBy(x => x).ToArray();
            html.Append("<nav class=\"filters\"><button class=\"active\" data-filter=\"*\">全部</button>");
            foreach (string category in categories)
                html.Append("<button data-filter=\"").Append(E(category)).Append("\">").Append(E(category)).Append("</button>");
            html.Append("</nav>");
        }

        private static void AppendItems(StringBuilder html, DailyNewsDocument document)
        {
            Dictionary<string, DailyNewsGroup> groups = document.Groups?.ToDictionary(x => x.GroupId) ?? new Dictionary<string, DailyNewsGroup>();
            List<DailyNewsItem> ordered = OrderItems(document.Items);
            html.Append("<section class=\"items\">");
            foreach (DailyNewsItem item in ordered)
            {
                groups.TryGetValue(item.GroupId ?? "", out DailyNewsGroup group);
                string classes = "card" + (item.SourceLevel == "C" ? " level-c" : "") + (item.GroupId != null ? " grouped" : "");
                html.Append("<article class=\"").Append(classes).Append("\" data-category=\"").Append(E(item.Category)).Append("\">");
                if (group != null)
                {
                    int otherCount = document.Items.Where(member => member.GroupId == group.GroupId)
                        .Select(member => member.SourceName).Where(name => !string.IsNullOrWhiteSpace(name) &&
                            !string.Equals(name, item.SourceName, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    html.Append("<div class=\"group-mark\">").Append(E(group.Topic)).Append(" · 另有 ").Append(otherCount).Append(" 家媒体报道</div>");
                }
                html.Append("<h2>").Append(E(item.TitleChinese)).Append("</h2>");
                if (!string.Equals(item.TitleChinese, item.TitleOriginal, StringComparison.OrdinalIgnoreCase))
                    html.Append("<p class=\"original\">").Append(E(item.TitleOriginal)).Append("</p>");
                html.Append("<p class=\"summary\">").Append(E(string.IsNullOrWhiteSpace(item.SummaryChinese) ? "该来源仅提供标题。" : item.SummaryChinese)).Append("</p>");
                html.Append("<div class=\"footer\"><span>").Append(E(item.SourceName)).Append(" · ").Append(E(item.SourceLevel)).Append("级 · ")
                    .Append(E(FormatTime(item))).Append("</span><span class=\"badges\">");
                if (item.SourceLevel == "C") html.Append("<i class=\"only-title\">仅标题</i>");
                foreach (string tag in item.Tags ?? new List<string>())
                    html.Append("<i class=\"tag ").Append(TagClass(tag)).Append("\">").Append(E(tag)).Append("</i>");
                html.Append("</span>");
                if (DailyNewsFetcher.IsWebUrl(item.Url))
                    html.Append("<a href=\"").Append(E(item.Url)).Append("\" target=\"_blank\" rel=\"noopener\">阅读原文</a>");
                html.Append("</div></article>");
            }
            if (ordered.Count == 0) html.Append("<div class=\"empty\">最近24小时暂无资讯。</div>");
            html.Append("</section>");
        }

        private static List<DailyNewsItem> OrderItems(List<DailyNewsItem> items)
        {
            List<DailyNewsItem> source = items.OrderByDescending(x => x.PublishedAt).ToList();
            List<DailyNewsItem> output = new List<DailyNewsItem>();
            HashSet<string> used = new HashSet<string>();
            foreach (DailyNewsItem item in source)
            {
                if (!used.Add(item.Id)) continue;
                output.Add(item);
                if (item.GroupId != null)
                {
                    foreach (DailyNewsItem member in source.Where(x => x.GroupId == item.GroupId && used.Add(x.Id))) output.Add(member);
                }
            }
            return output;
        }

        private static string FormatTime(DailyNewsItem item)
        {
            string value = item.PublishedAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            return item.TimeEstimated ? value + "（估算）" : value;
        }

        private static string TagClass(string tag) => tag switch
        {
            "权威" or "官方通报" => "blue",
            "传言" or "未证实" => "orange",
            "辟谣" => "green",
            _ => "gray"
        };

        private static string E(string value) => WebUtility.HtmlEncode(value ?? "");

        private const string Css = @"
:root{color-scheme:dark;font-family:'Microsoft YaHei UI','Microsoft YaHei',sans-serif;font-size:15px;background:#202020;color:#eee}
*{box-sizing:border-box}body{margin:0;background:#202020;color:#eee}main{max-width:1680px;margin:auto;padding:24px}
header{display:flex;justify-content:space-between;align-items:center;gap:20px;border-bottom:1px solid #555;margin-bottom:16px}h1{font-size:24px;margin:0 0 8px}h2{font-size:17px;margin:0 0 8px}.meta,.original{color:#aaa;font-size:12px}.switch-region{flex:none;color:#69adff;font-size:14px;text-decoration:none;padding:7px 10px;border-radius:5px}.switch-region:hover{background:#303b48;color:#8fc3ff}
.digest,.empty{background:#303030;border:1px solid #4c4c4c;border-radius:8px;padding:16px;margin-bottom:14px}.digest ol{padding-left:24px}.briefs{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:4px 18px}.briefs p{margin:4px 0;color:#d5d5d5}
.sources{font-size:11px;line-height:1.45;background:#292929;color:#999;border:1px solid #404040;border-radius:6px;padding:5px 8px;margin-bottom:8px}.sources p{margin:0}.sources p+p{margin-top:2px}.source-ok b{color:#88bfa0}.source-failed b{color:#d69b82}.filters{display:flex;gap:8px;flex-wrap:wrap;position:sticky;top:0;background:#202020;padding:10px 0;z-index:2}.filters button{background:#353535;color:#ddd;border:1px solid #555;border-radius:16px;padding:6px 14px;cursor:pointer}.filters button.active{background:#4b8ad9;color:#fff;border-color:#4b8ad9}
.items{display:flex;flex-direction:column;gap:10px}.card{background:#2b2b2b;border:1px solid #454545;border-radius:8px;padding:16px}.card.grouped{border-left:4px solid #5c87b2}.card.level-c{background:#282522;border-color:#695d4b}.group-mark{font-size:12px;color:#8fbbe8;margin-bottom:7px}.original{margin:0 0 10px}.summary{line-height:1.75;color:#ddd}.footer{display:flex;align-items:center;gap:12px;flex-wrap:wrap;color:#aaa;font-size:12px}.footer a{margin-left:auto;color:#75baff;text-decoration:none}.badges{display:flex;gap:5px}.tag,.only-title{font-style:normal;border-radius:10px;padding:2px 7px;background:#555;color:#eee}.tag.blue{background:#245b91}.tag.orange{background:#8a5422}.tag.green{background:#287348}.tag.gray{background:#505050}.only-title{background:#6d5836}.hidden{display:none!important}
@media(max-width:800px){main{padding:12px}.footer a{margin-left:0}.briefs{grid-template-columns:1fr}}
";

        private const string Script = @"
document.querySelectorAll('[data-filter]').forEach(function(button){
 button.addEventListener('click',function(){
  document.querySelectorAll('[data-filter]').forEach(function(x){x.classList.remove('active')});
  button.classList.add('active');
  var filter=button.getAttribute('data-filter');
  document.querySelectorAll('.card').forEach(function(card){card.classList.toggle('hidden',filter!=='*'&&card.getAttribute('data-category')!==filter)});
 });
});";
    }
}
