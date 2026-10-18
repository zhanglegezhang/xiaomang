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
    /// <summary>A small, always-on-top todo widget with a draggable, click-to-open edge tab.</summary>
    public sealed partial class TodoWidgetForm : Form
    {
        private readonly TodoPageComponent todoPage;
        private readonly ClipboardPageComponent clipboardPage;
        private readonly FavoritesPageComponent favoritesPage;
        private readonly ToolsPageComponent toolsPage;
        private readonly DiaryPageComponent diaryPage;

        private const int CollapsedWidth = 36;
        private const int CollapsedHeight = 48;
        private const int ExpandedWidth = 400;
        private const int ExpandedHeight = 560;
        private const int EmSetCueBanner = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);


        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        // Keep the widget dark while lifting it away from the near-black ShareX theme.
        // Opaque layered colours preserve text clarity better than whole-window opacity.
        private static readonly Color WidgetBackgroundColor = Color.FromArgb(28, 35, 41);
        private static readonly Color WidgetInputColor = Color.FromArgb(34, 43, 51);
        private static readonly Color WidgetCompactHoverColor = Color.FromArgb(44, 57, 68);
        private static readonly Color WidgetCompactBackgroundColor = Color.FromArgb(16, 27, 44);
        private static readonly Color WidgetRowColor = Color.FromArgb(38, 48, 57);
        private static readonly Color WidgetHoverColor = Color.FromArgb(48, 60, 70);
        private static readonly Color WidgetBorderColor = Color.FromArgb(58, 73, 84);
        private static readonly Color WidgetAccentColor = Color.FromArgb(255, 132, 55);
        private static readonly Color HalfWhiteColor = Color.FromArgb(173, 186, 195);

        private readonly RecordingCompactLabel compactLabel;
        private readonly Panel moduleSidebar;
        private StickerOverlayForm stickerOverlay;
        private CompactOverlayForm compactOverlay;
        private bool compactOverlayFailed;
        private FrameCornerOverlayForm[] frameCornerOverlays;
        private bool expandedFrameFailed;
        private bool showingWidgetOverlay;
        private bool closingWidget;
        private readonly Button todoModuleButton;
        private readonly Button clipboardModuleButton;
        private readonly Button favoritesModuleButton;
        private readonly Button newsModuleButton;
        private readonly Button diaryModuleButton;
        private int? heightBeforeNewsAutoExpand;
        private readonly Timer collapseTimer;
        private readonly Timer noteSaveTimer;
        private readonly Timer compactHintTimer;
        private CompactHintForm compactHint;
        private Control compactHintSurface;
        private string compactHintText;
        private readonly NoteLogStore noteLogStore;
        private Task noteLogSeedTask = Task.CompletedTask;
        private bool pendingWritesPaused;
        private readonly Bitmap titleLogoImage;
        private readonly Bitmap compactLogoImage;
        private readonly Bitmap sharedDeleteIcon;
        private readonly ToolTip recordTip = new ToolTip();
        private readonly Bitmap sharedSpeakerIcon;
        private readonly Bitmap sharedStopIcon;
        private DateTime lastLogCleanupDate;
        private bool expanded;
        private bool animatingGenie;
        private GenieAnimationForm genieAnimationForm;
        private Bitmap genieContentSnapshot;
        private Timer genieAnimationTimer;
        private Stopwatch genieAnimationClock;
        private Rectangle genieStartBounds;
        private Rectangle genieTargetBounds;
        private bool titleDragging;
        private int titleDragStartMouseY;
        private int titleDragStartWindowY;
        private Control titleDragControl;
        private bool ready, resizing, compactDragging, compactMoved, clipboardMetricsWarmed;
        private bool changingExpansion;
        internal bool PersonalDataRecordingActive => compactRecording || stoppingVoiceRecording;
        private Point compactDragStartMouse;
        private int compactDragStartY;
        private Point resizeStartMouse;
        private Rectangle resizeStartBounds;
        private Screen dockScreen;
        private readonly System.Collections.Generic.List<Panel> resizeGrips = new System.Collections.Generic.List<Panel>();

        private sealed class ClipboardRowMetric
        {
            public ClipboardRowMetric Alternate;
            public Guid Id;
            public string Text;
            public int EditorWidth;
            public int MaximumEditorHeight;
            public int EditorHeight;
            public int RowHeight;
        }

        private sealed class VirtualRowMetric
        {
            public VirtualRowMetric Alternate;
            public Guid Id;
            public string Text;
            public bool State;
            public bool HasVoice;
            public int EditorWidth;
            public int MaximumEditorHeight;
            public int EditorHeight;
            public int RowHeight;
        }

        private TodoSettings Settings
        {
            get
            {
                Program.Settings.TodoSettings ??= new TodoSettings();
                Program.Settings.TodoSettings.Items ??= new System.Collections.Generic.List<TodoItem>();
                Program.Settings.TodoSettings.ClipboardItems ??= new List<ClipboardTextItem>();
                Program.Settings.TodoSettings.FavoriteItems ??= new List<FavoriteTextItem>();
                return Program.Settings.TodoSettings;
            }
        }

        public TodoWidgetForm(NoteLogStore noteLogStore = null)
        {
            this.noteLogStore = noteLogStore ?? new NoteLogStore(Program.PersonalFolder);
            this.noteLogStore.WriteFailed += NoteLogWriteFailed;
            Shown += (sender, args) => noteLogSeedTask = SeedExistingNoteItemsAsync();
            lastLogCleanupDate = DateTime.Today;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            // Layout uses measured text and explicit DPI-scaled spacing, not a second automatic scale pass.
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei", 8f);
            titleLogoImage = ShareXResources.Icon.ToBitmap();
            using (Stream stream = typeof(TodoWidgetForm).Assembly.GetManifestResourceStream("ShareX.Assets.XiaomangCompactRobot.png"))
            using (Image image = Image.FromStream(stream)) compactLogoImage = new Bitmap(image);
            sharedDeleteIcon = CreateDeleteIcon();
            sharedSpeakerIcon = CreateSpeakerIcon(false);
            sharedStopIcon = CreateSpeakerIcon(true);
            Text = "小蟒同学 - 待办";
            dockScreen = Screen.FromPoint(Cursor.Position);
            Padding = new Padding(1);
            BackColor = WidgetBackgroundColor;

            compactLabel = new RecordingCompactLabel
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold),
                ForeColor = ShareXResources.Theme.TextColor,
                BackColor = WidgetInputColor,
                Image = compactLogoImage
            };
            compactLabel.MouseDown += CompactLabel_MouseDown;
            compactLabel.MouseMove += CompactLabel_MouseMove;
            compactLabel.MouseUp += CompactLabel_MouseUp;
            compactLabel.MouseCaptureChanged += (sender, e) => CompactSurface_MouseCaptureChanged(compactLabel);
            compactLabel.MouseEnter += (sender, e) => CompactSurface_MouseEnter(compactLabel);
            compactLabel.MouseLeave += (sender, e) => CompactSurface_MouseLeave(compactLabel);
            compactHintTimer = new Timer { Interval = 500 };
            compactHintTimer.Tick += CompactHintTimer_Tick;
            Controls.Add(compactLabel);

            collapseTimer = new Timer { Interval = 300 };
            collapseTimer.Tick += (sender, e) =>
            {
                if (!ocrPanelHeldOpen && !animatingGenie && !resizing && !titleDragging && !compactDragging && !showingWidgetOverlay && !todoPage.choosingSchedule && !HasEditorFocus() &&
                    !ContainsWidgetPoint(Cursor.Position)) SetExpanded(false);
                collapseTimer.Stop();
            };

            noteSaveTimer = new Timer { Interval = 500 };
            noteSaveTimer.Tick += (sender, e) =>
            {
                noteSaveTimer.Stop();
                SettingManager.SaveApplicationConfigAsync();
            };
            todoPage = new TodoPageComponent(this);
            Controls.Add(todoPage);
            clipboardPage = new ClipboardPageComponent(this);
            Controls.Add(clipboardPage);
            favoritesPage = new FavoritesPageComponent(this);
            Controls.Add(favoritesPage);
            toolsPage = new ToolsPageComponent(this);
            Controls.Add(toolsPage);
            diaryPage = new DiaryPageComponent(this);
            Controls.Add(diaryPage);

            // Keep the background identical to the content area. Only the module buttons are
            // visible, so the navigation reads as separate paper-like tabs rather than a bar.
            moduleSidebar = new Panel { Dock = DockStyle.Left, Width = D(24), BackColor = Color.Transparent, Visible = false };
            todoModuleButton = CreateModuleButton("1\n待\n办", "待办");
            clipboardModuleButton = CreateModuleButton("2\n剪\n切\n板", "剪切板");
            favoritesModuleButton = CreateModuleButton("3\n收\n藏\n夹", "收藏夹");
            newsModuleButton = CreateModuleButton("4\n工\n具", "工具");
            diaryModuleButton = CreateModuleButton("5\n日\n记", "日记");
            todoModuleButton.Click += (sender, e) => SelectModule("Todo");
            clipboardModuleButton.Click += (sender, e) => SelectModule("Clipboard");
            favoritesModuleButton.Click += (sender, e) => SelectModule("Favorites");
            newsModuleButton.Click += (sender, e) => SelectModule("News");
            diaryModuleButton.Click += (sender, e) => SelectModule("Diary");
            todoModuleButton.Dock = DockStyle.None;
            clipboardModuleButton.Dock = DockStyle.None;
            favoritesModuleButton.Dock = DockStyle.None;
            newsModuleButton.Dock = DockStyle.None;
            diaryModuleButton.Dock = DockStyle.None;
            moduleSidebar.Controls.AddRange(new Control[] { todoModuleButton, clipboardModuleButton, favoritesModuleButton, newsModuleButton, diaryModuleButton });
            Controls.Add(moduleSidebar);

            compactLongPressTimer = new Timer { Interval = 500 };
            compactLongPressTimer.Tick += (sender, e) =>
            {
                compactLongPressTimer.Stop();
                if (compactDragging && !compactMoved) StartVoiceRecording();
            };
            voiceRecordingTimer = new Timer { Interval = 60 };
            voiceRecordingTimer.Tick += VoiceRecordingTimer_Tick;
            voicePlaybackTimer = new Timer { Interval = 100 };
            voicePlaybackTimer.Tick += (sender, e) =>
            {
                if (playingVoiceItemId.HasValue && DateTime.UtcNow >= voicePlaybackEndsAt) StopVoicePlayback();
            };

            reminderTimer = new Timer { Interval = 1000 };
            reminderTimer.Tick += (sender, e) => CheckReminders();
            reminderTimer.Start();

            MouseEnter += Widget_MouseEnter;
            MouseLeave += Widget_MouseLeave;
            SizeChanged += TodoWidgetForm_SizeChanged;
            AttachHoverEvents(this);
            CreateResizeGrips();
            ready = true;
            SelectModule(Settings.ActiveModule, false);
            SetExpanded(false);
            todoPage.RenderItems();
        }

        public void AddOcrText(string text)
        {
            if (pendingWritesPaused) throw new InvalidOperationException("数据正在处理中，请稍后再添加待办。");
            if (string.IsNullOrWhiteSpace(text)) return;
            TodoItem item = new TodoItem { Text = "**From OCR**" + Environment.NewLine + text.Trim() };
            Settings.Items.Add(item);
            WriteNoteLog(NoteLogKind.Todo, "OCR 新增", item.Text, item.CreatedAt);
            SettingManager.SaveApplicationConfigAsync();
            try
            {
                HoldOcrPanelOpen();
                SelectModule("Todo");
                OpenPanel();
                todoPage.RenderItems();
                ScrollToOcrItem(item.Id);
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex);
                MessageBox.Show("文字已加入待办，但自动显示失败。请打开待办查看，无需重复添加。",
                    "添加到待办", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ScrollToOcrItem(Guid id)
        {
            int index = Array.FindIndex(todoPage.todoViewItems, x => x.Id == id);
            if (index >= 0 && index < todoPage.todoRowOffsets.Length &&
                index < todoPage.todoRowHeights.Length && todoPage.todoList.ClientSize.Height > 0)
            {
                int scrollY = Math.Max(0, -todoPage.todoList.AutoScrollPosition.Y);
                int rowTop = todoPage.todoRowOffsets[index];
                int rowBottom = rowTop + todoPage.todoRowHeights[index];
                if (rowTop < scrollY) scrollY = rowTop;
                else if (rowBottom > scrollY + todoPage.todoList.ClientSize.Height)
                    scrollY = rowBottom - todoPage.todoList.ClientSize.Height;
                todoPage.todoList.AutoScrollPosition = new Point(0, scrollY);
                todoPage.UpdateTodoViewport();
            }
        }

        public bool AddClipboardText(string text)
        {
            return AddClipboardItem(new ClipboardTextItem { Text = text });
        }

        public bool AddClipboardItem(ClipboardTextItem item)
        {
            if (pendingWritesPaused || PersonalDataManager.Busy) return false;
            if (item == null || string.IsNullOrWhiteSpace(item.Text)) return false;
            item.Text = NormalizeClipboardText(item.Text);
            if (string.IsNullOrEmpty(item.Text)) return false;
            // A clipboard owner can publish several format updates for the same copy. Keep one record.
            List<ClipboardTextItem> items = Settings.ClipboardItems;
            if (items.Take(10).Any(existing => item.HasSameContent(existing) ||
                (string.IsNullOrEmpty(item.SourceKey) && existing.Text == item.Text))) return false;

            // Publish a new list instead of mutating the list that a background settings save
            // may currently be enumerating.
            items = new List<ClipboardTextItem>(items);
            items.Insert(0, item);
            if (items.Count > 100) items.RemoveRange(100, items.Count - 100);
            Settings.ClipboardItems = items;
            WriteNoteLog(NoteLogKind.Clipboard, "记录", item.GetDisplayText(), item.CreatedAt);
            noteSaveTimer.Stop();
            noteSaveTimer.Start();
            if (Settings.ActiveModule == "Clipboard") clipboardPage.RenderClipboard();
            return true;
        }

        private Button CreateModuleButton(string text, string name)
        {
            Color tabColor = name switch
            {
                "待办" => Color.FromArgb(173, 97, 12),
                "剪切板" => Color.FromArgb(19, 128, 95),
                "收藏夹" => Color.FromArgb(40, 100, 197),
                "日记" => Color.FromArgb(133, 84, 200),
                _ => Color.FromArgb(8, 127, 155)
            };
            Button button = new StickerButton
            {
                Text = text,
                AccessibleName = name,
                Dock = DockStyle.Top,
                Width = D(24),
                Height = D(70),
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 7f, FontStyle.Bold),
                ForeColor = Color.FromArgb(239, 242, 244),
                BackColor = tabColor,
                TextAlign = ContentAlignment.MiddleCenter,
                // Keep the label centered in the actual tab bounds. The trapezoid itself
                // is already asymmetric; an extra horizontal inset made "待办" appear off-center.
                Padding = Padding.Empty
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(tabColor.R + 14, tabColor.G + 14, tabColor.B + 14);
            return button;
        }

        private FlowLayoutPanel CreateModuleList()
        {
            FlowLayoutPanel list = new DarkFlowLayoutPanel
            {
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = WidgetBackgroundColor
            };
            list.MouseWheel += ModuleList_MouseWheel;
            return list;
        }

        private VirtualClipboardPanel CreateVirtualClipboardList()
        {
            VirtualClipboardPanel list = new VirtualClipboardPanel
            {
                BackColor = WidgetBackgroundColor
            };
            list.MouseWheel += ModuleList_MouseWheel;
            return list;
        }

        private void ModuleList_MouseWheel(object sender, MouseEventArgs e)
        {
            if (sender is ScrollableControl list) ScrollModuleList(list, e);
        }

        private void AttachListWheel(Control control, ScrollableControl list)
        {
            control.MouseWheel += (sender, e) => ScrollModuleList(list, e);
            foreach (Control child in control.Controls) AttachListWheel(child, list);
        }

        private void ScrollModuleList(ScrollableControl list, MouseEventArgs e)
        {
            if (e.Delta == 0) return;
            if (list is VirtualClipboardPanel virtualList)
            {
                if (!virtualList.HasVerticalScroll) return;
                int virtualMovement = (int)Math.Round(-(double)e.Delta * D(55) / SystemInformation.MouseWheelScrollDelta);
                if (virtualMovement == 0) virtualMovement = -Math.Sign(e.Delta) * D(3);
                virtualList.SetScrollOffset(virtualList.ScrollOffset + virtualMovement);
                if (e is HandledMouseEventArgs virtualHandled) virtualHandled.Handled = true;
                return;
            }
            if (!list.VerticalScroll.Visible) return;
            int current = -list.AutoScrollPosition.Y;
            int movement = (int)Math.Round(-(double)e.Delta * D(55) / SystemInformation.MouseWheelScrollDelta);
            if (movement == 0) movement = -Math.Sign(e.Delta) * D(3);
            int maximum = Math.Max(0, list.VerticalScroll.Maximum - list.VerticalScroll.LargeChange + 1);
            list.AutoScrollPosition = new Point(0, Math.Clamp(current + movement, 0, maximum));
            if (ReferenceEquals(list, todoPage.todoList)) todoPage.UpdateTodoViewport();
            else if (ReferenceEquals(list, clipboardPage.clipboardList)) clipboardPage.UpdateClipboardViewport();
            else if (ReferenceEquals(list, favoritesPage.favoritesList)) favoritesPage.UpdateFavoritesViewport();
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
        }

        private Label CreateEmptyLabel(string text)
        {
            return new Label
            {
                AutoSize = false,
                Height = D(54),
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ShareXResources.Theme.TextColor,
                BackColor = Color.Transparent
            };
        }

        private void UpdateCompactText()
        {
            string module = Settings.ActiveModule switch
            {
                "Clipboard" => "剪切板",
                "Favorites" => "收藏夹",
                "News" => "常用工具",
                "Diary" => "本地日记",
                _ => "待办事项"
            };
            int count = Settings.Items.Count(x => !x.IsCompleted);
            compactLabel.Text = "";
            compactLabel.PendingCount = count;
            compactLabel.AccessibleName = $"小蟒同学，{module}，{count} 项待办未完成";
            compactHintText = stoppingVoiceRecording ? "正在处理录音…" : $"{count}项待办\n长按录音\n右键搜索";
            compactLabel.AccessibleDescription = stoppingVoiceRecording ? "正在处理录音" : "";
            if (compactHint?.Visible == true) UpdateCompactHintWindow();
            compactLabel.Invalidate();
            if (compactOverlay != null && !compactOverlay.IsDisposed)
            {
                RefreshCompactOverlay();
            }
        }

        private void SaveAndRender()
        {
            SettingManager.SaveApplicationConfigAsync();
            todoPage.RenderItems();
        }

        private static void GetVisibleRange(int[] offsets, int[] heights, int scrollY, int viewportHeight, int overscan,
            out int start, out int end)
            => WidgetListLayout.GetVisibleRange(offsets, heights, scrollY, viewportHeight, overscan, out start, out end);
    }
}
