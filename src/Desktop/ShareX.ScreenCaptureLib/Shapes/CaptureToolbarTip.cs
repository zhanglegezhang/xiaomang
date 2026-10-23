using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    // Scoped to one capture toolbar; never activates or intercepts mouse input.
    internal sealed class CaptureToolbarTip : Form
    {
        private readonly ToolStrip strip;
        private readonly Timer delay = new Timer { Interval = 450 };
        private ToolStripItem hovered;
        private string caption;
        private Font tipFont;
        private int padding;
        private bool hiding;

        internal CaptureToolbarTip(ToolStrip strip)
        {
            this.strip = strip;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(32, 44, 52);
            strip.ShowItemToolTips = false;
            strip.MouseMove += Track;
            strip.MouseLeave += HideTip;
            strip.MouseDown += HideTip;
            strip.VisibleChanged += HideTip;
            strip.LocationChanged += HideTip;
            strip.Disposed += StripDisposed;
            strip.ItemAdded += ItemAdded;
            strip.ItemRemoved += ItemRemoved;
            foreach (ToolStripItem item in strip.Items) Attach(item);
            delay.Tick += ShowTip;
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x20; return cp; }
        }

        private void Track(object sender, MouseEventArgs e)
        {
            TrackItem(strip.GetItemAt(e.Location));
        }

        private void Attach(ToolStripItem item)
        {
            item.MouseEnter += ItemEnter;
            item.MouseLeave += HideTip;
            item.MouseDown += HideTip;
        }

        private void Detach(ToolStripItem item)
        {
            item.MouseEnter -= ItemEnter;
            item.MouseLeave -= HideTip;
            item.MouseDown -= HideTip;
        }

        private void ItemAdded(object sender, ToolStripItemEventArgs e) => Attach(e.Item);
        private void ItemRemoved(object sender, ToolStripItemEventArgs e)
        {
            Detach(e.Item);
            if (hovered == e.Item) HideTip(sender, EventArgs.Empty);
        }
        private void ItemEnter(object sender, EventArgs e) => TrackItem(sender as ToolStripItem);
        private static string Caption(ToolStripItem item)
        {
            string text = (string.IsNullOrWhiteSpace(item.ToolTipText) ? item.Text : item.ToolTipText) ?? "";
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*绘图\s*[:：]\s*", "");
            // Remove shortcut hints while retaining other descriptive text.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*[（(]\s*[A-Za-z0-9+\s]+[)）]", "");
            text = text.Replace("（", "").Replace("）", "").Replace("(", "").Replace(")", "")
                .Replace("：", "").Replace(":", "").Trim();
            return text == "文字描边" ? "文字" : text;
        }

        private void TrackItem(ToolStripItem item)
        {
            if (MouseButtons != MouseButtons.None || item is ToolStripSeparator || item is ToolStripControlHost || item?.Enabled != true)
                item = null;
            if (hovered == item) return;
            HideTip(strip, EventArgs.Empty);
            hovered = item;
            if (item != null && !string.IsNullOrWhiteSpace(Caption(item)))
            {
                delay.Interval = 450;
                delay.Start();
            }
        }

        private void HideTip(object sender, EventArgs e)
        {
            if (hiding) return;
            hiding = true;
            delay.Stop();
            hovered = null;
            Hide();
            hiding = false;
        }

        private void ShowTip(object sender, EventArgs e)
        {
            delay.Stop();
            if (Visible) { HideTip(sender, e); return; }
            if (hovered == null || strip.IsDisposed || !strip.Visible || MouseButtons != MouseButtons.None ||
                strip.GetItemAt(strip.PointToClient(Cursor.Position)) != hovered) return;
            caption = Caption(hovered);
            float scale = strip.DeviceDpi / 96f;
            tipFont?.Dispose();
            tipFont = new Font("Microsoft YaHei UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            padding = Math.Max(4, (int)Math.Round(8 * scale));
            var area = Screen.FromControl(strip).WorkingArea;
            var textSize = TextRenderer.MeasureText(caption, tipFont, new Size(Math.Min((int)(320 * scale), area.Width - padding * 2), 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Size = new Size(textSize.Width + padding * 2, textSize.Height + padding);
            var anchor = strip.RectangleToScreen(hovered.Bounds);
            int x = anchor.Left + (anchor.Width - Width) / 2;
            int y = anchor.Top - Height - (int)(5 * scale);
            if (y < area.Top) y = anchor.Bottom + (int)(5 * scale);
            Location = new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width)),
                Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
            using var path = CaptureToolbarRenderer.Round(new RectangleF(0, 0, Width, Height), 5 * scale);
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
            var owner = strip.FindForm();
            TopMost = owner?.TopMost == true;
            Show(owner);
            Invalidate();
            delay.Interval = 5000;
            delay.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CaptureToolbarRenderer.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 5 * strip.DeviceDpi / 96f);
            using var pen = new Pen(Color.FromArgb(64, 81, 93));
            e.Graphics.DrawPath(pen, path);
            TextRenderer.DrawText(e.Graphics, caption, tipFont, new Rectangle(padding, padding / 2, Width - padding * 2, Height - padding),
                Color.FromArgb(222, 230, 235), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        }

        private void StripDisposed(object sender, EventArgs e) => Dispose();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                strip.MouseMove -= Track;
                strip.MouseLeave -= HideTip;
                strip.MouseDown -= HideTip;
                strip.VisibleChanged -= HideTip;
                strip.LocationChanged -= HideTip;
                strip.Disposed -= StripDisposed;
                strip.ItemAdded -= ItemAdded;
                strip.ItemRemoved -= ItemRemoved;
                foreach (ToolStripItem item in strip.Items) Detach(item);
                delay.Dispose();
                tipFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
