using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    // The native scrolling canvas extends past this clipping viewport. Its native
    // rail stays outside the visible area; scrolling and ScrollControlIntoView
    // still use WinForms' existing implementation.
    internal sealed class SettingsDesignHost : Panel
    {
        internal SettingsDesignSurface Surface { get; }
        private readonly Rail rail;
        internal SettingsDesignHost(SettingsDesignSurface surface)
        {
            Surface = surface; Name = "settingsDesignHost"; Dock = DockStyle.Fill;
            BackColor = SettingsDesignSurface.Canvas;
            surface.Dock = DockStyle.None;
            rail = new Rail(surface);
            Controls.Add(surface); Controls.Add(rail);
            surface.Scroll += (_, _) => rail.Invalidate();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if(Surface == null || rail == null)return;
            Surface.SetBounds(0, 0, Width + SystemInformation.VerticalScrollBarWidth, Height);
            int width = (int)Math.Round(12 * DeviceDpi / 96d);
            rail.SetBounds(Math.Max(0, Width - width), 0, width, Height);
            rail.BringToFront(); rail.Invalidate();
        }
        internal void RefreshRail() => rail.Invalidate();
        private sealed class Rail : Control
        {
            private readonly SettingsDesignSurface surface;
            private bool dragging, hover;
            private int offset;
            internal Rail(SettingsDesignSurface surface) { this.surface = surface; Name = "settingsScrollRail"; DoubleBuffered = true; TabStop = false; }
            private int Maximum => Math.Max(0, surface.AutoScrollMinSize.Height - surface.ClientSize.Height);
            private Rectangle Thumb
            {
                get {
                    if(Maximum == 0 || Height < 1)return Rectangle.Empty;
                    int height = Math.Min(Height, Math.Max((int)Math.Round(28 * DeviceDpi / 96d), (int)((long)Height * surface.ClientSize.Height / Math.Max(1, surface.AutoScrollMinSize.Height))));
                    int top = (int)((long)Math.Clamp(-surface.AutoScrollPosition.Y, 0, Maximum) * (Height - height) / Maximum);
                    int pad = Math.Max(2, (int)Math.Round(3 * DeviceDpi / 96d));
                    return new Rectangle(pad, top, Math.Max(1, Width - pad * 2), height);
                }
            }
            protected override void OnPaint(PaintEventArgs e) {
                e.Graphics.Clear(SettingsDesignSurface.Canvas);var r = Thumb;if(r.IsEmpty)return;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = MainMenuAppearance.Round(r, Math.Max(1, r.Width / 2));
                using var brush = new SolidBrush(hover || dragging ? Color.FromArgb(110, 130, 144) : Color.FromArgb(65, 83, 96));e.Graphics.FillPath(brush, path);
            }
            protected override void OnMouseDown(MouseEventArgs e) {
                base.OnMouseDown(e);if(e.Button != MouseButtons.Left || Maximum == 0)return;
                var thumb = Thumb;if(thumb.Contains(e.Location)){dragging = Capture = true;offset = e.Y - thumb.Top;}
                else surface.AutoScrollPosition = new Point(0, Math.Clamp(-surface.AutoScrollPosition.Y + (e.Y < thumb.Top ? -1 : 1) * surface.ClientSize.Height, 0, Maximum));
                Invalidate();
            }
            protected override void OnMouseMove(MouseEventArgs e) {
                base.OnMouseMove(e);hover = Thumb.Contains(e.Location);
                if(dragging){int travel = Height - Thumb.Height;if(travel > 0)surface.AutoScrollPosition = new Point(0, (int)((long)Math.Clamp(e.Y - offset, 0, travel) * Maximum / travel));}Invalidate();
            }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e);dragging = Capture = false;Invalidate(); }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e);if(!Capture)dragging = false; }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e);hover = false;Invalidate(); }
            protected override void OnMouseWheel(MouseEventArgs e) { if(e is HandledMouseEventArgs h)h.Handled = true;surface.ScrollFromNumber(e);Invalidate(); }
        }
    }
}
