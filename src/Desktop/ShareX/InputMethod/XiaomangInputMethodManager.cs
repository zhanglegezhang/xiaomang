using Microsoft.Win32;
using ShareX.HelpersLib;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    // 引擎独立常驻；主程序只连接和监测，明确重试或撤销注册才停止引擎。
    internal static class XiaomangInputMethodManager
    {
        internal const string ClassId = "{745D01A8-3F53-41D6-969B-42A734F28B1E}";
        internal static string BundleDirectory => Path.Combine(AppContext.BaseDirectory, "ThirdParty", "XiaomangInputMethod");
        internal static string UserDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XiaomangInputMethod");
        internal static string ComponentDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "XiaomangInputMethod", "0.1.4-hotkey1");
        internal static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XiaomangInputMethod", "logs");
        private static readonly object gate = new();
        private static System.Threading.Timer monitor;
        private static Process worker;
        private static bool workerReady;
        private static EventWaitHandle stopEvent;
        private static string eventName;
        private static bool stopping;
        private static int failures;
        private static DateTime nextRetry;
        private static string lastError = "";
        private static InputMethodExitSwitcher exitSwitcher;
        // 测试用依赖接缝；产品入口始终读取真实机器注册与随包目录。
        private static Func<bool> registrationProbe = () => IsRegistered;
        private static Func<string> bundleProbe = () => BundleDirectory;

        internal static bool IsRegistered
        {
            get
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = machine.OpenSubKey(@"Software\Classes\CLSID\" + ClassId + @"\InprocServer32");
                var path = key?.GetValue("") as string;
                string expected = Path.Combine(ComponentDirectory, "x86_64", "XiaomangInputMethod.dll");
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "XiaomangInputMethod", "0.1.4", "x86_64", "XiaomangInputMethod.dll");
                using var profile = machine.OpenSubKey(@"Software\Microsoft\CTF\TIP\" + ClassId + @"\LanguageProfile\0x00000804\{81C29015-D151-48C4-9643-EA1BA891612E}");
                return (string.Equals(path, expected, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, legacy, StringComparison.OrdinalIgnoreCase)) && File.Exists(path) && profile != null;
            }
        }

        internal static string Status
        {
            get
            {
                if (!System.Threading.Monitor.TryEnter(gate)) return "状态：后台检查中";
                try
                {
                    if (!IsRegistered) return "状态：未注册";
                    if (!string.IsNullOrEmpty(lastError)) return lastError;
                    return worker != null && !worker.HasExited ? workerReady ? "状态：已就绪" :
                        "状态：加载中" : "状态：未启动";
                }
                finally { System.Threading.Monitor.Exit(gate); }
            }
        }

        internal static void Start()
        {
            // Never block the UI behind a lifecycle operation; a later request can retry.
            if (!System.Threading.Monitor.TryEnter(gate)) return;
            try
            {
                if (monitor != null) return;
                stopping = false;
                monitor = new System.Threading.Timer(_ => Tick(), null, 0, 1500);
            }
            finally { System.Threading.Monitor.Exit(gate); }
        }

        private static void Tick()
        {
            lock (gate)
            {
                if (stopping) return;
                try
                {
                    if (worker != null && !worker.HasExited)
                    {
                        if (!workerReady) workerReady = ProbeWorkerReady();
                        if (DateTime.UtcNow - worker.StartTime.ToUniversalTime() > TimeSpan.FromSeconds(30)) failures = 0;
                        return;
                    }
                    if (worker != null)
                    {
                        workerReady = false;
                        worker.Dispose(); worker = null; stopEvent?.Dispose(); stopEvent = null;
                        failures++;
                        nextRetry = DateTime.UtcNow.AddSeconds(Math.Min(60, 2 << Math.Min(failures, 4)));
                    }
                    if (failures >= 5) { lastError = "后台连续启动失败，已停止自动重试；请在此页点击重试后台。"; return; }
                    if (DateTime.UtcNow < nextRetry || !registrationProbe()) return;
                    string bundle = bundleProbe();
                    string exe = Path.Combine(bundle, "XiaomangInputWorker.exe");
                    if (!File.Exists(exe) || !File.Exists(Path.Combine(bundle, "data", "generated", "dict.qj")))
                    { lastError = "输入法随包文件不完整，请修复程序文件。"; return; }
                    eventName = GetStopEventName();
                    worker = FindWorker(exe);
                    if (worker != null)
                    {
                        string previousBundle = Path.GetDirectoryName(worker.MainModule.FileName);
                        if (!string.Equals(previousBundle, bundle, StringComparison.OrdinalIgnoreCase) && !SameBundle(previousBundle, bundle))
                        {
                            // A different payload must not silently keep using the old
                            // dictionaries/engine. Stop the validated own worker first.
                            if (!EventWaitHandle.TryOpenExisting(eventName, out stopEvent))
                                throw new InvalidOperationException("旧输入法后台的停止通道不可用，请重试后台。");
                            stopEvent.Set();
                            if (!worker.WaitForExit(4000)) { worker.Kill(); if (!worker.WaitForExit(2000)) throw new InvalidOperationException("旧输入法后台尚未退出。"); }
                            worker.Dispose(); worker = null; stopEvent.Dispose(); stopEvent = null;
                        }
                    }
                    if (worker != null)
                    {
                        // 后台持有事件句柄，因此主程序退出后，下次仍可打开同一个事件。
                        stopEvent = EventWaitHandle.OpenExisting(eventName);
                        workerReady = ProbeWorkerReady();
                        lastError = "";
                        return;
                    }
                    stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
                    stopEvent.Reset();
                    var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = bundle };
                    start.ArgumentList.Add("--resident");
                    start.ArgumentList.Add("--stop-event"); start.ArgumentList.Add(eventName);
                    worker = Process.Start(start) ?? throw new InvalidOperationException("后台未启动");
                    workerReady = false;
                    lastError = "";
                }
                catch (Exception error)
                {
                    failures++; nextRetry = DateTime.UtcNow.AddSeconds(10);
                    worker?.Dispose(); worker = null; workerReady = false;
                    stopEvent?.Dispose(); stopEvent = null;
                    lastError = "启动后台失败：" + error.Message;
                    DebugHelper.WriteException(error, "Xiaomang input worker startup");
                }
            }
        }

        internal static void Retry()
        {
            Stop();
            lock (gate) { failures = 0; nextRetry = DateTime.MinValue; lastError = ""; }
            Start(); ThreadPool.QueueUserWorkItem(_ => Tick());
        }

        // 仅恢复旧版本留下的自有暂停标记，不主动改变用户当前选择。
        internal static void ObserveInputProfile()
        {
            string pausePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XiaomangInputMethod", "input-profile-paused.state");
            try { exitSwitcher ??= new InputMethodExitSwitcher(IsWorkerReady, pausePath); }
            catch (Exception error) { DebugHelper.WriteException(error, "Input profile observer startup"); }
        }

        internal static void Detach()
        {
            lock (gate)
            {
                stopping = true; monitor?.Dispose(); monitor = null;
                workerReady = false;
                worker?.Dispose(); worker = null;
                stopEvent?.Dispose(); stopEvent = null;
            }
            try { exitSwitcher?.Dispose(); }
            catch (Exception error) { DebugHelper.WriteException(error, "Input profile observer shutdown"); }
            exitSwitcher = null;
        }

        internal static void Stop()
        {
            lock (gate)
            {
                stopping = true; monitor?.Dispose(); monitor = null;
                workerReady = false;
                worker ??= FindWorker(Path.Combine(bundleProbe(), "XiaomangInputWorker.exe"));
                if (worker != null && stopEvent == null)
                {
                    EventWaitHandle.TryOpenExisting(GetStopEventName(), out stopEvent);
                }
                stopEvent?.Set();
                if (worker != null)
                {
                    try { if (!worker.WaitForExit(4000)) { worker.Kill(); worker.WaitForExit(2000); } }
                    catch (Exception error) { DebugHelper.WriteException(error, "Xiaomang input worker shutdown"); }
                    worker.Dispose(); worker = null;
                }
                stopEvent?.Dispose(); stopEvent = null;
            }
        }

        // Backup requires a normal flush. Never kill the worker to obtain a snapshot.
        internal static void PauseForPersonalData()
        {
            lock (gate)
            {
                stopping = true; monitor?.Dispose(); monitor = null; workerReady = false;
                worker ??= FindWorker(Path.Combine(bundleProbe(), "XiaomangInputWorker.exe"));
                if (worker != null && !worker.HasExited)
                {
                    if (stopEvent == null && !EventWaitHandle.TryOpenExisting(GetStopEventName(), out stopEvent)) throw new IOException("无法正常暂停输入法后台，请稍后重试。");
                    stopEvent.Set();
                    if (!worker.WaitForExit(15000)) throw new IOException("输入法尚未保存并退出，未修改个人数据。");
                }
                worker?.Dispose(); worker = null; stopEvent?.Dispose(); stopEvent = null;
            }
        }

        private static string GetStopEventName() => @"Local\XiaomangInputStop." +
            WindowsIdentity.GetCurrent().User.Value + "." + Process.GetCurrentProcess().SessionId;

        private static Process FindWorker(string exe)
        {
            Process found = null;
            int session = Process.GetCurrentProcess().SessionId;
            foreach (var process in Process.GetProcessesByName("XiaomangInputWorker"))
            {
                try
                {
                    if (found == null && process.SessionId == session && !process.HasExited &&
                        (string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase) || IsOwnedBundleWorker(process)))
                    { found = process; continue; }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                process.Dispose();
            }
            return found;
        }

        private static bool SameBundle(string first, string second)
        {
            string firstManifest = Path.Combine(first, "runtime-files.sha256"), secondManifest = Path.Combine(second, "runtime-files.sha256");
            if (File.Exists(firstManifest) && File.Exists(secondManifest))
                return File.ReadAllLines(firstManifest).Select(line => line.Trim()).SequenceEqual(File.ReadAllLines(secondManifest).Select(line => line.Trim()), StringComparer.OrdinalIgnoreCase);
            using var firstFile = File.OpenRead(Path.Combine(first, "XiaomangInputWorker.exe"));
            using var secondFile = File.OpenRead(Path.Combine(second, "XiaomangInputWorker.exe"));
            return SHA256.HashData(firstFile).AsSpan().SequenceEqual(SHA256.HashData(secondFile));
        }

        private static bool IsOwnedBundleWorker(Process candidate)
        {
            try
            {
                if (!OpenProcessToken(candidate.Handle, 8, out IntPtr token)) return false;
                try
                {
                    using var identity = new WindowsIdentity(token);
                    using var current = WindowsIdentity.GetCurrent();
                    if (identity.User == null || !identity.User.Equals(current.User)) return false;
                }
                finally { CloseHandle(token); }
                string path = candidate.MainModule?.FileName;
                if (string.IsNullOrEmpty(path) || !Path.GetFileName(path).Equals("XiaomangInputWorker.exe", StringComparison.OrdinalIgnoreCase)) return false;
                string manifest = Path.Combine(Path.GetDirectoryName(path), "runtime-files.sha256");
                if (!File.Exists(manifest)) return false;
                string expected = File.ReadLines(manifest).Select(line => line.Trim()).FirstOrDefault(line => line.Length > 66 && line[64..].Trim().Equals("XiaomangInputWorker.exe", StringComparison.OrdinalIgnoreCase));
                if (expected == null || expected.Length < 64) return false;
                using var binary = File.OpenRead(path);
                if (!Convert.ToHexString(SHA256.HashData(binary)).Equals(expected[..64], StringComparison.OrdinalIgnoreCase)) return false;
                // Bind discovery to the real service PID, not a process name alone.
                using var pipe = new NamedPipeClientStream(".", "xiaomang-input-v1-" + Process.GetCurrentProcess().SessionId, PipeDirection.InOut);
                pipe.Connect(100);
                return GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid) && pid == candidate.Id;
            }
            catch (Exception error) when (error is IOException || error is TimeoutException || error is UnauthorizedAccessException || error is InvalidOperationException || error is System.ComponentModel.Win32Exception) { return false; }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        internal static bool IsWorkerReady()
        {
            if (!System.Threading.Monitor.TryEnter(gate)) return false;
            try { return !stopping && workerReady && worker != null && !worker.HasExited; }
            finally { System.Threading.Monitor.Exit(gate); }
        }

        private static bool ProbeWorkerReady()
        {
            // 只在启动阶段检查一次成功连接；模型加载与监听就绪后才允许恢复可选状态。
            using var pipe = new NamedPipeClientStream(".", "xiaomang-input-v1-" + Process.GetCurrentProcess().SessionId,
                PipeDirection.InOut);
            try
            {
                pipe.Connect(50);
                return GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid) && worker != null && pid == worker.Id;
            }
            catch (TimeoutException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);

        internal static async Task RegisterAsync(bool install)
        {
            string exe = Path.Combine(BundleDirectory, "InputMethodSetup.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("缺少小蟒输入法注册工具", exe);
            if (!install) Stop();
            try
            {
                using var process = Process.Start(new ProcessStartInfo(exe, install ? "install" : "uninstall") { UseShellExecute = true, Verb = "runas", WorkingDirectory = BundleDirectory });
                if (process == null) throw new InvalidOperationException("安装器未启动");
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new InvalidOperationException("系统注册未完成，退出代码：" + process.ExitCode);
            }
            finally { await Task.Run(Retry); }
        }

        internal static void OpenSettings()
        {
            string exe = Path.Combine(BundleDirectory, "qingjian-settings.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("缺少输入法设置程序", exe);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = BundleDirectory });
        }
    }
}
