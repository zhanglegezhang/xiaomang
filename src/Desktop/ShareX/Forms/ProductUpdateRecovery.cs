using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace ShareX;

internal static class ProductUpdateRecovery
{
    internal const string MarkerName = ".xiaomang-update.json";
    internal const string ScriptName = "XiaomangUpdateRecovery.ps1";
    internal const string CommandName = "恢复小蟒更新.cmd";

    internal static string EnsureScripts(string app)
    {
        if (File.Exists(Path.Combine(app, MarkerName)))
            throw new IOException("上次更新尚需恢复，请先运行“恢复小蟒更新.cmd”，并保留更新备份。");
        using var stream = typeof(ProductUpdateRecovery).Assembly.GetManifestResourceStream("ShareX.Assets.XiaomangUpdateRecovery.ps1")
            ?? throw new IOException("更新恢复资源缺失。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string script = Path.Combine(app, ScriptName);
        WriteFile(script, reader.ReadToEnd(), true);
        WriteFile(Path.Combine(app, CommandName), "@echo off\r\nsetlocal DisableDelayedExpansion\r\n\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -File \"%~dp0" + ScriptName + "\"\r\n", false);
        return script;
    }

    private static void WriteFile(string path, string content, bool bom)
    {
        string temporary = path + ".write-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] prefix = bom ? Encoding.UTF8.GetPreamble() : [];
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(prefix); stream.Write(bytes); stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static JsonDocument ReadJson(string path, long maximum)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > maximum) throw new IOException("更新恢复记录无效。");
        for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("更新恢复路径含链接。");
        return JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 32 });
    }

    // This runs before reading any user configuration. A broken runtime can still
    // use the independent PowerShell recovery entry without loading managed code.
    internal static bool TryRecoverPendingUpdate()
    {
        string app = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        string marker = Path.Combine(app, MarkerName);
        if (!File.Exists(marker)) return false;
        try
        {
            using var pointer = ReadJson(marker, 16384);
            var p = pointer.RootElement;
            string transaction = p.GetProperty("Transaction").GetString();
            string updateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XiaomangUpdates") + Path.DirectorySeparatorChar;
            if (p.GetProperty("Version").GetInt32() != 1 || !string.Equals(p.GetProperty("App").GetString(), app, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(p.GetProperty("Id").GetString(), "N", out _) ||
                !Path.GetFullPath(transaction).StartsWith(updateRoot, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(transaction) != "transaction.json")
                throw new IOException("更新恢复记录与当前程序不匹配。");
            using var document = ReadJson(transaction, 8 * 1024 * 1024);
            var state = document.RootElement;
            if (state.GetProperty("Id").GetString() != p.GetProperty("Id").GetString() || !string.Equals(state.GetProperty("App").GetString(), app, StringComparison.OrdinalIgnoreCase))
                throw new IOException("更新事务标识不匹配。");
            bool helperAlive = false;
            try
            {
                using var helper = Process.GetProcessById(state.GetProperty("HelperId").GetInt32());
                helperAlive = !helper.HasExited && helper.StartTime.ToUniversalTime().Ticks == state.GetProperty("HelperStarted").GetInt64();
            }
            catch (ArgumentException) { }
            if (helperAlive)
            {
                if (state.GetProperty("Phase").GetString() == "validating" &&
                    string.Equals(Environment.GetEnvironmentVariable("XIAOMANG_UPGRADE_CHECK"), state.GetProperty("Snapshot").GetString(), StringComparison.OrdinalIgnoreCase)) return false;
                MessageBox.Show("更新助手正在处理程序文件，请等待更新完成后再打开小蟒。", "小蟒更新");
                return true;
            }
            string script = Path.Combine(app, ScriptName);
            using (var stream = File.OpenRead(script))
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(p.GetProperty("ScriptHash").GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("恢复脚本缺失或发生变化，请保留更新备份后手动处理。");
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = app };
            foreach (string value in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Marker", marker,
                "-WaitForProcessId", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), "-WaitForStarted", Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) })
                info.ArgumentList.Add(value);
            using var recovery = Process.Start(info) ?? throw new IOException("无法启动独立更新恢复助手。");
        }
        catch (Exception error)
        {
            MessageBox.Show("检测到未完成更新，已停止启动以保护现有数据。\n" + error.Message + "\n请保留状态文件和更新备份：\n" + marker, "小蟒更新恢复", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return true;
    }
}
