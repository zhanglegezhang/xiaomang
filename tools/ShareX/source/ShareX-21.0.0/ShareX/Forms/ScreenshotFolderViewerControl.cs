using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    public sealed class ScreenshotFolderViewerControl : UserControl
    {
        public event Action<string[]> CombineImagesRequested;
        internal string VideoFFmpegPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        private readonly Image videoPlaceholder = CreateVideoPlaceholder();
        private const int PageSize = 100;
        private const int ThumbnailCacheCapacity = 160;
        private static readonly Color Background = Color.FromArgb(42, 42, 42);
        private static readonly Color Surface = Color.FromArgb(58, 58, 58);
        private static readonly Color Selected = Color.FromArgb(72, 72, 72);

        private sealed class ScreenshotEntry
        {
            public string FullPath { get; set; }
            public string RelativePath { get; set; }
            public DateTime Modified { get; set; }
            public long Length { get; set; }
            public bool IsImage { get; set; }
        }

        private readonly record struct ThumbnailKey(string Path, long ModifiedTicks, long Length);

        private sealed class CachedThumbnail
        {
            public Image Image { get; init; }
            public bool Failed { get; init; }
            public LinkedListNode<ThumbnailKey> RecencyNode { get; init; }
        }

        private readonly DataGridView filesGrid;
        private readonly VisibleMetadataColumnSizer columnSizer;
        private readonly Label statusLabel;
        private readonly Button previousButton;
        private readonly Button nextButton;
        private readonly Dictionary<ThumbnailKey, CachedThumbnail> thumbnailCache = new Dictionary<ThumbnailKey, CachedThumbnail>();
        private readonly LinkedList<ThumbnailKey> thumbnailRecency = new LinkedList<ThumbnailKey>();
        private readonly HashSet<ThumbnailKey> currentPageKeys = new HashSet<ThumbnailKey>();
        private List<ScreenshotEntry> entries = new List<ScreenshotEntry>();
        private CancellationTokenSource scanCancellation;
        private CancellationTokenSource pageCancellation;
        private string rootFolder;
        private int skippedFiles;
        private int scanVersion;
        private int pageVersion;
        private int currentPage;
        private bool thumbnailWorkerRunning;
        private bool thumbnailRequestPending;
        private bool thumbnailRequestScheduled;

        public ScreenshotFolderViewerControl()
        {
            Dock = DockStyle.Fill;
            BackColor = Background;
            ForeColor = ShareXResources.Theme.TextColor;
            Font = new Font("Microsoft YaHei", 9f);

            Panel titlePanel = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Background };
            titlePanel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "截图文件夹",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                ForeColor = ForeColor
            });

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 60,
                WrapContents = false,
                Padding = new Padding(0, 7, 0, 7),
                BackColor = Background
            };
            actions.Controls.Add(CreateButton("刷新", (sender, e) => RefreshFiles(rootFolder)));
            actions.Controls.Add(CreateButton("打开所在文件夹", (sender, e) => OpenSelectedFolder()));
            Button combine = CreateButton("拼图", (sender, e) =>
            {
                string[] paths = SelectedImagePaths();
                if (paths.Length >= 2) CombineImagesRequested?.Invoke(paths);
            });
            combine.Enabled = false;
            actions.Controls.Add(combine);

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Background };
            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ForeColor,
                Text = "暂无文件"
            };
            nextButton = CreateButton("下一页", (sender, e) => ShowPage(currentPage + 1));
            nextButton.Dock = DockStyle.Right;
            nextButton.Width = 90;
            previousButton = CreateButton("上一页", (sender, e) => ShowPage(currentPage - 1));
            previousButton.Dock = DockStyle.Right;
            previousButton.Width = 90;
            footer.Controls.Add(statusLabel);
            footer.Controls.Add(nextButton);
            footer.Controls.Add(previousButton);

            filesGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoGenerateColumns = false,
                AllowUserToResizeColumns = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Background,
                GridColor = Color.FromArgb(68, 68, 68),
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 44,
                RowTemplate = { Height = 62 }
            };
            filesGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(48, 48, 48),
                ForeColor = ForeColor,
                SelectionBackColor = Color.FromArgb(48, 48, 48),
                Font = new Font(Font, FontStyle.Bold)
            };
            filesGrid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = ForeColor,
                SelectionBackColor = Selected,
                SelectionForeColor = ForeColor,
                Padding = new Padding(5)
            };
            filesGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(52, 52, 52);
            filesGrid.Columns.Add(new DataGridViewImageColumn
            {
                HeaderText = "预览",
                Width = 112,
                MinimumWidth = 72,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Resizable = DataGridViewTriState.True,
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { NullValue = null }
            });
            filesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "文件名 / 子目录",
                Width = 500,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 220,
                Resizable = DataGridViewTriState.True,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            filesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "修改时间",
                Width = 205,
                MinimumWidth = 120,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Resizable = DataGridViewTriState.True,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            filesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "大小",
                Width = 120,
                MinimumWidth = 75,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Resizable = DataGridViewTriState.True,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            filesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "类型",
                Width = 90,
                MinimumWidth = 65,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Resizable = DataGridViewTriState.True,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            columnSizer = new VisibleMetadataColumnSizer(filesGrid, (2, 240), (3, 140), (4, 120));
            filesGrid.CellDoubleClick += FilesGrid_CellDoubleClick;
            filesGrid.SelectionChanged += (_, _) => combine.Enabled = SelectedImagePaths().Length >= 2;
            filesGrid.Scroll += (sender, args) =>
            {
                if (args.ScrollOrientation == ScrollOrientation.VerticalScroll) RequestVisibleThumbnails();
            };
            filesGrid.SizeChanged += (sender, args) => RequestVisibleThumbnails();

            Controls.Add(filesGrid);
            Controls.Add(footer);
            Controls.Add(actions);
            Controls.Add(titlePanel);
            MainMenuAppearance.Apply(this);
            int S(int value) => MainMenuAppearance.Scale(this, value);
            Padding = new Padding(S(16));
            titlePanel.Height = S(32);
            actions.Height = S(42);
            actions.Padding = new Padding(0, S(5), 0, S(7));
            footer.Height = S(40);
            foreach (Button button in actions.Controls.OfType<Button>().Concat(new[] { previousButton, nextButton }))
            {
                button.Height = S(30);
                button.Width = TextRenderer.MeasureText(button.Text, button.Font).Width + S(24);
                button.Margin = new Padding(0, 0, S(6), 0);
            }
            filesGrid.ColumnHeadersHeight = S(28);
            filesGrid.RowTemplate.Height = S(42);
            MainMenuAppearance.LayoutPager(footer, statusLabel, previousButton, nextButton);
            UpdateNavigation();
        }

        private string[] SelectedImagePaths() => filesGrid.SelectedRows.Cast<DataGridViewRow>()
            .OrderBy(row => row.Index).Select(row => row.Tag).OfType<ScreenshotEntry>()
            .Where(entry => entry.IsImage).Select(entry => entry.FullPath).ToArray();

        private Button CreateButton(string text, EventHandler click)
        {
            int textWidth = TextRenderer.MeasureText(text, Font).Width;
            Button button = new Button
            {
                Text = text,
                Width = Math.Max(100, textWidth + 32),
                Height = 44,
                FlatStyle = FlatStyle.Flat,
                BackColor = Surface,
                ForeColor = ForeColor,
                Margin = new Padding(0, 0, 8, 0)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(78, 78, 78);
            button.Click += click;
            return button;
        }

        public void RefreshFiles(string folder)
        {
            rootFolder = folder;
            scanCancellation?.Cancel();
            scanCancellation?.Dispose();
            scanCancellation = new CancellationTokenSource();
            int version = ++scanVersion;
            CancelPageLoad();
            entries.Clear();
            skippedFiles = 0;
            currentPage = 0;
            ClearRows();
            statusLabel.Text = "正在读取截图文件夹…";
            // Explicit refresh retries failures (for example a recording that was not yet finalized).
            foreach (ThumbnailKey key in thumbnailCache.Where(pair => pair.Value.Failed).Select(pair => pair.Key).ToArray())
            {
                CachedThumbnail failed = thumbnailCache[key];
                thumbnailRecency.Remove(failed.RecencyNode);
                failed.Image?.Dispose();
                thumbnailCache.Remove(key);
            }
            UpdateNavigation();
            _ = ScanAndDisplayAsync(folder, version, scanCancellation.Token);
        }

        private async Task ScanAndDisplayAsync(string folder, int version, CancellationToken cancellation)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    if (IsCurrentScan(version)) statusLabel.Text = "截图文件夹不存在，暂无可显示的文件。";
                    return;
                }

                (List<ScreenshotEntry> files, int skipped) = await Task.Run(() => ScanFiles(folder, cancellation), cancellation);
                if (!IsCurrentScan(version)) return;
                entries = files;
                skippedFiles = skipped;
                if (entries.Count == 0)
                {
                    statusLabel.Text = skipped > 0 ? $"暂无可显示的文件；{skipped} 个文件无法读取。" : "暂无截图或录屏。";
                    return;
                }

                ShowPage(0);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (IsCurrentScan(version)) statusLabel.Text = "读取截图文件夹失败：" + ex.Message;
            }
        }

        private static (List<ScreenshotEntry>, int) ScanFiles(string folder, CancellationToken cancellation)
        {
            List<ScreenshotEntry> files = new List<ScreenshotEntry>();
            int skipped = 0;
            EnumerationOptions options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (string path in Directory.EnumerateFiles(folder, "*", options))
            {
                cancellation.ThrowIfCancellationRequested();
                bool image = FileHelpers.IsImageFile(path);
                if (!image && !FileHelpers.IsVideoFile(path)) continue;
                try
                {
                    FileInfo file = new FileInfo(path);
                    if (!file.Exists)
                    {
                        skipped++;
                        continue;
                    }
                    files.Add(new ScreenshotEntry
                    {
                        FullPath = path,
                        RelativePath = Path.GetRelativePath(folder, path),
                        Modified = file.LastWriteTime,
                        Length = file.Length,
                        IsImage = image
                    });
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    skipped++;
                }
            }
            files.Sort((left, right) =>
            {
                int timeOrder = right.Modified.CompareTo(left.Modified);
                return timeOrder != 0 ? timeOrder : string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase);
            });
            return (files, skipped);
        }

        private void ShowPage(int index)
        {
            int pages = (entries.Count + PageSize - 1) / PageSize;
            if (index < 0 || index >= pages) return;

            CancelPageLoad();
            pageCancellation = new CancellationTokenSource();
            currentPage = index;
            ScreenshotEntry[] page = entries.Skip(index * PageSize).Take(PageSize).ToArray();
            ClearRows();
            currentPageKeys.Clear();
            filesGrid.SuspendLayout();
            try
            {
                foreach (ScreenshotEntry file in page)
                {
                    currentPageKeys.Add(GetThumbnailKey(file));
                    int rowIndex = filesGrid.Rows.Add(file.IsImage ? null : videoPlaceholder, file.RelativePath,
                        file.Modified.ToString("yyyy-MM-dd HH:mm:ss"), FormatSize(file.Length), file.IsImage ? "图片" : "录屏");
                    filesGrid.Rows[rowIndex].Tag = file;
                }
            }
            finally
            {
                filesGrid.ResumeLayout();
            }
            filesGrid.ClearSelection();
            columnSizer.RequestFit();
            UpdateNavigation();
            statusLabel.Text = $"第 {currentPage + 1}/{pages} 页，共 {entries.Count} 个文件"
                + (skippedFiles > 0 ? $"；{skippedFiles} 个文件无法读取" : "");
            RequestVisibleThumbnails();
        }

        private static ThumbnailKey GetThumbnailKey(ScreenshotEntry file) =>
            new ThumbnailKey(file.FullPath, file.Modified.Ticks, file.Length);

        private void RequestVisibleThumbnails()
        {
            if (IsDisposed || !filesGrid.IsHandleCreated || !filesGrid.Visible || pageCancellation == null ||
                pageCancellation.IsCancellationRequested) return;
            thumbnailRequestPending = true;
            if (thumbnailWorkerRunning || thumbnailRequestScheduled) return;
            thumbnailRequestScheduled = true;
            BeginInvoke((Action)(() =>
            {
                thumbnailRequestScheduled = false;
                if (!IsDisposed && !thumbnailWorkerRunning && thumbnailRequestPending)
                    _ = LoadVisibleThumbnailsAsync();
            }));
        }

        private ScreenshotEntry FindNextVisibleThumbnail()
        {
            if (!filesGrid.Visible || filesGrid.RowCount == 0) return null;
            int first = filesGrid.FirstDisplayedScrollingRowIndex;
            int visibleCount = filesGrid.DisplayedRowCount(false);
            if (first < 0 || visibleCount < 1) return null;
            int end = Math.Min(filesGrid.RowCount, first + visibleCount + 2);
            for (int index = first; index < end; index++)
            {
                DataGridViewRow row = filesGrid.Rows[index];
                if (row.Tag is not ScreenshotEntry file) continue;
                ThumbnailKey key = GetThumbnailKey(file);
                if (thumbnailCache.TryGetValue(key, out CachedThumbnail cached))
                {
                    thumbnailRecency.Remove(cached.RecencyNode);
                    thumbnailRecency.AddLast(cached.RecencyNode);
                    Image display = cached.Image ?? (file.IsImage ? null : videoPlaceholder);
                    if (!ReferenceEquals(row.Cells[0].Value, display)) row.Cells[0].Value = display;
                    if (cached.Failed) row.Cells[0].ToolTipText = file.IsImage ? "缩略图无法读取" : "视频缩略图暂不可用（文件可能尚未录完、损坏或读取超时）";
                }
                else return file;
            }
            return null;
        }

        private async Task LoadVisibleThumbnailsAsync()
        {
            if (thumbnailWorkerRunning) return;
            thumbnailWorkerRunning = true;
            try
            {
                do
                {
                    thumbnailRequestPending = false;
                    while (!IsDisposed && filesGrid.Visible && pageCancellation != null &&
                        !pageCancellation.IsCancellationRequested)
                    {
                        ScreenshotEntry file = FindNextVisibleThumbnail();
                        if (file == null) break;
                        int version = pageVersion;
                        CancellationToken cancellation = pageCancellation.Token;
                        Image thumbnail = null;
                        bool failed = false;
                        try
                        {
                            if (file.IsImage)
                                thumbnail = await Task.Run(() => CreateThumbnail(file, cancellation), cancellation);
                            else
                            {
                                string ffmpegPath = VideoFFmpegPath;
                                using Image frame = await Task.Run(() => VideoThumbnailLoader.LoadAsync(ffmpegPath, file.FullPath, cancellation), cancellation);
                                thumbnail = CreateThumbnail(frame, true, cancellation);
                            }
                        }
                        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception) { failed = true; }

                        if (IsDisposed || version != pageVersion || cancellation.IsCancellationRequested)
                        {
                            thumbnail?.Dispose();
                            return;
                        }
                        CacheThumbnail(GetThumbnailKey(file), thumbnail, failed);
                    }
                }
                while (thumbnailRequestPending && !IsDisposed);
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Screenshot thumbnail loading failed.");
            }
            finally
            {
                thumbnailWorkerRunning = false;
                if (thumbnailRequestPending && !IsDisposed) RequestVisibleThumbnails();
            }
        }

        private static Image CreateThumbnail(ScreenshotEntry file, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            using Image source = Image.FromFile(file.FullPath);
            return CreateThumbnail(source, false, cancellation);
        }

        private static Image CreateThumbnail(Image source, bool video, CancellationToken cancellation)
        {
            if (source.Width <= 0 || source.Height <= 0) throw new ArgumentException("无效的图片尺寸。");
            Bitmap bitmap = new Bitmap(78, 48);
            try
            {
                using Graphics graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Surface);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float scale = Math.Min(78f / source.Width, 48f / source.Height);
                int width = Math.Max(1, (int)(source.Width * scale));
                int height = Math.Max(1, (int)(source.Height * scale));
                graphics.DrawImage(source, (78 - width) / 2, (48 - height) / 2, width, height);
                if (video) DrawVideoBadge(graphics);
                cancellation.ThrowIfCancellationRequested();
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static Image CreateVideoPlaceholder()
        {
            Bitmap image = new Bitmap(78, 48);
            using Graphics graphics = Graphics.FromImage(image);
            graphics.Clear(Surface);
            DrawVideoBadge(graphics);
            return image;
        }

        private static void DrawVideoBadge(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var background = new SolidBrush(Color.FromArgb(210, 25, 25, 25));
            graphics.FillEllipse(background, 52, 23, 22, 22);
            graphics.FillPolygon(Brushes.White, new[] { new Point(60, 28), new Point(60, 40), new Point(69, 34) });
        }

        private void CacheThumbnail(ThumbnailKey key, Image thumbnail, bool failed)
        {
            LinkedListNode<ThumbnailKey> node = thumbnailRecency.AddLast(key);
            thumbnailCache.Add(key, new CachedThumbnail { Image = thumbnail, Failed = failed, RecencyNode = node });
            while (thumbnailCache.Count > ThumbnailCacheCapacity)
            {
                LinkedListNode<ThumbnailKey> oldest = thumbnailRecency.First;
                while (oldest != null && currentPageKeys.Contains(oldest.Value)) oldest = oldest.Next;
                if (oldest == null) break;
                thumbnailRecency.Remove(oldest);
                CachedThumbnail evicted = thumbnailCache[oldest.Value];
                thumbnailCache.Remove(oldest.Value);
                evicted.Image?.Dispose();
            }
        }

        private void FilesGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || filesGrid.Rows[e.RowIndex].Tag is not ScreenshotEntry file) return;
            try
            {
                if (!File.Exists(file.FullPath)) throw new FileNotFoundException("文件已被移动或删除。", file.FullPath);
                Process.Start(new ProcessStartInfo { FileName = file.FullPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开文件失败：" + ex.Message, "截图文件夹", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenSelectedFolder()
        {
            try
            {
                string folder = filesGrid.SelectedRows.Count > 0 && filesGrid.SelectedRows[0].Tag is ScreenshotEntry file
                    ? Path.GetDirectoryName(file.FullPath)
                    : rootFolder;
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    throw new DirectoryNotFoundException("文件夹不存在，请刷新列表。");
                FileHelpers.OpenFolder(folder);
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开所在文件夹失败：" + ex.Message, "截图文件夹", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateNavigation()
        {
            int pages = (entries.Count + PageSize - 1) / PageSize;
            previousButton.Enabled = currentPage > 0;
            nextButton.Enabled = currentPage + 1 < pages;
        }

        private void CancelPageLoad()
        {
            pageCancellation?.Cancel();
            pageCancellation?.Dispose();
            pageCancellation = null;
            pageVersion++;
            thumbnailRequestPending = false;
        }

        private void ClearRows() => filesGrid.Rows.Clear();

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
            {
                pageCancellation?.Cancel();
            }
            else if (entries.Count > 0)
            {
                if (pageCancellation?.IsCancellationRequested == true)
                {
                    pageCancellation.Dispose();
                    pageCancellation = new CancellationTokenSource();
                    pageVersion++;
                }
                RequestVisibleThumbnails();
            }
        }

        private bool IsCurrentScan(int version) => !IsDisposed && version == scanVersion;

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):0.#} MB";
            return $"{bytes / (1024d * 1024 * 1024):0.#} GB";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                scanCancellation?.Cancel();
                scanCancellation?.Dispose();
                scanCancellation = null;
                CancelPageLoad();
                ClearRows();
                foreach (CachedThumbnail cached in thumbnailCache.Values) cached.Image?.Dispose();
                thumbnailCache.Clear();
                thumbnailRecency.Clear();
                videoPlaceholder.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
