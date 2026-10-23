using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        private BlurStrengthSlider blurStrengthSlider;
        private ToolStripControlHost blurStrengthHost;
        private bool blurStrengthChanged;
        private BlurEffectShape blurStrengthEditedShape;
        private ImageEditorMemento blurStrengthSnapshot;
        private int blurStrengthOriginal;

        private void CreateBlurStrengthTools()
        {
            blurStrengthSlider = new BlurStrengthSlider();
            blurStrengthSlider.ValueChanged += PreviewBlurStrength;
            blurStrengthSlider.EditCompleted += CommitBlurStrength;
            blurStrengthHost = new ToolStripControlHost(blurStrengthSlider)
            {
                AutoSize = false, Size = blurStrengthSlider.Size, Margin = Padding.Empty,
                Padding = Padding.Empty, Available = false, ToolTipText = "模糊强度"
            };
        }

        private void UpdateBlurStrengthTools()
        {
            if (blurStrengthHost == null) return;
            if (blurStrengthChanged && CurrentShape != blurStrengthEditedShape) CommitBlurStrength();
            blurStrengthHost.Available = CurrentShapeTool == ShapeType.EffectBlur;
            if (blurStrengthHost.Available)
                blurStrengthSlider.Value = CurrentShape is BlurEffectShape blur ? blur.BlurRadius : AnnotationOptions.BlurRadius;
        }

        private void PreviewBlurStrength(int value)
        {
            if (CurrentShapeTool != ShapeType.EffectBlur) return;
            value = BlurStrengthPresets.Normalize(value);
            var blur = CurrentShape as BlurEffectShape;
            if ((blur?.BlurRadius ?? AnnotationOptions.BlurRadius) == value) return;
            if (blurStrengthChanged && blur != blurStrengthEditedShape) CommitBlurStrength();
            if (!blurStrengthChanged && blur != null)
            {
                blurStrengthOriginal = blur.BlurRadius;
                blurStrengthSnapshot = history.CaptureShapes();
            }
            blurStrengthChanged = true;
            blurStrengthEditedShape = blur;
            AnnotationOptions.BlurRadius = value;
            if (blur != null)
            {
                blur.BlurRadius = value;
                blur.OnMoved();
            }
            Form.Invalidate();
        }

        private void CommitBlurStrength()
        {
            if (!blurStrengthChanged) return;
            blurStrengthChanged = false;
            var blur = blurStrengthEditedShape;
            blurStrengthEditedShape = null;
            history.CompleteShapes(ref blurStrengthSnapshot,
                blur != null && Shapes.Contains(blur) && blur.BlurRadius != blurStrengthOriginal);
            if (blur != null && Shapes.Contains(blur))
            {
                if (CurrentShape == blur) blur.OnConfigSave();
                OnImageModified();
            }
            if (menuForm != null && !menuForm.IsDisposed && !Form.IsDisposed) UpdateMenu();
        }

        private sealed class BlurStrengthSlider : Control
        {
            internal event Action<int> ValueChanged;
            internal event Action EditCompleted;
            private int value = 23;
            private bool dragging;
            private readonly Font captionFont = new Font("Microsoft YaHei UI", 9);
            internal int Value
            {
                get => value;
                set
                {
                    this.value = BlurStrengthPresets.Normalize(value);
                    AccessibleDescription = BlurStrengthPresets.Label(this.value);
                    Invalidate();
                }
            }
            internal BlurStrengthSlider()
            {
                Name = "captureBlurStrengthSlider"; Size = new Size(S(96), S(24));
                BackColor = CaptureToolbarRenderer.Surface; ForeColor = Color.FromArgb(224, 233, 240);
                Font = captionFont; TabStop = DoubleBuffered = true;
                AccessibleName = "模糊强度"; AccessibleRole = AccessibleRole.Slider;
                AccessibleDescription = BlurStrengthPresets.Label(value);
            }
            private int S(int n) => (int)Math.Round(n * DeviceDpi / 96d);
            private int LeftEnd => S(10);
            private int RightEnd => Math.Max(LeftEnd + 2, Width - S(28));
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
                TextRenderer.DrawText(g, BlurStrengthPresets.Label(value), Font, new Rectangle(Width - S(23), 0, S(23), Height),
                    ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                int y = Height / 2, x = LeftEnd + (int)Math.Round((RightEnd - LeftEnd) * BlurStrengthPresets.Index(value) / 2d);
                using var rail = new Pen(Color.FromArgb(79, 95, 108), S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var accent = new Pen(Color.FromArgb(255, 139, 67), S(3));
                using var ticks = new Pen(Color.FromArgb(101, 116, 127), Math.Max(1, S(1)));
                using var thumb = new SolidBrush(Color.FromArgb(255, 139, 67));
                g.DrawLine(rail, LeftEnd, y, RightEnd, y); g.DrawLine(accent, LeftEnd, y, x, y);
                for (int i = 0; i < 3; i++)
                {
                    int tick = LeftEnd + (RightEnd - LeftEnd) * i / 2;
                    g.DrawEllipse(ticks, tick - S(1), y - S(1), S(2), S(2));
                }
                g.FillEllipse(thumb, x - S(6), y - S(6), S(12), S(12));
            }
            private void Change(int next)
            {
                next = BlurStrengthPresets.Normalize(next);
                if (value == next) return;
                Value = next; ValueChanged?.Invoke(value);
            }
            private void ChangeAt(int x) => Change(BlurStrengthPresets.FromIndex((int)Math.Round(Math.Clamp((x - LeftEnd) / (double)(RightEnd - LeftEnd), 0, 1) * 2)));
            private void ChangeKey(Keys key) => Change(key == Keys.Home ? 3 : key == Keys.End ? 23 : BlurStrengthPresets.FromIndex(BlurStrengthPresets.Index(value) + (key == Keys.Left ? -1 : 1)));
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
                Focus(); dragging = Capture = true; ChangeAt(e.X);
            }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) ChangeAt(e.X); }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e); if (!dragging || e.Button != MouseButtons.Left) return;
                dragging = Capture = false; EditCompleted?.Invoke();
            }
            protected override void OnMouseCaptureChanged(EventArgs e)
            {
                base.OnMouseCaptureChanged(e); if (dragging && !Capture) { dragging = false; EditCompleted?.Invoke(); }
            }
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End)
                { ChangeKey(keyData); EditCompleted?.Invoke(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyData is Keys.Left or Keys.Right or Keys.Home or Keys.End)
                { ChangeKey(e.KeyData); e.Handled = e.SuppressKeyPress = true; }
            }
            protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); EditCompleted?.Invoke(); }
            protected override void OnLostFocus(EventArgs e)
            {
                dragging = Capture = false; base.OnLostFocus(e); EditCompleted?.Invoke();
            }
            protected override void OnMouseWheel(MouseEventArgs e) { if (e is HandledMouseEventArgs handled) handled.Handled = true; }
            protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) captionFont.Dispose(); }
        }
    }
}
