using ShareX.HelpersLib;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ShareX
{
    // Registration belongs to HotkeyManager; this controller only selects the
    // input profile without showing a window or changing the input focus.
    internal sealed class InputMethodSwitchController : IDisposable
    {
        private readonly Func<bool> allowed;
        private readonly Action<string> notify;
        private bool busy, disposed;

        internal InputMethodSwitchController(Func<bool> allowed, Action<string> notify)
        {
            this.allowed = allowed; this.notify = notify;
        }

        internal async Task SwitchAsync()
        {
            if (disposed || busy) return;
            if (!allowed()) { notify("正在处理个人数据，请稍后再切换输入法。"); return; }
            busy = true;
            IntPtr foreground = GetForegroundWindow();
            try
            {
                if (!XiaomangInputMethodManager.IsRegistered)
                { notify("小蟒文本输入法尚未注册，请在「文本输入」页注册后重试。"); return; }
                XiaomangInputMethodManager.Start();
                // Async waiting keeps menus responsive while an existing startup loads.
                for (int attempt = 0; attempt < 30 && !XiaomangInputMethodManager.IsWorkerReady(); attempt++)
                {
                    await Task.Delay(100);
                    if (disposed || !allowed() || GetForegroundWindow() != foreground) return;
                }
                if (disposed || !allowed() || GetForegroundWindow() != foreground) return;
                using var profiles = new TsfInputProfiles();
                uint inputThread = GetWindowThreadProcessId(foreground, out _);
                bool activated = false;
                // GetActiveProfile describes our thread, not an unrelated editor's.
                // For another thread Windows performs the idempotent activation;
                // selecting an already active profile does not toggle its mode.
                var action = new InputMethodSwitchAction(() => activated || inputThread == GetCurrentThreadId() ? profiles.Active : null, () => profiles.Xiaomang,
                    () => XiaomangInputMethodManager.IsRegistered, XiaomangInputMethodManager.IsWorkerReady,
                    profile => { int result = profiles.ActivateFromShortcut(profile); activated = true; return result; });
                string error = action.Execute();
                if (error != null) { notify(error); return; }
                IntPtr selectedLayout = GetKeyboardLayout(0);
                if (GetKeyboardLayout(inputThread) != selectedLayout)
                {
                    if (GetForegroundWindow() != foreground) return;
                    // TSF activates the profile across the desktop, but an editor
                    // using another language also needs its input language changed.
                    if (!PostMessage(foreground, 0x0050, IntPtr.Zero, selectedLayout))
                    { notify("当前窗口未接受输入语言切换，请检查该程序的权限后重试。"); return; }
                    for (int attempt = 0; attempt < 10 && GetKeyboardLayout(inputThread) != selectedLayout; attempt++)
                    {
                        await Task.Delay(50);
                        if (disposed || GetForegroundWindow() != foreground) return;
                    }
                    if (GetKeyboardLayout(inputThread) != selectedLayout)
                    { notify("当前窗口尚未完成输入语言切换，请稍后重试。"); return; }
                }
                DebugHelper.WriteLine("Input method switch completed.");
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Switch to Xiaomang input method");
                if (!disposed) notify("切换小蟒输入法失败：" + error.Message);
            }
            finally { busy = false; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint thread);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
