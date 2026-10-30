using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX;

internal static class InputComponentUpdater
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    internal static bool RecoveryEnabled
    {
        get
        {
            using var state = Registry.CurrentUser.OpenSubKey(InputMethodRecovery.RegistryPath);
            return state?.GetValue("Enabled") is not int enabled || enabled == 1;
        }
    }
    internal static bool RegistrationMatches(string folder)
    {
        foreach (var pair in new[] { (RegistryView.Registry64, "x86_64"), (RegistryView.Registry32, "i686") })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, pair.Item1);
            using var key = machine.OpenSubKey(@"Software\Classes\CLSID\" + XiaomangInputMethodManager.ClassId + @"\InprocServer32");
            string installed = key?.GetValue("") as string;
            string expected = Path.Combine(folder, pair.Item2, "XiaomangInputMethod.dll");
            if (!File.Exists(installed) || !File.Exists(expected)) return false;
            using var first = File.OpenRead(installed);
            using var second = File.OpenRead(expected);
            if (!SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second))) return false;
        }
        return XiaomangInputMethodManager.IsRegistered;
    }

    private static async Task Register(string folder)
    {
        if (RegistrationMatches(folder)) return;
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(folder, "InputMethodSetup.exe"), "install")
        { UseShellExecute = true, Verb = "runas", WorkingDirectory = folder });
        if (process == null) throw new IOException("输入法安装器未启动。");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !RegistrationMatches(folder)) throw new IOException("系统输入法注册未完成，更新未成功。");
    }

    internal static async Task<bool> UpdateAsync(InputComponentRelease release, string cache, IProgress<int> progress, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token)) throw new IOException("输入法组件正在更新。");
        string stage = Path.Combine(cache, "text-stage-" + Guid.NewGuid().ToString("N"));
        string destination = XiaomangInputMethodManager.BundleDirectory;
        try
        {
            InputComponentCatalog.Validate(release, Program.ProductVersion);
            if (await ComponentBundles.IsValidAsync(release.Bundle, destination, token) && RegistrationMatches(destination)) return false;
            await ComponentBundles.EnsureAsync(release.Bundle, stage, cache, progress, token);
            bool wasRegistered = XiaomangInputMethodManager.IsRegistered;
            bool resume = !wasRegistered || RecoveryEnabled;
            bool? profileEnabled = null;
            if (wasRegistered && Program.MainForm != null)
                profileEnabled = await Program.MainForm.InvokeAsync(() => { using var profiles = new TsfInputProfiles(); return profiles.XiaomangEnabled; });
            async Task RestoreDisabledProfile()
            {
                if (profileEnabled == false && Program.MainForm != null)
                    await Program.MainForm.InvokeAsync(() => { using var profiles = new TsfInputProfiles(); profiles.EnableXiaomang(false); });
            }
            bool registrationAttempted = false;
            async Task Resume()
            {
                if (!resume) return;
                XiaomangInputMethodManager.Start();
                for (int i = 0; i < 100; i++)
                {
                    if (XiaomangInputMethodManager.IsWorkerReady()) return;
                    await Task.Delay(100);
                }
                throw new IOException("输入法后台未在规定时间内就绪。");
            }
            await InputComponentTransaction.CommitAsync(release.Bundle, stage, destination,
                () => Task.Run(XiaomangInputMethodManager.PauseForPersonalData),
                async () => { registrationAttempted = !RegistrationMatches(destination); await Register(destination); await RestoreDisabledProfile(); await Resume(); },
                async () =>
                {
                    if (wasRegistered && registrationAttempted) await Register(destination);
                    await RestoreDisabledProfile();
                    if (!wasRegistered && registrationAttempted && XiaomangInputMethodManager.IsRegistered)
                    {
                        using var undo = Process.Start(new ProcessStartInfo(Path.Combine(stage,"InputMethodSetup.exe"),"uninstall")
                        { UseShellExecute=true,Verb="runas",WorkingDirectory=stage });
                        if(undo==null)throw new IOException("无法撤回本次新增的输入法注册。");
                        await undo.WaitForExitAsync();
                        if(undo.ExitCode!=0 || XiaomangInputMethodManager.IsRegistered)throw new IOException("本次新增的输入法注册尚未撤回。");
                    }
                    if (wasRegistered) await Resume();
                }, token);
            return true;
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
            gate.Release();
        }
    }
}
