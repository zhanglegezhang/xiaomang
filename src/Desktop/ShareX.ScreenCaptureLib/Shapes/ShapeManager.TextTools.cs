using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        internal static readonly int[] TextSizePoints = { 12, 18, 24, 30, 36, 42, 48, 54, 60, 66 };
        private TextSizeSlider inlineSizeSlider;
        private ToolStripControlHost inlineSizeHost;
        private bool updatingInlineTools, inlineSizeChanged;
        private ImageEditorMemento inlineSizeSnapshot;
        private TextDrawingShape inlineSizeShape;
        private float inlineSizeOriginal;
        private bool inlineSizeWasPixels;
        private RectangleF inlineSizeBounds;
        private TextDrawingOptions CurrentTextOptions => CurrentShape is TextDrawingShape selected ? selected.TextOptions :
            CurrentShapeTool == ShapeType.DrawingTextOutline ? AnnotationOptions.TextOutlineOptions : AnnotationOptions.TextOptions;
        internal static int TextSizePixels(int points) => (int)Math.Round(points * 96d / 72d);
        internal static void ApplyInlineTextDefaults(TextDrawingOptions options)
        {
            if (options.InlineDefaultsVersion >= 3) return;
            if (options.InlineDefaultsVersion < 2 || options.SizeInPixels && options.Size == TextSizePixels(20))
            { options.Size = TextSizePixels(28); options.SizeInPixels = true; }
            options.Font = "Microsoft YaHei";
            options.InlineDefaultsApplied = options.PlainInlineRendering = true; options.InlineDefaultsVersion = 3;
            options.Bold = options.Italic = options.Underline = options.Gradient = false;
            options.AlignmentHorizontal = StringAlignment.Near; options.AlignmentVertical = StringAlignment.Center;
        }
        private void PreviewInlineTextSize(int index)
        {
            if (updatingInlineTools) return;
            int pixels = TextSizePixels(TextSizePoints[index]); var options = CurrentTextOptions;
            if (options.SizeInPixels && options.Size == pixels) return;
            if (CurrentShape is TextDrawingShape shape)
            {
                if (!inlineSizeChanged)
                {
                    inlineSizeSnapshot = history.CaptureShapes();
                    inlineSizeShape = shape;
                    inlineSizeOriginal = options.Size;
                    inlineSizeWasPixels = options.SizeInPixels;
                    inlineSizeBounds = shape.Rectangle;
                }
                options.Size = pixels; options.SizeInPixels = options.InlineDefaultsApplied = true;
                RectangleF bounds = shape.Rectangle; shape.AutoSize(false);
                shape.Rectangle = new RectangleF(bounds.Location, new SizeF(Math.Max(bounds.Width, shape.Rectangle.Width), Math.Max(bounds.Height, shape.Rectangle.Height)));
                Form.Invalidate();
            }
            else { ApplyInlineTextDefaults(options); options.Size = pixels; }
            inlineSizeChanged = true;
        }
        private void CommitInlineTextSize()
        {
            if (!inlineSizeChanged) return; inlineSizeChanged = false;
            history.CompleteShapes(ref inlineSizeSnapshot, inlineSizeShape != null && Shapes.Contains(inlineSizeShape) &&
                (inlineSizeShape.TextOptions.Size != inlineSizeOriginal || inlineSizeShape.TextOptions.SizeInPixels != inlineSizeWasPixels ||
                 inlineSizeShape.Rectangle != inlineSizeBounds));
            inlineSizeShape = null;
            if (CurrentShape is TextDrawingShape shape) { shape.OnConfigSave(); OnImageModified(); }
            UpdateMenu();
        }
        private void CreateInlineTextTools()
        {
            inlineSizeSlider = new TextSizeSlider();
            inlineSizeSlider.MouseDown += (_, _) => FinishInlineText(true);
            inlineSizeSlider.Enter += (_, _) => FinishInlineText(true);
            inlineSizeSlider.ValueChanged += PreviewInlineTextSize; inlineSizeSlider.EditCompleted += CommitInlineTextSize;
            inlineSizeHost = new ToolStripControlHost(inlineSizeSlider) { AutoSize = false, Size = inlineSizeSlider.Size, Margin = Padding.Empty, Padding = Padding.Empty };
            CreateStyleToolbar();
        }
        private void UpdateInlineTextTools(ShapeType type)
        {
            if (inlineSizeHost == null) return;
            inlineSizeHost.Available = type is ShapeType.DrawingTextOutline or ShapeType.DrawingTextBackground or ShapeType.DrawingSpeechBalloon;
            if (!inlineSizeHost.Available) return;
            updatingInlineTools = true;
            try
            {
                var options = CurrentTextOptions;
                double points = options.InlineDefaultsVersion >= 3 || CurrentShape is TextDrawingShape ? options.SizeInPixels ? options.Size * 72d / 96d : options.Size : 28;
                inlineSizeSlider.Index = Enumerable.Range(0, TextSizePoints.Length).OrderBy(i => Math.Abs(TextSizePoints[i] - points)).First();
            }
            finally { updatingInlineTools = false; }
        }
        private sealed class TextSizeSlider : Control
        {
            internal static readonly Color Background = CaptureToolbarRenderer.Surface;
            internal event Action<int> ValueChanged;
            internal event Action EditCompleted;
            private int index = 4; private bool dragging;
            private readonly Font captionFont = new Font("Microsoft YaHei UI", 9);
            internal int Index { get => index; set { index = Math.Clamp(value, 0, 9); Invalidate(); } }
            internal TextSizeSlider()
            {
                Name = "captureTextSizeSlider"; Size = new Size(S(192), S(24)); BackColor = Background; ForeColor = Color.FromArgb(224, 233, 240);
                Font = captionFont; TabStop = true; DoubleBuffered = true; AccessibleName = "文字大小，十档"; AccessibleRole = AccessibleRole.Slider;
            }
            private int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);
            private int LeftEnd => S(10);
            private int RightEnd => Math.Max(LeftEnd + 9, Width - S(58));
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(Background); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                string caption = TextSizePoints[index].ToString();
                TextRenderer.DrawText(g, caption, Font, new Rectangle(Width - S(53), 0, S(51), Height), ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                int y = Height / 2, x = LeftEnd + (RightEnd - LeftEnd) * index / 9;
                using var rail = new Pen(Color.FromArgb(79, 95, 108), S(3)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                using var accent = new Pen(Color.FromArgb(255, 139, 67), S(3));
                g.DrawLine(rail, LeftEnd, y, RightEnd, y); g.DrawLine(accent, LeftEnd, y, x, y);
                using var ticks = new Pen(Color.FromArgb(101, 116, 127), Math.Max(1, S(1)));
                for (int i = 0; i < 10; i++) { int tick = LeftEnd + (RightEnd - LeftEnd) * i / 9; g.DrawEllipse(ticks, tick - S(1), y - S(1), S(2), S(2)); }
                using var thumb = new SolidBrush(Color.FromArgb(255, 139, 67)); g.FillEllipse(thumb, x - S(6), y - S(6), S(12), S(12));
                AccessibleDescription = caption;
            }
            private void Change(int value) { value = Math.Clamp(value, 0, 9); if (index == value) return; index = value; ValueChanged?.Invoke(index); Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus(); dragging = Capture = true; Change((int)Math.Round((e.X - LeftEnd) * 9d / (RightEnd - LeftEnd))); }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) Change((int)Math.Round((e.X - LeftEnd) * 9d / (RightEnd - LeftEnd))); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (!dragging) return; dragging = Capture = false; EditCompleted?.Invoke(); }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (dragging && !Capture) { dragging = false; EditCompleted?.Invoke(); } }
            protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
            protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End) { Change(e.KeyCode == Keys.Home ? 0 : e.KeyCode == Keys.End ? 9 : index + (e.KeyCode == Keys.Left ? -1 : 1)); e.Handled = e.SuppressKeyPress = true; } }
            protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); EditCompleted?.Invoke(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); EditCompleted?.Invoke(); }
            protected override void OnMouseWheel(MouseEventArgs e) { if (e is HandledMouseEventArgs handled) handled.Handled = true; }
            protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) captionFont.Dispose(); }
        }
    }
}
