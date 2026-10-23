using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        internal PointF? SelectionPointerPosition { get; private set; }
        internal bool UsesSelectionResize => !Form.IsEditorMode && CurrentShape is RectangleRegionShape &&
            IsCurrentShapeTypeRegion && NodesVisible && IsCurrentShapeValid && !IsCreating && !IsMoving && !IsPanning;

        // Canvas coordinates; contract hit areas on tiny selections to leave a movable centre.
        internal static NodePosition? HitSelectionBorder(RectangleF rect, PointF point, float scale)
        {
            if (rect.Width <= 1 || rect.Height <= 1) return null;
            float left = rect.Left, right = rect.Right - 1, top = rect.Top, bottom = rect.Bottom - 1;
            float corner = Math.Min(10 * scale, Math.Min(rect.Width, rect.Height) / 3);
            NodePosition? result = null; float nearest = float.MaxValue;
            TestCorner(NodePosition.TopLeft, left, top); TestCorner(NodePosition.TopRight, right, top);
            TestCorner(NodePosition.BottomRight, right, bottom); TestCorner(NodePosition.BottomLeft, left, bottom);
            if (result.HasValue) return result;
            float horizontal = Math.Min(6 * scale, rect.Width / 4), vertical = Math.Min(6 * scale, rect.Height / 4);
            if (point.X >= left && point.X <= right)
            {
                TestEdge(NodePosition.Top, Math.Abs(point.Y - top), vertical);
                TestEdge(NodePosition.Bottom, Math.Abs(point.Y - bottom), vertical);
            }
            if (point.Y >= top && point.Y <= bottom)
            {
                TestEdge(NodePosition.Left, Math.Abs(point.X - left), horizontal);
                TestEdge(NodePosition.Right, Math.Abs(point.X - right), horizontal);
            }
            return result;

            void TestCorner(NodePosition position, float x, float y)
            {
                float dx = Math.Abs(point.X - x), dy = Math.Abs(point.Y - y), distance = dx * dx + dy * dy;
                if (dx <= corner && dy <= corner && distance < nearest) { nearest = distance; result = position; }
            }
            void TestEdge(NodePosition position, float distance, float limit)
            {
                if (distance <= limit && distance < nearest) { nearest = distance; result = position; }
            }
        }

        private ResizeNode SelectionNodeAt(PointF point)
        {
            if (!UsesSelectionResize || IsCtrlModifier) return null;
            Point client = new Point((int)(point.X * Form.ZoomFactor), (int)(point.Y * Form.ZoomFactor));
            if (Form.GetChildAtPoint(client) != null ||
                (menuForm != null && menuForm.Visible && menuForm.Bounds.Contains(Form.PointToScreen(client)))) return null;
            NodePosition? hit = HitSelectionBorder(CurrentRectangle, point, Form.DeviceDpi / 96f / Form.ZoomFactor);
            return hit.HasValue ? ResizeNodes[(int)hit.Value] : null;
        }

        private void StartSelectionResize(ResizeNode node, PointF point)
        {
            foreach (ResizeNode other in ResizeNodes) other.IsCursorHover = other == node;
            history.CreateShapesMemento();
            node.SelectionDragAnchor = point;
            node.SelectionAnchorPending = true;
            node.OnMouseDown(Point.Round(point));
            IsResizing = true;
            SelectionPointerPosition = point;
            try { CurrentShape.OnNodeUpdate(); }
            finally { SelectionPointerPosition = null; }
        }

        private bool FinishSelectionResize(PointF point)
        {
            ResizeNode node = ResizeNodes.FirstOrDefault(item => item.IsDragging && item.SelectionDragAnchor.HasValue);
            if (node == null)
            {
                if (UsesSelectionResize) foreach (ResizeNode other in ResizeNodes) other.IsCursorHover = false;
                return false;
            }
            SelectionPointerPosition = point;
            try { CurrentShape?.OnNodeUpdate(); }
            finally { SelectionPointerPosition = null; }
            node.OnMouseUp(Point.Round(point));
            IsResizing = false;
            CurrentShape?.OnResized();
            CurrentShape?.OnNodePositionUpdate();
            return true;
        }

        private bool TryStartSelectionResize(PointF point)
        {
            if ((Control.ModifierKeys & Keys.Control) != 0) return false;
            ResizeNode node = SelectionNodeAt(point);
            if (node == null) return false;
            StartSelectionResize(node, point);
            return true;
        }

        private void SetObjectCursor(ImageEditorControl control, bool dragging)
        {
            if (!Form.IsEditorMode && control is ResizeNode node && CurrentShape != null &&
                CurrentShape is not LineDrawingShape && CurrentShape is not FreehandRegionShape)
            {
                int index = Array.IndexOf(ResizeNodes, node);
                // Only the eight bounding-box handles resize in fixed directions.
                // Extra handles and line/path control points keep their move cursor.
                if (index >= 0 && index < (int)NodePosition.Extra)
                {
                    Form.SetSelectionResizeCursor((NodePosition)index);
                    return;
                }
            }
            Form.SetHandCursor(dragging);
        }

        private bool UpdateSelectionObjects(ImageEditorControl[] objects, PointF point)
        {
            if (!UsesSelectionResize || !objects.Any(item => item is ResizeNode)) return false;
            ResizeNode hit = SelectionNodeAt(point);
            foreach (ImageEditorControl item in objects) item.IsCursorHover = item == hit;
            if (hit != null)
            {
                Form.SetSelectionResizeCursor((NodePosition)Array.IndexOf(ResizeNodes, hit));
                if (InputManager.IsMousePressed(MouseButtons.Left)) StartSelectionResize(hit, point);
            }
            return true;
        }
    }
}
