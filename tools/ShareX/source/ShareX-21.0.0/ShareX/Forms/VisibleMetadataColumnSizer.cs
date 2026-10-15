using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ShareX
{
    /// <summary>Sizes short metadata columns from visible rows while keeping manual adjustments.</summary>
    internal sealed class VisibleMetadataColumnSizer
    {
        private readonly DataGridView grid;
        private readonly (int Index, int MaximumWidth)[] columns;
        private readonly HashSet<int> manuallySized = new HashSet<int>();
        private bool sizing;
        private bool refreshPending;
        private bool fittedOnce;

        internal VisibleMetadataColumnSizer(DataGridView grid, params (int Index, int MaximumWidth)[] columns)
        {
            this.grid = grid;
            this.columns = columns;
            grid.ColumnWidthChanged += (sender, args) =>
            {
                if (!sizing && fittedOnce && IsMetadataColumn(args.Column.Index))
                    manuallySized.Add(args.Column.Index);
            };
            grid.SizeChanged += (sender, args) => RequestFit();
            grid.VisibleChanged += (sender, args) => RequestFit();
            grid.HandleCreated += (sender, args) => RequestFit();
        }

        internal void RequestFit()
        {
            if (refreshPending || grid.IsDisposed || !grid.IsHandleCreated || !grid.Visible) return;
            refreshPending = true;
            grid.BeginInvoke((Action)(() =>
            {
                refreshPending = false;
                if (grid.IsDisposed || !grid.Visible) return;
                FitVisibleRows();
            }));
        }

        private bool IsMetadataColumn(int index)
        {
            foreach (var (columnIndex, _) in columns)
                if (columnIndex == index) return true;
            return false;
        }

        private void FitVisibleRows()
        {
            if (sizing || grid.ClientSize.Width <= 0) return;
            sizing = true;
            try
            {
                foreach ((int index, int maximumWidth) in columns)
                {
                    if (manuallySized.Contains(index)) continue;
                    DataGridViewColumn column = grid.Columns[index];
                    int preferred = column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.DisplayedCells, true);
                    int limit = Math.Max(column.MinimumWidth, (int)Math.Round(maximumWidth * grid.DeviceDpi / 96d));
                    column.Width = Math.Clamp(preferred, column.MinimumWidth, limit);
                }
            }
            finally
            {
                sizing = false;
                fittedOnce = true;
            }
        }
    }
}
