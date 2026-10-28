using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;

namespace ShareX
{
    // Local lifecycle state only; never included in product defaults or personal-data archives.
    internal static class InputMethodRecovery
    {
        internal const string RegistryPath = @"Software\Xiaomang\InputMethodRecovery";

        internal static void Publish(string workerPath, bool enabled, string reason)
        {
            string name = @"Local\xiaomang-input-v1-" + Process.GetCurrentProcess().SessionId + ".RecoveryLaunch";
            using var mutex = new Mutex(false, name);
            bool acquired;
            try { acquired = mutex.WaitOne(1500); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("输入后台正在恢复，请稍后重试。");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
                // Disable before changing the path so readers never use a partially published entry.
                key.SetValue("Enabled", 0, RegistryValueKind.DWord);
                key.SetValue("WorkerPath", Path.GetFullPath(workerPath), RegistryValueKind.String);
                key.SetValue("StopEvent", @"Local\XiaomangInputStop." + WindowsIdentity.GetCurrent().User.Value + "." + Process.GetCurrentProcess().SessionId, RegistryValueKind.String);
                key.SetValue("Reason", reason, RegistryValueKind.String);
                if (enabled)
                {
                    key.SetValue("Attempts", 0, RegistryValueKind.DWord);
                    key.SetValue("LastAttempt", 0L, RegistryValueKind.QWord);
                    key.SetValue("WindowStart", 0L, RegistryValueKind.QWord);
                }
                key.SetValue("Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
            }
            finally { mutex.ReleaseMutex(); }
        }
    }
}
