using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShareX
{
    /// <summary>Shared single-row caption for the TXT and Markdown scratch editors.</summary>
    internal sealed class ScratchCaptionBar : Panel
    {
        internal readonly Label FileLabel;
        internal readonly Button SaveAsButton;
        private readonly Form owner;
        private readonly CaptionIcon icon;
        private readonly CaptionButton minimize;
        private readonly CaptionButton maximize;
        private readonly CaptionButton close;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        internal static int ResizeBorder(int dpi) => GetSystemMetricsForDpi(33, (uint)dpi) + GetSystemMetricsForDpi(92, (uint)dpi);
        internal static void RefreshFrame(IntPtr window) => SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96f);
        internal int PreferredCaptionHeight => Math.Max(Scale(24), Font.Height + Scale(4));
        // WM_NCCALCSIZE already excludes the native top resize frame from the client area.
        private static Rectangle ContentBounds(Control control, Form owner) =>
            control.ClientRectangle;

        internal ScratchCaptionBar(Form owner)
        {
            this.owner = owner;
            Name = "ScratchCaptionBar";
            Margin = Padding.Empty;
            BackColor = ScratchEditorForm.CaptionBackgroundColor;
            ForeColor = ScratchEditorForm.EditorTextColor;
            Font = owner.Font;
            DoubleBuffered = true;
            icon = new CaptionIcon(owner) { TabStop = false };
            FileLabel = new CaptionLabel(owner)
            {
                AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ForeColor, Font = Font, TabStop = false, UseMnemonic = false
            };
            SaveAsButton = new CaptionButton(3, owner)
            {
                Name = "ScratchSaveAs", Text = "另存为", FlatStyle = FlatStyle.Flat,
                Font = Font, BackColor = BackColor, ForeColor = ForeColor,
                UseVisualStyleBackColor = false, AccessibleName = "另存为", Margin = Padding.Empty
            };
            SaveAsButton.FlatAppearance.BorderSize = 0;
            SaveAsButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(197, 217, 237);
            SaveAsButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 187, 219);
            minimize = new CaptionButton(0, owner) { AccessibleName = "最小化", Name = "ScratchMinimize" };
            maximize = new CaptionButton(1, owner) { AccessibleName = "最大化", Name = "ScratchMaximize" };
            close = new CaptionButton(2, owner) { AccessibleName = "关闭", Name = "ScratchClose" };
            minimize.Click += (sender, args) => owner.WindowState = FormWindowState.Minimized;
            maximize.Click += (sender, args) => owner.WindowState = owner.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
            close.Click += (sender, args) => owner.Close();
            Controls.AddRange(new Control[] { icon, FileLabel, SaveAsButton, minimize, maximize, close });
        }

        protected override void OnLayout(LayoutEventArgs args)
        {
            base.OnLayout(args);
            if (close == null) return;
            int width = Scale(46);
            int right = ClientSize.Width;
            foreach (Control button in new Control[] { close, maximize, minimize })
            {
                right -= width;
                button.SetBounds(right, 0, width, ClientSize.Height);
            }
            int saveWidth = Math.Max(Scale(76), TextRenderer.MeasureText(SaveAsButton.Text, SaveAsButton.Font).Width + Scale(20));
            right -= saveWidth;
            SaveAsButton.SetBounds(right, 0, saveWidth, ClientSize.Height);
            SaveAsButton.Padding = Padding.Empty;
            icon.SetBounds(Scale(8), 0, Scale(24), ClientSize.Height);
            FileLabel.SetBounds(Scale(38), 0, Math.Max(0, right - Scale(46)), ClientSize.Height);
        }

        internal int HitTest(Point screen)
        {
            Point point = PointToClient(screen);
            if (!ClientRectangle.Contains(point)) return 1; // HTCLIENT
            if (icon.Bounds.Contains(point)) return 3; // HTSYSMENU
            if (SaveAsButton.Bounds.Contains(point) || minimize.Bounds.Contains(point) ||
                maximize.Bounds.Contains(point) || close.Bounds.Contains(point)) return 1;
            return 2; // HTCAPTION: native drag, double-click maximize, system menu and snap.
        }

        internal void RefreshWindowState()
        {
            maximize.AccessibleName = owner.WindowState == FormWindowState.Maximized ? "还原" : "最大化";
            // Keep all caption controls within the same full client row.
            SaveAsButton.Padding = Padding.Empty;
            foreach (Control control in Controls) control.Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            if (HandleCaptionMessage(owner, ref message, 2)) return;
            base.WndProc(ref message);
        }

        // Child HWNDs receive their own non-client mouse messages. Forward those to
        // the form so Windows performs the move, double-click and system-menu actions.
        private static bool HandleCaptionMessage(Form owner, ref Message message, int hit)
        {
            if (message.Msg == 0x0084) { message.Result = (IntPtr)hit; return true; }
            if (message.Msg is 0x00A1 or 0x00A3 or 0x00A5)
            {
                message.Result = SendMessage(owner.Handle, message.Msg, (IntPtr)hit, message.LParam);
                return true;
            }
            return false;
        }

        private sealed class CaptionLabel(Form owner) : Label
        {
            protected override void OnPaint(PaintEventArgs args)
            {
                TextRenderer.DrawText(args.Graphics, Text, Font, ContentBounds(this, owner), ForeColor,
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping);
            }
            protected override void WndProc(ref Message message)
            {
                if (HandleCaptionMessage(owner, ref message, 2)) return;
                base.WndProc(ref message);
            }
        }

        private sealed class CaptionIcon(Form owner) : Control
        {
            protected override void OnPaint(PaintEventArgs args)
            {
                base.OnPaint(args);
                int size = (int)Math.Round(16 * DeviceDpi / 96f);
                if (owner.Icon != null) args.Graphics.DrawIcon(owner.Icon,
                    new Rectangle((Width - size) / 2, (ContentBounds(this, owner).Height - size) / 2, size, size));
            }
            protected override void WndProc(ref Message message)
            {
                if (HandleCaptionMessage(owner, ref message, 3)) return;
                base.WndProc(ref message);
            }
        }

        private sealed class CaptionButton : Button
        {
            private readonly int kind;
            private readonly Form owner;
            private bool hover;
            internal CaptionButton(int kind, Form owner)
            {
                this.kind = kind;
                this.owner = owner;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = ScratchEditorForm.CaptionBackgroundColor;
                ForeColor = ScratchEditorForm.EditorTextColor;
                UseVisualStyleBackColor = false;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnMouseEnter(EventArgs args) { hover = true; Invalidate(); base.OnMouseEnter(args); }
            protected override void OnMouseLeave(EventArgs args) { hover = false; Invalidate(); base.OnMouseLeave(args); }
            protected override void OnPaint(PaintEventArgs args)
            {
                var graphics = args.Graphics;
                graphics.Clear(hover ? kind == 2 ? Color.FromArgb(196, 43, 28) : Color.FromArgb(197, 217, 237) : BackColor);
                if (kind == 3)
                {
                    TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, ForeColor,
                        TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
                    return;
                }
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = DeviceDpi / 96f;
                float size = 10 * scale;
                float x = (Width - size) / 2f, y = (ContentBounds(this, owner).Height - size) / 2f;
                using var pen = new Pen(hover && kind == 2 ? Color.White : ForeColor, Math.Max(1, scale));
                if (kind == 0) graphics.DrawLine(pen, x, y + size / 2, x + size, y + size / 2);
                else if (kind == 2)
                {
                    graphics.DrawLine(pen, x, y, x + size, y + size);
                    graphics.DrawLine(pen, x + size, y, x, y + size);
                }
                else if (owner.WindowState == FormWindowState.Maximized)
                {
                    graphics.DrawLines(pen, new[] { new PointF(x + 3 * scale, y + 2 * scale), new PointF(x + 3 * scale, y),
                        new PointF(x + size, y), new PointF(x + size, y + size - 3 * scale), new PointF(x + size - 2 * scale, y + size - 3 * scale) });
                    graphics.DrawRectangle(pen, x, y + 3 * scale, size - 3 * scale, size - 3 * scale);
                }
                else graphics.DrawRectangle(pen, x, y, size, size);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
            }
        }
    }
}
