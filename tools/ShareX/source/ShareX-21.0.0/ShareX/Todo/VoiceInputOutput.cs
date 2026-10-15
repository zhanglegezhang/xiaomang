using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal static class VoiceInputComponent
    {
        internal const string ClassId = "{7FD8708D-A55C-492D-918F-1802DC259B3A}";
        internal static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "XiaomangVoiceInput", "0.1.0");
        internal static string LibraryPath => Path.Combine(InstallFolder, "x86_64", "XiaomangVoiceInput.dll");
        internal static bool Installed
        {
            get => RegistrationState == VoiceInputRegistrationState.Registered;
        }
        internal static VoiceInputRegistrationState RegistrationState
        {
            get
            {
                if (!Environment.Is64BitOperatingSystem || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                    return VoiceInputRegistrationState.Unsupported;
                try
                {
                    bool complete = true, found = false;
                    foreach (var item in new[] { (RegistryView.Registry64, "x86_64"), (RegistryView.Registry32, "i686") })
                    {
                        string folder = Path.Combine(InstallFolder, item.Item2), dll = Path.Combine(folder, "XiaomangVoiceInput.dll");
                        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, item.Item1);
                        using RegistryKey key = machine.OpenSubKey(@"SOFTWARE\Classes\CLSID\" + ClassId + @"\InprocServer32");
                        using RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, item.Item1);
                        using RegistryKey ownOverride = user.OpenSubKey(@"SOFTWARE\Classes\CLSID\" + ClassId);
                        using RegistryKey serverOverride = ownOverride?.OpenSubKey("InprocServer32");
                        string overridden = serverOverride?.GetValue("") as string;
                        using RegistryKey tip = machine.OpenSubKey(@"SOFTWARE\Microsoft\CTF\TIP\" + ClassId);
                        using RegistryKey category = tip?.OpenSubKey(@"Category\Category\{B5A73CD1-8355-426B-A161-259808F26B14}\" + ClassId);
                        using RegistryKey languages = tip?.OpenSubKey("LanguageProfile");
                        bool profile = false;
                        if (languages != null) foreach (string language in languages.GetSubKeyNames())
                        {
                            using RegistryKey entry = languages.OpenSubKey(language + @"\{96A307E7-E071-429B-A384-D9485884515F}");
                            if (entry != null) { profile = true; break; }
                        }
                        bool file = File.Exists(dll), helper = File.Exists(Path.Combine(folder, "RegisterVoiceInput.exe"));
                        // Unregistered DLLs held by a host after uninstall are inert
                        // files, not a broken registration. They may be removed later.
                        found |= key != null || ownOverride != null || tip != null;
                        complete &= file && helper && category != null && profile &&
                            string.Equals(key?.GetValue("") as string, dll, StringComparison.OrdinalIgnoreCase) &&
                            (ownOverride == null || string.Equals(overridden, dll, StringComparison.OrdinalIgnoreCase));
                    }
                    var payload = VoiceInputPayload.Inspect(Path.Combine(AppContext.BaseDirectory, "ThirdParty", "VoiceInput"), InstallFolder);
                    if (payload == VoiceInputPayloadState.Missing) return VoiceInputRegistrationState.BundleIncomplete;
                    if (!complete) return found ? VoiceInputRegistrationState.NeedsRepair : VoiceInputRegistrationState.Unregistered;
                    return payload == VoiceInputPayloadState.Matching ? VoiceInputRegistrationState.Registered : VoiceInputRegistrationState.NeedsUpdate;
                }
                catch { return VoiceInputRegistrationState.NeedsRepair; }
            }
        }
        internal static async Task InstallAsync(bool install, bool forceRepair = false)
        {
            string setup = Path.Combine(AppContext.BaseDirectory, "ThirdParty", "VoiceInput", "VoiceInputSetup.exe");
            if (!File.Exists(setup)) throw new FileNotFoundException("语音输入组件安装器缺失，请重新获取完整程序。", setup);
            try
            {
                using Process process = Process.Start(new ProcessStartInfo(setup, install ? forceRepair ? "repair" : "install" : "uninstall")
                { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(setup) });
                if (process == null) throw new InvalidOperationException("无法启动组件安装器。");
                await process.WaitForExitAsync();
                if (process.ExitCode == 6) throw new InvalidOperationException("组件操作已完成，但旧注册项没有完全清理。请检查安装器提示的日志，再点击修复注册重试。");
                if (process.ExitCode != 0) throw new InvalidOperationException($"组件{(install ? "安装" : "卸载")}失败（{process.ExitCode}）。现有语音模型未改动。");
                if (install && !Installed) throw new InvalidOperationException("安装器已结束，但未检测到完整注册。");
                if (!install && RegistrationState != VoiceInputRegistrationState.Unregistered)
                    throw new InvalidOperationException("卸载程序已结束，但系统注册仍有残留，请查看安装日志后重试。");
            }
            catch (Win32Exception error) when (error.NativeErrorCode == 1223)
            { throw new OperationCanceledException("已取消 Windows 管理员授权。"); }
        }
    }

    internal readonly record struct VoiceInputResult(bool Success, uint Status, uint Length);

    internal sealed class VoiceInputOutput
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int OpenDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void CloseDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate int CommandDelegate(uint sequence, uint action, ulong session, ulong window, [MarshalAs(UnmanagedType.LPWStr)] string text, uint length);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool AckDelegate(uint sequence, ulong session, out uint status, out uint length, out uint pid);
        private static IntPtr library;
        private static OpenDelegate activate;
        private static CloseDelegate close;
        private static CommandDelegate command;
        private static AckDelegate ack;
        private static Mutex lease;
        private static uint sequence;
        private readonly ulong id = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
        private readonly IntPtr window;
        private readonly IntPtr focus;
        private VoiceTypingInputState inputState;
        private Exception inputStateError;
        private readonly uint targetPid;
        private bool started, ended, attempted;
        internal bool HasComposition => attempted && !ended;
        internal string Text { get; private set; } = "";

        internal VoiceInputOutput(IntPtr window, IntPtr focus)
        {
            this.window = window;
            this.focus = focus;
            GetWindowThreadProcessId(focus != IntPtr.Zero ? focus : window, out targetPid);
        }
        internal void CaptureInputState()
        {
            // Capture before creating/showing feedback controls can affect Windows IME state.
            try { inputState = VoiceTypingInputState.Capture(window, focus); }
            catch (Exception error) { inputStateError = error; }
        }
        private static void EnsureBroker()
        {
            if (library != IntPtr.Zero) return;
            if (!VoiceInputComponent.Installed) throw new InvalidOperationException("系统语音输入组件未完整注册，请到输入法 → 小蟒语音输入注册或修复组件。");
            lease = new Mutex(false, @"Local\XiaomangVoiceInput.Broker." + WindowsIdentity.GetCurrent().User.Value);
            bool held;
            try { held = lease.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
            if (!held) { lease.Dispose(); lease = null; throw new InvalidOperationException("另一个小蟒程序正在使用系统语音输入组件。"); }
            try
            {
                library = NativeLibrary.Load(VoiceInputComponent.LibraryPath);
                T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
                activate = Function<OpenDelegate>("VoiceActivate"); close = Function<CloseDelegate>("VoiceCloseChannel");
                command = Function<CommandDelegate>("VoiceCommand"); ack = Function<AckDelegate>("VoiceAck");
                Marshal.ThrowExceptionForHR(Function<OpenDelegate>("VoiceOpenChannel")());
            }
            catch
            {
                if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; }
                lease.ReleaseMutex(); lease.Dispose(); lease = null; throw;
            }
        }
        internal async Task<VoiceInputResult> BeginAsync(CancellationToken token)
        {
            // Called on the UI STA, outside keyboard hooks; no UIA discovery is involved.
            token.ThrowIfCancellationRequested();
            EnsureBroker();
            if (inputStateError != null) throw new InvalidOperationException("无法保存原输入法状态，语音结果将复制到系统剪贴板。", inputStateError);
            inputState ??= VoiceTypingInputState.Capture(window, focus);
            Marshal.ThrowExceptionForHR(activate());
            inputState.Activated();
            attempted = true;
            VoiceInputResult result = await SendAsync(1, "", token);
            started = result.Success; ended = !result.Success && result.Status != 8; return result;
        }
        internal async Task<VoiceInputResult> UpdateAsync(string text, bool final, CancellationToken token)
        {
            if (!started || ended) return new(false, 3, (uint)Text.Length);
            VoiceInputResult result = await SendAsync(final ? 3u : 2u, text ?? "", token);
            if (result.Success) { Text = text ?? ""; ended = final; }
            return result;
        }
        internal async Task CancelAsync()
        {
            if (!attempted || ended || library == IntPtr.Zero) return;
            VoiceInputResult result = await SendAsync(4, "", CancellationToken.None);
            if (result.Success) Text = "";
            ended = true;
        }
        private async Task<VoiceInputResult> SendAsync(uint action, string text, CancellationToken token)
        {
            if (text.Length >= 4096) return new(false, 7, (uint)Text.Length);
            if (window == IntPtr.Zero || targetPid == 0 || GetForegroundWindow() != window) return new(false, 3, (uint)Text.Length);
            token.ThrowIfCancellationRequested();
            uint seq = ++sequence;
            Marshal.ThrowExceptionForHR(command(seq, action, id, (ulong)window, text, (uint)text.Length));
            Stopwatch wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 2300)
            {
                token.ThrowIfCancellationRequested();
                if (ack(seq, id, out uint status, out uint length, out uint pid))
                    return new(status == 0 && pid == targetPid && length == text.Length, status, length);
                await Task.Delay(15, token);
            }
            return new(false, 8, (uint)Text.Length);
        }
        internal static void CloseBroker()
        {
            if (library == IntPtr.Zero) return;
            close(); NativeLibrary.Free(library); library = IntPtr.Zero;
            lease.ReleaseMutex(); lease.Dispose(); lease = null;
        }
        internal void RestoreInputState()
        {
            var state = inputState; inputState = null;
            if (state == null) return;
            try { state.Restore(); }
            catch (Exception error)
            {
                ShareX.HelpersLib.DebugHelper.WriteException(error, "Voice input profile restore");
                TaskHelpers.ShowNotificationTip("恢复原输入法失败，请手动切换输入法。", "语音打字", 5000);
            }
        }
        internal void PreserveUserInputChoice() => inputState?.PreserveUserChoice();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    }
}
