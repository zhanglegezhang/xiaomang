using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    // A short-lived elevated mode, dispatched before application startup or IPC.
    // It accepts only an operation; paths and registry locations are never supplied by callers.
    internal static class FileFavoritesMenu
    {
        internal const string Label = "添加到小蟒收藏夹";
        internal const string Verb = "XiaomangPinFile";
        private const string Probe = "XiaomangFileFavoritesProbe";
        private const string Argument = "-FileFavoritesMenu";
        private static readonly string[] Types = { "*", "Directory" };
        private static string KeyPath(string type, string verb) => @"Software\Classes\" + type + @"\shell\" + verb;
        private static string Command(string executable) => "\"" + executable + "\" -PinFile \"%1\"";

        internal static bool TryRun(string[] args)
        {
            if (args.Length == 0 || !args[0].Equals(Argument, StringComparison.OrdinalIgnoreCase)) return false;
            Environment.ExitCode = 1;
            if (args.Length != 2 || (args[1] != "install" && args[1] != "remove")) return true;
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) { Environment.ExitCode = 5; return true; }
            using var mutex = new Mutex(false, @"Local\Xiaomang.FileFavoritesMenu");
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) { Environment.ExitCode = 6; return true; }
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                Apply(root, Application.ExecutablePath, args[1] == "install");
                Notify();
                Environment.ExitCode = 0;
            }
            catch (InvalidOperationException) { Environment.ExitCode = 3; }
            catch (AggregateException) { Environment.ExitCode = 4; }
            catch { Environment.ExitCode = 1; }
            finally { if (acquired) mutex.ReleaseMutex(); }
            return true;
        }

        internal static async Task ChangeAsync(bool install)
        {
            using var process = Process.Start(new ProcessStartInfo(Application.ExecutablePath, Argument + (install ? " install" : " remove"))
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            });
            if (process == null) throw new IOException("无法启动菜单设置程序。");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException(process.ExitCode switch
            {
                3 => "入口包含不属于小蟒的配置，未覆盖。请联系维护者检查。",
                4 => "菜单设置失败，且未能完整恢复旧入口。请重新执行“启用 / 修复”。收藏数据不受影响。",
                5 => "没有获得管理员权限，未更改菜单。",
                6 => "另一个菜单设置操作正在进行，请稍后重试。",
                _ => "菜单设置失败，已尝试恢复原入口。请重试或检查系统权限。"
            });
            // Run this in the original user's process, including over-the-shoulder UAC.
            try
            {
                using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                Apply(user, Application.ExecutablePath, false);
                Notify();
            }
            catch (Exception ex) { throw new IOException("系统入口已更新，但旧用户入口未清理完成，请重试修复或移除。", ex); }
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            if (install ? !Matches(machine, Application.ExecutablePath) : HasEntries(machine))
                throw new IOException("菜单状态验证未通过，请重试。收藏数据不受影响。");
        }

        internal static string Status()
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                if (Matches(root, Application.ExecutablePath)) return "已启用：文件或文件夹右键 → 显示更多选项 → 添加到小蟒收藏夹";
                return HasEntries(root) ? "已有入口需要修复，可能是程序位置已改变。" : "尚未启用文件收藏右键菜单。";
            }
            catch { return "无法读取菜单状态，可尝试修复。"; }
        }

        internal static bool Matches(RegistryKey root, string executable) => Types.All(type =>
        {
            using var key = root.OpenSubKey(KeyPath(type, Verb));
            using var command = key?.OpenSubKey("command");
            using var probe = root.OpenSubKey(KeyPath(type, Probe));
            return probe == null && (string)key?.GetValue("") == Label && (string)key?.GetValue("Icon") == executable &&
                (string)key?.GetValue("MultiSelectModel") == "Single" && (string)command?.GetValue("") == Command(executable);
        });

        private static bool HasEntries(RegistryKey root) => Types.Any(type => new[] { Verb, Probe }.Any(verb =>
        {
            using var key = root.OpenSubKey(KeyPath(type, verb)); return key != null;
        }));

        private sealed record Snapshot(string Path, (string Name, object Value, RegistryValueKind Kind)[] Values, string Command);

        // The root parameter also allows isolated registry tests without touching real associations.
        internal static void Apply(RegistryKey root, string executable, bool install, Action<int> checkpoint = null)
        {
            var snapshots = (from type in Types from verb in new[] { Verb, Probe }
                             select ReadOwned(root, KeyPath(type, verb))).ToArray();
            try
            {
                int index = 0;
                foreach (var snapshot in snapshots)
                {
                    root.DeleteSubKeyTree(snapshot.Path, false);
                    if (install && snapshot.Path.EndsWith(@"\" + Verb, StringComparison.Ordinal))
                    {
                        using var key = root.CreateSubKey(snapshot.Path);
                        key.SetValue("", Label); key.SetValue("Icon", executable); key.SetValue("MultiSelectModel", "Single");
                        using var command = key.CreateSubKey("command"); command.SetValue("", Command(executable));
                    }
                    checkpoint?.Invoke(++index);
                }
                root.Flush();
            }
            catch (Exception error)
            {
                try
                {
                    foreach (var snapshot in snapshots)
                    {
                        root.DeleteSubKeyTree(snapshot.Path, false);
                        if (snapshot.Values == null) continue;
                        using var key = root.CreateSubKey(snapshot.Path);
                        foreach (var value in snapshot.Values) key.SetValue(value.Name, value.Value, value.Kind);
                        using var command = key.CreateSubKey("command"); command.SetValue("", snapshot.Command);
                    }
                    root.Flush();
                }
                catch (Exception rollback) { throw new AggregateException(error, rollback); }
                throw;
            }
        }

        private static Snapshot ReadOwned(RegistryKey root, string path)
        {
            using var key = root.OpenSubKey(path);
            if (key == null) return new Snapshot(path, null, null);
            using var command = key.OpenSubKey("command");
            string value = command?.GetValue("") as string;
            const string suffix = "\" -PinFile \"%1\"";
            bool owned = value != null && value.StartsWith('"') && value.EndsWith(suffix, StringComparison.Ordinal) &&
                Path.GetFileName(value.Substring(1, value.Length - suffix.Length - 1)).Equals("xiaomang.exe", StringComparison.OrdinalIgnoreCase);
            if (!owned || (string)key.GetValue("") != Label || key.GetSubKeyNames().Any(x => x != "command") ||
                key.GetValueNames().Any(x => x != "" && x != "Icon" && x != "MultiSelectModel") ||
                command.GetValueNames().Any(x => x != "") || command.SubKeyCount != 0 || command.GetValueKind("") != RegistryValueKind.String)
                throw new InvalidOperationException("Unrecognized menu registration.");
            return new Snapshot(path, key.GetValueNames().Select(name => (name, key.GetValue(name), key.GetValueKind(name))).ToArray(), value);
        }

        private static void Notify() => SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint e, uint flags, IntPtr a, IntPtr b);
    }
}
