using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    internal sealed class RegionCaptureRefreshScheduler
    {
        private Point previousPosition;
        private MouseButtons previousButtons;
        private Keys previousKeys;
        private long lastFrame = -1000;
        private long lastActivity = -1000;
        private bool inputPending = true;

        internal void RequestInputRefresh() => inputPending = true;

        internal static int GetInterval(int fpsLimit) =>
            (int)Math.Ceiling(1000d / Math.Clamp(fpsLimit > 0 ? fpsLimit : 60, 1, 60));

        internal bool ShouldRender(long milliseconds, Point position, MouseButtons buttons, Keys keys,
            int fpsLimit, bool continuous)
        {
            bool changed = inputPending || position != previousPosition || buttons != previousButtons || keys != previousKeys;
            if (changed) lastActivity = milliseconds;
            previousPosition = position;
            previousButtons = buttons;
            previousKeys = keys;
            // Keep animations, delayed tooltips and asynchronously detected windows alive
            // at 10 Hz while idle. Moving/dragging and the image editor use normal cadence.
            int interval = continuous || buttons != MouseButtons.None || milliseconds - lastActivity < 150 ?
                GetInterval(fpsLimit) : Math.Max(100, GetInterval(fpsLimit));
            if (milliseconds - lastFrame < interval) return false;
            lastFrame = milliseconds;
            inputPending = false;
            return true;
        }
    }
}
