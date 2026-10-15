using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    public sealed class NoteLogViewerControl : UserControl
    {
        private static readonly Color Background = Color.FromArgb(42, 42, 42);
        private static readonly Color Sidebar = Color.FromArgb(48, 48, 48);
        private static readonly Color Input = Color.FromArgb(58, 58, 58);
        private static readonly Color Selected = Color.FromArgb(72, 72, 72);

        private sealed class DisplayRow
        {
            public string TimestampText { get; set; }
            public string Action { get; set; }
            public string Text { get; set; }
        }

        private sealed class CategoryState
        {
            public List<NoteLogEntry> Entries { get; set; } = new List<NoteLogEntry>();
            public List<NoteLogEntry> DisplayedEntries { get; set; } = new List<NoteLogEntry>();
            public List<DisplayRow> DisplayRows { get; set; }
            public string Fingerprint { get; set; } = "";
            public string AppliedFilter { get; set; }
            public bool AppliedSortDescending { get; set; }
            public bool Loaded { get; set; }
            public bool Loading { get; set; }
            public bool Dirty { get; set; } = true;
            public int LoadVersion { get; set; }
            public int ProbeVersion { get; set; }
            public int FilterVersion { get; set; }
        }

        private readonly NoteLogStore store;
        private readonly Dictionary<NoteLogKind, Button> categoryButtons = new Dictionary<NoteLogKind, Button>();
        private readonly Dictionary<NoteLogKind, string> categoryFilters = new Dictionary<NoteLogKind, string>();
        private readonly Dictionary<NoteLogKind, int> categoryScrollRows = new Dictionary<NoteLogKind, int>();
        private readonly Dictionary<NoteLogKind, CategoryState> categoryStates = new Dictionary<NoteLogKind, CategoryState>();
        private readonly List<FileSystemWatcher> logWatchers = new List<FileSystemWatcher>();
        private readonly HashSet<NoteLogKind> pendingExternalChanges = new HashSet<NoteLogKind>();
        private readonly object pendingExternalChangesLock = new object();
        private readonly Queue<Action> pendingUiActions = new Queue<Action>();
        private readonly object pendingUiActionsLock = new object();
        private readonly TextBox filterBox;
        private readonly DataGridView detailsGrid;
        private readonly VisibleMetadataColumnSizer columnSizer;
        private readonly Timer filterTimer;
        private readonly Timer externalRefreshTimer;
        private readonly Button sortButton;
        private readonly Label pageTitle;
        private NoteLogKind selectedKind;
        private List<NoteLogEntry> currentEntries = new List<NoteLogEntry>();
        private List<NoteLogEntry> displayedEntries = new List<NoteLogEntry>();
        private bool sortDescending = true;
        private bool categoryLoaded;
        private bool changingCategory;

        public NoteLogKind SelectedKind => selectedKind;

        public NoteLogViewerControl(NoteLogStore store, NoteLogKind initialKind)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            selectedKind = initialKind;
            Dock = DockStyle.Fill;
            Font = new Font("Microsoft YaHei", 9f);
            BackColor = Background;
            ForeColor = ShareXResources.Theme.TextColor;

            Panel left = new Panel { Dock = DockStyle.Left, Width = 198, Padding = new Padding(8), BackColor = Sidebar };
            Controls.Add(left);
            int top = 8;
            foreach (NoteLogKind kind in new[] { NoteLogKind.Todo, NoteLogKind.Clipboard, NoteLogKind.Favorites })
            {
                Button button = new Button
                {
                    Text = NoteLogStore.GetDisplayName(kind),
                    Tag = kind,
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(8, top),
                    Size = new Size(182, 38),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Sidebar,
                    ForeColor = ForeColor
                };
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = Selected;
                button.Click += CategoryButton_Click;
                left.Controls.Add(button);
                categoryButtons[kind] = button;
                top += 42;
            }

            Panel right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Background };
            Controls.Add(right);
            right.BringToFront();

            Panel headerPanel = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Background };
            pageTitle = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                ForeColor = ForeColor
            };
            headerPanel.Controls.Add(pageTitle);
            Label storageHint = new Label
            {
                Name = "NoteLogStorageHint", Dock = DockStyle.Top, Height = 40,
                Text = "这里展示历史日志。删除日志文件不会删除当前待办、剪切板列表或收藏内容。",
                TextAlign = ContentAlignment.MiddleLeft
            };

            TableLayoutPanel filterPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Background
            };
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label filterLabel = new Label
            {
                Text = "筛选：",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ForeColor
            };
            filterPanel.Controls.Add(filterLabel, 0, 0);
            filterBox = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = Padding.Empty,
                PlaceholderText = "输入日期、操作或内容",
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Input,
                ForeColor = ForeColor
            };
            filterTimer = new Timer { Interval = 300 };
            filterTimer.Tick += (sender, e) =>
            {
                filterTimer.Stop();
                ApplyFilter();
            };
            filterBox.TextChanged += (sender, e) =>
            {
                if (changingCategory) return;
                categoryFilters[selectedKind] = filterBox.Text;
                filterTimer.Stop();
                filterTimer.Start();
            };
            filterPanel.Controls.Add(filterBox, 1, 0);

            FlowLayoutPanel commands = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 46,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 4, 0, 3),
                BackColor = Background
            };
            sortButton = CreateCommandButton("时间：新→旧", (sender, e) =>
            {
                sortDescending = !sortDescending;
                sortButton.Text = sortDescending ? "时间：新→旧" : "时间：旧→新";
                ApplyFilter();
            });
            commands.Controls.Add(sortButton);
            commands.Controls.Add(CreateCommandButton("复制选中", (sender, e) => CopySelected()));
            commands.Controls.Add(CreateCommandButton("导出筛选结果", (sender, e) => ExportDisplayed()));
            commands.Controls.Add(CreateCommandButton("打开日志文件夹", (sender, e) => OpenLogFolder()));

            detailsGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = true,
                RowHeadersVisible = false,
                AutoGenerateColumns = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Background,
                GridColor = Color.FromArgb(68, 68, 68),
                EnableHeadersVisualStyles = false
            };
            detailsGrid.VirtualMode = true;
            detailsGrid.CellValueNeeded += (sender, args) =>
            {
                if (args.RowIndex < 0 || args.RowIndex >= displayedEntries.Count) return;
                NoteLogEntry entry = displayedEntries[args.RowIndex];
                args.Value = args.ColumnIndex switch { 0 => entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), 1 => entry.Action, _ => entry.Text };
            };
            detailsGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Sidebar,
                ForeColor = ForeColor,
                SelectionBackColor = Sidebar,
                Font = new Font(Font, FontStyle.Bold)
            };
            detailsGrid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Input,
                ForeColor = ForeColor,
                SelectionBackColor = Selected,
                SelectionForeColor = ForeColor,
                WrapMode = DataGridViewTriState.True,
                Padding = new Padding(4)
            };
            detailsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(52, 52, 52);
            detailsGrid.Columns.Add(CreateColumn("时间", "TimestampText", 175));
            detailsGrid.Columns.Add(CreateColumn("操作", "Action", 90));
            DataGridViewTextBoxColumn textColumn = CreateColumn("内容明细", "Text", 300);
            textColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            detailsGrid.Columns.Add(textColumn);
            columnSizer = new VisibleMetadataColumnSizer(detailsGrid, (0, 240), (1, 170));
            right.Controls.Add(detailsGrid);
            right.Controls.Add(commands);
            right.Controls.Add(filterPanel);
            right.Controls.Add(storageHint);
            right.Controls.Add(headerPanel);

            MainMenuAppearance.Apply(this);
            int S(int value) => MainMenuAppearance.Scale(this, value);
            left.Width = S(146);
            left.Padding = new Padding(S(8));
            top = S(16);
            foreach (Button button in categoryButtons.Values)
            {
                button.SetBounds(S(8), top, left.Width - S(16), S(34));
                top += S(40);
            }
            right.Padding = new Padding(S(16));
            headerPanel.Height = S(32);
            storageHint.Height = S(40);
            filterPanel.Height = S(34);
            filterPanel.ColumnStyles[0].Width = S(48);
            commands.WrapContents = true;
            commands.AutoSize = true;
            commands.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            commands.Padding = new Padding(0, S(4), 0, S(6));
            foreach (Button button in commands.Controls)
            {
                button.Size = new Size(TextRenderer.MeasureText(button.Text, button.Font).Width + S(24), S(30));
                button.Margin = new Padding(0, 0, S(6), S(4));
            }
            detailsGrid.ColumnHeadersHeight = S(28);
            foreach (DataGridViewColumn column in detailsGrid.Columns)
                column.MinimumWidth = TextRenderer.MeasureText(column.HeaderText, detailsGrid.ColumnHeadersDefaultCellStyle.Font).Width + S(16);

            externalRefreshTimer = new Timer { Interval = 300 };
            externalRefreshTimer.Tick += ExternalRefreshTimer_Tick;
            InitializeLogWatchers();
            SelectCategory(initialKind);
        }

        private static DataGridViewTextBoxColumn CreateColumn(string header, string property, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = property,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private void CategoryButton_Click(object sender, EventArgs e)
        {
            if (sender is Button button && button.Tag is NoteLogKind kind) SelectCategory(kind);
        }

        public void SelectCategory(NoteLogKind kind)
        {
            if (categoryLoaded)
            {
                categoryFilters[selectedKind] = filterBox.Text;
                SaveScrollPosition(selectedKind);
            }

            selectedKind = kind;
            changingCategory = true;
            filterBox.Text = categoryFilters.TryGetValue(kind, out string filter) ? filter : string.Empty;
            changingCategory = false;
            foreach (KeyValuePair<NoteLogKind, Button> pair in categoryButtons)
                MainMenuAppearance.SetSelected(pair.Value, pair.Key == selectedKind);
            categoryLoaded = true;
            EnsureCategoryLoaded(kind);
        }

        private CategoryState GetCategoryState(NoteLogKind kind)
        {
            if (!categoryStates.TryGetValue(kind, out CategoryState state))
            {
                state = new CategoryState();
                categoryStates[kind] = state;
            }
            return state;
        }

        private void SaveScrollPosition(NoteLogKind kind)
        {
            if (detailsGrid.RowCount > 0 && detailsGrid.FirstDisplayedScrollingRowIndex >= 0)
                categoryScrollRows[kind] = detailsGrid.FirstDisplayedScrollingRowIndex;
        }

        private void EnsureCategoryLoaded(NoteLogKind kind)
        {
            CategoryState state = GetCategoryState(kind);
            int probe = ++state.ProbeVersion;
            Task.Run(() => store.GetFingerprint(kind)).ContinueWith(task => PostToUi(() =>
            {
                if (IsDisposed || probe != state.ProbeVersion) return;
                if (task.IsFaulted) { state.Dirty = true; return; }
                EnsureCategoryLoaded(kind, state, task.Result);
            }), TaskScheduler.Default);
        }

        private void EnsureCategoryLoaded(NoteLogKind kind, CategoryState state, string fingerprint)
        {
            if (state.Loaded && !state.Dirty && state.Fingerprint == fingerprint)
            {
                ApplyFilter();
                RestoreScrollPosition(kind);
                return;
            }

            if (state.Loaded)
            {
                ApplyFilter();
                pageTitle.Text = "便签文件夹 / " + NoteLogStore.GetDisplayName(kind) + "（正在刷新…）";
            }
            else
            {
                currentEntries = new List<NoteLogEntry>();
                displayedEntries = new List<NoteLogEntry>();
                detailsGrid.Rows.Clear();
                pageTitle.Text = "便签文件夹 / " + NoteLogStore.GetDisplayName(kind) + "（正在加载…）";
            }

            if (state.Loading)
            {
                if (state.Fingerprint != fingerprint && !state.Dirty)
                {
                    state.Dirty = true;
                    state.LoadVersion++;
                }
                return;
            }

            StartCategoryLoad(kind, state);
        }

        private void StartCategoryLoad(NoteLogKind kind, CategoryState state)
        {
            state.Loading = true;
            state.Dirty = false;
            int version = ++state.LoadVersion;
            Task.Run(() =>
            {
                return store.ReadSnapshot(kind);
            }).ContinueWith(task => PostToUi(() => CompleteCategoryLoad(kind, state, version, task)), TaskScheduler.Default);
        }

        private void CompleteCategoryLoad(NoteLogKind kind, CategoryState state, int version,
            Task<(List<NoteLogEntry> entries, string fingerprint)> task)
        {
            if (IsDisposed) return;
            state.Loading = false;
            if (version != state.LoadVersion)
            {
                StartCategoryLoad(kind, state);
                return;
            }

            if (task.IsFaulted)
            {
                state.Dirty = true;
                Exception error = task.Exception?.GetBaseException();
                DebugHelper.WriteException(error, "Could not load note log entries.");
                if (selectedKind == kind)
                    pageTitle.Text = "便签文件夹 / " + NoteLogStore.GetDisplayName(kind) + "（加载失败）";
                return;
            }

            state.Entries = task.Result.entries;
            state.Fingerprint = task.Result.fingerprint;
            state.Loaded = true;
            state.Dirty = false;
            state.DisplayRows = null;
            state.FilterVersion++;
            state.AppliedFilter = null;
            if (selectedKind == kind)
            {
                ApplyFilter();
                RestoreScrollPosition(kind);
            }
        }

        private void RestoreScrollPosition(NoteLogKind kind)
        {
            if (!categoryScrollRows.TryGetValue(kind, out int scrollRow) || detailsGrid.RowCount == 0) return;

            void RestoreAfterLayout()
            {
                if (IsDisposed || detailsGrid.IsDisposed || selectedKind != kind || !detailsGrid.IsHandleCreated || !detailsGrid.Visible ||
                    detailsGrid.RowCount == 0 || detailsGrid.ClientSize.Height <= detailsGrid.ColumnHeadersHeight ||
                    detailsGrid.DisplayedRowCount(false) == 0) return;

                try
                {
                    detailsGrid.FirstDisplayedScrollingRowIndex = Math.Min(scrollRow, detailsGrid.RowCount - 1);
                }
                catch (InvalidOperationException ex)
                {
                    DebugHelper.WriteException(ex, "Could not restore note log scroll position.");
                }
            }

            if (IsHandleCreated)
            {
                BeginInvoke((Action)RestoreAfterLayout);
            }
        }

        private void ApplyFilter()
        {
            NoteLogKind kind = selectedKind;
            CategoryState state = GetCategoryState(kind);
            if (!state.Loaded) return;
            string filter = filterBox.Text.Trim();
            bool descending = sortDescending;
            if (state.DisplayRows != null && state.AppliedFilter == filter && state.AppliedSortDescending == descending)
            {
                PublishRows(kind, state);
                return;
            }
            int version = ++state.FilterVersion;
            List<NoteLogEntry> source = state.Entries;
            Task.Run(() => {
                IEnumerable<NoteLogEntry> entries = source;
                if (filter.Length > 0)
                {
                    entries = entries.Where(x => x.Timestamp.ToString("yyyy-MM-dd HH:mm:ss").Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        x.Action.Contains(filter, StringComparison.OrdinalIgnoreCase) || x.Text.Contains(filter, StringComparison.OrdinalIgnoreCase));
                }
                return (descending ? entries.OrderByDescending(x => x.Timestamp) : entries.OrderBy(x => x.Timestamp)).ToList();
            }).ContinueWith(task => PostToUi(() => {
                if (IsDisposed || version != state.FilterVersion || task.IsFaulted) return;
                state.DisplayedEntries = task.Result;
                state.DisplayRows = new List<DisplayRow>(); // Completion marker; cells are formatted only when visible.
                state.AppliedFilter = filter;
                state.AppliedSortDescending = descending;
                PublishRows(kind, state);
            }), TaskScheduler.Default);
        }

        private void PublishRows(NoteLogKind kind, CategoryState state)
        {
            if (selectedKind != kind) return;
            NoteLogEntry selectedEntry = detailsGrid.CurrentCell is DataGridViewCell cell &&
                cell.RowIndex >= 0 && cell.RowIndex < displayedEntries.Count
                ? displayedEntries[cell.RowIndex] : null;
            currentEntries = state.Entries;
            displayedEntries = state.DisplayedEntries;
            // Shrinking RowCount removes virtual rows individually. Clearing the
            // collection uses the bulk path and avoids quadratic removal work.
            if (detailsGrid.RowCount > displayedEntries.Count) detailsGrid.Rows.Clear();
            detailsGrid.RowCount = displayedEntries.Count;
            if (selectedEntry != null)
            {
                int selectedIndex = displayedEntries.IndexOf(selectedEntry);
                if (selectedIndex >= 0)
                {
                    detailsGrid.ClearSelection();
                    detailsGrid.CurrentCell = detailsGrid[0, selectedIndex];
                    detailsGrid.Rows[selectedIndex].Selected = true;
                }
                else detailsGrid.ClearSelection();
            }
            detailsGrid.Invalidate();
            columnSizer.RequestFit();
            pageTitle.Text = "便签文件夹 / " + NoteLogStore.GetDisplayName(selectedKind);
            RestoreScrollPosition(kind);
        }

        private void InitializeLogWatchers()
        {
            try
            {
                Directory.CreateDirectory(store.RootFolder);
                AddLogWatcher(store.RootFolder, true, path => ResolveLogKind(path));

                string legacyClipboardFolder = Path.Combine(Path.GetDirectoryName(store.RootFolder), "剪切板归档");
                if (Directory.Exists(legacyClipboardFolder))
                    AddLogWatcher(legacyClipboardFolder, false, path => NoteLogKind.Clipboard);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                DebugHelper.WriteException(ex, "Could not watch note log folders.");
            }
        }

        private void AddLogWatcher(string folder, bool includeSubdirectories, Func<string, NoteLogKind?> resolveKind)
        {
            FileSystemWatcher watcher = new FileSystemWatcher(folder)
            {
                Filter = "*",
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler changed = (sender, e) =>
            {
                NoteLogKind? kind = resolveKind(e.FullPath);
                if (kind.HasValue) ScheduleExternalRefresh(kind.Value);
            };
            watcher.Created += changed;
            watcher.Changed += changed;
            watcher.Deleted += changed;
            watcher.Renamed += (sender, e) =>
            {
                NoteLogKind? oldKind = resolveKind(e.OldFullPath);
                NoteLogKind? newKind = resolveKind(e.FullPath);
                if (oldKind.HasValue) ScheduleExternalRefresh(oldKind.Value);
                if (newKind.HasValue && newKind != oldKind) ScheduleExternalRefresh(newKind.Value);
            };
            watcher.Error += (sender, e) =>
            {
                foreach (NoteLogKind kind in Enum.GetValues<NoteLogKind>()) ScheduleExternalRefresh(kind);
            };
            logWatchers.Add(watcher);
        }

        private NoteLogKind? ResolveLogKind(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }

            foreach (NoteLogKind kind in Enum.GetValues<NoteLogKind>())
            {
                string folder = Path.GetFullPath(store.GetFolder(kind)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (fullPath.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return kind;
            }
            return null;
        }

        private void ScheduleExternalRefresh(NoteLogKind kind)
        {
            lock (pendingExternalChangesLock) pendingExternalChanges.Add(kind);
            PostToUi(() =>
            {
                if (IsDisposed) return;
                externalRefreshTimer.Stop();
                externalRefreshTimer.Start();
            });
        }

        private void ExternalRefreshTimer_Tick(object sender, EventArgs e)
        {
            externalRefreshTimer.Stop();
            NoteLogKind[] changedKinds;
            lock (pendingExternalChangesLock)
            {
                changedKinds = pendingExternalChanges.ToArray();
                pendingExternalChanges.Clear();
            }

            foreach (NoteLogKind kind in changedKinds)
            {
                store.Invalidate(kind);
                if (categoryStates.TryGetValue(kind, out CategoryState state))
                {
                    state.Dirty = true;
                    if (state.Loading) state.LoadVersion++;
                }
            }

            if (changedKinds.Contains(selectedKind) && Visible && detailsGrid.Visible)
            {
                SaveScrollPosition(selectedKind);
                EnsureCategoryLoaded(selectedKind);
            }
        }

        private void PostToUi(Action action)
        {
            bool canPost;
            lock (pendingUiActionsLock)
            {
                if (IsDisposed) return;
                canPost = IsHandleCreated;
                if (!canPost) pendingUiActions.Enqueue(action);
            }

            if (canPost)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (InvalidOperationException) when (IsDisposed || Disposing)
                {
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Action[] actions;
            lock (pendingUiActionsLock)
            {
                actions = pendingUiActions.ToArray();
                pendingUiActions.Clear();
            }
            foreach (Action action in actions) BeginInvoke(action);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && categoryLoaded && categoryStates.TryGetValue(selectedKind, out CategoryState state) && state.Dirty)
                EnsureCategoryLoaded(selectedKind);
        }

        private Button CreateCommandButton(string text, EventHandler click)
        {
            Button button = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 29,
                FlatStyle = FlatStyle.Flat,
                BackColor = Input,
                ForeColor = ForeColor,
                Margin = new Padding(0, 0, 8, 0)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            button.Click += click;
            return button;
        }

        private void CopySelected()
        {
            if (detailsGrid.CurrentRow == null) return;
            int index = detailsGrid.CurrentRow.Index;
            if (index < 0 || index >= displayedEntries.Count) return;
            NoteLogEntry entry = displayedEntries[index];
            Clipboard.SetText($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Action}{Environment.NewLine}{entry.Text}");
        }

        private void ExportDisplayed()
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "文本文件|*.txt|CSV 文件|*.csv",
                DefaultExt = "txt",
                AddExtension = true,
                FileName = NoteLogStore.GetDisplayName(selectedKind) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
            if (Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                IEnumerable<string> lines = new[] { "时间,操作,内容" }.Concat(displayedEntries.Select(x =>
                    $"\"{x.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{Csv(x.Action)}\",\"{Csv(x.Text)}\""));
                File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
            }
            else
            {
                string text = string.Join(Environment.NewLine + new string('-', 40) + Environment.NewLine,
                    displayedEntries.Select(x => $"[{x.Timestamp:yyyy-MM-dd HH:mm:ss}] {x.Action}{Environment.NewLine}{x.Text}"));
                File.WriteAllText(dialog.FileName, text, new UTF8Encoding(true));
            }
        }

        private static string Csv(string value) => (value ?? "").Replace("\"", "\"\"");

        private void OpenLogFolder()
        {
            try
            {
                string folder = store.GetFolder(selectedKind);
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "打开日志文件夹失败：" + error.Message, "便签文件夹",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                filterTimer?.Dispose();
                externalRefreshTimer?.Dispose();
                foreach (FileSystemWatcher watcher in logWatchers) watcher.Dispose();
                logWatchers.Clear();
                lock (pendingUiActionsLock) pendingUiActions.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
