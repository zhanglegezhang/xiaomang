using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal sealed class QingjianInstallation
    {
        public string Directory { get; init; } = QingjianInputMethodManager.DefaultInstallDirectory;
        public string Version { get; init; } = "";
        public string Uninstaller { get; init; } = "";
        public bool Registered { get; init; }
        public bool HasRegistration { get; init; }
        public string SettingsPath => Path.Combine(Directory, "qingjian-settings.exe");
        public bool CanOpenSettings => File.Exists(SettingsPath);
        public bool CanUninstall => File.Exists(Uninstaller);
        public bool IsInstalled => Registered && CanOpenSettings &&
            File.Exists(Path.Combine(Directory, "qingjian-server.exe"));
    }

    internal sealed class QingjianProgress
    {
        public string Message { get; init; } = "";
        public int Percent { get; init; }
        public bool CanCancel { get; init; } = true;
    }

    // Only the official installer writes system state. Never import or restore a registry snapshot.
    internal static class QingjianInputMethodManager
    {
        public const string Version = "0.1.4";
        public const string InstallerFileName = "qingjian-0.1.4-windows-x86_64-setup.exe";
        public const string InstallerSha256 = "88dbcb5562628ad09bd710fa9117804d2d671970a465c06226ea719d276adafd";
        public const long InstallerBytes = 98_584_137;
        public const string ProjectUrl = "https://github.com/qingjian-team/qingjian";
        public const string SourceUrl = ProjectUrl + "/tree/v0.1.4";
        public const string InstallHelpUrl = "https://qingjian.app/docs/getting-started/install";
        public const string InstallerUrl = ProjectUrl + "/releases/download/v0.1.4/" + InstallerFileName;
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A7E3C1F2-5B94-4D6A-9C0E-2F8B1D3A6E70}_is1";
        private const string ClassId = "{4FDCA82D-E923-49BF-9E75-BB906B93B8BB}";
        private static readonly SemaphoreSlim OperationGate = new(1, 1);
        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };
        public static bool IsBusy => OperationGate.CurrentCount == 0;
        public static bool IsSupported => RuntimeInformation.OSArchitecture == Architecture.X64;
        public static string DefaultInstallDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Qingjian");
        public static string UserDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Qingjian");
        public static string LogDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qingjian", "logs");
        public static string GetDownloadDirectory() => Path.Combine(
            AppContext.BaseDirectory, "xiaomangtongxue", "输入法安装器", Version);

        public static QingjianInstallation GetInstallation()
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey record = machine.OpenSubKey(UninstallKey, false);
            string directory = record?.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(directory)) directory = DefaultInstallDirectory;
            directory = Path.GetFullPath(directory);
            string uninstaller = GetUninstallerPath(record?.GetValue("UninstallString") as string, directory);
            bool registered64 = IsClassRegistered(RegistryView.Registry64, directory, out bool exists64);
            bool registered32 = IsClassRegistered(RegistryView.Registry32, directory, out bool exists32);
            return new QingjianInstallation
            {
                Directory = directory,
                Version = record?.GetValue("DisplayVersion") as string ?? "",
                Uninstaller = uninstaller,
                Registered = registered64 && registered32,
                HasRegistration = exists64 || exists32
            };
        }

        private static bool IsClassRegistered(RegistryView view, string directory, out bool exists)
        {
            using RegistryKey classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using RegistryKey key = classes.OpenSubKey(@"CLSID\" + ClassId + @"\InprocServer32", false);
            exists = key != null;
            string dll = key?.GetValue("") as string;
            if (string.IsNullOrWhiteSpace(dll) || !File.Exists(dll)) return false;
            return string.Equals(Path.GetDirectoryName(Path.GetFullPath(dll)), directory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        internal static string GetUninstallerPath(string command, string directory)
        {
            // Inno stores one executable. Reject extra commands and executables outside its directory.
            if (string.IsNullOrWhiteSpace(command)) return "";
            string path = command.Trim();
            if (path.StartsWith('"'))
            {
                if (path.Length < 3 || !path.EndsWith('"')) return "";
                path = path[1..^1];
            }
            if (!Path.IsPathFullyQualified(path) || path.Contains('"')) return "";
            try
            {
                path = Path.GetFullPath(path);
                string name = Path.GetFileName(path);
                if (!name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
                    !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "";
                string number = name[5..^4];
                if (number.Length == 0) return "";
                foreach (char digit in number) if (digit < '0' || digit > '9') return "";
                return string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase) ? path : "";
            }
            catch (ArgumentException) { return ""; }
            catch (NotSupportedException) { return ""; }
        }

        public static async Task<string> InstallAsync(IProgress<QingjianProgress> progress, CancellationToken token)
        {
            if (!IsSupported) throw new InvalidOperationException("当前青简组件支持 x64 Windows，此电脑的系统架构暂不支持。");
            if (!await OperationGate.WaitAsync(0, token)) throw new InvalidOperationException("输入法操作正在进行，请稍后重试。");
            try
            {
                QingjianInstallation previous = GetInstallation();
                if (previous.IsInstalled) return "青简已安装，可直接打开设置或按 Win + 空格切换输入法。";
                if (previous.Version.Length > 0 && previous.Version != Version)
                    throw new InvalidOperationException("检测到其他版本的青简（" + previous.Version + "）。请通过青简官方安装器修复，避免降级覆盖。");
                var downloader = new QingjianInstallerDownloader(GetDownloadDirectory(), Client);
                string installer = await downloader.PrepareAsync(progress, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                progress?.Report(new QingjianProgress { Message = "请在青简安装窗口完成安装；Windows 将请求管理员权限。", Percent = 100, CanCancel = false });
                int code = await RunElevatedAsync(CreateSetupStartInfo(installer)).ConfigureAwait(false);
                if (code == 2 || code == 5) return "已取消安装；下载的安装器保留，可稍后重试。";
                if (code != 0 && code != 3010) throw new InvalidOperationException("青简安装程序返回错误码 " + code + "，可打开下载目录重新运行官方安装器。");
                QingjianInstallation current = GetInstallation();
                if (!current.IsInstalled)
                    throw new InvalidOperationException("安装程序已结束，但输入法文件或注册不完整。请重试安装，或查看官方安装说明。");
                return "安装完成。按 Win + 空格切换到青简；已打开的应用可能需要重新打开，或注销后重新登录。";
            }
            finally { OperationGate.Release(); }
        }

        internal static ProcessStartInfo CreateSetupStartInfo(string installer)
        {
            var start = new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas" };
            start.ArgumentList.Add("/NORESTART");
            start.ArgumentList.Add("/DIR=" + DefaultInstallDirectory);
            return start;
        }

        public static async Task<string> UninstallAsync()
        {
            if (!await OperationGate.WaitAsync(0)) throw new InvalidOperationException("输入法操作正在进行，请稍后重试。");
            try
            {
                QingjianInstallation current = GetInstallation();
                if (!current.CanUninstall) throw new InvalidOperationException("未找到青简官方卸载程序，请在 Windows“已安装的应用”中检查，或先修复安装。");
                var start = new ProcessStartInfo(current.Uninstaller) { UseShellExecute = true, Verb = "runas" };
                start.ArgumentList.Add("/NORESTART");
                int code = await RunElevatedAsync(start).ConfigureAwait(false);
                if (code != 0 && code != 3010) throw new InvalidOperationException("卸载程序返回错误码 " + code + "，请在 Windows“已安装的应用”中检查。");
                current = GetInstallation();
                if (current.HasRegistration || current.CanUninstall)
                    return "卸载尚未完成或已取消，当前安装状态已刷新。请检查官方卸载窗口；必要时重启后再检查。";
                return "输入法已卸载，学习词库与设置已保留。被其他应用占用的组件可能需要重启后才能完全清除。";
            }
            finally { OperationGate.Release(); }
        }

        private static async Task<int> RunElevatedAsync(ProcessStartInfo start)
        {
            try
            {
                using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动青简安装或卸载程序。");
                await process.WaitForExitAsync().ConfigureAwait(false);
                return process.ExitCode;
            }
            catch (Win32Exception error) when (error.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("已取消管理员授权。");
            }
        }

        public static void OpenSettings()
        {
            if (IsBusy) throw new InvalidOperationException("请等待安装或卸载完成。");
            string settings = GetInstallation().SettingsPath;
            if (!File.Exists(settings)) throw new FileNotFoundException("未找到青简设置程序，请先安装或修复输入法。");
            Process.Start(new ProcessStartInfo(settings) { UseShellExecute = true });
        }
    }

    internal sealed class QingjianInstallerDownloader
    {
        private readonly string folder;
        private readonly HttpClient client;
        private readonly string expectedHash;
        private readonly long expectedBytes;
        public QingjianInstallerDownloader(string folder, HttpClient client,
            string expectedHash = QingjianInputMethodManager.InstallerSha256,
            long expectedBytes = QingjianInputMethodManager.InstallerBytes)
        {
            this.folder = folder;
            this.client = client;
            this.expectedHash = expectedHash;
            this.expectedBytes = expectedBytes;
        }

        public async Task<string> PrepareAsync(IProgress<QingjianProgress> progress, CancellationToken token)
        {
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(folder, QingjianInputMethodManager.InstallerFileName);
            progress?.Report(new QingjianProgress { Message = "正在校验已有安装器…" });
            if (await IsValidAsync(destination, token).ConfigureAwait(false)) return destination;
            string partial = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using HttpResponseMessage response = await client.GetAsync(QingjianInputMethodManager.InstallerUrl,
                    HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme is string scheme && scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException("下载地址未使用 HTTPS，已停止下载。");
                if (response.Content.Headers.ContentLength is long length && length != expectedBytes)
                    throw new InvalidDataException("安装器文件大小与官方发布记录不一致，请稍后重试。");
                await using (Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[128 * 1024];
                    long received = 0;
                    int lastPercent = -1;
                    int read;
                    while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        received += read;
                        if (received > expectedBytes) throw new InvalidDataException("安装器超过预期大小，已停止下载。");
                        await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        int percent = (int)(received * 100 / expectedBytes);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress?.Report(new QingjianProgress
                            {
                                Percent = percent,
                                Message = $"正在下载青简 {QingjianInputMethodManager.Version}：{received / 1048576d:F1} / {expectedBytes / 1048576d:F1} MB"
                            });
                        }
                    }
                    await output.FlushAsync(token).ConfigureAwait(false);
                }
                progress?.Report(new QingjianProgress { Message = "下载完成，正在校验安装器…", Percent = 100 });
                if (!await IsValidAsync(partial, token).ConfigureAwait(false))
                    throw new InvalidDataException("青简安装器校验失败，文件未启用，请重新下载。");
                token.ThrowIfCancellationRequested();
                File.Move(partial, destination, true);
                return destination;
            }
            finally
            {
                if (File.Exists(partial)) File.Delete(partial);
            }
        }

        private async Task<bool> IsValidAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(path) || new FileInfo(path).Length != expectedBytes) return false;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
            return string.Equals(Convert.ToHexString(hash), expectedHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
