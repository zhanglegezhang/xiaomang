using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class TemporaryFileViewerControl : UserControl
    {
        private const int PageSize = 100;
        private static readonly Color Background = Color.FromArgb(42, 42, 42);
        private static readonly Color Surface = Color.FromArgb(58, 58, 58);
        private readonly Action<string> openFile;
        private readonly DataGridView filesGrid;
        private readonly VisibleMetadataColumnSizer columnSizer;
        private readonly Label statusLabel;
        private readonly Button previousButton;
        private readonly Button nextButton;
        private List<TemporaryFileEntry> entries = new List<TemporaryFileEntry>();
        private CancellationTokenSource scanCancellation;
        private int scanVersion;
        private int currentPage;

        private sealed class TemporaryFileEntry
        {
            internal string Path;
            internal string RelativePath;
            internal DateTime Modified;
            internal long Length;
        }

        internal TemporaryFileViewerControl(Action<string> openFile)
        {
            this.openFile = openFile ?? throw new ArgumentNullException(nameof(openFile));
            Dock = DockStyle.Fill;
            BackColor = Background;
            ForeColor = ShareXResources.Theme.TextColor;
            Font = new Font("Microsoft YaHei", 9f);

            Panel title = new Panel { Dock = DockStyle.Top, Height = 55, BackColor = Background };
            title.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "临时文件夹",
                Font = new Font("Microsoft YaHei", 11f, FontStyle.Bold),
                ForeColor = ForeColor,
                TextAlign = ContentAlignment.MiddleLeft
            });
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 54,
                WrapContents = false,
                Padding = new Padding(0, 5, 0, 5),
                BackColor = Background
            };
            actions.Controls.Add(CreateButton("刷新", (sender, args) => RefreshFiles()));
            actions.Controls.Add(CreateButton("打开", (sender, args) => OpenSelected()));
            actions.Controls.Add(CreateButton("打开所在文件夹", (sender, args) => OpenSelectedFolder()));

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Background };
            statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = ForeColor };
            nextButton = CreateButton("下一页", (sender, args) => ShowPage(currentPage + 1));
            previousButton = CreateButton("上一页", (sender, args) => ShowPage(currentPage - 1));
            nextButton.Dock = DockStyle.Right;
            previousButton.Dock = DockStyle.Right;
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
                AllowUserToResizeColumns = true,
                RowHeadersVisible = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Background,
                GridColor = Color.FromArgb(68, 68, 68),
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 42,
                RowTemplate = { Height = 42 }
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
                SelectionBackColor = Color.FromArgb(72, 72, 72),
                SelectionForeColor = ForeColor,
                Padding = new Padding(5)
            };
            filesGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(52, 52, 52);
            filesGrid.Columns.Add(CreateColumn("月份", 125, 90));
            filesGrid.Columns.Add(CreateColumn("文件名", 550, 220));
            filesGrid.Columns.Add(CreateColumn("类型", 105, 70));
            filesGrid.Columns.Add(CreateColumn("修改时间", 205, 130));
            filesGrid.Columns.Add(CreateColumn("大小", 110, 70));
            filesGrid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            columnSizer = new VisibleMetadataColumnSizer(filesGrid, (0, 145), (2, 145), (3, 240), (4, 140));
            filesGrid.CellDoubleClick += (sender, args) =>
            {
                if (args.RowIndex >= 0) OpenRow(args.RowIndex);
            };

            Controls.Add(filesGrid);
            Controls.Add(footer);
            Controls.Add(actions);
            Controls.Add(title);
            MainMenuAppearance.Apply(this);
            int S(int value) => MainMenuAppearance.Scale(this, value);
            Padding = new Padding(S(16));
            title.Height = S(32);
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
            MainMenuAppearance.LayoutPager(footer, statusLabel, previousButton, nextButton);
            UpdateNavigation();
        }

        private DataGridViewTextBoxColumn CreateColumn(string title, int width, int minimumWidth) => new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            Width = width,
            MinimumWidth = minimumWidth,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.True,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        private Button CreateButton(string text, EventHandler click)
        {
            Button button = new Button
            {
                Text = text,
                Width = Math.Max(95, TextRenderer.MeasureText(text, Font).Width + 28),
                Height = 42,
                FlatStyle = FlatStyle.Flat,
                BackColor = Surface,
                ForeColor = ForeColor,
                Margin = new Padding(0, 0, 8, 0)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(78, 78, 78);
            button.Click += click;
            return button;
        }

        internal void RefreshFiles()
        {
            scanCancellation?.Cancel();
            scanCancellation?.Dispose();
            scanCancellation = new CancellationTokenSource();
            int version = ++scanVersion;
            entries.Clear();
            currentPage = 0;
            filesGrid.Rows.Clear();
            statusLabel.Text = "正在读取临时文件夹…";
            UpdateNavigation();
            _ = ScanFilesAsync(version, scanCancellation.Token);
        }

        private async Task ScanFilesAsync(int version, CancellationToken cancellation)
        {
            try
            {
                List<TemporaryFileEntry> files = await Task.Run(() => ScanFiles(cancellation), cancellation);
                if (IsDisposed || version != scanVersion) return;
                entries = files;
                if (entries.Count == 0)
                {
                    statusLabel.Text = "暂无临时文件。";
                    return;
                }
                ShowPage(0);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (!IsDisposed && version == scanVersion) statusLabel.Text = "读取临时文件失败：" + error.Message;
            }
        }

        private static List<TemporaryFileEntry> ScanFiles(CancellationToken cancellation)
        {
            string root = TemporaryFileStorage.RootFolder;
            List<TemporaryFileEntry> files = new List<TemporaryFileEntry>();
            if (!Directory.Exists(root)) return files;
            EnumerationOptions options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (string path in Directory.EnumerateFiles(root, "*", options))
            {
                cancellation.ThrowIfCancellationRequested();
                string extension = Path.GetExtension(path);
                if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    FileInfo info = new FileInfo(path);
                    if (!info.Exists) continue;
                    files.Add(new TemporaryFileEntry
                    {
                        Path = path,
                        RelativePath = Path.GetRelativePath(root, path),
                        Modified = info.LastWriteTime,
                        Length = info.Length
                    });
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            files.Sort((left, right) => right.Modified.CompareTo(left.Modified));
            return files;
        }

        private void ShowPage(int page)
        {
            int pages = (entries.Count + PageSize - 1) / PageSize;
            if (page < 0 || page >= pages) return;
            currentPage = page;
            filesGrid.Rows.Clear();
            foreach (TemporaryFileEntry entry in entries.Skip(page * PageSize).Take(PageSize))
            {
                string month = Path.GetDirectoryName(entry.RelativePath) ?? "";
                string type = Path.GetExtension(entry.Path).Equals(".md", StringComparison.OrdinalIgnoreCase) ? "Markdown" : "TXT";
                int row = filesGrid.Rows.Add(month, Path.GetFileName(entry.Path), type,
                    entry.Modified.ToString("yyyy-MM-dd HH:mm:ss"), FormatSize(entry.Length));
                filesGrid.Rows[row].Tag = entry;
            }
            filesGrid.ClearSelection();
            columnSizer.RequestFit();
            statusLabel.Text = $"第 {page + 1}/{pages} 页，共 {entries.Count} 个文件";
            UpdateNavigation();
        }

        private void OpenSelected()
        {
            if (filesGrid.SelectedRows.Count > 0) OpenRow(filesGrid.SelectedRows[0].Index);
        }

        private void OpenRow(int row)
        {
            if (filesGrid.Rows[row].Tag is not TemporaryFileEntry entry) return;
            try
            {
                if (!File.Exists(entry.Path)) throw new FileNotFoundException("文件已被移动或删除，请刷新列表。", entry.Path);
                openFile(entry.Path);
            }
            catch (Exception error)
            {
                MessageBox.Show("打开临时文件失败：" + error.Message, "临时文件夹",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenSelectedFolder()
        {
            try
            {
                string folder = filesGrid.SelectedRows.Count > 0 &&
                    filesGrid.SelectedRows[0].Tag is TemporaryFileEntry entry
                    ? Path.GetDirectoryName(entry.Path) : TemporaryFileStorage.RootFolder;
                OpenFolder(folder);
            }
            catch (Exception error)
            {
                MessageBox.Show("打开所在文件夹失败：" + error.Message, "临时文件夹",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                FileHelpers.OpenFolder(folder);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "打开文件夹失败：" + error.Message, "临时文件夹",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateNavigation()
        {
            int pages = (entries.Count + PageSize - 1) / PageSize;
            previousButton.Enabled = currentPage > 0;
            nextButton.Enabled = currentPage + 1 < pages;
        }

        private static string FormatSize(long bytes) => bytes < 1024 ? $"{bytes} B" :
            bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KB" : $"{bytes / (1024d * 1024):0.#} MB";

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                scanCancellation?.Cancel();
                scanCancellation?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
