using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    /// <summary>A live, manual scrolling session kept separate from ShareX's old auto-scroll form.</summary>
    public sealed class LongCaptureSessionForm : Form
    {
        private const int SelectionBorderSize = 3;
        private const int PreviewBorderSize = 2;
        private static readonly Color PreviewBorderColor = Color.FromArgb(58, 197, 109);
        private readonly Rectangle captureRegion;
        private ILongCaptureStitcher stitcher = new LongCaptureStitcher();
        private readonly Timer captureTimer = new Timer { Interval = 180 };
        private readonly Panel previewPanel = new Panel();
        private readonly PictureBox previewImage = new PictureBox();
        private readonly Label statusLabel = new Label();
        private readonly Label sizeLabel = new Label();
        private readonly Button copyButton = new LongCaptureActionButton(LongCaptureButtonKind.Copy);
        private readonly Button cancelButton = new LongCaptureActionButton(LongCaptureButtonKind.Cancel);
        private readonly LongCaptureActionButton directionButton = new LongCaptureActionButton(LongCaptureButtonKind.Vertical);
        private readonly ToolTip buttonTips = new ToolTip();
        private LongCaptureBorderForm border;
        private KeyboardHook escapeHook;
        private Bitmap result;
        private bool isCapturing;
        private bool previewOverlapsCapture;
        private bool previewExcludedFromCapture;
        private bool horizontalCapture;
        private bool directionLocked;
        private HookProc previewMouseHookProc;
        private IntPtr previewMouseHookHandle;
        private bool previewMouseDown;
        private long previousPreviewClickTime;
        private Point previousPreviewClickPosition;

        private const int WmLeftButtonDown = 0x0201;
        private const int WmLeftButtonUp = 0x0202;
        private const int WmMouseWheel = 0x020A;
        private const MouseEventFlags MouseEventHorizontalWheel = (MouseEventFlags)0x1000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

        public LongCaptureSessionForm(Rectangle selectedRegion)
        {
            captureRegion = selectedRegion;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(28, 28, 28);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9f);
            KeyPreview = true;
            ClientSize = new Size(224, 456);

            previewPanel.Location = new Point(8, 8);
            previewPanel.Size = new Size(208, 440);
            previewPanel.BackColor = Color.FromArgb(28, 28, 28);
            previewPanel.AutoScroll = false;
            previewImage.SizeMode = PictureBoxSizeMode.Normal;
            previewImage.Size = Size.Empty;
            previewPanel.Controls.Add(previewImage);
            Controls.Add(previewPanel);

            sizeLabel.AutoSize = true;
            sizeLabel.Padding = new Padding(5, 3, 5, 3);
            sizeLabel.BackColor = Color.FromArgb(28, 28, 28);
            sizeLabel.Text = "";
            sizeLabel.Visible = false;
            Controls.Add(sizeLabel);

            statusLabel.Location = new Point(8, 8);
            statusLabel.Size = new Size(208, 38);
            statusLabel.BackColor = Color.FromArgb(75, 45, 45);
            statusLabel.Visible = false;
            Controls.Add(statusLabel);

            copyButton.Location = new Point(140, 410);
            copyButton.Enabled = false;
            copyButton.Visible = false;
            copyButton.Click += (sender, e) => CompleteCapture();
            Controls.Add(copyButton);

            cancelButton.Location = new Point(176, 410);
            cancelButton.Click += (sender, e) => CancelCapture();
            Controls.Add(cancelButton);

            directionButton.Cursor = Cursors.Hand;
            directionButton.Click += (sender, e) => SwitchDirection();
            Controls.Add(directionButton);

            buttonTips.SetToolTip(copyButton, "完成长截图，复制并保存");
            buttonTips.SetToolTip(cancelButton, "取消长截图");
            buttonTips.SetToolTip(directionButton, "当前：上下长截图；点击切换到左右");

            UpdatePreviewLayout();
            PlacePreview();
            captureTimer.Tick += (sender, e) => CaptureFrame();
        }

        private void PlacePreview()
        {
            Rectangle work = Screen.FromRectangle(captureRegion).WorkingArea;
            Rectangle previewFrame = Rectangle.Inflate(previewPanel.Bounds, PreviewBorderSize, PreviewBorderSize);
            int gap = previewFrame.Left; // Preserve the existing right-side red-to-green spacing.
            int rightX = captureRegion.Right + SelectionBorderSize + gap - previewFrame.Left;
            int leftX = captureRegion.Left - SelectionBorderSize - gap - previewFrame.Right;
            int alignedY = captureRegion.Top - SelectionBorderSize - previewFrame.Top;
            int minY = work.Top - previewFrame.Top;
            int maxY = Math.Max(minY, work.Bottom - previewFrame.Bottom);
            int x;
            int y = Math.Max(minY, Math.Min(alignedY, maxY));

            if (rightX + previewFrame.Right <= work.Right)
            {
                x = rightX;
            }
            else if (leftX + previewFrame.Left >= work.Left)
            {
                x = leftX;
            }
            else if (captureRegion.Bottom + SelectionBorderSize + Height <= work.Bottom)
            {
                x = Math.Max(work.Left, Math.Min(captureRegion.Right - Width, work.Right - Width));
                y = captureRegion.Bottom + SelectionBorderSize;
            }
            else if (captureRegion.Top - SelectionBorderSize - Height >= work.Top)
            {
                x = Math.Max(work.Left, Math.Min(captureRegion.Right - Width, work.Right - Width));
                y = captureRegion.Top - SelectionBorderSize - Height;
            }
            else
            {
                // A nearly full-screen selection has no outside area; capture exclusion or
                // the off-screen fallback keeps this overlay out of the stitched image.
                x = Math.Max(work.Left, work.Right - Width);
                previewOverlapsCapture = true;
            }

            Location = new Point(x, y);
            Rectangle visiblePreviewBounds = new Rectangle(Location.X + previewFrame.Left,
                Location.Y + previewFrame.Top, previewFrame.Width, previewFrame.Height);
            previewOverlapsCapture = visiblePreviewBounds.IntersectsWith(captureRegion);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // The final size is known only after Windows applies the monitor's DPI scale.
            UpdatePreviewLayout();
            PlacePreview();
            border = new LongCaptureBorderForm(captureRegion);
            border.Show();
            if (previewOverlapsCapture)
            {
                // Let the live thumbnail remain visible without being photographed into itself.
                previewExcludedFromCapture = SetWindowDisplayAffinity(Handle, 0x11);
            }
            escapeHook = new KeyboardHook();
            escapeHook.KeyDown += EscapeHook_KeyDown;
            previewMouseHookProc = PreviewMouseHookProc;
            previewMouseHookHandle = NativeMethods.SetWindowsHookEx(NativeConstants.WH_MOUSE_LL,
                previewMouseHookProc, IntPtr.Zero, 0);
            captureTimer.Start();
        }

        private IntPtr PreviewMouseHookProc(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !IsDisposed && !Disposing && Visible)
            {
                int kind = message.ToInt32();
                // MSLLHOOKSTRUCT begins with POINT. The remaining fields are irrelevant here.
                Point position = new Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, sizeof(int)));
                if (kind == WmMouseWheel && horizontalCapture && captureRegion.Contains(position))
                {
                    int wheelData = Marshal.ReadInt32(data, sizeof(int) * 2);
                    int verticalDelta = (short)(wheelData >> 16);
                    // SendInput inside WH_MOUSE_LL can synchronously re-enter the hook.
                    // Queue it after this callback returns, then suppress the original wheel.
                    BeginInvoke(new Action(() => SendHorizontalWheel(-verticalDelta)));
                    return (IntPtr)1;
                }

                if (kind != WmLeftButtonDown && kind != WmLeftButtonUp)
                    return NativeMethods.CallNextHookEx(previewMouseHookHandle, code, message, data);

                Rectangle previewBounds = Rectangle.Inflate(previewPanel.RectangleToScreen(previewPanel.ClientRectangle),
                    PreviewBorderSize, PreviewBorderSize);
                bool overPreview = previewBounds.Contains(position) &&
                    !copyButton.RectangleToScreen(copyButton.ClientRectangle).Contains(position) &&
                    !cancelButton.RectangleToScreen(cancelButton.ClientRectangle).Contains(position) &&
                    !(directionButton.Visible &&
                        directionButton.RectangleToScreen(directionButton.ClientRectangle).Contains(position));

                if (kind == WmLeftButtonDown)
                {
                    if (overPreview)
                    {
                        previewMouseDown = true;
                        return (IntPtr)1; // Transparent blank preview must not click the page behind it.
                    }
                    previousPreviewClickTime = 0;
                }
                else if (previewMouseDown)
                {
                    previewMouseDown = false;
                    if (overPreview)
                    {
                        long now = Environment.TickCount64;
                        Size tolerance = SystemInformation.DoubleClickSize;
                        if (stitcher.HasImage && previousPreviewClickTime != 0 &&
                            now - previousPreviewClickTime <= SystemInformation.DoubleClickTime &&
                            Math.Abs(position.X - previousPreviewClickPosition.X) <= tolerance.Width / 2 &&
                            Math.Abs(position.Y - previousPreviewClickPosition.Y) <= tolerance.Height / 2)
                        {
                            previousPreviewClickTime = 0;
                            BeginInvoke(new Action(CompleteCapture));
                        }
                        else
                        {
                            previousPreviewClickTime = now;
                            previousPreviewClickPosition = position;
                        }
                    }
                    else
                    {
                        previousPreviewClickTime = 0;
                    }
                    return (IntPtr)1;
                }
            }
            return NativeMethods.CallNextHookEx(previewMouseHookHandle, code, message, data);
        }

        private void SendHorizontalWheel(int delta)
        {
            if (IsDisposed || Disposing || !Visible) return;
            INPUT horizontalWheel = new INPUT
            {
                Type = InputType.InputMouse,
                Data = new InputUnion
                {
                    Mouse = new MOUSEINPUT
                    {
                        mouseData = unchecked((uint)delta),
                        dwFlags = MouseEventHorizontalWheel
                    }
                }
            };
            NativeMethods.SendInput(1, ref horizontalWheel, Marshal.SizeOf(typeof(INPUT)));
        }

        private void SwitchDirection()
        {
            if (directionLocked || isCapturing) return;
            horizontalCapture = !horizontalCapture;
            stitcher.Dispose();
            stitcher = horizontalCapture ? new HorizontalLongCaptureStitcher() : new LongCaptureStitcher();
            Image previous = previewImage.Image;
            previewImage.Image = null;
            previewImage.Size = Size.Empty;
            previous?.Dispose();
            sizeLabel.Visible = false;
            copyButton.Enabled = false;
            copyButton.Visible = false;
            statusLabel.Visible = false;
            directionButton.Kind = horizontalCapture ? LongCaptureButtonKind.Horizontal : LongCaptureButtonKind.Vertical;
            buttonTips.SetToolTip(directionButton, horizontalCapture ?
                "当前：左右长截图；点击切换到上下" : "当前：上下长截图；点击切换到左右");
            UpdatePreviewLayout();
            CaptureFrame(); // The new direction starts from the current screen, not the previous orientation's frame.
        }

        private void EscapeHook_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape || IsDisposed || Disposing) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (IsHandleCreated) BeginInvoke(new Action(CancelCapture));
        }

        private void CancelCapture()
        {
            if (IsDisposed || Disposing) return;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                CancelCapture();
                return;
            }
            base.OnKeyDown(e);
        }

        private void CaptureFrame()
        {
            if (isCapturing || IsDisposed || !Visible) return;
            isCapturing = true;
            Point previewLocation = Location;
            try
            {
                if (previewOverlapsCapture && !previewExcludedFromCapture)
                {
                    Location = new Point(-32000, -32000);
                    Application.DoEvents();
                    if (IsDisposed) return;
                }

                using (Bitmap frame = new Screenshot().CaptureRectangle(captureRegion))
                {
                    ProcessCapturedFrame(frame);
                }
            }
            catch (Exception)
            {
                ShowStatus("当前画面采集失败，可复制已有长图");
            }
            finally
            {
                if (previewOverlapsCapture && !previewExcludedFromCapture && !IsDisposed)
                {
                    Location = previewLocation;
                }
                isCapturing = false;
            }
        }

        private void ProcessCapturedFrame(Bitmap frame)
        {
            LongCaptureFrameResult state = stitcher.AddFrame(frame);
            if ((state == LongCaptureFrameResult.Appended || state == LongCaptureFrameResult.Prepended) &&
                !directionLocked)
            {
                directionLocked = true;
                directionButton.Visible = false;
            }
            if (state == LongCaptureFrameResult.First || state == LongCaptureFrameResult.Appended ||
                state == LongCaptureFrameResult.Prepended)
            {
                statusLabel.Visible = false;
                RefreshPreview();
            }
            else if (state == LongCaptureFrameResult.NoOverlap && stitcher.HasImage)
            {
                ShowStatus("未对齐，请放慢滚动；可复制已有长图");
            }
            else if (state == LongCaptureFrameResult.LimitReached)
            {
                captureTimer.Stop();
                ShowStatus("已到长图上限，可复制已有内容");
            }
        }

        private void RefreshPreview()
        {
            Bitmap next = stitcher.CreatePreview(previewPanel.ClientSize.Width, previewPanel.ClientSize.Height);
            Image previous = previewImage.Image;
            previewImage.Image = next;
            previewImage.Size = next.Size;
            previewImage.Location = new Point((previewPanel.ClientSize.Width - next.Width) / 2,
                (previewPanel.ClientSize.Height - next.Height) / 2);
            previous?.Dispose();
            sizeLabel.Text = $"{stitcher.Width} × {stitcher.Height} px";
            sizeLabel.Visible = true;
            copyButton.Enabled = true;
            copyButton.Visible = true;
            UpdatePreviewLayout();
        }

        private void ShowStatus(string message)
        {
            statusLabel.Text = message;
            statusLabel.Visible = true;
            statusLabel.BringToFront();
            UpdatePreviewLayout();
        }

        private void UpdatePreviewLayout()
        {
            Rectangle imageBounds = previewImage.Image == null ? Rectangle.Empty :
                new Rectangle(previewPanel.Left + previewImage.Left, previewPanel.Top + previewImage.Top,
                    previewImage.Width, previewImage.Height);

            sizeLabel.Location = new Point(previewPanel.Left + 4, previewPanel.Top + 4);

            int buttonWidth = copyButton.Width + cancelButton.Width + 4;
            int buttonX = previewPanel.Right - buttonWidth - 4;
            int buttonY = previewPanel.Bottom - cancelButton.Height - 4;
            copyButton.Location = new Point(buttonX, buttonY);
            cancelButton.Location = new Point(buttonX + copyButton.Width + 4, buttonY);
            directionButton.Location = new Point(buttonX - directionButton.Width - 4, buttonY);

            if (statusLabel.Visible)
            {
                statusLabel.Location = new Point(previewPanel.Left, Math.Max(previewPanel.Top,
                    Math.Min(imageBounds.Bottom - statusLabel.Height, buttonY - statusLabel.Height - 4)));
            }

            Region visibleParts = new Region();
            visibleParts.MakeEmpty();
            Rectangle frame = Rectangle.Inflate(previewPanel.Bounds, PreviewBorderSize, PreviewBorderSize);
            visibleParts.Union(new Rectangle(frame.Left, frame.Top, frame.Width, PreviewBorderSize));
            visibleParts.Union(new Rectangle(frame.Left, frame.Bottom - PreviewBorderSize, frame.Width, PreviewBorderSize));
            visibleParts.Union(new Rectangle(frame.Left, frame.Top + PreviewBorderSize, PreviewBorderSize,
                frame.Height - PreviewBorderSize * 2));
            visibleParts.Union(new Rectangle(frame.Right - PreviewBorderSize, frame.Top + PreviewBorderSize,
                PreviewBorderSize, frame.Height - PreviewBorderSize * 2));
            if (!imageBounds.IsEmpty) visibleParts.Union(imageBounds);
            if (sizeLabel.Visible) visibleParts.Union(sizeLabel.Bounds);
            if (copyButton.Visible) visibleParts.Union(copyButton.Bounds);
            visibleParts.Union(cancelButton.Bounds);
            if (directionButton.Visible) visibleParts.Union(directionButton.Bounds);
            if (statusLabel.Visible) visibleParts.Union(statusLabel.Bounds);
            Region previous = Region;
            Region = visibleParts;
            previous?.Dispose();

            // WinForms places the first added sibling above later controls. Keep the badges
            // above the preview panel even after a new thumbnail or warning repaints it.
            sizeLabel.BringToFront();
            if (statusLabel.Visible) statusLabel.BringToFront();
            copyButton.BringToFront();
            cancelButton.BringToFront();
            if (directionButton.Visible) directionButton.BringToFront();
            Invalidate(frame);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle frame = Rectangle.Inflate(previewPanel.Bounds, PreviewBorderSize, PreviewBorderSize);
            using (SolidBrush brush = new SolidBrush(PreviewBorderColor))
            {
                e.Graphics.FillRectangle(brush, frame.Left, frame.Top, frame.Width, PreviewBorderSize);
                e.Graphics.FillRectangle(brush, frame.Left, frame.Bottom - PreviewBorderSize,
                    frame.Width, PreviewBorderSize);
                e.Graphics.FillRectangle(brush, frame.Left, frame.Top + PreviewBorderSize,
                    PreviewBorderSize, frame.Height - PreviewBorderSize * 2);
                e.Graphics.FillRectangle(brush, frame.Right - PreviewBorderSize, frame.Top + PreviewBorderSize,
                    PreviewBorderSize, frame.Height - PreviewBorderSize * 2);
            }
        }

        private void CompleteCapture()
        {
            captureTimer.Stop();
            if (!stitcher.HasImage) return;
            try
            {
                result = stitcher.CreateResult();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception)
            {
                ShowStatus("长图生成失败，请缩短截取范围");
                captureTimer.Start();
            }
        }

        public Bitmap TakeResult()
        {
            Bitmap image = result;
            result = null;
            return image;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            captureTimer.Stop();
            captureTimer.Dispose();
            if (previewMouseHookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(previewMouseHookHandle);
                previewMouseHookHandle = IntPtr.Zero;
            }
            previewMouseHookProc = null;
            buttonTips.Dispose();
            escapeHook?.Dispose();
            escapeHook = null;
            border?.Close();
            border?.Dispose();
            previewImage.Image?.Dispose();
            previewImage.Image = null;
            stitcher.Dispose();
            base.OnFormClosed(e);
        }

        private enum LongCaptureButtonKind { Copy, Cancel, Horizontal, Vertical }

        private sealed class LongCaptureActionButton : Button
        {
            private bool isHovered;
            private LongCaptureButtonKind kind;

            public LongCaptureButtonKind Kind
            {
                get => kind;
                set
                {
                    kind = value;
                    Invalidate();
                }
            }

            public LongCaptureActionButton(LongCaptureButtonKind kind)
            {
                this.kind = kind;
                Size = new Size(36, 36);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                isHovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                isHovered = false;
                Invalidate();
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                if (kind == LongCaptureButtonKind.Horizontal || kind == LongCaptureButtonKind.Vertical)
                {
                    // Clear the old glyph and the thumbnail behind this button on every repaint.
                    e.Graphics.Clear(Color.FromArgb(28, 28, 28));
                }
                // Copy and cancel retain their existing thumbnail-backed appearance.
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF bounds = new RectangleF(1, 1, Width - 2, Height - 2);
                using (GraphicsPath path = new GraphicsPath())
                {
                    float diameter = 9;
                    path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                    path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                    path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                    path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                    path.CloseFigure();
                    int neutral = isHovered ? 74 : 55;
                    Color fill = kind == LongCaptureButtonKind.Copy ? Color.FromArgb(27, isHovered ? 105 : 82, 59) :
                        kind == LongCaptureButtonKind.Cancel ? Color.FromArgb(isHovered ? 111 : 87, 40, 46) :
                        Color.FromArgb(neutral, neutral, neutral);
                    using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                }

                using (Pen icon = new Pen(Enabled ? Color.White : Color.FromArgb(145, 155, 150), 2.4f))
                {
                    icon.StartCap = LineCap.Round;
                    icon.EndCap = LineCap.Round;
                    icon.LineJoin = LineJoin.Round;
                    if (kind == LongCaptureButtonKind.Copy)
                    {
                        // Offset sheets: the rear outline stays clear of the front sheet.
                        e.Graphics.DrawLines(icon, new[] {
                            new PointF(11, 22), new PointF(9, 22), new PointF(9, 9),
                            new PointF(22, 9), new PointF(22, 11) });
                        using (GraphicsPath sheet = new GraphicsPath())
                        {
                            sheet.AddArc(14, 14, 4, 4, 180, 90);
                            sheet.AddArc(23, 14, 4, 4, 270, 90);
                            sheet.AddArc(23, 23, 4, 4, 0, 90);
                            sheet.AddArc(14, 23, 4, 4, 90, 90);
                            sheet.CloseFigure();
                            e.Graphics.DrawPath(icon, sheet);
                        }
                    }
                    else if (kind == LongCaptureButtonKind.Cancel)
                    {
                        e.Graphics.DrawLine(icon, 11.5f, 11.5f, 24.5f, 24.5f);
                        e.Graphics.DrawLine(icon, 24.5f, 11.5f, 11.5f, 24.5f);
                    }
                    else if (kind == LongCaptureButtonKind.Horizontal)
                    {
                        e.Graphics.DrawLine(icon, 9, 18, 27, 18);
                        e.Graphics.DrawLines(icon, new[] {
                            new PointF(14, 13), new PointF(9, 18), new PointF(14, 23) });
                        e.Graphics.DrawLines(icon, new[] {
                            new PointF(22, 13), new PointF(27, 18), new PointF(22, 23) });
                    }
                    else
                    {
                        e.Graphics.DrawLine(icon, 18, 9, 18, 27);
                        e.Graphics.DrawLines(icon, new[] {
                            new PointF(13, 14), new PointF(18, 9), new PointF(23, 14) });
                        e.Graphics.DrawLines(icon, new[] {
                            new PointF(13, 22), new PointF(18, 27), new PointF(23, 22) });
                    }
                }
            }
        }

        private sealed class LongCaptureBorderForm : Form
        {
            public LongCaptureBorderForm(Rectangle selectedRegion)
            {
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                BackColor = Color.FromArgb(225, 36, 54);
                Bounds = new Rectangle(selectedRegion.Left - SelectionBorderSize, selectedRegion.Top - SelectionBorderSize,
                    selectedRegion.Width + SelectionBorderSize * 2, selectedRegion.Height + SelectionBorderSize * 2);
                Region outline = new Region(new Rectangle(Point.Empty, Size));
                outline.Exclude(new Rectangle(SelectionBorderSize, SelectionBorderSize, selectedRegion.Width, selectedRegion.Height));
                Region = outline;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams parameters = base.CreateParams;
                    parameters.ExStyle |= 0x20 | 0x80 | 0x08000000; // transparent, toolwindow, no-activate
                    return parameters;
                }
            }
        }
    }
}
