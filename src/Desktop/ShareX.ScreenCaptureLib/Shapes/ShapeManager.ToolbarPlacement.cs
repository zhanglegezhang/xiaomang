using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        private bool toolbarPositionedByUser, toolbarCompacted, toolbarAutoHideActive;
        private Panel toolbarRevealHandle;
        private Timer toolbarRevealTimer;
        private Rectangle expandedToolbarBounds;

        internal static bool TryPlaceToolbarOutside(Rectangle screen, Rectangle selection, Size size, out Point position)
        {
            const int gap = 8;
            int centerX = Math.Clamp(selection.X + (selection.Width - size.Width) / 2, screen.Left, Math.Max(screen.Left, screen.Right - size.Width));
            int centerY = Math.Clamp(selection.Y + (selection.Height - size.Height) / 2, screen.Top, Math.Max(screen.Top, screen.Bottom - size.Height));
            var candidates = new[]
            {
                new Point(centerX, selection.Top - size.Height - gap), new Point(centerX, selection.Bottom + gap),
                new Point(selection.Left - size.Width - gap, centerY), new Point(selection.Right + gap, centerY)
            };
            foreach (var candidate in candidates)
                if (screen.Contains(new Rectangle(candidate, size))) { position = candidate; return true; }
            position = new Point(centerX, screen.Top);
            return false;
        }

        private Rectangle ToolbarSelection => ValidRegions.Length > 0 ?
            Form.RectangleToScreen(Rectangle.Ceiling(ValidRegions[0].Rectangle)) : Form.RectangleToScreen(Form.ClientArea);

        private void RefreshEditingToolbar()
        {
            if (menuForm == null || menuForm.IsDisposed || tsStyle == null || !ToolbarCreated) return;
            Rectangle selection = ToolbarSelection;
            Rectangle bounds = toolbarCompacted ? expandedToolbarBounds : menuForm.Bounds;
            Rectangle screen = Form.IsFullscreen ? Screen.FromRectangle(selection).Bounds : Form.RectangleToScreen(Form.ClientArea);
            bool shouldHide = IsEditingText && selection.IntersectsWith(bounds) &&
                !TryPlaceToolbarOutside(screen, selection, bounds.Size, out _);
            toolbarAutoHideActive = shouldHide;
            if (!shouldHide)
            {
                toolbarRevealTimer?.Stop();
                SetToolbarCompacted(false);
                return;
            }
            if (toolbarRevealHandle == null)
            {
                toolbarRevealHandle = new Panel { Name = "captureToolbarReveal", BackColor = Color.FromArgb(255, 139, 67), Cursor = Cursors.Hand };
                toolbarRevealHandle.MouseEnter += (_, _) => SetToolbarCompacted(false);
                menuForm.Controls.Add(toolbarRevealHandle);
                toolbarRevealTimer = new Timer { Interval = 120 };
                toolbarRevealTimer.Tick += (_, _) => RefreshToolbarReveal();
                menuForm.Disposed += (_, _) => { toolbarRevealTimer.Stop(); toolbarRevealTimer.Dispose(); };
            }
            if (!toolbarRevealTimer.Enabled)
            {
                SetToolbarCompacted(true);
                toolbarRevealTimer.Start();
            }
        }

        private void RefreshToolbarReveal()
        {
            if (!toolbarAutoHideActive || !IsEditingText || Form.IsClosing) { RefreshEditingToolbar(); return; }
            if (!toolbarCompacted && !draggingToolbar && !menuForm.Bounds.Contains(Cursor.Position) &&
                !styleBorder.DropDown.Visible && !styleFill.DropDown.Visible &&
                !tsMain.Items.OfType<ToolStripDropDownItem>().Any(item => item.DropDown.Visible)) SetToolbarCompacted(true);
        }

        private void SetToolbarCompacted(bool compact)
        {
            if (menuForm == null || menuForm.IsDisposed || toolbarCompacted == compact) return;
            if (compact)
            {
                expandedToolbarBounds = menuForm.Bounds;
                toolbarCompacted = true;
                tsMain.Visible = tsStyle.Visible = false;
                Rectangle screen = Screen.FromRectangle(expandedToolbarBounds).Bounds;
                Size size = new Size(StyleScale(44), StyleScale(6));
                toolbarRevealHandle.Bounds = new Rectangle(Point.Empty, size);
                toolbarRevealHandle.Visible = true;
                menuForm.ClientSize = size;
                menuForm.Location = new Point(Math.Clamp(expandedToolbarBounds.X, screen.Left, Math.Max(screen.Left, screen.Right - size.Width)), screen.Top);
                var old = menuForm.Region;
                menuForm.Region = new Region(new Rectangle(Point.Empty, size)); old?.Dispose();
            }
            else
            {
                toolbarCompacted = false;
                toolbarRevealHandle.Visible = false;
                tsMain.Visible = true; tsStyle.Visible = styleRowVisible;
                lastMainOutline = lastStyleOutline = Rectangle.Empty;
                menuForm.Bounds = expandedToolbarBounds;
                LayoutStyleToolbar();
            }
        }
    }
}
