using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        private InlineTextEditor inlineText;
        private Panel inlineTextFrame;
        private TextDrawingShape inlineTextShape;
        private TextDrawingShape inlineTextOriginal;
        private TextDrawingShape pendingNewTextHistory;
        private bool inlineTextNew, inlineTextFinishing, inlineTextKeyPreview;
        private Font inlineTextFont;
        private PlainInlineTextRenderer.LayoutBox inlineTextMeasurer;
        private float inlineTextAnchorY;
        private bool inlineTextAnchorInitialized;
        private RectangleF inlineTextLastRectangle;
        private bool inlineTextLayoutQueued;
        internal bool IsEditingText => inlineText != null;
        internal bool IsInlineTextShape(TextDrawingShape shape) => IsEditingText && inlineTextShape == shape;
        private TextDrawingShape GetInlineTextAt(PointF position) => IsCtrlModifier ? null :
            Shapes.LastOrDefault(shape => shape.ShapeCategory == ShapeCategory.Drawing && shape.Intersects(position)) as TextDrawingShape;

        internal void BeginInlineText(TextDrawingShape shape, bool isNew)
        {
            if (inlineTextShape == shape) return;
            FinishInlineText(true);
            inlineTextShape = shape;
            inlineTextOriginal = (TextDrawingShape)shape.Duplicate();
            inlineTextNew = isNew;
            inlineTextAnchorInitialized = isNew;
            inlineTextAnchorY = shape.Rectangle.Y;
            inlineTextLastRectangle = shape.Rectangle;
            ResetModifiers();
            isLeftPressed = isRightPressed = isUpPressed = isDownPressed = false;
            IsCreating = IsMoving = IsResizing = false;
            CurrentShape = shape;
            NodesVisible = false;
            inlineTextKeyPreview = Form.KeyPreview;
            Form.KeyPreview = false;

            shape.TextOptions.PlainInlineRendering = true;
            shape.TextOptions.Bold = shape.TextOptions.Italic = shape.TextOptions.Underline = shape.TextOptions.Gradient = false;
            shape.TextOptions.Font = "Microsoft YaHei";
            shape.TextOptions.AlignmentHorizontal = StringAlignment.Near;
            inlineTextFont = PlainInlineTextRenderer.CreateInputFont(shape.TextOptions, Form.DeviceDpi, Form.ZoomFactor);
            shape.TextOptions.AlignmentVertical = StringAlignment.Center;
            inlineText = new InlineTextEditor
            {
                Name = "captureInlineText", BorderStyle = System.Windows.Forms.BorderStyle.None, Multiline = true,
                AcceptsTab = true, WordWrap = true, ScrollBars = RichTextBoxScrollBars.None,
                DetectUrls = false, Font = inlineTextFont, BackColor = Color.Transparent,
                ForeColor = shape.TextOptions.Color, Text = shape.Text ?? "",
                ExplicitHorizontalMargins = shape is SpeechBalloonDrawingShape,
                Finish = FinishInlineText,
                CompositionLayout = QueueInlineTextLayout
            };
            inlineTextMeasurer = new PlainInlineTextRenderer.LayoutBox
            {
                BorderStyle = System.Windows.Forms.BorderStyle.None, Multiline = true,
                WordWrap = true, ScrollBars = RichTextBoxScrollBars.None, DetectUrls = false,
                BackColor = Color.White, ForeColor = Color.Black, Font = inlineTextFont,
                ExplicitHorizontalMargins = shape is SpeechBalloonDrawingShape
            };
            _ = inlineTextMeasurer.Handle;
            inlineTextFrame = new Panel { Name = "captureInlineTextFrame", BackColor = Color.Transparent, Cursor = Cursors.SizeAll };
            inlineTextFrame.Controls.Add(inlineText);
            inlineTextFrame.Paint += (_, e) =>
            {
                using var pen = new Pen(shape.TextOptions.Color, Math.Max(1, Form.DeviceDpi / 96f)) { DashStyle = DashStyle.Dash };
                float zoom = Form.ZoomFactor;
                e.Graphics.DrawRectangle(pen, shape.Rectangle.X * zoom - inlineTextFrame.Left - 2,
                    shape.Rectangle.Y * zoom - inlineTextFrame.Top - 2,
                    Math.Max(1, shape.Rectangle.Width * zoom + 4), Math.Max(1, shape.Rectangle.Height * zoom + 4));
            };
            Point drag = Point.Empty;
            inlineTextFrame.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { drag = e.Location; inlineTextFrame.Capture = true; } };
            inlineTextFrame.MouseMove += (_, e) =>
            {
                if (!inlineTextFrame.Capture) return;
                float dx = (e.X - drag.X) / Form.ZoomFactor, dy = (e.Y - drag.Y) / Form.ZoomFactor;
                shape.Move(dx, dy);
                LayoutInlineText();
                Form.Invalidate();
            };
            inlineTextFrame.MouseUp += (_, _) => inlineTextFrame.Capture = false;
            // RichEdit raises TextChanged inside EM_STREAMIN/EM_REPLACESEL.
            // Repositioning controls or changing focus inside that native call
            // can invalidate its active text buffer. Layout after it unwinds.
            inlineText.TextChanged += (_, _) => QueueInlineTextLayout();
            inlineText.DpiChangedAfterParent += (_, _) => RefreshInlineTextFont();
            inlineText.Leave += (_, _) =>
            {
                if (!Form.IsHandleCreated || inlineTextFinishing) return;
                Form.BeginInvoke((Action)(() =>
                {
                    if (IsEditingText && !inlineTextFrame.ContainsFocus && !inlineTextFrame.Capture &&
                        !inlineText.Composing && Form.ContainsFocus) FinishInlineText(true);
                }));
            };
            Form.Controls.Add(inlineTextFrame);
            Form.SizeChanged += InlineTextViewportChanged;
            Form.LocationChanged += InlineTextViewportChanged;
            LayoutInlineText();
            inlineTextFrame.BringToFront();
            Form.BeginInvoke((Action)(() => { if (inlineTextShape == shape) { inlineText.Focus(); inlineText.Select(inlineText.TextLength, 0); } }));
            Form.Invalidate();
            RefreshEditingToolbar();
        }

        private void InlineTextViewportChanged(object sender, EventArgs e)
        {
            if (!IsEditingText || inlineTextLayoutQueued || !Form.IsHandleCreated) return;
            inlineTextLayoutQueued = true;
            Form.BeginInvoke((Action)(() => { inlineTextLayoutQueued = false; RefreshInlineTextFont(); }));
        }

        private void QueueInlineTextLayout()
        {
            if (!IsEditingText || inlineTextLayoutQueued || !Form.IsHandleCreated) return;
            var editor = inlineText;
            inlineTextLayoutQueued = true;
            Form.BeginInvoke((Action)(() =>
            {
                inlineTextLayoutQueued = false;
                if (inlineText != editor || inlineTextFinishing) return;
                LayoutInlineText();
                Form.Invalidate(inlineTextFrame.Bounds, true);
            }));
        }

        private void RefreshInlineTextFont()
        {
            if (!IsEditingText || inlineTextFinishing) return;
            var options = inlineTextShape.TextOptions;
            var font = PlainInlineTextRenderer.CreateInputFont(options, Form.DeviceDpi, Form.ZoomFactor);
            if (inlineText.Font.Equals(font)) { font.Dispose(); LayoutInlineText(); return; }
            Font old = inlineTextFont;
            inlineTextFont = font;
            inlineText.Font = font;
            inlineTextMeasurer.Font = font;
            old?.Dispose();
            LayoutInlineText();
        }

        private void LayoutInlineText()
        {
            if (!IsEditingText || inlineTextFinishing) return;
            float zoom = Form.ZoomFactor;
            RectangleF rect = inlineTextShape.Rectangle;
            float previousBottom = rect.Bottom;
            if (inlineTextAnchorInitialized) inlineTextAnchorY += rect.Y - inlineTextLastRectangle.Y;
            int padding = Math.Max(2, (int)Math.Round(3 * Form.DeviceDpi / 96d));
            bool balloonText = inlineTextShape is SpeechBalloonDrawingShape;
            int inset = balloonText ? Math.Max(1, (int)Math.Round(PlainInlineTextRenderer.BalloonHorizontalPadding * zoom)) : 0;
            int x = (int)Math.Round(rect.X * zoom) - padding, y = (int)Math.Round(rect.Y * zoom) - padding;
            int rightEdge = Form.ClientSize.Width;
            // Annotation coordinates share the capture region's image space.
            // Keep the editor inside that region, including its frame padding.
            var region = Shapes.OfType<BaseRegionShape>().FirstOrDefault(region =>
                region.IsValidShape && region.Rectangle.Contains(rect.Location));
            if (region != null) rightEdge = Math.Min(rightEdge, (int)Math.Floor(region.Rectangle.Right * zoom));
            int available = Math.Max(1, rightEdge - Math.Max(0, x) - padding * 2 - inset * 2);
            // RichEdit needs room for the end caret, even with zero native
            // margins. Keep that allowance separate from the balloon padding.
            int naturalWidth = PlainInlineTextRenderer.MeasureUnwrappedWidth(inlineText, balloonText ? 2 : 8);
            int minimumWidth = Math.Max(1, (int)Math.Ceiling((inlineTextNew ? 40 : inlineTextOriginal.Rectangle.Width) * zoom) - inset * 2);
            int contentWidth = Math.Min(available, Math.Max(minimumWidth, naturalWidth));
            inlineText.Width = Math.Max(1, contentWidth);
            Size nativeSize = PlainInlineTextRenderer.MeasureEditor(inlineText);
            inlineTextMeasurer.Width = inlineText.Width;
            if (inlineTextMeasurer.Text != inlineText.Text) inlineTextMeasurer.Text = inlineText.Text;
            Rectangle visible = inlineTextMeasurer.GetVisibleBounds();
            if (!inlineTextAnchorInitialized)
            {
                // Reconstruct the native editor origin from the committed
                // visible glyphs, then retain it throughout this edit session.
                inlineTextAnchorY = rect.Y + (rect.Height - visible.Height / zoom) / 2 - visible.Top / zoom;
                inlineTextAnchorInitialized = true;
            }
            int editorY = (int)Math.Round(inlineTextAnchorY * zoom);
            int availableHeight = Math.Max(1, Form.ClientSize.Height - Math.Max(0, editorY) - padding);
            bool clipped = nativeSize.Height > availableHeight;
            inlineText.ScrollBars = clipped ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.None;
            nativeSize = PlainInlineTextRenderer.MeasureEditor(inlineText);
            inlineTextShape.TextOptions.InlineWrapWidth = naturalWidth > inlineText.ClientSize.Width
                ? inlineText.ClientSize.Width / zoom : 0;
            float bodyHeight = Math.Max(visible.Height / zoom + PlainInlineTextRenderer.VerticalPadding * 2,
                inlineTextNew ? 0 : inlineTextOriginal.Rectangle.Height);
            rect.Size = new SizeF((contentWidth + inset * 2) / zoom, bodyHeight);
            rect.Y = inlineTextAnchorY + (visible.Top + visible.Height / 2f) / zoom - bodyHeight / 2;
            if (Math.Abs(rect.Y - inlineTextLastRectangle.Y) < .01f) rect.Y = inlineTextLastRectangle.Y;
            if (Math.Abs(rect.Height - inlineTextLastRectangle.Height) < .01f) rect.Height = inlineTextLastRectangle.Height;
            inlineTextShape.Rectangle = rect;
            inlineTextLastRectangle = rect;
            if (inlineTextNew && inlineTextShape is SpeechBalloonDrawingShape balloon)
                balloon.SetInlineTailPosition(new PointF(balloon.TailPosition.X, balloon.TailPosition.Y + rect.Bottom - previousBottom));
            int editorHeight = Math.Min(availableHeight, nativeSize.Height);
            y = Math.Min((int)Math.Floor(rect.Y * zoom), editorY) - padding;
            int bottom = Math.Max((int)Math.Ceiling(rect.Bottom * zoom), editorY + editorHeight) + padding;
            inlineTextFrame.SetBounds(x, y, contentWidth + inset * 2 + padding * 2, Math.Max(1, bottom - y));
            inlineText.SetBounds(padding + inset, editorY - y, Math.Max(1, contentWidth), Math.Max(1, editorHeight));
            inlineText.ResetViewportScroll(!clipped);
            RefreshEditingToolbar();
        }

        private static void RestoreInlineTextShape(TextDrawingShape target, TextDrawingShape source)
        {
            target.Move(source.Rectangle.X - target.Rectangle.X, source.Rectangle.Y - target.Rectangle.Y);
            target.Rectangle = source.Rectangle;
            if (target is SpeechBalloonDrawingShape balloon && source is SpeechBalloonDrawingShape original)
                balloon.SetInlineTailPosition(original.TailPosition);
            target.Text = source.Text;
            target.TextOptions = source.TextOptions.Copy();
        }

        internal void FinishInlineText(bool commit)
        {
            if (!IsEditingText || inlineTextFinishing) return;
            if (inlineText.ProcessingText)
            {
                var pending = inlineText;
                Form.BeginInvoke((Action)(() => { if (inlineText == pending) FinishInlineText(commit); }));
                return;
            }
            // Commit must include text entered just before the queued layout.
            LayoutInlineText();
            inlineTextFinishing = true;
            var shape = inlineTextShape;
            var original = inlineTextOriginal;
            bool isNew = inlineTextNew;
            string text = inlineText.Text;
            try
            {
                if (isNew && pendingNewTextHistory == shape && commit && !string.IsNullOrWhiteSpace(text))
                {
                    int index = Shapes.IndexOf(shape);
                    if (index >= 0)
                    {
                        Shapes.RemoveAt(index);
                        try { history.CreateShapesMemento(); }
                        finally { Shapes.Insert(index, shape); }
                    }
                }
                if (!commit) RestoreInlineTextShape(shape, original);
                else if (isNew || text != original.Text || shape.Rectangle != original.Rectangle || !original.TextOptions.PlainInlineRendering ||
                    shape.TextOptions.Style != original.TextOptions.Style || shape.TextOptions.Font != original.TextOptions.Font ||
                    shape.TextOptions.AlignmentHorizontal != original.TextOptions.AlignmentHorizontal ||
                    shape.TextOptions.AlignmentVertical != original.TextOptions.AlignmentVertical || shape.TextOptions.Gradient != original.TextOptions.Gradient)
                {
                    if (!isNew)
                    {
                        var edited = (TextDrawingShape)shape.Duplicate();
                        RestoreInlineTextShape(shape, original);
                        history.CreateShapesMemento();
                        RestoreInlineTextShape(shape, edited);
                        edited.Dispose();
                    }
                    shape.Text = text;
                    shape.OnConfigSave();
                    OnImageModified();
                    if (isNew) OnShapeCreated(shape);
                }
                if ((isNew && !commit) || (commit && string.IsNullOrWhiteSpace(text)))
                {
                    Shapes.Remove(shape);
                    DeselectShape(shape);
                    shape.Dispose();
                }
            }
            finally
            {
                if (pendingNewTextHistory == shape) pendingNewTextHistory = null;
                var frame = inlineTextFrame;
                Form.SizeChanged -= InlineTextViewportChanged;
                Form.LocationChanged -= InlineTextViewportChanged;
                inlineText = null;
                inlineTextShape = inlineTextOriginal = null;
                inlineTextFrame = null;
                frame?.Dispose();
                inlineTextMeasurer?.Dispose();
                inlineTextMeasurer = null;
                inlineTextFont?.Dispose();
                inlineTextFont = null;
                original?.Dispose();
                Form.KeyPreview = inlineTextKeyPreview;
                inlineTextFinishing = false;
                if (!Form.IsDisposed && !Form.Disposing)
                {
                    if (Shapes.Contains(shape)) { CurrentShape = shape; SelectCurrentShape(); }
                    UpdateMenu();
                    RefreshEditingToolbar();
                    Form.Invalidate();
                }
            }
        }

        private sealed class InlineTextEditor : PlainInlineTextRenderer.LayoutBox
        {
            [DllImport("user32.dll", EntryPoint = "SendMessageW")]
            private static extern IntPtr SendScrollMessage(IntPtr window, int message, IntPtr parameter, ref Point point);
            internal void ResetViewportScroll(bool fitsVertically)
            {
                if (!IsHandleCreated) return;
                Point scroll = Point.Empty;
                SendScrollMessage(Handle, 0x04DD, IntPtr.Zero, ref scroll); // EM_GETSCROLLPOS
                if (scroll.X == 0 && (!fitsVertically || scroll.Y == 0)) return;
                scroll.X = 0;
                if (fitsVertically) scroll.Y = 0;
                SendScrollMessage(Handle, 0x04DE, IntPtr.Zero, ref scroll); // EM_SETSCROLLPOS
            }
            internal InlineTextEditor() { SetStyle(ControlStyles.SupportsTransparentBackColor, true); }
            protected override CreateParams CreateParams
            {
                get { var parameters = base.CreateParams; parameters.ExStyle |= 0x20; return parameters; }
            }
            internal Action<bool> Finish;
            internal Action CompositionLayout;
            internal bool Composing { get; private set; }
            private int textMessageDepth;
            internal bool ProcessingText => textMessageDepth > 0;
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0302) // WM_PASTE: keep external rich formatting out of plain annotations
                {
                    Paste(DataFormats.GetFormat(DataFormats.UnicodeText));
                    return;
                }
                if (m.Msg == 0x010D) Composing = true; // WM_IME_STARTCOMPOSITION
                bool changing = m.Msg is 0x000C or 0x0449 or 0x00C2 or 0x0102 or 0x010F;
                if (changing) textMessageDepth++;
                try { base.WndProc(ref m); }
                finally { if (changing) textMessageDepth--; }
                if (m.Msg == 0x010E) Composing = false; // WM_IME_ENDCOMPOSITION
                // RichEdit does not raise TextChanged for temporary IME text.
                // Resize after the native composition call unwinds, including
                // cancellation/result replacement, without committing it.
                if (m.Msg is 0x010D or 0x010F or 0x010E) CompositionLayout?.Invoke();
            }
            protected override void OnSelectionChanged(EventArgs e)
            {
                base.OnSelectionChanged(e);
                // TSF input can update the composition/caret without sending
                // WM_IME_COMPOSITION through the control's managed WndProc.
                if (Composing) CompositionLayout?.Invoke();
            }
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (!Composing && (keyData == Keys.Escape || keyData == (Keys.Control | Keys.Enter)))
                {
                    Finish?.Invoke(keyData != Keys.Escape);
                    return true;
                }
                // Keep editing shortcuts out of the capture/tool shortcut path.
                if (keyData == (Keys.Control | Keys.A)) { SelectAll(); return true; }
                if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
                if (keyData == (Keys.Control | Keys.Y)) { Redo(); return true; }
                if ((keyData & Keys.Control) != 0 && (keyData & Keys.KeyCode) is Keys.B or Keys.I or Keys.U or Keys.L or Keys.E or Keys.R ||
                    keyData is (Keys.Control | Keys.Shift | Keys.OemPeriod) or (Keys.Control | Keys.Shift | Keys.Oemcomma)) return true;
                return base.ProcessCmdKey(ref msg, keyData);
            }
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                // Ctrl+wheel must not change the editor font independently of
                // the annotation's saved font size.
                if ((ModifierKeys & Keys.Control) == 0) base.OnMouseWheel(e);
            }
        }
    }
}
