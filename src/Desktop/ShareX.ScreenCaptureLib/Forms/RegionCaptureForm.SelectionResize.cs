using System.Drawing;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    public sealed partial class RegionCaptureForm
    {
        private Cursor resizeHorizontal, resizeVertical, resizeDownDiagonal, resizeUpDiagonal;
        private Pen selectionBorder, selectionDashes, selectionHover;

        private void CreateSelectionAffordances()
        {
            resizeHorizontal = SelectionCursor.Create(DeviceDpi, 0);
            resizeVertical = SelectionCursor.Create(DeviceDpi, 90);
            resizeDownDiagonal = SelectionCursor.Create(DeviceDpi, 45);
            resizeUpDiagonal = SelectionCursor.Create(DeviceDpi, -45);
            float width = System.Math.Max(1f, DeviceDpi / 96f);
            selectionBorder = new Pen(Color.Black, width);
            selectionDashes = new Pen(Color.White, width) { DashPattern = new float[] { 4, 4 } };
            selectionHover = new Pen(Color.FromArgb(255, 140, 55), width);
        }

        internal void SetSelectionResizeCursor(NodePosition position)
        {
            Cursor cursor = position switch
            {
                NodePosition.Top or NodePosition.Bottom => resizeVertical,
                NodePosition.Left or NodePosition.Right => resizeHorizontal,
                NodePosition.TopLeft or NodePosition.BottomRight => resizeDownDiagonal,
                _ => resizeUpDiagonal
            };
            if (Cursor != cursor) Cursor = cursor;
        }

        private void DisposeSelectionAffordances()
        {
            SelectionCursor.Dispose(resizeHorizontal); SelectionCursor.Dispose(resizeVertical);
            SelectionCursor.Dispose(resizeDownDiagonal); SelectionCursor.Dispose(resizeUpDiagonal);
            selectionBorder?.Dispose(); selectionDashes?.Dispose(); selectionHover?.Dispose();
        }
    }
}
