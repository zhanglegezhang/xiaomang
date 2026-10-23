using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        internal static readonly Color[] StyleColors = { Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Purple, Color.Black, Color.White, Color.Transparent };
        private ToolStripEx tsStyle;
        private ToolStripDropDownButton styleBorder, styleFill;
        private ToolStripButton restartSteps;
        private bool styleRowVisible, arrangingStyleToolbar, styleRowAboveMain;
        private Rectangle lastMainOutline, lastStyleOutline;

        private int StyleScale(int value) => (int)Math.Round(value * menuForm.DeviceDpi / 96d);

        private void CreateStyleToolbar()
        {
            // Keep both rows in one owned window, so dragging, hiding and closing
            // retain the capture toolbar's existing lifetime and focus behavior.
            menuForm.AutoScaleMode = AutoScaleMode.None;
            menuForm.AutoSize = false;
            tsMain.MaximumSize = new Size(Math.Max(120, Form.ClientSize.Width), 0);
            tsMain.Items.Remove(tsbBorderColor);
            tsMain.Items.Remove(tsbFillColor);
            menuForm.Disposed += (_, _) => { tsbBorderColor.Dispose(); tsbFillColor.Dispose(); };
            tsStyle = new ToolStripEx
            {
                Name = "captureStyleRow", AutoSize = false, CanOverflow = false, ClickThrough = true,
                Dock = DockStyle.None, GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new CaptureToolbarRenderer(), BackColor = CaptureToolbarRenderer.Surface,
                ForeColor = Color.FromArgb(224, 233, 240), ShowItemToolTips = true
            };
            styleBorder = CreateStyleColorButton(false);
            styleFill = CreateStyleColorButton(true);
            restartSteps = new ToolStripButton
            {
                Name = "restart-steps", Text = "从1开始", ToolTipText = "从1开始",
                DisplayStyle = ToolStripItemDisplayStyle.Image, AutoSize = false, Padding = Padding.Empty,
                Available = false
            };
            restartSteps.Click += (_, _) => RestartStepSequence();
            restartSteps.Disposed += (_, _) => restartSteps.Image?.Dispose();
            CreateArrowWidthTools();
            CreateBlurStrengthTools();
            tsStyle.Items.AddRange(new ToolStripItem[] { styleBorder, styleFill, inlineSizeHost, arrowWidthHost, blurStrengthHost, restartSteps });
            tsStyle.MouseDown += (_, _) => FinishInlineText(true);
            menuForm.Controls.Add(tsStyle);
            tsStyle.Disposed += (_, _) => DisposeStyleIcons();
            _ = new CaptureToolbarTip(tsStyle);
            tsMain.SizeChanged += (_, _) => LayoutStyleToolbar();
            menuForm.SizeChanged += (_, _) => LayoutStyleToolbar();
            menuForm.Shown += (_, _) => LayoutStyleToolbar();
            menuForm.DpiChanged += (_, _) =>
            {
                if (!menuForm.IsHandleCreated) return;
                menuForm.BeginInvoke((Action)(() =>
                {
                    if (menuForm.IsDisposed) return;
                    if (!Form.IsEditorMode) ConfigureCaptureToolbarAppearance();
                    else ConfigureEditorToolbarSurface();
                    ConfigureStyleToolbarAppearance();
                    UpdateMenu();
                }));
            };
            ConfigureStyleToolbarAppearance();
        }

        private ToolStripDropDownButton CreateStyleColorButton(bool fill)
        {
            var button = new ToolStripDropDownButton
            {
                Name = fill ? "style-fill" : "style-border", Text = fill ? "填充颜色" : "边框颜色",
                DisplayStyle = ToolStripItemDisplayStyle.Image, ShowDropDownArrow = true,
                AutoSize = false, Padding = Padding.Empty
            };
            var palette = new StyleColorPalette();
            var host = new ToolStripControlHost(palette) { AutoSize = false, Size = palette.Size, Margin = Padding.Empty, Padding = Padding.Empty };
            palette.SizeChanged += (_, _) => host.Size = palette.Size;
            palette.ColorChosen += color => { button.HideDropDown(); ApplyStyleColor(fill, color); };
            button.DropDown = new ToolStripDropDown
            {
                AutoSize = true, Padding = Padding.Empty, Margin = Padding.Empty,
                LayoutStyle = ToolStripLayoutStyle.Flow, BackColor = CaptureToolbarRenderer.Surface
            };
            button.DropDownItems.Add(host);
            button.DropDown.Padding = Padding.Empty;
            button.DropDown.Renderer = new CaptureToolbarRenderer();
            if (button.DropDown is ToolStripDropDownMenu menu) { menu.ShowImageMargin = false; menu.ShowCheckMargin = false; }
            button.DropDownOpening += (_, _) => { FinishInlineText(true); palette.SelectedColor = GetStyleColor(fill); };
            button.DropDownOpened += (_, _) => palette.Focus();
            // Color icons belong to the bounded cache, released with the style row.
            return button;
        }

        private Color GetStyleColor(bool fill)
        {
            var type = CurrentShapeTool;
            if (type == ShapeType.EffectHighlight) return AnnotationOptions.HighlightColor;
            if (!fill && type == ShapeType.DrawingSpeechBalloon)
                return CurrentShape is SpeechBalloonDrawingShape balloon ? balloon.TextOptions.Color : AnnotationOptions.TextOptions.Color;
            if (type is ShapeType.DrawingTextBackground or ShapeType.DrawingSpeechBalloon)
                return fill ? AnnotationOptions.TextFillColor : AnnotationOptions.TextBorderColor;
            if (!fill && type == ShapeType.DrawingTextOutline) return AnnotationOptions.TextOutlineOptions.Color;
            if (type == ShapeType.DrawingStep) return fill ? AnnotationOptions.StepFillColor : AnnotationOptions.StepBorderColor;
            return fill ? AnnotationOptions.FillColor : AnnotationOptions.BorderColor;
        }

        private void ApplyStyleColor(bool fill, Color color)
        {
            if (fill && CurrentShapeTool == ShapeType.DrawingMagnify) return;
            FinishInlineText(true);
            if (GetStyleColor(fill).ToArgb() == color.ToArgb()) return;
            if (CurrentShape != null) history.CreateShapesMemento();
            var type = CurrentShapeTool;
            bool balloonText = !fill && type == ShapeType.DrawingSpeechBalloon;
            if (type == ShapeType.EffectHighlight) AnnotationOptions.HighlightColor = color;
            else if (balloonText)
            {
                AnnotationOptions.TextOptions = AnnotationOptions.TextOptions.Copy();
                AnnotationOptions.TextOptions.Color = color;
                if (CurrentShape is SpeechBalloonDrawingShape balloon)
                {
                    // Recolor only the text; reloading all annotation options
                    // would also replace this balloon's existing fill and border.
                    balloon.TextOptions = balloon.TextOptions.Copy();
                    balloon.TextOptions.Color = color;
                    balloon.OnMoved();
                }
            }
            else if (type is ShapeType.DrawingTextBackground or ShapeType.DrawingSpeechBalloon)
            {
                if (fill) AnnotationOptions.TextFillColor = color; else AnnotationOptions.TextBorderColor = color;
            }
            else if (!fill && type == ShapeType.DrawingTextOutline) AnnotationOptions.TextOutlineOptions.Color = color;
            else if (type == ShapeType.DrawingStep)
            {
                if (fill) AnnotationOptions.StepFillColor = color; else AnnotationOptions.StepBorderColor = color;
            }
            else if (fill) AnnotationOptions.FillColor = color; else AnnotationOptions.BorderColor = color;
            if (balloonText) Form.Resume(); else UpdateCurrentShape();
            if (CurrentShape != null) { CurrentShape.OnConfigSave(); OnImageModified(); }
            UpdateMenu();
            Form.Invalidate();
        }

        private void ConfigureStyleToolbarAppearance()
        {
            if (tsStyle == null) return;
            tsStyle.BackColor = CaptureToolbarRenderer.Surface;
            inlineSizeHost.BackColor = inlineSizeSlider.BackColor = CaptureToolbarRenderer.Surface;
            arrowWidthHost.BackColor = arrowWidthSlider.BackColor = CaptureToolbarRenderer.Surface;
            blurStrengthHost.BackColor = blurStrengthSlider.BackColor = CaptureToolbarRenderer.Surface;
            tsStyle.ForeColor = Color.FromArgb(224, 233, 240);
            tsStyle.ImageScalingSize = tsMain.ImageScalingSize;
            tsStyle.Padding = new Padding(StyleScale(4));
            foreach (var item in new[] { styleBorder, styleFill })
            {
                item.Size = new Size(StyleScale(28), StyleScale(24));
                item.Margin = new Padding(StyleScale(2), 0, StyleScale(2), 0);
            }
            restartSteps.Size = new Size(StyleScale(28), StyleScale(24));
            restartSteps.Margin = new Padding(StyleScale(2), 0, StyleScale(2), 0);
            var previous = restartSteps.Image;
            restartSteps.Image = CreateRestartStepsIcon(tsStyle.ImageScalingSize);
            previous?.Dispose();
            LayoutStyleToolbar();
        }

        private void UpdateStyleToolbar()
        {
            if (tsStyle == null) return;
            styleBorder.Available = tsbBorderColor.Available;
            styleBorder.Text = styleBorder.ToolTipText = CurrentShapeTool is ShapeType.DrawingTextOutline or ShapeType.DrawingSpeechBalloon ? "文字颜色" : "边框颜色";
            styleFill.Available = tsbFillColor.Available || CurrentShapeTool == ShapeType.EffectHighlight;
            styleFill.Text = styleFill.ToolTipText = CurrentShapeTool == ShapeType.EffectHighlight ? "高亮颜色" : "填充颜色";
            restartSteps.Available = CurrentTool == ShapeType.DrawingStep;
            UpdateArrowWidthTools();
            UpdateBlurStrengthTools();
            UpdateStyleSwatch(styleBorder, false);
            UpdateStyleSwatch(styleFill, true);
            styleRowVisible = !ToolbarCollapsed && (styleBorder.Available || styleFill.Available || inlineSizeHost.Available || arrowWidthHost.Available || blurStrengthHost.Available || restartSteps.Available);
            tsStyle.Visible = styleRowVisible;
            LayoutStyleToolbar();
        }

        private void UpdateStyleSwatch(ToolStripDropDownButton button, bool fill)
        {
            Color color = GetStyleColor(fill);
            bool sequence = CurrentShapeTool == ShapeType.DrawingStep;
            // Leave room for the drop-down arrow without squeezing the glyph or disc.
            button.Width = StyleScale(sequence ? 34 : 28);
            button.ImageScaling = sequence ? ToolStripItemImageScaling.None : ToolStripItemImageScaling.SizeToFit;
            button.Image = GetStyleIcon(CurrentShapeTool, fill, color, tsStyle.ImageScalingSize);
        }

        private void LayoutStyleToolbar()
        {
            if (tsStyle == null || arrangingStyleToolbar || menuForm.IsDisposed || toolbarCompacted) return;
            arrangingStyleToolbar = true;
            try
            {
                int available = Math.Max(120, Form.ClientSize.Width);
                if (tsMain.MaximumSize.Width > 0) available = Math.Min(available, tsMain.MaximumSize.Width);
                int colorsWidth = new ToolStripItem[] { styleBorder, styleFill, restartSteps }.Where(i => i.Available).Sum(i => i.Width + i.Margin.Horizontal);
                int sliderWidth = Math.Max(StyleScale(80), Math.Min(StyleScale(192), available - colorsWidth - tsStyle.Padding.Horizontal));
                int controlHeight = Math.Max(StyleScale(16), Math.Min(Math.Max(StyleScale(24), tsMain.ImageScalingSize.Height), tsMain.Height - tsStyle.Padding.Vertical));
                foreach (var button in new ToolStripItem[] { styleBorder, styleFill, restartSteps }) button.Height = controlHeight;
                var size = new Size(sliderWidth, controlHeight);
                if (inlineSizeHost.Size != size) { inlineSizeSlider.Size = size; inlineSizeHost.Size = size; }
                if (arrowWidthHost.Size != size) { arrowWidthSlider.Size = size; arrowWidthHost.Size = size; }
                var blurSize = new Size(Math.Max(1, sliderWidth / 2), controlHeight);
                if (blurStrengthHost.Size != blurSize) { blurStrengthSlider.Size = blurSize; blurStrengthHost.Size = blurSize; }
                int width = colorsWidth + (inlineSizeHost.Available || arrowWidthHost.Available ? sliderWidth : blurStrengthHost.Available ? blurSize.Width : 0) + tsStyle.Padding.Horizontal;
                tsStyle.Size = new Size(Math.Max(1, width), controlHeight + tsStyle.Padding.Vertical);
                tsStyle.PerformLayout();
                int rowGap = Math.Max(1, (int)Math.Round(2.5 * menuForm.DeviceDpi / 96d, MidpointRounding.AwayFromZero));
                tsMain.Location = new Point(0, styleRowVisible && styleRowAboveMain ? tsStyle.Height + rowGap : 0);
                tsStyle.Location = new Point(0, styleRowAboveMain ? 0 : tsMain.Bottom + rowGap);
                Size required = new Size(Math.Max(tsMain.Right, styleRowVisible ? tsStyle.Right : 0),
                    styleRowVisible ? Math.Max(tsMain.Bottom, tsStyle.Bottom) : tsMain.Bottom);
                bool resized = menuForm.ClientSize != required;
                if (resized) menuForm.ClientSize = required;
                Form.ToolbarHeight = menuForm.Height;
                Rectangle main = tsMain.Bounds, style = styleRowVisible ? tsStyle.Bounds : Rectangle.Empty;
                if (main != lastMainOutline || style != lastStyleOutline)
                {
                    using var top = CaptureToolbarRenderer.Round(main, StyleScale(7));
                    var region = new Region(top);
                    if (!style.IsEmpty) { using var bottom = CaptureToolbarRenderer.Round(style, StyleScale(7)); region.Union(bottom); }
                    var old = menuForm.Region; menuForm.Region = region; old?.Dispose();
                    lastMainOutline = main; lastStyleOutline = style;
                }
                if (menuForm.Visible)
                {
                    if (resized && ToolbarCreated) UpdateMenuPosition();
                    else CheckMenuPosition();
                }
            }
            finally { arrangingStyleToolbar = false; }
            // Placement may change the direction during a resize callback.
            // Apply that direction once the active layout pass has finished.
            if (styleRowVisible && (tsMain.Top > 0) != styleRowAboveMain) LayoutStyleToolbar();
        }

        private void UpdateToolbarRowOrder(Rectangle region)
        {
            if (Form.IsEditorMode || tsStyle == null || toolbarCompacted) return;
            // Keep the direction during overlap to avoid flicker while dragging.
            bool above = styleRowAboveMain;
            if (menuForm.Bottom <= region.Top) above = true;
            else if (menuForm.Top >= region.Bottom) above = false;
            if (above == styleRowAboveMain) return;
            styleRowAboveMain = above;
            LayoutStyleToolbar();
        }

        private static Bitmap CreateRestartStepsIcon(Size size)
        {
            var image = new Bitmap(size.Width, size.Height);
            using var g = Graphics.FromImage(image);
            g.ScaleTransform(size.Width / 24f, size.Height / 24f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(224, 233, 240), 1.7f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawArc(pen, 3, 3, 18, 18, -70, 295);
            g.DrawLines(pen, new[] { new PointF(3, 3), new PointF(3, 8), new PointF(8, 8) });
            g.DrawLines(pen, new[] { new PointF(10, 10), new PointF(12, 8), new PointF(12, 16) });
            g.DrawLine(pen, 10, 16, 14, 16);
            return image;
        }

        private sealed class StyleColorPalette : Control
        {
            internal event Action<Color> ColorChosen;
            private int selectedIndex;
            private Color selectedColor;
            internal Color SelectedColor
            {
                get => selectedColor;
                set { selectedColor = value; int i = Array.FindIndex(StyleColors, c => c.ToArgb() == value.ToArgb()); selectedIndex = Math.Max(0, i); Invalidate(); }
            }
            private int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);
            internal StyleColorPalette()
            {
                Name = "standardColorPalette"; Size = new Size(S(124), S(124)); TabStop = true; DoubleBuffered = true;
                BackColor = CaptureToolbarRenderer.Surface; AccessibleName = "标准色和透明色";
            }
            protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Size = new Size(S(124), S(124)); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < StyleColors.Length; i++)
                {
                    var rect = new Rectangle(S(14 + i % 3 * 36), S(14 + i / 3 * 36), S(24), S(24));
                    if (StyleColors[i].A > 0) { using var brush = new SolidBrush(StyleColors[i]); g.FillEllipse(brush, rect); }
                    using var edge = new Pen(Color.FromArgb(180, 195, 205), Math.Max(1, S(1))); g.DrawEllipse(edge, rect);
                    if (StyleColors[i].A == 0) g.DrawLine(edge, rect.Left + S(4), rect.Bottom - S(4), rect.Right - S(4), rect.Top + S(4));
                    if (i == selectedIndex && Focused || StyleColors[i].ToArgb() == SelectedColor.ToArgb())
                    { rect.Inflate(S(4), S(4)); using var active = new Pen(Color.FromArgb(255, 139, 67), Math.Max(1, S(1))); g.DrawEllipse(active, rect); }
                }
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
                for (int i = 0; i < StyleColors.Length; i++)
                {
                    var cell = new Rectangle(S(8 + i % 3 * 36), S(8 + i / 3 * 36), S(36), S(36));
                    if (cell.Contains(e.Location)) { ColorChosen?.Invoke(StyleColors[i]); return; }
                }
            }
            protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
                { selectedIndex = Math.Clamp(selectedIndex + (e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Up ? -3 : 3), 0, 8); Invalidate(); }
                else if (e.KeyCode is Keys.Enter or Keys.Space) ColorChosen?.Invoke(StyleColors[selectedIndex]);
                else return;
                e.Handled = e.SuppressKeyPress = true;
            }
            protected override void OnMouseWheel(MouseEventArgs e) { if (e is HandledMouseEventArgs handled) handled.Handled = true; }
        }
    }
}
