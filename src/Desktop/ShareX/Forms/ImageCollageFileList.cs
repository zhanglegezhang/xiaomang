using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    // Virtual rows keep model mutations out of ListBox's synchronous per-item
    // UIA-provider removal. ListView retains its standard accessibility support.
    internal sealed class ImageCollageFileList : ListView
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool ShowScrollBar(IntPtr handle, int bar, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool show);
        internal sealed class ItemCollection : Collection<object>
        {
            private readonly ImageCollageFileList owner;
            internal ItemCollection(ImageCollageFileList owner) => this.owner = owner;
            protected override void InsertItem(int index, object item)
            {
                base.InsertItem(index, item);
                if (owner.selectedIndex >= index) owner.selectedIndex++;
                owner.ItemsChanged();
            }
            protected override void RemoveItem(int index)
            {
                base.RemoveItem(index);
                if (owner.selectedIndex == index) owner.selectedIndex = -1;
                else if (owner.selectedIndex > index) owner.selectedIndex--;
                owner.ItemsChanged();
            }
            protected override void ClearItems() { base.ClearItems(); owner.selectedIndex = -1; owner.ItemsChanged(); }
        }

        internal new ItemCollection Items { get; }
        internal new event DrawItemEventHandler DrawItem;
        private int selectedIndex = -1, itemHeight = 86, updateDepth;
        private bool synchronizing, fittingColumn;
        private ImageList rowHeightImages;

        internal ImageCollageFileList()
        {
            Items = new ItemCollection(this);
            View = View.Details; HeaderStyle = ColumnHeaderStyle.None;
            FullRowSelect = true; HideSelection = false; MultiSelect = false;
            VirtualMode = true; OwnerDraw = true; DoubleBuffered = true;
            AccessibleName = "拼图素材";
            Columns.Add("图片", 240);
            RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem(e.ItemIndex < Items.Count ? Items[e.ItemIndex].ToString() : "");
            base.DrawSubItem += (_, e) =>
            {
                if (e.ItemIndex >= Items.Count) return;
                DrawItemState state = e.ItemIndex == SelectedIndex ? DrawItemState.Selected : DrawItemState.None;
                if (Focused && e.ItemIndex == SelectedIndex) state |= DrawItemState.Focus;
                DrawItem?.Invoke(this, new DrawItemEventArgs(e.Graphics, Font, e.Bounds, e.ItemIndex, state, ForeColor, BackColor));
            };
            SetRowHeight();
        }
        internal int ItemHeight { get => itemHeight; set { itemHeight = Math.Max(16, value); SetRowHeight(); } }
        private void SetRowHeight()
        {
            var replacement = new ImageList { ImageSize = new Size(1, Math.Min(256, Math.Max(1, itemHeight - 1))), ColorDepth = ColorDepth.Depth32Bit };
            var old = rowHeightImages; rowHeightImages = replacement;
            SmallImageList = replacement; old?.Dispose();
        }
        internal int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException(nameof(value));
                selectedIndex = value;
                if (updateDepth == 0) SynchronizeSelection();
            }
        }
        internal object SelectedItem { get => selectedIndex < 0 ? null : Items[selectedIndex]; set => SelectedIndex = value == null ? -1 : Items.IndexOf(value); }
        internal int IndexFromPoint(Point point) => GetItemAt(point.X, point.Y)?.Index ?? -1;
        internal new void BeginUpdate() { if (updateDepth++ == 0) base.BeginUpdate(); }
        internal new void EndUpdate()
        {
            if (updateDepth == 0) return;
            if (--updateDepth == 0)
            {
                try { ApplyItems(); }
                finally { base.EndUpdate(); }
            }
        }
        private void ItemsChanged() { if (updateDepth == 0) ApplyItems(); }
        private void ApplyItems()
        {
            synchronizing = true;
            try { VirtualListSize = Items.Count; }
            finally { synchronizing = false; }
            FitColumn(); SynchronizeSelection(); Invalidate();
        }
        private void SynchronizeSelection()
        {
            synchronizing = true;
            try
            {
                SelectedIndices.Clear();
                if (selectedIndex >= 0 && selectedIndex < VirtualListSize)
                {
                    base.Items[selectedIndex].Selected = true;
                    base.Items[selectedIndex].Focused = true;
                    EnsureVisible(selectedIndex);
                }
            }
            finally { synchronizing = false; }
            base.OnSelectedIndexChanged(EventArgs.Empty);
        }
        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            if (synchronizing) return;
            selectedIndex = SelectedIndices.Count > 0 ? SelectedIndices[0] : -1;
            if (updateDepth == 0) base.OnSelectedIndexChanged(e);
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitColumn();
            if (IsHandleCreated && selectedIndex >= 0 && selectedIndex < VirtualListSize) EnsureVisible(selectedIndex);
        }
        protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
        {
            base.OnColumnWidthChanged(e); FitColumn();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); FitColumn();
            if (IsHandleCreated && selectedIndex >= 0 && selectedIndex < VirtualListSize) EnsureVisible(selectedIndex);
        }
        private void FitColumn()
        {
            if (fittingColumn || Columns.Count == 0 || !IsHandleCreated) return;
            fittingColumn = true;
            try
            {
                int width = Math.Max(1, ClientSize.Width - 4);
                if (Columns[0].Width != width) Columns[0].Width = width;
                // A virtual ListView can retain an obsolete horizontal extent
                // after DPI/layout changes. The single column always fits.
                ShowScrollBar(Handle, 0, false);
            }
            finally { fittingColumn = false; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { SmallImageList = null; rowHeightImages?.Dispose(); rowHeightImages = null; }
            base.Dispose(disposing);
        }
    }
}
