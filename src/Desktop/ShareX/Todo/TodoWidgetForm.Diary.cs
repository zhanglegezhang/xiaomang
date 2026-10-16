using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;
using ShareX.Properties;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private sealed class DiaryPageComponent : WidgetPageComponent
        {

            internal Panel diaryPanel => this;
            internal readonly Label diaryTitleLabel;
            internal readonly Button localDiaryButton;
            internal readonly VirtualClipboardPanel diaryRecordsPanel;
            internal readonly SlimListScrollBar diarySlimScrollBar;
            private bool arrangingDiary;
            internal readonly Label diaryRecentLabel, diaryFrequentLabel;
            internal readonly Button[] diaryRecordButtons = new Button[LocalDiaryManager.ShortcutLimit * 2];
            internal readonly Timer diaryActivityTimer;
            internal readonly ToolTip diaryRecordToolTip = new ToolTip();
            internal DateTime? diaryActivityStamp;
            private string diaryActivityPath;
            internal bool diaryRefreshing, diaryOpening;
            internal readonly Label diaryHintLabel;
            private readonly Label diaryEntryLabel;

            internal async Task OpenDiaryAsync(string path = null)
            {
                if (diaryOpening) return;
                diaryOpening = true; localDiaryButton.Enabled = false;
                foreach (Button button in diaryRecordButtons) button.Enabled = false;
                try
                {
                    await LocalDiaryManager.OpenAsync(path);
                    if (!IsDisposed) diaryHintLabel.Text = "日记已打开，内容保存在本地。";
                }
                catch (Exception error)
                {
                    DebugHelper.WriteException(error, "打开本地日记失败。");
                    if (!IsDisposed) diaryHintLabel.Text = "打开失败：" + error.Message;
                }
                finally
                {
                    diaryOpening = false;
                    if (!IsDisposed) { localDiaryButton.Enabled = true; foreach (Button button in diaryRecordButtons) button.Enabled = true; await RefreshDiaryShortcutsAsync(true); }
                }
            }

            internal async Task RefreshDiaryShortcutsAsync(bool force = false)
            {
                if (diaryRefreshing || IsDisposed || Disposing) return;
                diaryRefreshing = true;
                try
                {
                    var result = await Task.Run(() =>
                    {
                        string path = LocalDiaryManager.ActivityPath;
                        bool hasActivity = File.Exists(path);
                        DateTime stamp = File.GetLastWriteTimeUtc(path);
                        // Without an activity file, retain the directory-scan fallback so
                        // older diary data and externally created notes remain discoverable.
                        LocalDiaryManager.Shortcuts records = force || !hasActivity || path != diaryActivityPath || stamp != diaryActivityStamp
                            ? LocalDiaryManager.ReadShortcuts(LocalDiaryManager.DataFolder) : null;
                        return (path, stamp, records);
                    });
                    if (IsDisposed || Disposing || result.records == null) return;
                    diaryActivityStamp = result.stamp;
                    diaryActivityPath = result.path;
                    diaryRecentLabel.Text = result.records.Recent.Length == 0 ? "最近编辑 · 暂无记录" : "最近编辑";
                    diaryFrequentLabel.Text = result.records.Frequent.Length == 0 ? "最常使用 · 暂无记录" : "最常使用";
                    for (int i = 0; i < diaryRecordButtons.Length; i++)
                    {
                        var group = i < LocalDiaryManager.ShortcutLimit ? result.records.Recent : result.records.Frequent;
                        var entry = group.ElementAtOrDefault(i % LocalDiaryManager.ShortcutLimit);
                        Button button = diaryRecordButtons[i]; button.Tag = entry; button.Visible = entry != null;
                        if (entry == null) continue;
                        button.Text = (entry.folder ? "目录 · " : "") + entry.DisplayName;
                        button.AccessibleName = "打开日记 " + entry.path;
                        diaryRecordToolTip.SetToolTip(button, entry.path + (i < LocalDiaryManager.ShortcutLimit ? "" : $"\n已打开 {entry.opens} 次"));
                    }
                    if (Owner.expanded && Owner.Settings.ActiveModule == Module) ArrangePage();
                }
                catch (Exception error)
                {
                    if (!IsDisposed && !Disposing) { diaryHintLabel.Text = "记录暂未读取：" + error.Message; diaryActivityStamp = null; }
                }
                finally { diaryRefreshing = false; }
            }


            internal readonly PictureBox diaryTitleLogo;

            internal void LayoutDiaryPanel()
            {
                if (arrangingDiary) return;
                arrangingDiary = true;
                try
                {
                    Owner.LayoutPageTitle(diaryTitleLogo, diaryTitleLabel, diaryPanel.ClientSize.Width - Owner.D(26));
                    int fullWidth = Math.Max(Owner.D(80), diaryPanel.ClientSize.Width - Owner.D(16));
                    int contentHeight = 2 * (Owner.D(26) + Owner.D(4)) + diaryRecordButtons.Count(button => button.Tag != null) * Owner.D(36);
                    int width = fullWidth;
                    int inset = Owner.D(12), gap = Owner.D(10);
                    int titleWidth = TextRenderer.MeasureText(diaryEntryLabel.Text, diaryEntryLabel.Font).Width;
                    int hintWidth = TextRenderer.MeasureText(diaryHintLabel.Text, diaryHintLabel.Font).Width;
                    int titleHeight = diaryEntryLabel.Font.Height;
                    bool wrap = false;
                    int hintHeight = 0;
                    int MeasureEntryHeight()
                    {
                        wrap = titleWidth + hintWidth + gap + inset * 2 > width;
                        hintHeight = TextRenderer.MeasureText(diaryHintLabel.Text, diaryHintLabel.Font,
                            new Size(Math.Max(1, width - inset * 2), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                        return wrap ? titleHeight + hintHeight + Owner.D(18) : Math.Max(Owner.D(34), Math.Max(titleHeight, hintHeight) + Owner.D(12));
                    }
                    int height = MeasureEntryHeight();
                    int entryTop = diaryTitleLabel.Bottom + Owner.D(12);
                    int AvailableRecordsHeight() => Math.Max(Owner.D(24), diaryPanel.ClientSize.Height - entryTop - height - Owner.D(8) - Owner.D(12));
                    if (contentHeight > AvailableRecordsHeight())
                    {
                        width = Math.Max(Owner.D(60), fullWidth - Owner.D(12));
                        height = MeasureEntryHeight();
                    }
                    localDiaryButton.Bounds = new Rectangle(Owner.D(8), diaryTitleLabel.Bottom + Owner.D(12), width, height);
                    diaryEntryLabel.Bounds = new Rectangle(inset, wrap ? Owner.D(6) : (height - titleHeight) / 2, Math.Min(titleWidth, width - inset * 2), titleHeight);
                    diaryHintLabel.Bounds = wrap
                        ? new Rectangle(inset, diaryEntryLabel.Bottom + Owner.D(4), width - inset * 2, hintHeight)
                        : new Rectangle(diaryEntryLabel.Right + gap, 0, width - diaryEntryLabel.Right - gap - inset, height);
                    int recordsTop = localDiaryButton.Bottom + Owner.D(8);
                    diaryRecordsPanel.Bounds = new Rectangle(Owner.D(8), recordsTop, fullWidth, AvailableRecordsHeight());
                    diaryRecordsPanel.SetContentHeight(contentHeight);
                    int rowWidth = width;
                    Point scroll = diaryRecordsPanel.AutoScrollPosition;
                    int y = 0;
                    for (int group = 0; group < 2; group++)
                    {
                        Label heading = group == 0 ? diaryRecentLabel : diaryFrequentLabel;
                        heading.Bounds = new Rectangle(0, y + scroll.Y, rowWidth, Owner.D(22));
                        y += Owner.D(26);
                        foreach (Button button in diaryRecordButtons.Skip(group * LocalDiaryManager.ShortcutLimit).Take(LocalDiaryManager.ShortcutLimit))
                        {
                            if (button.Tag == null) continue;
                            button.Bounds = new Rectangle(0, y + scroll.Y, rowWidth, Owner.D(30));
                            y += Owner.D(36);
                        }
                        y += Owner.D(4);
                    }
                    Owner.LayoutSlimScrollBar(diaryRecordsPanel, diarySlimScrollBar);
                }
                finally { arrangingDiary = false; }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    diaryActivityTimer.Dispose();
                    diaryRecordToolTip.Dispose();
                }
                base.Dispose(disposing);
            }

            internal DiaryPageComponent(TodoWidgetForm owner) : base(owner, "Diary")
            {
                diaryTitleLabel = new Label { Text = "日记", Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), BackColor = Color.Transparent };
                Owner.ConfigureTitleDrag(diaryTitleLabel);
                diaryPanel.Controls.Add(diaryTitleLabel);
                diaryTitleLogo = Owner.CreateTitleLogo(diaryPanel);
                localDiaryButton = Owner.CreateOtherButton("");
                ((OtherActionButton)localDiaryButton).PersistentAccentBorder = true;
                localDiaryButton.Font = new Font(localDiaryButton.Font.FontFamily, localDiaryButton.Font.SizeInPoints + 2f, localDiaryButton.Font.Style);
                localDiaryButton.AccessibleName = "打开本地日记";
                localDiaryButton.Click += async (sender, e) => await OpenDiaryAsync();
                diaryPanel.Controls.Add(localDiaryButton);
                diaryHintLabel = new Label { Text = "树形目录 · 全文搜索 · 自动保存", ForeColor = TodoWidgetForm.HalfWhiteColor, BackColor = Color.Transparent, AutoSize = false };
                                diaryHintLabel.Font = Font;
                diaryHintLabel.TextAlign = ContentAlignment.MiddleLeft;
                diaryEntryLabel = new Label { Text = "本地日记", Font = localDiaryButton.Font, ForeColor = localDiaryButton.ForeColor, BackColor = Color.Transparent, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
                localDiaryButton.Controls.Add(diaryEntryLabel);
                localDiaryButton.Controls.Add(diaryHintLabel);
                diaryEntryLabel.Click += (sender, e) => localDiaryButton.PerformClick();
                diaryHintLabel.Click += (sender, e) => localDiaryButton.PerformClick();
                diaryHintLabel.TextChanged += (sender, e) => { if (diaryRecordsPanel != null && Owner.expanded) ArrangePage(); };
                diaryRecordsPanel = Owner.CreateVirtualClipboardList();
                diaryPanel.Controls.Add(diaryRecordsPanel);
                diarySlimScrollBar = new SlimListScrollBar(diaryRecordsPanel, Owner.D(12));
                diaryRecordsPanel.Controls.Add(diarySlimScrollBar);
                diaryRecentLabel = new Label { Text = "最近编辑", AutoSize = false, BackColor = Color.Transparent, ForeColor = TodoWidgetForm.HalfWhiteColor };
                diaryFrequentLabel = new Label { Text = "最常使用", AutoSize = false, BackColor = Color.Transparent, ForeColor = TodoWidgetForm.HalfWhiteColor };
                diaryRecordsPanel.Controls.AddRange(new Control[] { diaryRecentLabel, diaryFrequentLabel });
                Owner.AttachListWheel(diaryRecentLabel, diaryRecordsPanel);
                Owner.AttachListWheel(diaryFrequentLabel, diaryRecordsPanel);
                for (int i = 0; i < diaryRecordButtons.Length; i++)
                {
                    Button button = Owner.CreateOtherButton("");
                    ((OtherActionButton)button).LeftAlignedText = true;
                    button.Font = new Font(Font.FontFamily, 9f);
                    button.TextAlign = ContentAlignment.MiddleLeft;
                    button.AutoEllipsis = true;
                    button.Padding = new Padding(Owner.D(8), 0, Owner.D(8), 0);
                    button.Visible = false;
                    button.Click += async (sender, e) => { if (button.Tag is LocalDiaryManager.DiaryEntry entry) await OpenDiaryAsync(entry.path); };
                    diaryRecordButtons[i] = button; diaryRecordsPanel.Controls.Add(button);
                    Owner.AttachListWheel(button, diaryRecordsPanel);
                }
                diaryRecordsPanel.ViewportChanged += (_, _) => LayoutDiaryPanel();
                diaryActivityTimer = new Timer { Interval = 1500 };
                diaryActivityTimer.Tick += async (sender, e) => { if (Owner.expanded && Owner.Settings.ActiveModule == "Diary") await RefreshDiaryShortcutsAsync(); };
                diaryActivityTimer.Start();

            }

            internal override void ArrangePage() => LayoutDiaryPanel();
            internal override void RefreshPage() { _ = RefreshDiaryShortcutsAsync(); }
        }
    }
}
