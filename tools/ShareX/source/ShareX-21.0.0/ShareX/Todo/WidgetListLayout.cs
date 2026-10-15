using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ShareX
{
    // Shared by Todo, Clipboard and Favorites. Item-specific measurement and editing
    // stay with each module; only ordered row geometry belongs here.
    internal static class WidgetListLayout
    {
        internal static void Calculate(int count, int gap, Func<int, int> measure,
            out int[] offsets, out int[] heights, out int totalHeight)
        {
            offsets = new int[count]; heights = new int[count];
            int y = 0;
            for (int i = 0; i < count; i++)
            {
                int height = measure(i);
                offsets[i] = y; heights[i] = height; y += height + gap;
            }
            totalHeight = Math.Max(0, y - gap);
        }

        internal static void GetVisibleRange(int[] offsets, int[] heights, int scrollY,
            int viewportHeight, int overscan, out int start, out int end)
        {
            int low = 0, high = offsets.Length;
            int top = scrollY - overscan, bottom = scrollY + viewportHeight + overscan;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (offsets[middle] + heights[middle] < top) low = middle + 1;
                else high = middle;
            }
            start = low; high = offsets.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (offsets[middle] <= bottom) low = middle + 1;
                else high = middle;
            }
            end = low;
        }

        internal static void HideUnusedRows(IReadOnlyList<Panel> rows, ISet<Panel> used)
        {
            foreach (Panel row in rows) if (!used.Contains(row)) row.Visible = false;
        }
    }
}
