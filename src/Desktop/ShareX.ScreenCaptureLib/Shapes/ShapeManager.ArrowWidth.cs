using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        internal static readonly int[] ArrowWidths = { 1, 4, 8, 12, 20 };
        private ArrowWidthSlider arrowWidthSlider;
        private ToolStripControlHost arrowWidthHost;
        private bool updatingArrowWidthTools, arrowWidthChanged;
        private ArrowDrawingShape arrowWidthEditedShape;
        private ImageEditorMemento arrowWidthSnapshot;
        private int arrowWidthOriginal;

        private void CreateArrowWidthTools()
        {
            arrowWidthSlider = new ArrowWidthSlider();
            arrowWidthSlider.ValueChanged += PreviewArrowWidth;
            arrowWidthSlider.EditCompleted += CommitArrowWidth;
            arrowWidthHost = new ToolStripControlHost(arrowWidthSlider)
            {
                AutoSize = false, Size = arrowWidthSlider.Size, Margin = Padding.Empty,
                Padding = Padding.Empty, Available = false, ToolTipText = "粗细"
            };
        }

        private void UpdateArrowWidthTools()
        {
            if (arrowWidthHost == null) return;
            arrowWidthHost.Available = CurrentShapeTool == ShapeType.DrawingArrow;
            if (!arrowWidthHost.Available) return;
            int width = CurrentShape is ArrowDrawingShape arrow ? arrow.BorderSize :
                AnnotationOptions.ArrowBorderSize ?? AnnotationOptions.BorderSize;
            // Reading a legacy width never snaps or changes the actual drawing.
            arrowWidthSlider.Index = Enumerable.Range(0, ArrowWidths.Length)
                .OrderBy(i => Math.Abs(ArrowWidths[i] - (double)width)).First();
        }

        private void PreviewArrowWidth(int index)
        {
            if (updatingArrowWidthTools || CurrentShapeTool != ShapeType.DrawingArrow) return;
            int width = ArrowWidths[Math.Clamp(index, 0, ArrowWidths.Length - 1)];
            var arrow = CurrentShape as ArrowDrawingShape;
            int currentWidth = arrow?.BorderSize ?? AnnotationOptions.ArrowBorderSize ?? AnnotationOptions.BorderSize;
            if (currentWidth == width) return;
            if (arrowWidthChanged && arrow != arrowWidthEditedShape) CommitArrowWidth();
            if (!arrowWidthChanged && arrow != null)
            {
                arrowWidthOriginal = arrow.BorderSize;
                arrowWidthSnapshot = history.CaptureShapes();
            }
            arrowWidthChanged = true;
            arrowWidthEditedShape = arrow;
            AnnotationOptions.ArrowBorderSize = width;
            if (arrow != null) arrow.BorderSize = width;
            // Do not rebuild the toolbar or reload unrelated shape options during a drag.
            Form.Invalidate();
        }

        private void CommitArrowWidth()
        {
            if (!arrowWidthChanged) return;
            arrowWidthChanged = false;
            var arrow = arrowWidthEditedShape;
            arrowWidthEditedShape = null;
            history.CompleteShapes(ref arrowWidthSnapshot,
                arrow != null && Shapes.Contains(arrow) && arrow.BorderSize != arrowWidthOriginal);
            if (arrow != null && Shapes.Contains(arrow))
            {
                if (CurrentShape == arrow) arrow.OnConfigSave();
                OnImageModified();
            }
            if (menuForm != null && !menuForm.IsDisposed && !Form.IsDisposed) UpdateMenu();
        }

        private sealed class ArrowWidthSlider : Control
        {
            internal event Action<int> ValueChanged;
            internal event Action EditCompleted;
            private int index = 1;
            private bool dragging;
            private readonly Font captionFont = new Font("Microsoft YaHei UI", 9);
            internal int Index
            {
                get => index;
                set
                {
                    index = Math.Clamp(value, 0, ArrowWidths.Length - 1);
                    AccessibleDescription = $"第 {index + 1} 档，共 5 档";
                    Invalidate();
                }
            }
            internal ArrowWidthSlider()
            {
                Name = "captureArrowWidthSlider";
                Size = new Size(S(192), S(24));
                BackColor = CaptureToolbarRenderer.Surface;
                ForeColor = Color.FromArgb(224, 233, 240);
                Font = captionFont;
                TabStop = DoubleBuffered = true;
                AccessibleName = "箭头粗细，五档";
                AccessibleRole = AccessibleRole.Slider;
                Index = 1;
            }
            private int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);
            private int LeftEnd => S(10);
            private int RightEnd => Math.Max(LeftEnd + 4, Width - S(58));
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                TextRenderer.DrawText(g, (index + 1).ToString(), Font,
                    new Rectangle(Width - S(53), 0, S(51), Height), ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                int y = Height / 2, x = LeftEnd + (RightEnd - LeftEnd) * index / 4;
                using var rail = new Pen(Color.FromArgb(79, 95, 108), S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var accent = new Pen(Color.FromArgb(255, 139, 67), S(3));
                using var ticks = new Pen(Color.FromArgb(101, 116, 127), Math.Max(1, S(1)));
                using var thumb = new SolidBrush(Color.FromArgb(255, 139, 67));
                g.DrawLine(rail, LeftEnd, y, RightEnd, y);
                g.DrawLine(accent, LeftEnd, y, x, y);
                for (int i = 0; i < 5; i++)
                {
                    int tick = LeftEnd + (RightEnd - LeftEnd) * i / 4;
                    g.DrawEllipse(ticks, tick - S(1), y - S(1), S(2), S(2));
                }
                g.FillEllipse(thumb, x - S(6), y - S(6), S(12), S(12));
            }
            private void Change(int value)
            {
                value = Math.Clamp(value, 0, 4);
                if (index == value) return;
                Index = value;
                ValueChanged?.Invoke(index);
            }
            private void ChangeAt(int x) => Change((int)Math.Round((x - LeftEnd) * 4d / (RightEnd - LeftEnd)));
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                Focus(); dragging = Capture = true; ChangeAt(e.X);
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e); if (dragging) ChangeAt(e.X);
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (!dragging || e.Button != MouseButtons.Left) return;
                dragging = Capture = false;
                EditCompleted?.Invoke();
            }
            protected override void OnMouseCaptureChanged(EventArgs e)
            {
                base.OnMouseCaptureChanged(e);
                if (dragging && !Capture) { dragging = false; EditCompleted?.Invoke(); }
            }
            // Consume adjustment keys here so the capture form cannot also move the selected arrow.
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End)
                {
                    ChangeKey(keyData); EditCompleted?.Invoke(); return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            private void ChangeKey(Keys key) => Change(key == Keys.Home ? 0 : key == Keys.End ? 4 : index + (key == Keys.Left ? -1 : 1));
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyData is Keys.Left or Keys.Right or Keys.Home or Keys.End)
                { ChangeKey(e.KeyData); e.Handled = e.SuppressKeyPress = true; }
            }
            protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); EditCompleted?.Invoke(); }
            protected override void OnLostFocus(EventArgs e)
            {
                // A pending native mouse move must not start another change after focus leaves.
                dragging = Capture = false;
                base.OnLostFocus(e);
                EditCompleted?.Invoke();
            }
            protected override void OnMouseWheel(MouseEventArgs e) { if (e is HandledMouseEventArgs handled) handled.Handled = true; }
            protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) captionFont.Dispose(); }
        }
    }
}
