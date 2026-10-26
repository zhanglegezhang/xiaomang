using System;
using System.Windows.Forms;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private Timer compactIdleTimer;
        private long compactLastActivity = Environment.TickCount64;
        private bool changingIdleSize;
        private int CurrentCompactWidth => compactLabel.IdleCompact ? CollapsedWidth / 2 : CollapsedWidth;

        private void InitializeCompactIdle()
        {
            compactIdleTimer = new Timer { Interval = 1000 };
            compactIdleTimer.Tick += (_, _) => CheckCompactIdle();
            compactIdleTimer.Start();
        }

        private void CheckCompactIdle()
        {
            if (!ready || IsDisposed || Disposing) return;
            if (!Visible || expanded || animatingGenie || compactDragging || compactRecording || stoppingVoiceRecording ||
                titleDragging || resizing || searchMenu?.Visible == true || compactLabel.PointerInside ||
                Bounds.Contains(Cursor.Position) || compactOverlay?.Bounds.Contains(Cursor.Position) == true)
            {
                compactLastActivity = Environment.TickCount64;
                return;
            }
            if (Environment.TickCount64 - compactLastActivity >= 60000) SetCompactIdle(true);
        }

        private void ResetCompactIdle()
        {
            compactLastActivity = Environment.TickCount64;
            SetCompactIdle(false);
        }

        private void SetCompactIdle(bool idle)
        {
            if (compactLabel.IdleCompact == idle || expanded || changingIdleSize) return;
            compactLabel.IdleCompact = idle;
            changingIdleSize = true;
            try { SetExpanded(false); }
            finally { changingIdleSize = false; }
        }
    }
}
