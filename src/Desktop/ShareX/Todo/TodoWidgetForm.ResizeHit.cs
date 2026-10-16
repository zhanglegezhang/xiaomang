using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private BottomResizeHitFilter bottomResizeHitFilter;

        private void BeginGripResize(Panel grip, Point screenPoint)
        {
            resizing = true;
            collapseTimer.Stop();
            resizeStartMouse = screenPoint;
            resizeStartBounds = Bounds;
            grip.Capture = true;
        }

        private Panel GetBottomResizeHit(Point point, Control target)
        {
            int left = moduleSidebar.Right, bottom = ClientSize.Height, right = ClientSize.Width;
            int corner = D(14), edge = D(8);
            if (point.X < left || point.X >= right || point.Y < bottom - corner || point.Y >= bottom) return null;
            string name = point.X < left + corner ? "LB" : point.X >= right - corner ? "RB" : point.Y >= bottom - edge ? "B" : null;
            if (name == null) return null;
            // Keep controls under the enlarged zone usable; the existing visible grips retain priority.
            for (Control control = target; control != null && control != this; control = control.Parent)
            {
                if (resizeGrips.Contains(control)) return null;
                if (control is ButtonBase or TextBoxBase or ListBox or ComboBox or UpDownBase or ScrollBar
                    or SlimListScrollBar or ArrowlessTextScrollBar or System.Windows.Forms.Integration.ElementHost) return null;
            }
            return resizeGrips.FirstOrDefault(grip => (string)grip.Tag == name);
        }

        private sealed class BottomResizeHitFilter : IMessageFilter
        {
            private readonly TodoWidgetForm owner;
            private bool enabled;
            internal BottomResizeHitFilter(TodoWidgetForm owner) => this.owner = owner;
            internal void SetEnabled(bool value)
            {
                if (value == enabled) return;
                enabled = value;
                if (value) Application.AddMessageFilter(this); else Application.RemoveMessageFilter(this);
            }
            public bool PreFilterMessage(ref Message message)
            {
                if (!enabled || message.Msg is not (0x200 or 0x201) || owner.IsDisposed || !owner.Visible
                    || !owner.expanded || owner.resizing || owner.animatingGenie || owner.titleDragging || owner.compactDragging) return false;
                if (message.Msg == 0x200 && ((long)message.WParam & 0x73) != 0) return false;
                Control target = Control.FromChildHandle(message.HWnd);
                if (target == null || target.FindForm() != owner) return false;
                long coordinates = (long)message.LParam;
                Point screenPoint = new(unchecked((short)coordinates), unchecked((short)(coordinates >> 16)));
                if (!NativeMethods.ClientToScreen(message.HWnd, ref screenPoint)) return false;
                Panel grip = owner.GetBottomResizeHit(owner.PointToClient(screenPoint), target);
                if (grip == null) return false;
                owner.collapseTimer.Stop();
                Cursor.Current = grip.Cursor;
                if (message.Msg == 0x201) owner.BeginGripResize(grip, screenPoint);
                return true;
            }
        }
    }
}
