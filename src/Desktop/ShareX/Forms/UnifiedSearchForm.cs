using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    internal sealed record SearchHit(string Source, string Title, string Text, Guid Id, string Path = null);

    internal static class UnifiedSearchEngine
    {
        internal static readonly SemaphoreSlim ReadGate = new(1, 1);
        internal static string Snippet(string text, string query)
        {
            text ??= "";
            int at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            int start = Math.Max(0, at - 35);
            return (start > 0 ? "…" : "") + text.Substring(start, Math.Min(180, text.Length - start)).Replace('\r', ' ').Replace('\n', ' ');
        }

        internal static (SearchHit[] Hits, int Skipped) Search(SearchHit[] snapshot, string notes, string query, CancellationToken token)
            => SearchFiltered(snapshot, notes, query, token, null);
        internal static (SearchHit[] Hits, int Skipped) SearchFiltered(SearchHit[] snapshot, string notes, string query, CancellationToken token, string source)
        {
            UnifiedSearchCache.Prepare(notes);
            var hits = new List<SearchHit>();
            int skipped = 0, visited = 0;
            bool Match(string text) => text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
            foreach (var item in snapshot)
            {
                token.ThrowIfCancellationRequested();
                if ((source == null || item.Source == source) && (Match(item.Title) || Match(item.Text))) hits.Add(item);
            }
            void Scan(string directory, int depth)
            {
                token.ThrowIfCancellationRequested();
                if (depth > 64 || visited >= 20000) { skipped++; return; }
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { skipped++; return; }
                    foreach (string file in Directory.EnumerateFileSystemEntries(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        if (++visited > 20000) { skipped++; break; }
                        try
                        {
                            string name = System.IO.Path.GetFileName(file);
                            if (name.StartsWith('.') && name != ".xiaomang-folder.md") continue;
                            var attributes = File.GetAttributes(file);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                            if ((attributes & FileAttributes.Directory) != 0) { Scan(file, depth + 1); continue; }
                            if (System.IO.Path.GetExtension(file).ToLowerInvariant() is not (".md" or ".txt")) continue;
                            string relative = System.IO.Path.GetRelativePath(notes, file).Replace('\\', '/');
                            if (name == ".xiaomang-folder.md") relative = System.IO.Path.GetDirectoryName(relative)?.Replace('\\', '/');
                            if (string.IsNullOrEmpty(relative)) continue;
                            string content = UnifiedSearchCache.Read(file, out bool oversized);
                            if (oversized) skipped++;
                            if (Match(relative) || Match(content)) hits.Add(new SearchHit("本地笔记", relative, Snippet(content, query), Guid.Empty, relative));
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipped++; }
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipped++; }
            }
            if ((source == null || source == "本地笔记") && Directory.Exists(notes)) Scan(notes, 0);
            return (hits.OrderByDescending(x => string.Equals(x.Title, query, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.Title?.StartsWith(query, StringComparison.OrdinalIgnoreCase) == true)
                .ThenByDescending(x => Match(x.Title)).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).Take(300).ToArray(), skipped);
        }
    }

    internal sealed class UnifiedSearchForm : Form
    {
        private static UnifiedSearchForm current;
        private readonly TextBox query = new() { BorderStyle = BorderStyle.None, PlaceholderText = "搜索待办、收藏、笔记和固定文件…" };
        private readonly CheckBox clipboard = new SearchHistoryCheckBox { Text = "包含剪切板历史", AutoSize = false };
        private readonly ComboBox sourceFilter = new() { Name = "searchSource", DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = SearchAppearance.Surface, ForeColor = SearchAppearance.Text, FlatStyle = FlatStyle.Flat };
        private readonly UnifiedSearchResultsView results = new() { Dock = DockStyle.Fill };
        private readonly Label status = new() { Text = "输入关键词开始搜索", AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label elapsed = new() { TextAlign = ContentAlignment.MiddleRight };
        private readonly Label keyboardHint = new() { Text = "↑↓ 选择   Enter 打开", TextAlign = ContentAlignment.MiddleRight };
        private readonly Panel top = new() { Dock = DockStyle.Top };
        private readonly Panel footer = new() { Dock = DockStyle.Bottom };
        private readonly SearchInputPanel input;
        private readonly System.Windows.Forms.Timer delay = new() { Interval = 250 };
        private CancellationTokenSource search;
        private int generation;
        private readonly Stopwatch queryElapsed = new();
        private bool sizeMemoryReady;
        private int completedGeneration = -1;
        private int? pendingOpen;
        internal static void Open()
        {
            if (current == null || current.IsDisposed) current = new UnifiedSearchForm();
            current.Show(); current.WindowState = FormWindowState.Normal; current.Activate(); current.query.Focus();
        }
        internal UnifiedSearchForm()
        {
            Text = "小蟒 · 全局搜索"; Icon = ShareXResources.Icon;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(950, 620); MinimumSize = new Size(520, 340);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f); BackColor = SearchAppearance.Background; ForeColor = SearchAppearance.Text;
            HandleCreated += (_, _) => NativeMethods.UseImmersiveDarkMode(Handle, true);
            input = new SearchInputPanel(query);
            sourceFilter.Items.AddRange(new object[] { "全部来源", "待办", "手动收藏", "本地笔记", "固定文件", "剪切板" });
            sourceFilter.SelectedIndex = 0;
            sourceFilter.DrawMode = DrawMode.OwnerDrawFixed;
            sourceFilter.DrawItem += (_, e) => {
                if (e.Index < 0) return;
                using var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(47,64,77) : SearchAppearance.Surface);
                e.Graphics.FillRectangle(brush,e.Bounds);
                TextRenderer.DrawText(e.Graphics,sourceFilter.Items[e.Index].ToString(),sourceFilter.Font,e.Bounds,SearchAppearance.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            };
            top.Controls.Add(input); top.Controls.Add(clipboard); top.Controls.Add(sourceFilter);
            status.ForeColor = elapsed.ForeColor = keyboardHint.ForeColor = SearchAppearance.Muted;
            footer.Controls.Add(status); footer.Controls.Add(keyboardHint); footer.Controls.Add(elapsed);
            footer.Paint += (_, e) => { using Pen pen = new(SearchAppearance.Border); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            Controls.Add(results); Controls.Add(top); Controls.Add(footer);
            top.SizeChanged += (_, _) => LayoutSearchChrome(); footer.SizeChanged += (_, _) => LayoutSearchChrome();
            Shown += (_, _) => LayoutSearchChrome(); DpiChanged += (_, _) => LayoutSearchChrome();
            LayoutSearchChrome();
            query.TextChanged += (_, _) => Schedule(); clipboard.CheckedChanged += (_, _) => Schedule();
            sourceFilter.SelectedIndexChanged += (_, _) => Schedule();
            delay.Tick += async (_, _) => { delay.Stop(); await Search(); };
            results.ResultInvoked += async (_, _) => await OpenResult();
            query.KeyDown += (_, e) => {
                if (e.KeyCode == Keys.Down && results.HitCount > 0) { results.FocusFirst(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) {
                    e.SuppressKeyPress = true;
                    if (completedGeneration == generation) { if (results.SelectedHit == null) results.FocusFirst(); results.InvokeSelected(); }
                    else if (query.Text.Trim().Length > 0) pendingOpen = generation;
                }
            };
            Load += (_, _) => RestoreRememberedSize();
            ResizeEnd += (_, _) => RememberWindowSize();
            FormClosing += (_, _) => RememberWindowSize();
            FormClosed += (_, _) => { generation++; search?.Cancel(); delay.Dispose(); };
        }
        internal static Rectangle RememberedWindowBounds(Size saved, int dpi, Rectangle workingArea)
        {
            double scale = Math.Max(1, dpi) / 96d;
            int availableWidth = Math.Max(1, workingArea.Width), availableHeight = Math.Max(1, workingArea.Height);
            int minimumWidth = Math.Min(availableWidth, (int)Math.Round(520 * scale));
            int minimumHeight = Math.Min(availableHeight, (int)Math.Round(340 * scale));
            int width = (int)Math.Round(Math.Clamp((saved.Width > 0 ? saved.Width : 950) * scale, minimumWidth, availableWidth));
            int height = (int)Math.Round(Math.Clamp((saved.Height > 0 ? saved.Height : 620) * scale, minimumHeight, availableHeight));
            return new Rectangle(workingArea.Left + (availableWidth - width) / 2, workingArea.Top + (availableHeight - height) / 2, width, height);
        }
        private void RestoreRememberedSize()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(D(520), area.Width), Math.Min(D(340), area.Height));
            Bounds = RememberedWindowBounds(Program.Settings.UnifiedSearchWindowSize, DeviceDpi, area);
            sizeMemoryReady = true;
        }
        private void RememberWindowSize()
        {
            if (!sizeMemoryReady) return;
            Size actual = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
            if (actual.Width <= 0 || actual.Height <= 0) return;
            Size logical = new Size((int)Math.Round(actual.Width * 96d / DeviceDpi), (int)Math.Round(actual.Height * 96d / DeviceDpi));
            if (logical == Program.Settings.UnifiedSearchWindowSize) return;
            Program.Settings.UnifiedSearchWindowSize = logical;
            SettingManager.SaveApplicationConfigAsync();
        }
        private int D(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.L)) { query.Focus(); query.SelectAll(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void LayoutSearchChrome()
        {
            if (input == null) return;
            top.Height = D(108); footer.Height = D(30);
            int margin = D(14), gap = D(14);
            int checkWidth = TextRenderer.MeasureText(clipboard.Text, clipboard.Font).Width + D(26);
            clipboard.Bounds = new Rectangle(Math.Max(margin, top.ClientSize.Width - margin - checkWidth), D(14), checkWidth, D(40));
            input.Bounds = new Rectangle(margin, D(14), Math.Max(D(120), clipboard.Left - gap - margin), D(40));
            sourceFilter.Bounds = new Rectangle(margin, D(63), D(160), D(30));
            sourceFilter.ItemHeight = D(22);
            int elapsedWidth = elapsed.Text.Length == 0 ? 0 : TextRenderer.MeasureText(elapsed.Text, elapsed.Font).Width + D(14);
            elapsed.Bounds = new Rectangle(footer.Width - margin - elapsedWidth, 0, elapsedWidth, footer.Height);
            int hintWidth = TextRenderer.MeasureText(keyboardHint.Text, keyboardHint.Font).Width + gap;
            keyboardHint.Visible = footer.Width >= D(640);
            keyboardHint.Bounds = new Rectangle(elapsed.Left - hintWidth, 0, hintWidth, footer.Height);
            int statusRight = keyboardHint.Visible ? keyboardHint.Left : elapsed.Left;
            status.Bounds = new Rectangle(margin, 0, Math.Max(D(40), statusRight - margin - gap), footer.Height);
        }
        private void Schedule()
        {
            generation++; pendingOpen = null; search?.Cancel(); delay.Stop(); queryElapsed.Restart();
            elapsed.Text = ""; LayoutSearchChrome();
            if (query.Text.Trim().Length == 0) { results.SetHits(Array.Empty<SearchHit>(), ""); status.Text = "输入关键词开始搜索"; results.EmptyMessage = "输入关键词，搜索待办、收藏和本地笔记"; return; }
            results.EmptyMessage = "正在搜索…";
            status.Text = results.HitCount > 0 ? "搜索中…暂时显示上次结果" : "搜索中…"; delay.Start();
        }
        private async Task Search()
        {
            int version = generation; string term = query.Text.Trim();
            string selectedSource = sourceFilter.SelectedIndex > 0 ? sourceFilter.SelectedItem.ToString() : null;
            var cancellation = new CancellationTokenSource(); search = cancellation;
            var snapshot = new List<SearchHit>(); var settings = Program.Settings.TodoSettings;
            snapshot.AddRange(settings.Items.Select(x => new SearchHit("待办", x.Text, x.Text, x.Id)));
            snapshot.AddRange(settings.FavoriteItems.Select(x => new SearchHit("手动收藏", x.Text, x.Text, x.Id)));
            if (clipboard.Checked) snapshot.AddRange(settings.ClipboardItems.Select(x => new SearchHit("剪切板", x.GetDisplayText(), x.Text, x.Id)));
            snapshot.AddRange((FileFavoritesService.Store?.Pinned ?? Array.Empty<FavoriteFile>()).Select(x => new SearchHit("固定文件", x.Name, x.Path, Guid.Empty, x.Path)));
            try
            {
                await UnifiedSearchEngine.ReadGate.WaitAsync(cancellation.Token);
                (SearchHit[] Hits, int Skipped) found;
                try { found = await Task.Run(() => UnifiedSearchEngine.SearchFiltered(snapshot.ToArray(), System.IO.Path.Combine(LocalDiaryManager.DataFolder, "notes"), term, cancellation.Token, selectedSource)); }
                finally { UnifiedSearchEngine.ReadGate.Release(); }
                if (IsDisposed || generation != version) return;
                results.SetHits(found.Hits, term);
                completedGeneration = version;
                results.EmptyMessage = "没有找到匹配结果，请换个关键词";
                status.Text = $"显示 {found.Hits.Length} 条 · 最多 300 条" + (found.Skipped > 0 ? $" · {found.Skipped} 项未完整检索（过大、不可读或链接）" : "");
                elapsed.Text = $"{queryElapsed.ElapsedMilliseconds} 毫秒"; LayoutSearchChrome();
                if (pendingOpen == version) { pendingOpen = null; if (results.SelectedHit == null) results.FocusFirst(); results.InvokeSelected(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!IsDisposed && generation == version) { status.Text = "搜索失败，可重新输入重试：" + error.Message; results.EmptyMessage = "搜索未完成，请重新输入关键词重试"; } }
            finally { if (ReferenceEquals(search, cancellation)) search = null; cancellation.Dispose(); }
        }
        private async Task OpenResult()
        {
            if (completedGeneration != generation) return;
            var hit = results.SelectedHit;
            if (hit == null) return;
            try
            {
                if (hit.Source == "本地笔记") await LocalDiaryManager.OpenAsync(hit.Path);
                else if (hit.Source == "固定文件")
                {
                    if (!File.Exists(hit.Path) && !Directory.Exists(hit.Path)) throw new IOException("文件或目录已不存在。");
                    Process.Start(new ProcessStartInfo(hit.Path) { UseShellExecute = true });
                }
                else Program.MainForm.OpenUnifiedSearchResult(hit);
            }
            catch (Exception error) { Show(); Activate(); MessageBox.Show(this, error.Message, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }
}
