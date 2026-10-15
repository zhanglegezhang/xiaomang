using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private sealed partial class FavoritesPageComponent
        {
            private FileFavoriteList pinnedFiles, discoveredFiles;
            private SlimListScrollBar pinnedFilesScroll, discoveredFilesScroll;
            private Panel fileArea, fileDivider, fileTopDivider;
            private Label fileEmpty;
            private Timer fileRefreshTimer;
            private ContextMenuStrip fileMenu;
            private volatile bool filesDirty = true;
            private bool fileBusy;
            private FileFavoritesWheelFilter fileWheelFilter;
            private void CreateFileFavorites()
            {
                fileArea = new Panel { BackColor = Color.FromArgb(38,48,57) };
                Controls.Add(fileArea);
                fileTopDivider = new Panel { BackColor = WidgetBorderColor, Height = 1 };
                fileDivider = new Panel { BackColor = WidgetBorderColor, Height = 1 };
                fileArea.Controls.Add(fileTopDivider); fileArea.Controls.Add(fileDivider);
                pinnedFiles = new FileFavoriteList(Owner, true);
                discoveredFiles = new FileFavoriteList(Owner, false);
                fileArea.Controls.Add(pinnedFiles); fileArea.Controls.Add(discoveredFiles);
                pinnedFilesScroll = new SlimListScrollBar(pinnedFiles, Owner.D(12));
                discoveredFilesScroll = new SlimListScrollBar(discoveredFiles, Owner.D(12));
                fileArea.Controls.Add(pinnedFilesScroll); fileArea.Controls.Add(discoveredFilesScroll);
                fileWheelFilter=new FileFavoritesWheelFilter(Owner,pinnedFiles,discoveredFiles,pinnedFilesScroll,discoveredFilesScroll);
                Application.AddMessageFilter(fileWheelFilter);
                fileEmpty = new Label { Text = "可在资源管理器右键添加文件或目录", ForeColor = HalfWhiteColor,
                    Font = favoriteMetadataFont, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
                fileArea.Controls.Add(fileEmpty);
                foreach (var list in new[] { pinnedFiles, discoveredFiles })
                {
                    list.OpenItem += item => FileAction(() => FileFavoritesService.Open(item));
                    list.UnpinItem += item => FileAction(() => FileFavoritesService.Store.Unpin(item.Path));
                    list.MenuItem += (item, point) => ShowFileMenu(list, item, point);
                    list.MouseEnter += (s, e) => Owner.collapseTimer.Stop();
                    list.MouseLeave += (s, e) => Owner.ScheduleCollapseIfPointerOutside();
                }
                fileRefreshTimer = new Timer { Interval = 1000 };
                fileRefreshTimer.Tick += (s, e) => {
                    if (!Visible || fileBusy || fileMenu?.Visible == true) return;
                    // Never move a target while the pointer is over either file list.
                    if (fileArea.RectangleToScreen(fileArea.ClientRectangle).Contains(Cursor.Position) || pinnedFiles.Focused || discoveredFiles.Focused) return;
                    if (filesDirty) RefreshFileFavorites(false);
                };
                fileRefreshTimer.Start();
                if (FileFavoritesService.Store != null) FileFavoritesService.Store.Changed += FilesChanged;
                VisibleChanged += (s, e) => { if (Visible) RefreshFileFavorites(true); };
            }
            private void FilesChanged() => filesDirty = true;
            private void DisposeFileFavorites()
            {
                if (FileFavoritesService.Store != null) FileFavoritesService.Store.Changed -= FilesChanged;
                fileRefreshTimer?.Dispose(); fileMenu?.Dispose();
                if(fileWheelFilter!=null)Application.RemoveMessageFilter(fileWheelFilter);
            }
            private void LayoutFileFavorites(int x, int y, int width, int height)
            {
                fileArea.Bounds = new Rectangle(x, y, Math.Max(1, width), height);
                RefreshFileFavorites(false);
                ArrangeFileLists();
            }
            private void ArrangeFileLists()
            {
                int width = fileArea.ClientSize.Width, top = Owner.D(5), height = Math.Max(1, fileArea.Height - top);
                fileTopDivider.SetBounds(0, 0, width, 1);
                bool pins = pinnedFiles.Items.Count > 0, discovered = discoveredFiles.Items.Count > 0;
                pinnedFiles.Visible = pins; discoveredFiles.Visible = discovered;
                fileDivider.Visible = pins && discovered;
                int pinHeight = pins && discovered ? height / 2 : height;
                pinnedFiles.SetBounds(0, top, width, pinHeight);
                fileDivider.SetBounds(0, top + pinHeight, width, 1);
                discoveredFiles.SetBounds(0, pins ? top + pinHeight + Owner.D(4) : top, width,
                    pins ? Math.Max(1, height - pinHeight - Owner.D(4)) : height);
                void Scroll(FileFavoriteList list, SlimListScrollBar bar)
                {
                    bar.SetBounds(width-Owner.D(12),list.Top,Owner.D(12),list.Height);
                    list.Width = Math.Max(1,width-(list.ScrollMaximum>0?Owner.D(12):0));
                    bar.RefreshFromList(); bar.Visible = list.Visible && list.ScrollMaximum>0;bar.BringToFront();
                }
                Scroll(pinnedFiles,pinnedFilesScroll);Scroll(discoveredFiles,discoveredFilesScroll);
                fileEmpty.Visible = !pins && !discovered;
                fileEmpty.SetBounds(Owner.D(4), top, Math.Max(1, width - Owner.D(8)), Math.Min(height, Owner.D(36)));
            }
            private void RefreshFileFavorites(bool force)
            {
                if (pinnedFiles == null || (!force && !filesDirty)) return;
                var store = FileFavoritesService.Store;
                if (store == null || !store.Ready.IsCompleted) return;
                if (store.Ready.IsFaulted) { fileEmpty.Text = "文件收藏读取失败，请恢复个人数据备份"; return; }
                filesDirty = false;
                string search = favoriteSearchBox.Text.Trim();
                bool Match(FavoriteFile item) => search.Length == 0 || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || item.Path.Contains(search, StringComparison.OrdinalIgnoreCase);
                pinnedFiles.SetItems(store.Pinned.Where(Match).ToArray());
                discoveredFiles.SetItems(store.Discovered.Where(Match).Take(10).ToArray());
                fileEmpty.Text = search.Length > 0 ? "没有匹配的文件或目录" : FileFavoritesService.Error ?? store.Warning ?? "可在资源管理器右键添加文件或目录";
                ArrangeFileLists();
            }
            private async void FileAction(Func<Task> action)
            {
                if (fileBusy) return;
                fileBusy = true;
                try { await action(); if (!IsDisposed) { filesDirty = true; RefreshFileFavorites(true); } }
                catch (Exception error) {
                    DebugHelper.WriteException(error);
                    if (!IsDisposed) {
                        Owner.showingWidgetOverlay = true;
                        try { MessageBox.Show(Owner, error.Message, "文件收藏", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                        finally { Owner.showingWidgetOverlay = false; }
                    }
                }
                finally { fileBusy = false; }
            }
            private void ShowFileMenu(FileFavoriteList list, FavoriteFile item, Point point)
            {
                fileMenu?.Dispose(); fileMenu = new ContextMenuStrip();
                fileMenu.Items.Add("打开", null, (s, e) => FileAction(() => FileFavoritesService.Open(item)));
                if (item.Pinned) {
                    fileMenu.Items.Add("取消固定", null, (s, e) => FileAction(() => FileFavoritesService.Store.Unpin(item.Path)));
                    fileMenu.Items.Add("更改位置…", null, (s, e) => BeginInvoke(new Action(() => RepointFile(item))));
                }
                else {
                    fileMenu.Items.Add("固定", null, (s, e) => FileAction(() => FileFavoritesService.Store.Pin(item.Path)));
                    fileMenu.Items.Add("不再推荐", null, (s, e) => FileAction(() => FileFavoritesService.Store.Exclude(item.Path)));
                }
                fileMenu.Opening += (s, e) => { Owner.showingWidgetOverlay = true; Owner.collapseTimer.Stop(); };
                fileMenu.Closed += (s, e) => { Owner.showingWidgetOverlay = false; Owner.ScheduleCollapseIfPointerOutside(); };
                ShareXResources.ApplyCustomThemeToContextMenuStrip(fileMenu);
                fileMenu.Show(list, point);
            }
            private void RepointFile(FavoriteFile item)
            {
                Owner.showingWidgetOverlay = true; Owner.collapseTimer.Stop();
                try {
                    string path = null;
                    if (item.Type == "folder") {
                        using var dialog = new FolderBrowserDialog { Description = "选择文件夹的新位置", UseDescriptionForTitle = true };
                        if (dialog.ShowDialog(Owner) == DialogResult.OK) path = dialog.SelectedPath;
                    } else {
                        using var dialog = new OpenFileDialog { Title = "选择文件的新位置", CheckFileExists = true };
                        if (dialog.ShowDialog(Owner) == DialogResult.OK) path = dialog.FileName;
                    }
                    if (path != null) FileAction(() => FileFavoritesService.Store.Repoint(item.Path, path));
                }
                finally { Owner.showingWidgetOverlay = false; }
            }
        }

        private sealed class FileFavoritesWheelFilter : IMessageFilter
        {
            private readonly Control owner;private readonly FileFavoriteList[] lists;private readonly Control[] bars;
            internal FileFavoritesWheelFilter(Control owner,FileFavoriteList first,FileFavoriteList second,Control firstBar,Control secondBar)
            {this.owner=owner;lists=new[]{first,second};bars=new[]{firstBar,secondBar};}
            public bool PreFilterMessage(ref Message message)
            {
                if(message.Msg!=0x20a || owner.IsDisposed || !owner.Visible)return false;
                Point point=Cursor.Position;
                for(int i=0;i<lists.Length;i++)
                    if(lists[i].Visible && (lists[i].RectangleToScreen(lists[i].ClientRectangle).Contains(point) ||
                        bars[i].Visible && bars[i].RectangleToScreen(bars[i].ClientRectangle).Contains(point)))
                    {lists[i].ScrollWheel(unchecked((short)((long)message.WParam>>16)));return true;}
                return false;
            }
        }

        private sealed class FileFavoriteList : ListBox
        {
            private readonly TodoWidgetForm owner;
            private readonly bool pins;
            private readonly ToolTip tip = new();
            private string hoverPath;
            private bool hidingScroll;
            private double wheelRemainder;
            internal void ScrollWheel(int delta)
            {
                if(delta==0 || ScrollMaximum==0){wheelRemainder=0;return;}
                // Match the upper collection's 55-DIP wheel movement, rounded
                // to whole rows. Keep high-resolution wheel deltas between events.
                wheelRemainder+=-(double)delta*owner.D(55)/SystemInformation.MouseWheelScrollDelta/ItemHeight;
                int rows=(int)wheelRemainder;
                if(rows==0)return;
                wheelRemainder-=rows;
                int before=TopIndex;SetScrollOffset(before+rows);
                if(TopIndex==before)wheelRemainder=0;
            }
            internal event EventHandler ViewportChanged;
            internal int ScrollPageSize => Math.Max(1,ClientSize.Height/Math.Max(1,ItemHeight));
            internal int ScrollMaximum => Math.Max(0,Items.Count-ScrollPageSize);
            internal void SetScrollOffset(int value) { if(Items.Count>0) TopIndex=Math.Clamp(value,0,ScrollMaximum);ViewportChanged?.Invoke(this,EventArgs.Empty); }
            [DllImport("user32.dll")] private static extern bool ShowScrollBar(IntPtr window,int bar,bool show);
            internal event Action<FavoriteFile> OpenItem, UnpinItem;
            internal event Action<FavoriteFile, Point> MenuItem;
            internal FileFavoriteList(TodoWidgetForm owner, bool pins)
            {
                this.owner = owner; this.pins = pins;
                Font = new Font(owner.Font.FontFamily, 8.5f);
                BackColor = Color.FromArgb(38,48,57); ForeColor = ShareXResources.Theme.TextColor;
                BorderStyle = BorderStyle.None; DrawMode = DrawMode.OwnerDrawFixed; IntegralHeight = false;
                int previous = Math.Max(owner.D(27), Font.Height + owner.D(8));
                ItemHeight = Font.Height + Math.Max(owner.D(2), (previous-Font.Height)/2);
                AccessibleName = pins ? "固定的文件和目录" : "常用的文件和目录";
            }
            internal void SetItems(FavoriteFile[] items)
            {
                if (Items.Cast<FavoriteFile>().Select(x => x.Path).SequenceEqual(items.Select(x => x.Path), StringComparer.OrdinalIgnoreCase)) return;
                string selected = (SelectedItem as FavoriteFile)?.Path; int top = TopIndex;
                BeginUpdate(); Items.Clear(); Items.AddRange(items);
                if (selected != null) SelectedIndex = Array.FindIndex(items, x => x.Path.Equals(selected, StringComparison.OrdinalIgnoreCase));
                if (Items.Count > 0) TopIndex = Math.Min(top, Items.Count - 1);
                EndUpdate();
                ViewportChanged?.Invoke(this,EventArgs.Empty);
            }
            protected override void WndProc(ref Message m)
            {
                if(m.Msg==0x20a){ScrollWheel(unchecked((short)((long)m.WParam>>16)));m.Result=IntPtr.Zero;return;}
                base.WndProc(ref m);
                if (!hidingScroll && IsHandleCreated && (m.Msg==0x5 || m.Msg==0xf || m.Msg==0x115 || m.Msg==0x20a || m.Msg==0x185))
                {
                    hidingScroll=true;
                    try { ShowScrollBar(Handle,1,false);ViewportChanged?.Invoke(this,EventArgs.Empty); }
                    finally { hidingScroll=false; }
                }
            }
            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= Items.Count) return;
                var item = (FavoriteFile)Items[e.Index];
                using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? WidgetInputColor : BackColor);
                e.Graphics.FillRectangle(background, e.Bounds);
                int d = owner.D(16), left = e.Bounds.Left + owner.D(6), top = e.Bounds.Top + (e.Bounds.Height - d) / 2;
                FavoriteFileIcon.Draw(e.Graphics,new RectangleF(left,top,d,d),item.Type=="folder",pins,
                    (e.State & DrawItemState.Selected)!=0 ? WidgetInputColor : BackColor);
                int textX = left + d + owner.D(8);
                TextRenderer.DrawText(e.Graphics, item.Name, Font, new Rectangle(textX, e.Bounds.Top, Math.Max(1, e.Bounds.Right - textX - owner.D(4)), e.Bounds.Height), ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                if (Focused) e.DrawFocusRectangle();
            }
            private Rectangle NameHitBounds(int index)
            {
                Rectangle row = GetItemRectangle(index);
                int left = row.Left + owner.D(30);
                Size text = TextRenderer.MeasureText(((FavoriteFile)Items[index]).Name, Font, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                return new Rectangle(left, row.Top + (row.Height - Font.Height) / 2,
                    Math.Min(text.Width, Math.Max(0, row.Right - left - owner.D(4))), Font.Height);
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e); int index = IndexFromPoint(e.Location);
                if (index < 0 || e.Clicks > 1) return;
                var item = (FavoriteFile)Items[index]; SelectedIndex = index;
                if (e.Button == MouseButtons.Right) MenuItem?.Invoke(item, e.Location);
                else if (e.Button == MouseButtons.Left) {
                                        Rectangle row = GetItemRectangle(index);
                    int size = owner.D(16);
                    Rectangle icon = new Rectangle(row.Left + owner.D(6), row.Top + (row.Height - size) / 2, size, size);
                    if (pins && icon.Contains(e.Location)) UnpinItem?.Invoke(item);
                    else if (NameHitBounds(index).Contains(e.Location)) OpenItem?.Invoke(item);
                }
            }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (SelectedItem is FavoriteFile item) {
                    if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; OpenItem?.Invoke(item); return; }
                    if (e.KeyCode == Keys.Apps || e.Shift && e.KeyCode == Keys.F10) { e.Handled = true; MenuItem?.Invoke(item, new Point(owner.D(20), GetItemRectangle(SelectedIndex).Bottom)); return; }
                }
                base.OnKeyDown(e);
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e); int index = IndexFromPoint(e.Location);
                string text = index >= 0 ? ((FavoriteFile)Items[index]).Path : null;
                if (text != hoverPath) { hoverPath = text; tip.SetToolTip(this, text); }
            }
            protected override void Dispose(bool disposing) { if (disposing) { tip.Dispose(); Font.Dispose(); } base.Dispose(disposing); }
        }
    }
}
