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
        private sealed partial class FavoritesPageComponent : WidgetPageComponent
        {

            internal Panel favoritesPanel => this;
            internal readonly Label favoriteTitleLabel;
            internal readonly Label favoriteSaveStatusLabel;
            internal readonly RoundedBorderPanel favoriteSearchPanel;
            internal readonly TextBox favoriteSearchBox;
            internal readonly Button addFavoriteButton;
            internal readonly VirtualClipboardPanel favoritesList;
            internal readonly SlimListScrollBar favoritesSlimScrollBar;
            internal readonly Label favoritesEmptyLabel;
            internal readonly Timer favoriteSearchTimer;
            internal readonly Timer favoriteSaveStatusTimer;
            internal readonly Stopwatch favoriteSaveStatusClock = new Stopwatch();
            internal readonly Font favoriteItemFont;
            private readonly Font favoriteMetadataFont;
            internal readonly List<Panel> favoriteRowPool = new List<Panel>();
            internal readonly Dictionary<Guid, VirtualRowMetric> favoriteMetricCache = new Dictionary<Guid, VirtualRowMetric>();
            internal readonly Dictionary<Guid, string> favoriteCommittedTexts = new Dictionary<Guid, string>();
            internal readonly HashSet<Guid> dirtyFavoriteIds = new HashSet<Guid>();
            internal FavoriteTextItem[] favoriteViewItems = Array.Empty<FavoriteTextItem>();
            internal int[] favoriteRowOffsets = Array.Empty<int>();
            internal int[] favoriteRowHeights = Array.Empty<int>();
            internal int favoriteVirtualRowWidth;
            internal bool updatingFavoriteViewport;
            internal bool favoriteRenderPending;
            internal int favoritePendingScrollY;

            internal void AddFavorite()
            {
                FavoriteTextItem item = new FavoriteTextItem();
                Owner.Settings.FavoriteItems.Insert(0, item);
                favoriteCommittedTexts[item.Id] = "";
                favoriteSearchBox.Clear();
                RenderFavorites();
                Control row = favoritesList.Controls.Cast<Control>().FirstOrDefault(x => Equals(x.Tag, item.Id));
                row?.Controls.OfType<TextBox>().FirstOrDefault()?.Focus();
            }

            internal void RenderFavorites()
            {
                CommitFocusedFavorite();
                string search = favoriteSearchBox.Text.Trim();
                favoriteViewItems = Owner.Settings.FavoriteItems
                    .Where(x => string.IsNullOrEmpty(search) || x.Text.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
                HashSet<Guid> currentIds = Owner.Settings.FavoriteItems.Select(x => x.Id).ToHashSet();
                foreach (Guid staleId in favoriteMetricCache.Keys.Where(x => !currentIds.Contains(x)).ToArray())
                {
                    favoriteMetricCache.Remove(staleId);
                }
                foreach (Guid staleId in favoriteCommittedTexts.Keys.Where(x => !currentIds.Contains(x)).ToArray())
                {
                    favoriteCommittedTexts.Remove(staleId);
                    dirtyFavoriteIds.Remove(staleId);
                }
                RebuildFavoriteMetrics(favoriteViewItems, favoritesList.ClientSize);

                if (favoriteViewItems.Length == 0)
                {
                    foreach (Panel row in favoriteRowPool) row.Visible = false;
                    favoritesEmptyLabel.Bounds = new Rectangle(0, 0,
                        Math.Max(Owner.D(80), favoritesList.ClientSize.Width - (favoritesList.HasVerticalScroll ? Owner.D(12) : 0)), Owner.D(54));
                    if (!favoritesList.Controls.Contains(favoritesEmptyLabel)) favoritesList.Controls.Add(favoritesEmptyLabel);
                    favoritesEmptyLabel.Visible = true;
                    favoritesEmptyLabel.Text = string.IsNullOrWhiteSpace(favoriteSearchBox.Text) ? "点击 + 新增一条收藏文本" : "没有匹配的收藏文本";
                }
                else
                {
                    favoritesEmptyLabel.Visible = false;
                    UpdateFavoritesViewport();
                }
                favoritesList.HorizontalScroll.Visible = false;
            }

            internal void ScheduleFavoritesRender()
            {
                favoritePendingScrollY = Math.Max(0, -favoritesList.AutoScrollPosition.Y);
                if (favoriteRenderPending || IsDisposed || Disposing || !IsHandleCreated) return;

                favoriteRenderPending = true;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        favoriteRenderPending = false;
                        if (IsDisposed || Disposing) return;

                        int scrollY = favoritePendingScrollY;
                        RenderFavorites();
                        int maximum = favoritesList.ScrollMaximum;
                        favoritesList.AutoScrollPosition = new Point(0, Math.Clamp(scrollY, 0, maximum));
                        UpdateFavoritesViewport();
                    }));
                }
                catch (InvalidOperationException)
                {
                    favoriteRenderPending = false;
                }
            }

            internal void RebuildFavoriteMetrics(FavoriteTextItem[] items, Size viewportSize)
            {
                if (items.Length == 0 || viewportSize.Width <= 0 || viewportSize.Height <= 0)
                {
                    favoriteRowOffsets = Array.Empty<int>();
                    favoriteRowHeights = Array.Empty<int>();
                    favoriteVirtualRowWidth = Math.Max(Owner.D(120), viewportSize.Width);
                    favoritesList.AutoScrollMinSize = Size.Empty;
                    Owner.LayoutSlimScrollBar(favoritesList, favoritesSlimScrollBar);
                    return;
                }

                int minimumRowHeight = Math.Max(favoriteItemFont.Height + Owner.D(3), Owner.D(20)) + Owner.D(24);
                bool mustScroll = (long)items.Length * minimumRowHeight + (long)(items.Length - 1) * Owner.D(7) > viewportSize.Height;
                int rowWidth = Math.Max(Owner.D(120), viewportSize.Width - (mustScroll ? Owner.D(12) : 0));
                CalculateFavoriteMetrics(items, rowWidth, viewportSize.Height, out int[] offsets, out int[] heights, out int totalHeight);
                if (!mustScroll && totalHeight > viewportSize.Height)
                {
                    rowWidth = Math.Max(Owner.D(120), viewportSize.Width - Owner.D(12));
                    CalculateFavoriteMetrics(items, rowWidth, viewportSize.Height, out offsets, out heights, out totalHeight);
                }
                favoriteVirtualRowWidth = rowWidth;
                favoriteRowOffsets = offsets;
                favoriteRowHeights = heights;
                favoritesList.AutoScrollMinSize = new Size(0, Math.Max(0, totalHeight));
                Owner.LayoutSlimScrollBar(favoritesList, favoritesSlimScrollBar);
            }

            internal void CalculateFavoriteMetrics(FavoriteTextItem[] items, int rowWidth, int viewportHeight,
                out int[] offsets, out int[] heights, out int totalHeight)
            {
                int editorWidth = Math.Max(Owner.D(55), rowWidth - Owner.D(39));
                int maximumEditorHeight = Owner.D(10000);
                WidgetListLayout.Calculate(items.Length, Owner.D(7),
                    i => GetFavoriteMetric(items[i], editorWidth, maximumEditorHeight).RowHeight,
                    out offsets, out heights, out totalHeight);
            }

            internal VirtualRowMetric GetFavoriteMetric(FavoriteTextItem item, int editorWidth, int maximumEditorHeight)
            {
                string text = string.IsNullOrEmpty(item.Text) ? " " : item.Text;
                favoriteMetricCache.TryGetValue(item.Id, out VirtualRowMetric previous);
                bool Matches(VirtualRowMetric candidate) => candidate != null && candidate.Text == text &&
                    candidate.EditorWidth == editorWidth && candidate.MaximumEditorHeight == maximumEditorHeight;
                if (Matches(previous)) return previous;
                if (Matches(previous?.Alternate))
                {
                    VirtualRowMetric metric = previous.Alternate;
                    previous.Alternate = null;
                    metric.Alternate = previous;
                    favoriteMetricCache[item.Id] = metric;
                    return metric;
                }

                // TextRenderer ignores a trailing empty line, while the native multiline editor
                // still needs that line for the caret after Enter. Measure it as a space.
                string measuredText = text.EndsWith("\n", StringComparison.Ordinal) ||
                    text.EndsWith("\r", StringComparison.Ordinal) ? text + " " : text;
                Size measured = TextRenderer.MeasureText(measuredText, favoriteItemFont, new Size(editorWidth, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
                int editorHeight = Math.Max(favoriteItemFont.Height + Owner.D(3), measured.Height + Owner.D(2));
                editorHeight = Math.Min(editorHeight, maximumEditorHeight);
                if (previous != null) previous.Alternate = null;
                VirtualRowMetric measuredMetric = new VirtualRowMetric
                {
                    Alternate = previous,
                    Id = item.Id,
                    Text = text,
                    EditorWidth = editorWidth,
                    MaximumEditorHeight = maximumEditorHeight,
                    EditorHeight = editorHeight,
                    RowHeight = Math.Max(editorHeight, Owner.D(20)) + Owner.D(24)
                };
                favoriteMetricCache[item.Id] = measuredMetric;
                return measuredMetric;
            }

            internal void UpdateFavoritesViewport()
            {
                if (Owner.changingExpansion || !Owner.expanded || Owner.Settings.ActiveModule != "Favorites") return;
                if (updatingFavoriteViewport || favoriteViewItems.Length == 0 || favoritesList.ClientSize.Height <= 0) return;
                updatingFavoriteViewport = true;
                try
                {
                    int scrollY = Math.Max(0, -favoritesList.AutoScrollPosition.Y);
                    TodoWidgetForm.GetVisibleRange(favoriteRowOffsets, favoriteRowHeights, scrollY, favoritesList.ClientSize.Height, Owner.D(80), out int start, out int end);
                    HashSet<Panel> used = new HashSet<Panel>();
                    HashSet<Guid> usedIds = new HashSet<Guid>();

                    // Keep an actively edited row bound to its model even if touchpad scrolling
                    // moves it just outside the viewport.
                    foreach (Panel focusedRow in favoriteRowPool.Where(x => x.ContainsFocus && x.Tag is Guid))
                    {
                        Guid id = (Guid)focusedRow.Tag;
                        int index = Array.FindIndex(favoriteViewItems, x => x.Id == id);
                        if (index < 0 || !usedIds.Add(id)) continue;
                        used.Add(focusedRow);
                        focusedRow.Bounds = new Rectangle(0, favoriteRowOffsets[index] - scrollY,
                            favoriteVirtualRowWidth, favoriteRowHeights[index]);
                        focusedRow.Visible = true;
                    }

                    for (int index = start; index < end; index++)
                    {
                        FavoriteTextItem item = favoriteViewItems[index];
                        Panel row = favoriteRowPool.FirstOrDefault(x => used.Contains(x) && x.Tag is Guid usedId && usedId == item.Id);
                        if (row == null) row = favoriteRowPool.FirstOrDefault(x => !used.Contains(x) && x.Tag is Guid id && id == item.Id);
                        if (row == null) row = favoriteRowPool.FirstOrDefault(x => !used.Contains(x) && !x.ContainsFocus);
                        if (row == null)
                        {
                            row = CreateFavoriteRow();
                            favoriteRowPool.Add(row);
                            favoritesList.Controls.Add(row);
                        }
                        used.Add(row);
                        usedIds.Add(item.Id);
                        BindFavoriteRow(row, item);
                         row.Bounds = new Rectangle(0, favoriteRowOffsets[index] - scrollY,
                             favoriteVirtualRowWidth, favoriteRowHeights[index]);
                        LayoutFavoriteRow(row);
                        row.Visible = true;
                    }
                    WidgetListLayout.HideUnusedRows(favoriteRowPool, used);
                }
                finally
                {
                    updatingFavoriteViewport = false;
                }
            }

            internal Panel CreateFavoriteRow()
            {
                Panel row = new FavoriteRowPanel { Size = new Size(Math.Max(Owner.D(120), favoritesList.ClientSize.Width), Owner.D(28)), Margin = Padding.Empty, BackColor = TodoWidgetForm.WidgetRowColor };
                int deleteSize = Owner.D(20);
                Button deleteButton = Owner.CreateButton("", Point.Empty, new Size(deleteSize, deleteSize));
                deleteButton.Name = "DeleteButton";
                deleteButton.Image = Owner.sharedDeleteIcon;
                deleteButton.AccessibleName = "删除收藏文本";
                deleteButton.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 50);
                deleteButton.FlatAppearance.BorderSize = 1;
                deleteButton.Click += (sender, e) => DeleteFavoriteRow(row);
                TextBox editor = new TextBox
                {
                    Multiline = true,
                    AcceptsReturn = true,
                    ScrollBars = ScrollBars.None,
                    ShortcutsEnabled = true,
                    BorderStyle = BorderStyle.None,
                    Font = favoriteItemFont,
                    ForeColor = ShareXResources.Theme.TextColor,
                    BackColor = row.BackColor,
                    Location = Point.Empty,
                    Size = Size.Empty
                };
                editor.TextChanged += (sender, e) =>
                {
                    if (updatingFavoriteViewport || row.Tag is not Guid id) return;
                    FavoriteTextItem item = Owner.Settings.FavoriteItems.FirstOrDefault(x => x.Id == id);
                    if (item == null) return;
                    item.Text = editor.Text;
                    dirtyFavoriteIds.Add(id);
                    favoriteMetricCache.Remove(id);
                    RebuildFavoriteMetrics(favoriteViewItems, favoritesList.ClientSize);
                    int index = Array.FindIndex(favoriteViewItems, x => x.Id == id);
                    if (index >= 0)
                    {
                        int scrollY = Math.Max(0, -favoritesList.AutoScrollPosition.Y);
                        row.Bounds = new Rectangle(0, favoriteRowOffsets[index] - scrollY,
                            favoriteVirtualRowWidth, favoriteRowHeights[index]);
                        LayoutFavoriteRow(row);
                        KeepFavoriteCaretVisible(row, editor, index);
                    }
                    Owner.noteSaveTimer.Stop();
                    Owner.noteSaveTimer.Start();
                };
                editor.Leave += (sender, e) =>
                {
                    CommitFavoriteRow(row);
                    Owner.ScheduleCollapseIfPointerOutside();
                };
                editor.LostFocus += (sender, e) => CommitFavoriteRow(row);
                row.Controls.Add(editor);
                row.Controls.Add(new Label
                {
                    Name = "CreatedAtLabel",
                    ForeColor = TodoWidgetForm.HalfWhiteColor,
                    BackColor = Color.Transparent,
                    Font = favoriteMetadataFont,
                    TextAlign = ContentAlignment.MiddleLeft
                });
                row.Controls.Add(deleteButton);
                LayoutFavoriteRow(row);
                Owner.AttachListWheel(row, favoritesList);
                Owner.AttachHoverEvents(editor);
                Owner.AttachHoverEvents(row);
                return row;
            }

            internal void BindFavoriteRow(Panel row, FavoriteTextItem item)
            {
                TextBox editor = row.Controls.OfType<TextBox>().Single();
                row.Controls.OfType<Label>().Single(x => x.Name == "CreatedAtLabel").Text =
                    item.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                if (row.Tag is Guid id && id == item.Id && editor.Text == item.Text) return;
                if (row.ContainsFocus) return;
                if (!favoriteCommittedTexts.ContainsKey(item.Id)) favoriteCommittedTexts[item.Id] = item.Text ?? "";
                row.Tag = item.Id;
                editor.Tag = item.Text;
                editor.Text = item.Text;
                editor.AccessibleName = "收藏文本，可编辑并复制";
            }

            internal void LayoutFavoriteRow(Panel row)
            {
                if (row.Tag is not Guid id) return;
                FavoriteTextItem item = Owner.Settings.FavoriteItems.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                TextBox editor = row.Controls.OfType<TextBox>().Single();
                Button deleteButton = row.Controls.OfType<Button>().Single(x => x.Name == "DeleteButton");
                Label createdAtLabel = row.Controls.OfType<Label>().Single(x => x.Name == "CreatedAtLabel");
                int rowWidth = favoriteVirtualRowWidth > 0 ? favoriteVirtualRowWidth : GetFavoriteRowWidth();
                deleteButton.Location = new Point(rowWidth - Owner.D(6) - deleteButton.Width, Owner.D(6));
                int editorWidth = Math.Max(Owner.D(55), deleteButton.Left - Owner.D(8) - Owner.D(5));
                int maximumEditorHeight = Owner.D(10000);
                VirtualRowMetric metric = GetFavoriteMetric(item, editorWidth, maximumEditorHeight);
                int editorHeight = metric.EditorHeight;
                int rowHeight = metric.RowHeight;
                row.Size = new Size(rowWidth, rowHeight);
                editor.Bounds = new Rectangle(Owner.D(8), Owner.D(6), editorWidth, editorHeight);
                createdAtLabel.Bounds = new Rectangle(Owner.D(8), editor.Bottom + Owner.D(1),
                    Math.Max(Owner.D(45), deleteButton.Left - Owner.D(10)), Owner.D(15));
            }

            internal void KeepFavoriteCaretVisible(Panel row, TextBox editor, int index)
            {
                if (!editor.Focused || index < 0 || index >= favoriteRowOffsets.Length) return;

                int viewportHeight = favoritesList.ClientSize.Height;
                if (viewportHeight <= 0) return;
                int caretTop;
                int caretBottom;
                if (editor.SelectionStart >= editor.TextLength)
                {
                    // The native editor reports (0, 0) for the caret after a final newline.
                    caretBottom = favoriteRowOffsets[index] + favoriteRowHeights[index] - Owner.D(3);
                    caretTop = caretBottom - editor.Font.Height;
                }
                else
                {
                    Point caretPosition = editor.GetPositionFromCharIndex(editor.SelectionStart);
                    caretTop = favoriteRowOffsets[index] + editor.Top + caretPosition.Y;
                    caretBottom = caretTop + editor.Font.Height;
                }

                int currentScroll = Math.Max(0, -favoritesList.AutoScrollPosition.Y);
                int nextScroll = currentScroll;
                if (caretBottom > currentScroll + viewportHeight - Owner.D(4))
                    nextScroll = caretBottom - viewportHeight + Owner.D(4);
                else if (caretTop < currentScroll + Owner.D(4))
                    nextScroll = caretTop - Owner.D(4);
                int maximumScroll = favoritesList.ScrollMaximum;
                nextScroll = Math.Clamp(nextScroll, 0, maximumScroll);
                if (nextScroll == currentScroll) return;
                favoritesList.AutoScrollPosition = new Point(0, nextScroll);
                UpdateFavoritesViewport();
            }

            internal bool CommitFocusedFavorite()
            {
                Panel row = favoriteRowPool.FirstOrDefault(x => x.ContainsFocus);
                return row != null && CommitFavoriteRow(row);
            }

            internal bool CommitPendingFavorites()
            {
                bool saved = CommitFocusedFavorite();
                foreach (Guid id in dirtyFavoriteIds.ToArray()) saved |= CommitFavorite(id, false);
                if (saved) ShowFavoriteSavedStatus();
                return saved;
            }

            internal void PreparePersonalDataSnapshot()
            {
                CommitPendingFavorites();
                if (dirtyFavoriteIds.Count != 0) throw new IOException("收藏夹尚未保存成功，请稍后重试。");
                Owner.noteSaveTimer.Stop(); Owner.RememberExpandedBounds();
            }

            internal bool CommitFavoriteRow(Panel row)
            {
                if (row.Tag is not Guid id) return false;
                FavoriteTextItem item = Owner.Settings.FavoriteItems.FirstOrDefault(x => x.Id == id);
                TextBox editor = row.Controls.OfType<TextBox>().FirstOrDefault();
                if (item == null || editor == null) return false;
                item.Text = editor.Text;
                if (!favoriteCommittedTexts.TryGetValue(id, out string previousText) || item.Text != previousText)
                    dirtyFavoriteIds.Add(id);
                bool saved = CommitFavorite(id, true);
                if (saved) editor.Tag = item.Text;
                return saved;
            }

            internal bool CommitFavorite(Guid id, bool showStatus)
            {
                FavoriteTextItem item = Owner.Settings.FavoriteItems.FirstOrDefault(x => x.Id == id);
                if (item == null)
                {
                    dirtyFavoriteIds.Remove(id);
                    favoriteCommittedTexts.Remove(id);
                    return false;
                }

                Owner.noteSaveTimer.Stop();
                string previousText = favoriteCommittedTexts.TryGetValue(id, out string committedText) ? committedText : "";
                if (string.IsNullOrWhiteSpace(item.Text))
                {
                    int originalIndex = Owner.Settings.FavoriteItems.IndexOf(item);
                    Owner.Settings.FavoriteItems.RemoveAll(x => x.Id == id);
                    if (!SettingManager.SaveApplicationConfig())
                    {
                        Owner.Settings.FavoriteItems.Insert(originalIndex, item);
                        ShowFavoriteSaveFailed(id);
                        return false;
                    }
                    favoriteMetricCache.Remove(id);
                    dirtyFavoriteIds.Remove(id);
                    favoriteCommittedTexts.Remove(id);
                    if (showStatus) ShowFavoriteSavedStatus();
                    ScheduleFavoritesRender();
                    return true;
                }
                if (item.Text != previousText)
                {
                    if (!SettingManager.SaveApplicationConfig())
                    {
                        ShowFavoriteSaveFailed(id);
                        return false;
                    }
                    Owner.WriteNoteLog(NoteLogKind.Favorites, string.IsNullOrWhiteSpace(previousText) ? "新增" : "修改", item.Text);
                    favoriteCommittedTexts[id] = item.Text;
                    dirtyFavoriteIds.Remove(id);
                    if (showStatus) ShowFavoriteSavedStatus();
                    ScheduleFavoritesRender();
                    return true;
                }
                dirtyFavoriteIds.Remove(id);
                return false;
            }

            internal void ShowFavoriteSavedStatus()
            {
                favoriteSaveStatusTimer.Stop();
                favoriteSaveStatusClock.Reset();
                bool allSaved = dirtyFavoriteIds.Count == 0;
                favoriteSaveStatusLabel.Text = allSaved ? "已保存" : "仍有内容未保存";
                favoriteSaveStatusLabel.ForeColor = TodoWidgetForm.HalfWhiteColor;
                favoriteSaveStatusLabel.Visible = true;
                favoriteSaveStatusLabel.BringToFront();
                if (allSaved)
                {
                    favoriteSaveStatusClock.Restart();
                    favoriteSaveStatusTimer.Start();
                }
            }

            internal void ShowFavoriteSaveFailed(Guid id)
            {
                dirtyFavoriteIds.Add(id);
                favoriteSaveStatusTimer.Stop();
                favoriteSaveStatusClock.Reset();
                favoriteSaveStatusLabel.Text = "保存失败，请重试";
                favoriteSaveStatusLabel.ForeColor = TodoWidgetForm.HalfWhiteColor;
                favoriteSaveStatusLabel.Visible = true;
                favoriteSaveStatusLabel.BringToFront();
            }

            internal void FavoriteBlankArea_MouseDown(object sender, MouseEventArgs e)
            {
                if (Owner.Settings.ActiveModule != "Favorites") return;
                CommitPendingFavorites();
                Owner.ActiveControl = null;
            }

            internal void DeleteFavoriteRow(Panel row)
            {
                if (row.Tag is not Guid id) return;
                FavoriteTextItem item = Owner.Settings.FavoriteItems.FirstOrDefault(x => x.Id == id);
                if (item == null) return;
                Owner.WriteNoteLog(NoteLogKind.Favorites, "删除", item.Text);
                Owner.Settings.FavoriteItems.RemoveAll(x => x.Id == id);
                favoriteMetricCache.Remove(id);
                dirtyFavoriteIds.Remove(id);
                favoriteCommittedTexts.Remove(id);
                SettingManager.SaveApplicationConfigAsync();
                RenderFavorites();
            }

            internal int GetFavoriteRowWidth()
            {
                return Math.Max(Owner.D(120), favoritesList.ClientSize.Width - (favoritesList.HasVerticalScroll ? Owner.D(12) : 0));
            }


            internal readonly PictureBox favoriteTitleLogo;

            internal void LayoutFavoritesPanel()
            {
                int edge = Owner.D(8);
                int gap = Owner.D(5), width = favoritesPanel.ClientSize.Width - edge * 2;
                int inputHeight = Math.Max(Owner.D(28), favoriteSearchBox.PreferredHeight);
                int addWidth = Math.Max(Owner.D(68), TextRenderer.MeasureText(addFavoriteButton.Text, addFavoriteButton.Font).Width + Owner.D(18));
                int addHeight = Math.Max(Owner.D(25), addFavoriteButton.Font.Height + Owner.D(8));
                int saveStatusWidth = new[] { "已保存", "仍有内容未保存", "保存失败，请重试" }
                    .Max(text => TextRenderer.MeasureText(text, favoriteSaveStatusLabel.Font,
                        Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width) + Owner.D(4);
                int addLeft = favoritesPanel.ClientSize.Width - edge - addWidth;
                int statusLeft = addLeft - gap - saveStatusWidth;
                Owner.LayoutPageTitle(favoriteTitleLogo, favoriteTitleLabel,
                    Math.Max(1, statusLeft - gap - Owner.D(34)));
                int titleTop = favoriteTitleLabel.Top, titleHeight = favoriteTitleLabel.Height;
                addFavoriteButton.Bounds = new Rectangle(addLeft,
                    titleTop + (titleHeight - addHeight) / 2, addWidth, addHeight);
                favoriteSaveStatusLabel.Bounds = new Rectangle(statusLeft, titleTop, saveStatusWidth, titleHeight);
                int inputTop = favoriteTitleLabel.Bottom + gap;
                favoriteSearchPanel.Bounds = new Rectangle(edge, inputTop, width, inputHeight);
                int searchInset = Owner.D(4);
                int searchHeight = Math.Min(favoriteSearchPanel.ClientSize.Height - Owner.D(2), favoriteSearchBox.PreferredHeight);
                favoriteSearchBox.Bounds = new Rectangle(searchInset, (favoriteSearchPanel.ClientSize.Height - searchHeight) / 2,
                    Math.Max(Owner.D(30), favoriteSearchPanel.ClientSize.Width - searchInset * 2), searchHeight);
                int available = Math.Max(Owner.D(40), favoritesPanel.ClientSize.Height - favoriteSearchPanel.Bottom - gap - edge);
                favoritesList.Bounds = new Rectangle(edge, favoriteSearchPanel.Bottom + gap, width, available / 2);
                LayoutFileFavorites(edge, favoritesList.Bottom + gap, width, Math.Max(1, available - available / 2 - gap));
                Owner.LayoutSlimScrollBar(favoritesList, favoritesSlimScrollBar);
                if (!Owner.resizing) RenderFavorites();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    favoriteSearchTimer.Dispose();
                    favoriteSaveStatusTimer.Dispose();
                    favoriteItemFont.Dispose();
                    favoriteMetadataFont.Dispose();
                    DisposeFileFavorites();
                }
                base.Dispose(disposing);
            }

            internal FavoritesPageComponent(TodoWidgetForm owner) : base(owner, "Favorites")
            {
                favoriteItemFont = new Font(Font.FontFamily, 8.5f);
                favoriteMetadataFont = new Font(Font.FontFamily, 7f);
                foreach (FavoriteTextItem item in Owner.Settings.FavoriteItems) favoriteCommittedTexts[item.Id] = item.Text ?? "";
                favoriteTitleLabel = new Label
                {
                    Text = "我的收藏",
                    Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
                    ForeColor = ShareXResources.Theme.TextColor,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Owner.ConfigureTitleDrag(favoriteTitleLabel);
                favoritesPanel.Controls.Add(favoriteTitleLabel);
                favoriteTitleLogo = Owner.CreateTitleLogo(favoritesPanel);
                favoriteSaveStatusLabel = new SingleLineStatusLabel
                {
                    Text = "已保存",
                    Font = new Font(Font.FontFamily, 7f),
                    ForeColor = TodoWidgetForm.HalfWhiteColor,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleRight,
                    AccessibleName = "收藏保存状态",
                    Visible = false
                };
                favoritesPanel.Controls.Add(favoriteSaveStatusLabel);
                favoriteSearchPanel = new RoundedBorderPanel
                {
                    BackColor = TodoWidgetForm.WidgetInputColor,
                    BorderColor = Color.FromArgb(145, 145, 145),
                    CornerRadius = 5
                };
                favoritesPanel.Controls.Add(favoriteSearchPanel);
                favoriteSearchBox = new TextBox
                {
                    PlaceholderText = "支持模糊搜索",
                    Font = new Font(Font.FontFamily, 8.5f),
                    BackColor = TodoWidgetForm.WidgetInputColor,
                    ForeColor = ShareXResources.Theme.TextColor,
                    BorderStyle = BorderStyle.None
                };
                // wParam=1 keeps the cue visible even when this field is focused on entry.
                favoriteSearchBox.HandleCreated += (sender, e) => TodoWidgetForm.SendMessage(favoriteSearchBox.Handle,
                    TodoWidgetForm.EmSetCueBanner, new IntPtr(1), favoriteSearchBox.PlaceholderText);
                favoriteSearchBox.TextChanged += (sender, e) =>
                {
                    favoriteSearchTimer.Stop();
                    favoriteSearchTimer.Start();
                };
                favoriteSearchPanel.Controls.Add(favoriteSearchBox);
                addFavoriteButton = Owner.CreateCompactAddButton("添加收藏文本");
                addFavoriteButton.Click += (sender, e) => AddFavorite();
                favoritesPanel.Controls.Add(addFavoriteButton);
                favoritesList = Owner.CreateVirtualClipboardList();
                favoritesList.ViewportChanged += (sender, e) => UpdateFavoritesViewport();
                favoritesList.MouseDown += FavoriteBlankArea_MouseDown;
                favoritesPanel.MouseDown += FavoriteBlankArea_MouseDown;
                favoritesPanel.Controls.Add(favoritesList);
                favoritesSlimScrollBar = new SlimListScrollBar(favoritesList, Owner.D(12));
                favoritesList.Controls.Add(favoritesSlimScrollBar);
                favoritesEmptyLabel = Owner.CreateEmptyLabel("点击 + 新增一条收藏文本");
                CreateFileFavorites();

                favoriteSearchTimer = new Timer { Interval = 300 };
                favoriteSearchTimer.Tick += (sender, e) =>
                {
                    favoriteSearchTimer.Stop();
                    RenderFavorites();
                    RefreshFileFavorites(true);
                };
                favoriteSaveStatusTimer = new Timer { Interval = 50 };
                favoriteSaveStatusTimer.Tick += (sender, e) =>
                {
                    double elapsedMilliseconds = favoriteSaveStatusClock.Elapsed.TotalMilliseconds;
                    const double holdMilliseconds = 3000;
                    const double fadeMilliseconds = 300;
                    if (elapsedMilliseconds <= holdMilliseconds) return;

                    float progress = (float)Math.Min(1d, (elapsedMilliseconds - holdMilliseconds) / fadeMilliseconds);
                    favoriteSaveStatusLabel.ForeColor = BlendColor(TodoWidgetForm.HalfWhiteColor, TodoWidgetForm.WidgetBackgroundColor, progress);
                    if (progress >= 1f)
                    {
                        favoriteSaveStatusTimer.Stop();
                        favoriteSaveStatusClock.Reset();
                        favoriteSaveStatusLabel.Visible = false;
                    }
                };
                favoriteSearchBox.Leave += (sender, e) => Owner.ScheduleCollapseIfPointerOutside();
            }

            internal override void ArrangePage() => LayoutFavoritesPanel();
            internal override void RefreshPage() { RenderFavorites(); RefreshFileFavorites(true); }
        }
    }
}
