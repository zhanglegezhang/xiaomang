using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Win32;

// Run only after stopping the daily main/worker normally; uses independent test data.
internal static class Check
{
    private const BindingFlags F = BindingFlags.Static | BindingFlags.NonPublic;
    [STAThread]
    private static int Main(string[] args)
    {
        string app = Path.GetFullPath(args[0]), data = Path.GetFullPath(args[1]);
        string bundle = Path.Combine(app, "ThirdParty", "XiaomangInputMethod");
        string native = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("XIAOMANG_INPUT_TEST_DATA", data);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", data);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string file = Path.Combine(app, name.Name + ".dll");
            return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
        };
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(app, "ShareX.dll"));
        var manager = assembly.GetType("ShareX.XiaomangInputMethodManager", true)!;
        void Call(string method) => manager.GetMethod(method, F)!.Invoke(null, null);
        manager.GetField("registrationProbe", F)!.SetValue(null, (Func<bool>)(() => true));
        manager.GetField("bundleProbe", F)!.SetValue(null, (Func<string>)(() => bundle));
        var checks = new List<string>();
        void Assert(bool value, string label)
        {
            if (!value) throw new Exception(label);
            checks.Add(label); Console.WriteLine("PASS " + label);
        }
        Process? Worker()
        {
            lock (manager.GetField("gate", F)!.GetValue(null)!)
                return (Process?)manager.GetField("worker", F)!.GetValue(null);
        }
        void Ready()
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 15000)
            {
                if ((bool)manager.GetMethod("IsWorkerReady", F)!.Invoke(null, null)!) return;
                Thread.Sleep(50);
            }
            throw new Exception("Worker not ready within deadline");
        }
        void Native(string phase)
        {
            var info = new ProcessStartInfo(native) { UseShellExecute = false };
            info.ArgumentList.Add(Path.Combine(data, phase + ".log"));
            using var process = Process.Start(info)!;
            if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new Exception("Native host failed: " + phase);
        }
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Xiaomang\InputMethodRecovery", false)!;
        try
        {
            Call("Start"); Ready();
            Assert(File.Exists(Path.Combine(data, "config.toml")), "worker uses independent test data");
            int previous = Worker()!.Id;
            using (var old = Process.GetProcessById(previous))
            {
                Call("PauseForPersonalData");
                Assert(old.WaitForExit(1000), "backup pause flushes and stops worker normally");
            }
            Assert((int)key.GetValue("Enabled")! == 0, "backup pause disables recovery");
            int attempts = (int)key.GetValue("Attempts")!;
            Native("backup-pause");
            Assert(!Process.GetProcessesByName("XiaomangInputWorker").Any(), "DLL activation cannot bypass backup pause");
            Assert((int)key.GetValue("Attempts")! == attempts, "paused activation consumes no recovery quota");
            var delayed = new ProcessStartInfo(Path.Combine(bundle, "XiaomangInputWorker.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = bundle };
            foreach (string arg in new[] { "--resident", "--stop-event", @"Local\XiaomangRecoveryPausedTest" }) delayed.ArgumentList.Add(arg);
            using (var process = Process.Start(delayed)!)
                Assert(process.WaitForExit(5000) && process.ExitCode == 0, "delayed worker launch respects explicit pause");
            Call("Start"); Ready();
            Assert(Worker()!.Id != previous && (int)key.GetValue("Enabled")! == 1, "resume restores worker and recovery permission");
            Native("resume");
            Call("Stop");
            Assert((int)key.GetValue("Enabled")! == 0, "explicit stop disables recovery");
            Native("explicit-stop");
            Assert(!Process.GetProcessesByName("XiaomangInputWorker").Any(), "explicit stop remains stopped after DLL activation");
            Call("Start"); Ready();
            using (var retained = Process.GetProcessById(Worker()!.Id))
            {
                Call("Detach");
                Assert(!retained.WaitForExit(300), "normal main detach keeps worker available");
                Call("Start"); Ready();
                Assert(Worker()!.Id == retained.Id, "reopen reuses resident worker");
            }
            File.WriteAllText(Path.Combine(data, "results.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Call("Stop"); }
    }
}
