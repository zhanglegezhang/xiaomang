using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int ReadWidgetWindowStyle(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RestoreWidgetZOrder(IntPtr window, IntPtr after,
            int x, int y, int width, int height, uint flags);

        private void EnsureWidgetTopMost()
        {
            if (!ready || closingWidget || IsDisposed || Disposing || !Visible || animatingGenie) return;
            RestoreTopMostIfLost(this);
            RestoreTopMostIfLost(compactOverlay);
            RestoreTopMostIfLost(stickerOverlay);
            if (frameCornerOverlays != null)
                foreach (Form corner in frameCornerOverlays) RestoreTopMostIfLost(corner);
        }

        private static void RestoreTopMostIfLost(Form window)
        {
            if (window == null || window.IsDisposed || !window.Visible || !window.IsHandleCreated) return;
            // Managed TopMost can stay true after native Z-order changes. Read the
            // real style, and repair only when needed without activation or layout.
            if ((ReadWidgetWindowStyle(window.Handle, -20) & 0x8) == 0)
                RestoreWidgetZOrder(window.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
        }
    }
}
