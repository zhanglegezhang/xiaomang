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
        private sealed class ClipboardPageComponent : WidgetPageComponent
        {

            internal Panel clipboardPanel => this;
            internal readonly Label clipboardTitleLabel;
            internal readonly Button clipboardClearButton;
            internal readonly Label clipboardCopyHintLabel;
            internal readonly Label clipboardPreviewLinesLabel;
            internal readonly RoundedBorderPanel clipboardPreviewLinesBorder;
            internal readonly TextBox clipboardPreviewLinesInput;
            internal readonly VirtualClipboardPanel clipboardList;
            internal readonly SlimListScrollBar clipboardSlimScrollBar;
            internal readonly Label clipboardEmptyLabel;
            internal readonly Timer clipboardCopyStatusTimer;
            internal readonly Font clipboardCopyHintFont;
            internal readonly Font clipboardCopySuccessFont;
            internal DateTime clipboardCopyStatusStartedAt;
            internal Color clipboardCopyStatusColor;
            internal readonly Font clipboardItemFont;
            internal readonly List<Panel> clipboardRowPool = new List<Panel>();
            internal readonly Dictionary<Guid, ClipboardRowMetric> clipboardMetricCache = new Dictionary<Guid, ClipboardRowMetric>();
            internal ClipboardTextItem[] clipboardViewItems = Array.Empty<ClipboardTextItem>();
            internal int[] clipboardRowOffsets = Array.Empty<int>();
            internal int[] clipboardRowHeights = Array.Empty<int>();
            internal int clipboardVirtualRowWidth;
            internal bool updatingClipboardViewport;
            internal Func<string, bool> clipboardCopyAction = ClipboardHelpers.CopyText;

            internal void RenderClipboard()
            {
                clipboardViewItems = Owner.Settings.ClipboardItems.ToArray();
                clipboardClearButton.Enabled = clipboardViewItems.Length > 0;
                HashSet<Guid> currentIds = clipboardViewItems.Select(x => x.Id).ToHashSet();
                foreach (Guid staleId in clipboardMetricCache.Keys.Where(x => !currentIds.Contains(x)).ToArray())
                {
                    clipboardMetricCache.Remove(staleId);
                }
                RebuildClipboardMetrics(clipboardViewItems, clipboardList.ClientSize);

                if (clipboardViewItems.Length == 0)
                {
                    foreach (Panel row in clipboardRowPool) row.Visible = false;
                    clipboardEmptyLabel.Bounds = new Rectangle(0, 0,
                        Math.Max(Owner.D(80), clipboardList.ClientSize.Width - (clipboardList.HasVerticalScroll ? Owner.D(12) : 0)), Owner.D(54));
                    if (!clipboardList.Controls.Contains(clipboardEmptyLabel)) clipboardList.Controls.Add(clipboardEmptyLabel);
                    clipboardEmptyLabel.Visible = true;
                }
                else
                {
                    clipboardEmptyLabel.Visible = false;
                    UpdateClipboardViewport();
                }
            }

            internal void RebuildClipboardMetrics(ClipboardTextItem[] items, Size viewportSize)
            {
                if (items.Length == 0 || viewportSize.Width <= 0 || viewportSize.Height <= 0)
                {
                    clipboardRowOffsets = Array.Empty<int>();
                    clipboardRowHeights = Array.Empty<int>();
                    clipboardVirtualRowWidth = Math.Max(Owner.D(120), viewportSize.Width);
                    clipboardList.AutoScrollMinSize = Size.Empty;
                    Owner.LayoutSlimScrollBar(clipboardList, clipboardSlimScrollBar);
                    return;
                }

                int minimumEditorHeight = Math.Min(clipboardItemFont.Height + Owner.D(3), GetClipboardMaximumEditorHeight(viewportSize.Height));
                int minimumRowHeight = Math.Max(minimumEditorHeight, Owner.D(20)) + Owner.D(24);
                bool mustScroll = (long)items.Length * minimumRowHeight + (long)(items.Length - 1) * Owner.D(7) > viewportSize.Height;
                int rowWidth = Math.Max(Owner.D(120), viewportSize.Width - (mustScroll ? Owner.D(12) : 0));
                CalculateClipboardMetrics(items, rowWidth, viewportSize.Height, out int[] offsets, out int[] heights, out int totalHeight);
                if (!mustScroll && totalHeight > viewportSize.Height)
                {
                    rowWidth = Math.Max(Owner.D(120), viewportSize.Width - Owner.D(12));
                    CalculateClipboardMetrics(items, rowWidth, viewportSize.Height, out offsets, out heights, out totalHeight);
                }

                clipboardVirtualRowWidth = rowWidth;
                clipboardRowOffsets = offsets;
                clipboardRowHeights = heights;
                Size minimumSize = new Size(0, Math.Max(0, totalHeight));
                if (clipboardList.AutoScrollMinSize != minimumSize) clipboardList.AutoScrollMinSize = minimumSize;
                Owner.LayoutSlimScrollBar(clipboardList, clipboardSlimScrollBar);
            }

            internal void CalculateClipboardMetrics(ClipboardTextItem[] items, int rowWidth, int viewportHeight,
                out int[] offsets, out int[] heights, out int totalHeight)
            {
                int editorWidth = Math.Max(Owner.D(55), rowWidth - Owner.D(45));
                int maximumEditorHeight = GetClipboardMaximumEditorHeight(viewportHeight);
                WidgetListLayout.Calculate(items.Length, Owner.D(7),
                    i => GetClipboardMetric(items[i], editorWidth, maximumEditorHeight).RowHeight,
                    out offsets, out heights, out totalHeight);
            }

            internal int GetClipboardMaximumEditorHeight(int viewportHeight)
            {
                int previewLines = Math.Clamp(Owner.Settings.ClipboardPreviewLines, 1, 30);
                int lineLimit = clipboardItemFont.Height * previewLines;
                return Math.Min(lineLimit, Math.Max(clipboardItemFont.Height + Owner.D(3), viewportHeight - Owner.D(20)));
            }

            internal void CommitClipboardPreviewLines()
            {
                if (!int.TryParse(clipboardPreviewLinesInput.Text, out int lines) || lines < 1 || lines > 30)
                {
                    clipboardPreviewLinesInput.Text = Math.Clamp(Owner.Settings.ClipboardPreviewLines, 1, 30).ToString();
                    return;
                }

                clipboardPreviewLinesInput.Text = lines.ToString();
                if (Owner.Settings.ClipboardPreviewLines == lines) return;
                Owner.Settings.ClipboardPreviewLines = lines;
                clipboardMetricCache.Clear();
                if (Owner.ready && Owner.Settings.ActiveModule == "Clipboard") RenderClipboard();
                SettingManager.SaveApplicationConfigAsync();
            }

            internal ClipboardRowMetric GetClipboardMetric(ClipboardTextItem item, int editorWidth, int maximumEditorHeight)
            {
                string text = string.IsNullOrEmpty(item.Text) ? " " : item.GetDisplayText();
                clipboardMetricCache.TryGetValue(item.Id, out ClipboardRowMetric previous);
                bool Matches(ClipboardRowMetric candidate) => candidate != null && candidate.Text == text &&
                    candidate.EditorWidth == editorWidth && candidate.MaximumEditorHeight == maximumEditorHeight;
                if (Matches(previous)) return previous;
                if (Matches(previous?.Alternate))
                {
                    ClipboardRowMetric metric = previous.Alternate;
                    previous.Alternate = null;
                    metric.Alternate = previous;
                    clipboardMetricCache[item.Id] = metric;
                    return metric;
                }

                // Measuring an entire large clipboard payload stalls the UI even though only
                // the first few preview lines are visible. Keep the full text in the editor.
                int measureLimit = Math.Max(4096, Math.Min(30000, Math.Clamp(Owner.Settings.ClipboardPreviewLines, 1, 30) * editorWidth));
                string previewText = (text.Length > measureLimit ? text.Substring(0, measureLimit) : text).TrimEnd();
                Size measured = TextRenderer.MeasureText(previewText, clipboardItemFont, new Size(editorWidth, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
                int editorHeight = Math.Max(clipboardItemFont.Height + Owner.D(3), measured.Height + Owner.D(2));
                editorHeight = Math.Min(editorHeight, maximumEditorHeight);
                if (previous != null) previous.Alternate = null;
                ClipboardRowMetric measuredMetric = new ClipboardRowMetric
                {
                    Alternate = previous,
                    Id = item.Id,
                    Text = text,
                    EditorWidth = editorWidth,
                    MaximumEditorHeight = maximumEditorHeight,
                    EditorHeight = editorHeight,
                    RowHeight = Math.Max(editorHeight, Owner.D(20)) + Owner.D(24)
                };
                clipboardMetricCache[item.Id] = measuredMetric;
                return measuredMetric;
            }

            internal void WarmClipboardMetrics()
            {
                if (IsDisposed || Owner.Settings.ClipboardItems.Count == 0) return;
                Size expandedSize = Owner.GetSavedExpandedSize();
                int listWidth = Math.Max(Owner.D(120), expandedSize.Width - Owner.moduleSidebar.Width - Owner.D(2) - Owner.D(12));
                int titleHeight = Math.Max(TextRenderer.MeasureText("2 剪切板", clipboardTitleLabel.Font).Height + Owner.D(6), Owner.D(26));
                int listHeight = Math.Max(Owner.D(80), expandedSize.Height - titleHeight - Owner.D(7));
                int editorWidth = Math.Max(Owner.D(55), listWidth - Owner.D(45));
                int maximumEditorHeight = GetClipboardMaximumEditorHeight(listHeight);
                foreach (ClipboardTextItem item in Owner.Settings.ClipboardItems)
                {
                    GetClipboardMetric(item, editorWidth, maximumEditorHeight);
                }
            }

            internal void UpdateClipboardViewport()
            {
                if (Owner.changingExpansion || !Owner.expanded || Owner.Settings.ActiveModule != "Clipboard") return;
                if (updatingClipboardViewport || clipboardViewItems.Length == 0 || clipboardList.ClientSize.Height <= 0) return;
                updatingClipboardViewport = true;
                try
                {
                    int scrollY = Math.Max(0, -clipboardList.AutoScrollPosition.Y);
                    int overscan = Owner.D(80);
                    int start = 0;
                    while (start < clipboardViewItems.Length && clipboardRowOffsets[start] + clipboardRowHeights[start] < scrollY - overscan) start++;
                    int end = start;
                    int bottom = scrollY + clipboardList.ClientSize.Height + overscan;
                    while (end < clipboardViewItems.Length && clipboardRowOffsets[end] <= bottom) end++;
                    int needed = Math.Max(0, end - start);

                    while (clipboardRowPool.Count < needed)
                    {
                        Panel row = CreateClipboardRow();
                        clipboardRowPool.Add(row);
                        clipboardList.Controls.Add(row);
                    }

                    HashSet<Panel> used = new HashSet<Panel>();
                    for (int index = start; index < end; index++)
                    {
                        ClipboardTextItem item = clipboardViewItems[index];
                        Panel row = clipboardRowPool.FirstOrDefault(x => !used.Contains(x) && x.Tag is Guid id && id == item.Id) ??
                            clipboardRowPool.First(x => !used.Contains(x));
                        used.Add(row);
                        BindClipboardRow(row, item);
                        row.Bounds = new Rectangle(0, clipboardRowOffsets[index] - scrollY,
                            clipboardVirtualRowWidth, clipboardRowHeights[index]);
                        LayoutClipboardRow(row);
                        row.Visible = true;
                    }

                    WidgetListLayout.HideUnusedRows(clipboardRowPool, used);
                }
                finally
                {
                    updatingClipboardViewport = false;
                }
            }

            internal Panel CreateClipboardRow()
            {
                int deleteSize = Owner.D(20);
                Panel row = new FavoriteRowPanel
                {
                    Size = new Size(Math.Max(Owner.D(120), clipboardList.ClientSize.Width), Owner.D(28)),
                    Margin = Padding.Empty,
                    BackColor = TodoWidgetForm.WidgetRowColor
                };
                TextBox textBox = new TextBox
                {
                    ReadOnly = true,
                    ShortcutsEnabled = true,
                    Multiline = true,
                    WordWrap = true,
                    ScrollBars = ScrollBars.None,
                    Font = clipboardItemFont,
                    ForeColor = ShareXResources.Theme.TextColor,
                    BackColor = row.BackColor,
                    BorderStyle = BorderStyle.None,
                    Location = Point.Empty,
                    Size = Size.Empty
                };
                ArrowlessTextScrollBar textScrollBar = new ArrowlessTextScrollBar(textBox)
                {
                    BackColor = row.BackColor,
                    Location = Point.Empty,
                    Size = Size.Empty
                };
                Button deleteButton = Owner.CreateButton("", Point.Empty, new Size(deleteSize, deleteSize));
                deleteButton.Name = "DeleteButton";
                deleteButton.Image = Owner.sharedDeleteIcon;
                deleteButton.AccessibleName = "从剪切板列表移除，历史日志保留";
                Owner.recordTip.SetToolTip(deleteButton, "从当前列表移除；历史日志仍保留。");
                deleteButton.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 50);
                deleteButton.FlatAppearance.BorderSize = 1;
                deleteButton.Click += (sender, e) =>
                {
                    if (row.Tag is Guid id) DeleteClipboardItem(id);
                };
                textBox.MouseDoubleClick += (sender, e) =>
                {
                    if (e.Button == MouseButtons.Left) CopyClipboardItem(row);
                };
                row.MouseDoubleClick += (sender, e) =>
                {
                    if (e.Button == MouseButtons.Left) CopyClipboardItem(row);
                };
                row.Controls.Add(textBox);
                row.Controls.Add(textScrollBar);
                row.Controls.Add(new Label
                {
                    Name = "CreatedAtLabel",
                    ForeColor = TodoWidgetForm.HalfWhiteColor,
                    BackColor = Color.Transparent,
                    Font = clipboardCopyHintFont,
                    TextAlign = ContentAlignment.MiddleLeft
                });
                row.Controls.Add(deleteButton);
                Owner.AttachListWheel(row, clipboardList);
                Owner.AttachHoverEvents(textBox);
                Owner.AttachHoverEvents(row);
                return row;
            }

            internal bool CopyClipboardItem(Panel row)
            {
                bool copied = false;
                if (row?.Tag is Guid id)
                {
                    ClipboardTextItem item = Owner.Settings.ClipboardItems.FirstOrDefault(x => x.Id == id);
                    copied = item != null && !string.IsNullOrEmpty(item.Text) && clipboardCopyAction(item.Text);
                }
                ShowClipboardCopyStatus(copied);
                return copied;
            }

            internal void ShowClipboardCopyStatus(bool copied)
            {
                clipboardCopyStatusTimer.Stop();
                clipboardCopyStatusStartedAt = DateTime.UtcNow;
                clipboardCopyStatusColor = copied ? ShareXResources.Theme.TextColor : Color.FromArgb(220, 155, 85);
                clipboardCopyHintLabel.Text = copied ? "已复制" : "复制失败";
                clipboardCopyHintLabel.Font = copied ? clipboardCopySuccessFont : clipboardCopyHintFont;
                clipboardCopyHintLabel.ForeColor = clipboardCopyStatusColor;
                clipboardCopyHintLabel.BringToFront();
                clipboardCopyStatusTimer.Start();
            }

            internal void RestoreClipboardCopyHint()
            {
                clipboardCopyStatusTimer.Stop();
                clipboardCopyStatusStartedAt = default;
                clipboardCopyHintLabel.Text = "双击复制";
                clipboardCopyHintLabel.Font = clipboardCopyHintFont;
                clipboardCopyHintLabel.ForeColor = TodoWidgetForm.HalfWhiteColor;
            }

            internal void BindClipboardRow(Panel row, ClipboardTextItem item)
            {
                TextBox editor = row.Controls.OfType<TextBox>().Single();
                row.Controls.OfType<Label>().Single(x => x.Name == "CreatedAtLabel").Text =
                    item.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                string displayText = item.GetDisplayText();
                if (row.Tag is not Guid id || id != item.Id || editor.Text != displayText)
                {
                    row.Tag = item.Id;
                    editor.Text = displayText;
                    editor.AccessibleName = "剪切板文本 " + item.CreatedAt.ToString("HH:mm:ss");
                }
            }

            internal void DeleteClipboardItem(Guid id)
            {
                Owner.Settings.ClipboardItems = Owner.Settings.ClipboardItems.Where(x => x.Id != id).ToList();
                clipboardMetricCache.Remove(id);
                SettingManager.SaveApplicationConfigAsync();
                RenderClipboard();
            }

            internal bool ClearClipboardItems()
            {
                if (Owner.Settings.ClipboardItems.Count == 0) return true;
                List<ClipboardTextItem> previous = Owner.Settings.ClipboardItems;
                Owner.Settings.ClipboardItems = new List<ClipboardTextItem>();
                try
                {
                    if (!SettingManager.SaveApplicationConfig())
                    {
                        Owner.Settings.ClipboardItems = previous;
                        return false;
                    }
                }
                catch (Exception error)
                {
                    Owner.Settings.ClipboardItems = previous;
                    DebugHelper.WriteException(error, "Clipboard clear could not be saved.");
                    return false;
                }
                clipboardMetricCache.Clear();
                RestoreClipboardCopyHint();
                RenderClipboard();
                return true;
            }

            internal void LayoutClipboardRow(Panel row)
            {
                TextBox editor = row.Controls.OfType<TextBox>().Single();
                Control editorScrollBar = row.Controls.Cast<Control>().Single(x => x is ArrowlessTextScrollBar);
                Button deleteButton = row.Controls.OfType<Button>().Single(x => x.Name == "DeleteButton");
                Label createdAtLabel = row.Controls.OfType<Label>().Single(x => x.Name == "CreatedAtLabel");
                int rowWidth = clipboardVirtualRowWidth > 0 ? clipboardVirtualRowWidth : GetClipboardRowWidth();
                int scrollGap = Owner.D(1);
                deleteButton.Location = new Point(rowWidth - Owner.D(6) - deleteButton.Width, Owner.D(6));
                int editorWidth = Math.Max(Owner.D(55), deleteButton.Left - Owner.D(8) - Owner.D(6) - Owner.D(5));
                int maximumEditorHeight = GetClipboardMaximumEditorHeight(clipboardList.ClientSize.Height);
                Guid itemId = row.Tag is Guid id ? id : Guid.Empty;
                ClipboardTextItem item = clipboardViewItems.FirstOrDefault(x => x.Id == itemId && x.Text == editor.Text) ??
                    new ClipboardTextItem { Id = itemId, Text = editor.Text };
                ClipboardRowMetric metric = GetClipboardMetric(item, editorWidth, maximumEditorHeight);
                int editorHeight = metric.EditorHeight;
                int rowHeight = metric.RowHeight;

                row.Size = new Size(rowWidth, rowHeight);
                editor.Bounds = new Rectangle(Owner.D(8), Owner.D(6), editorWidth, editorHeight);
                editorScrollBar.Bounds = new Rectangle(editor.Right + scrollGap, editor.Top, Owner.D(6), editor.Height);
                createdAtLabel.Bounds = new Rectangle(Owner.D(8), editor.Bottom + Owner.D(1),
                    Math.Max(Owner.D(45), deleteButton.Left - Owner.D(10)), Owner.D(15));
            }

            internal int GetClipboardRowWidth()
            {
                return Math.Max(Owner.D(120), clipboardList.ClientSize.Width - (clipboardList.HasVerticalScroll ? Owner.D(12) : 0));
            }


            internal readonly PictureBox clipboardTitleLogo;

            internal void LayoutClipboardPanel()
            {
                int padding = Owner.D(8), gap = Owner.D(6), width = clipboardPanel.ClientSize.Width - padding * 2;
                int titleWidth = TextRenderer.MeasureText(clipboardTitleLabel.Text, clipboardTitleLabel.Font).Width + Owner.D(8);
                Owner.LayoutPageTitle(clipboardTitleLogo, clipboardTitleLabel, titleWidth);
                int titleTop = clipboardTitleLabel.Top, titleHeight = clipboardTitleLabel.Height;
                int clearWidth = Math.Max(Owner.D(60), TextRenderer.MeasureText(clipboardClearButton.Text, clipboardClearButton.Font).Width + Owner.D(14));
                clipboardClearButton.Bounds = new Rectangle(clipboardPanel.ClientSize.Width - padding - clearWidth,
                    titleTop + Owner.D(2), clearWidth, titleHeight - Owner.D(4));
                int previewTop = clipboardTitleLabel.Bottom + gap;
                int previewLabelWidth = TextRenderer.MeasureText(clipboardPreviewLinesLabel.Text,
                    clipboardPreviewLinesLabel.Font).Width + Owner.D(6);
                int previewInputWidth = Owner.D(36), previewGap = Owner.D(5);
                int previewInputHeight = Owner.D(25);
                clipboardPreviewLinesLabel.Bounds = new Rectangle(padding, previewTop, previewLabelWidth, previewInputHeight);
                clipboardPreviewLinesBorder.Bounds = new Rectangle(clipboardPreviewLinesLabel.Right + previewGap,
                    previewTop, previewInputWidth, previewInputHeight);
                clipboardPreviewLinesInput.Bounds = new Rectangle(Owner.D(2),
                    Math.Max(0, (clipboardPreviewLinesBorder.ClientSize.Height - clipboardPreviewLinesInput.PreferredHeight) / 2),
                    clipboardPreviewLinesBorder.ClientSize.Width - Owner.D(4), clipboardPreviewLinesInput.PreferredHeight);
                int hintWidth = TextRenderer.MeasureText(clipboardCopyHintLabel.Text, clipboardCopyHintLabel.Font).Width + Owner.D(6);
                clipboardCopyHintLabel.Visible = clipboardPreviewLinesBorder.Right + gap + hintWidth < clipboardPanel.ClientSize.Width - padding;
                clipboardCopyHintLabel.Bounds = new Rectangle(clipboardPanel.ClientSize.Width - padding - hintWidth,
                    previewTop, hintWidth, previewInputHeight);
                int listTop = clipboardPreviewLinesBorder.Bottom + gap;
                clipboardList.Bounds = new Rectangle(padding, listTop, width,
                    Math.Max(Owner.D(20), clipboardPanel.ClientSize.Height - listTop - padding));
                Owner.LayoutSlimScrollBar(clipboardList, clipboardSlimScrollBar);
                if (!Owner.resizing) RenderClipboard();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    clipboardCopyStatusTimer.Dispose();
                    clipboardCopyHintFont.Dispose();
                    clipboardCopySuccessFont.Dispose();
                    clipboardItemFont.Dispose();
                }
                base.Dispose(disposing);
            }

            internal ClipboardPageComponent(TodoWidgetForm owner) : base(owner, "Clipboard")
            {
                clipboardCopyHintFont = new Font(Font.FontFamily, 7f);
                clipboardCopySuccessFont = new Font(Font.FontFamily, 7f, FontStyle.Bold);
                clipboardItemFont = new Font(Font.FontFamily, 8.3f);
                clipboardTitleLabel = new Label
                {
                    Text = "剪切板记录",
                    Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
                    ForeColor = ShareXResources.Theme.TextColor,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Owner.ConfigureTitleDrag(clipboardTitleLabel);
                clipboardPanel.Controls.Add(clipboardTitleLabel);
                clipboardTitleLogo = Owner.CreateTitleLogo(clipboardPanel);
                clipboardClearButton = Owner.CreateButton("清空列表", Point.Empty, new Size(Owner.D(53), Owner.D(24)));
                clipboardClearButton.Font = clipboardCopyHintFont;
                clipboardClearButton.TextAlign = ContentAlignment.MiddleCenter;
                clipboardClearButton.Padding = Padding.Empty;
                clipboardClearButton.FlatAppearance.BorderColor = Color.FromArgb(105, 105, 105);
                clipboardClearButton.FlatAppearance.BorderSize = 1;
                clipboardClearButton.AccessibleName = "清空当前剪切板列表，历史日志保留";
                Owner.recordTip.SetToolTip(clipboardClearButton, "清空当前列表；便签文件夹中的历史日志仍保留。");
                clipboardClearButton.Enabled = Owner.Settings.ClipboardItems.Count > 0;
                clipboardClearButton.Click += (sender, e) =>
                {
                    int count = Owner.Settings.ClipboardItems.Count;
                    if (count == 0) return;
                    if (MessageBox.Show(this, $"清空当前剪切板列表中的 {count} 条记录？\r\n历史日志仍保留，可在“便签文件夹”中查看。\r\nWindows 当前复制的内容不受影响。",
                        "清空列表", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    if (!ClearClipboardItems())
                        MessageBox.Show(this, "清空保存失败，原有记录已保留，请稍后重试。", "剪切板", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
                clipboardPanel.Controls.Add(clipboardClearButton);
                clipboardCopyHintLabel = new Label
                {
                    Text = "双击复制",
                    Font = clipboardCopyHintFont,
                    ForeColor = TodoWidgetForm.HalfWhiteColor,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleRight
                };
                Owner.ConfigureTitleDrag(clipboardCopyHintLabel);
                clipboardPanel.Controls.Add(clipboardCopyHintLabel);
                clipboardPreviewLinesLabel = new Label
                {
                    Text = "预览行数",
                    Font = clipboardCopyHintFont,
                    ForeColor = TodoWidgetForm.HalfWhiteColor,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleRight
                };
                clipboardPanel.Controls.Add(clipboardPreviewLinesLabel);
                clipboardPreviewLinesBorder = new RoundedBorderPanel
                {
                    BackColor = TodoWidgetForm.WidgetInputColor,
                    BorderColor = Color.FromArgb(105, 105, 105),
                    CornerRadius = 5
                };
                clipboardPanel.Controls.Add(clipboardPreviewLinesBorder);
                clipboardPreviewLinesInput = new TextBox
                {
                    Text = Math.Clamp(Owner.Settings.ClipboardPreviewLines, 1, 30).ToString(),
                    Font = clipboardCopyHintFont,
                    ForeColor = ShareXResources.Theme.TextColor,
                    BackColor = TodoWidgetForm.WidgetInputColor,
                    BorderStyle = BorderStyle.None,
                    MaxLength = 2,
                    TextAlign = HorizontalAlignment.Center,
                    AccessibleName = "剪贴板每条预览行数",
                    AccessibleDescription = "输入 1 到 30，按回车或移开焦点生效"
                };
                clipboardPreviewLinesInput.KeyDown += (sender, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        CommitClipboardPreviewLines();
                    }
                    else if (e.KeyCode == Keys.Escape)
                    {
                        e.SuppressKeyPress = true;
                        clipboardPreviewLinesInput.Text = Math.Clamp(Owner.Settings.ClipboardPreviewLines, 1, 30).ToString();
                    }
                };
                clipboardPreviewLinesInput.Leave += (sender, e) => CommitClipboardPreviewLines();
                clipboardPreviewLinesBorder.Controls.Add(clipboardPreviewLinesInput);
                clipboardList = Owner.CreateVirtualClipboardList();
                clipboardList.ViewportChanged += (sender, e) => UpdateClipboardViewport();
                clipboardPanel.Controls.Add(clipboardList);
                clipboardSlimScrollBar = new SlimListScrollBar(clipboardList, Owner.D(12));
                clipboardList.Controls.Add(clipboardSlimScrollBar);
                clipboardEmptyLabel = Owner.CreateEmptyLabel("还没有记录到纯文本");

                clipboardCopyStatusTimer = new Timer { Interval = 50 };
                clipboardCopyStatusTimer.Tick += (sender, e) =>
                {
                    double elapsedMilliseconds = (DateTime.UtcNow - clipboardCopyStatusStartedAt).TotalMilliseconds;
                    const double holdMilliseconds = 300;
                    const double fadeMilliseconds = 700;
                    if (elapsedMilliseconds <= holdMilliseconds) return;

                    float progress = (float)Math.Min(1d, (elapsedMilliseconds - holdMilliseconds) / fadeMilliseconds);
                    clipboardCopyHintLabel.ForeColor = BlendColor(clipboardCopyStatusColor, TodoWidgetForm.WidgetBackgroundColor, progress);
                    if (progress >= 1f) RestoreClipboardCopyHint();
                };
            }

            internal override void ArrangePage() => LayoutClipboardPanel();
            internal override void RefreshPage() { RenderClipboard(); }
        }
    }
}
