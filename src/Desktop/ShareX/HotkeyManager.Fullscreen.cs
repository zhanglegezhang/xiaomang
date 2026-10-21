using ShareX.HelpersLib;
using System.Collections.Generic;
using System.Linq;

namespace ShareX
{
    public partial class HotkeyManager
    {
        private ForegroundFullscreenMonitor fullscreenMonitor;
        private readonly HashSet<HotkeySettings> fullscreenReleased = new();
        internal bool FullscreenSuspended { get; private set; }
        internal bool CanRunGlobalActions => !IgnoreHotkeys && !FullscreenSuspended && !Program.Settings.DisableHotkeys;
        private bool fullscreenRecording;

        private void InitializeFullscreenMonitoring()
        {
            fullscreenMonitor = new ForegroundFullscreenMonitor(hotkeyForm, ApplyFullscreenState);
            RefreshFullscreenPolicy();
        }

        internal void RefreshFullscreenPolicy()
            => fullscreenMonitor?.Update(Program.Settings.DisableHotkeysOnFullscreen);

        private void ApplyFullscreenState(bool fullscreen)
        {
            bool recording = ScreenRecordManager.IsRecording;
            if (FullscreenSuspended == fullscreen && fullscreenRecording == recording) return;
            FullscreenSuspended = fullscreen;
            fullscreenRecording = recording;
            if (Hotkeys == null) return;

            bool restoreFailed = false;
            foreach (HotkeySettings entry in Hotkeys.ToArray())
            {
                if (fullscreen && !IsRecordingControl(entry.TaskSettings.Job))
                {
                    // Retain the binding and manual disabled state; release only the OS registration.
                    if (entry.HotkeyInfo.Status == HotkeyStatus.Registered)
                    {
                        UnregisterHotkey(entry, false);
                        fullscreenReleased.Add(entry);
                    }
                }
                else if (fullscreenReleased.Remove(entry))
                {
                    RegisterHotkey(entry);
                    restoreFailed |= entry.HotkeyInfo.Status == HotkeyStatus.Failed;
                }
            }
            if (restoreFailed && Program.MainForm != null)
                TaskHelpers.ShowNotificationTip("部分快捷键被其他程序占用，未能恢复。请打开「快捷键」查看并调整按键。", "小蟒快捷键", 5000);
        }

        internal static bool IsRecordingControl(HotkeyType job)
            => ScreenRecordManager.IsRecording && job is
                HotkeyType.ScreenRecorder or HotkeyType.ScreenRecorderActiveWindow or
                HotkeyType.ScreenRecorderCustomRegion or HotkeyType.StartScreenRecorder or
                HotkeyType.ScreenRecorderGIF or HotkeyType.ScreenRecorderGIFActiveWindow or
                HotkeyType.ScreenRecorderGIFCustomRegion or HotkeyType.StartScreenRecorderGIF or
                HotkeyType.StopScreenRecording or HotkeyType.PauseScreenRecording or HotkeyType.AbortScreenRecording;
    }
}
