using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ShareX
{
    public class TodoSettings
    {
        public List<TodoItem> Items = new List<TodoItem>();
        public int WidgetWidth;
        public int WidgetHeight;
        public int WidgetY = -1;
        public int CompactTheme;
        public int CompactThemeRevision;
        public int ReminderVersion;
        public int NoteLogSeedVersion;
        public string ActiveModule = "Todo";
        [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
        public List<string> VisibleModules = new List<string> { "Todo", "Clipboard", "Favorites", "News", "Diary" };
        private static readonly string[] ModuleOrder = { "Todo", "Clipboard", "Favorites", "News", "Diary" };
        public bool IsModuleVisible(string module) => ModuleOrder.Contains(module) && (VisibleModules == null || VisibleModules.Contains(module));
        internal string FirstVisibleModule() => ModuleOrder.FirstOrDefault(IsModuleVisible) ?? string.Empty;
        internal void EnsureSearchSourceVisible(string source)
        {
            string module = source switch { "本地笔记" => "Diary", "固定文件" or "手动收藏" => "Favorites", "待办" => "Todo", _ => "Clipboard" };
            if (IsModuleVisible(module)) return;
            string name = module switch { "Todo" => "待办", "Diary" => "日记", "Favorites" => "收藏夹", _ => "剪切板" };
            throw new InvalidOperationException($"请先在“全局设置 → 常规 → 启动和页签”中启用“{name}”页签。");
        }
        public List<ClipboardTextItem> ClipboardItems = new List<ClipboardTextItem>();
        public int ClipboardPreviewLines = 8;
        public List<FavoriteTextItem> FavoriteItems = new List<FavoriteTextItem>();
        public Size NoteLogWindowSize = new Size(1200, 800);
        public int NoteLogWindowSizeVersion;
    }

    public class TodoItem
    {
        public Guid Id = Guid.NewGuid();
        public string Text = "";
        public bool IsCompleted;
        public DateTime CreatedAt = DateTime.Now;
        public DateTime? CompletedAt;
        public DateTime? ReminderAt;
        public bool OneHourReminderSent;
        public bool DueReminderSent;
        public string VoiceAudioRelativePath = "";
        public int VoiceDurationSeconds;
    }

    public class ClipboardTextItem
    {
        public Guid Id = Guid.NewGuid();
        public string Text = "";
        public DateTime CreatedAt = DateTime.Now;
        public string SourceLabel = "";
        public string SourceKey = "";

        public string GetDisplayText() => string.IsNullOrEmpty(SourceLabel) ? Text : $"【{SourceLabel}】{Text}";

        public bool HasSameContent(ClipboardTextItem other)
        {
            if (other == null) return false;
            if (!string.IsNullOrEmpty(SourceKey) || !string.IsNullOrEmpty(other.SourceKey))
                return !string.IsNullOrEmpty(SourceKey) && SourceKey == other.SourceKey;
            return Text == other.Text;
        }
    }

    public class FavoriteTextItem
    {
        public Guid Id = Guid.NewGuid();
        public string Text = "";
        public DateTime CreatedAt = DateTime.Now;
    }
}
