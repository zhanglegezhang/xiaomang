using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX;

internal sealed class UpgradeInputComponentForm : Form
{
    private static UpgradeInputComponentForm current;
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly ProductUpdateButton action = new() { Text = "取消", Dock = DockStyle.Right, Width = 110 };
    private readonly UpgradeInputComponentFlow flow = new();
    private CancellationTokenSource cancellation;
    private Task pending;
    private bool retry, exitRequested, attemptComplete;
    private string receiptPath;
    internal static bool IsBusy => current?.pending is { IsCompleted: false };

    internal static void OpenAfterUpgrade(string receiptPath)
    {
        if (!XiaomangInputMethodManager.IsRegistered ||
            !File.Exists(Path.Combine(XiaomangInputMethodManager.BundleDirectory, "XiaomangInputWorker.exe")))
        {
            WriteReceipt(receiptPath, new(UpgradeInputOutcome.Skipped));
            return;
        }
        if (current != null && !current.IsDisposed) return;
        current = new();
        current.receiptPath = receiptPath;
        current.Show(); // Modeless: tray, widgets and the main menu remain usable.
    }

    private static void WriteReceipt(string path, UpgradeInputResult result)
    {
        try
        {
            File.WriteAllText(path + ".tmp", System.Text.Json.JsonSerializer.Serialize(new { Outcome = result.Outcome.ToString(), result.Error }));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception error) { ShareX.HelpersLib.DebugHelper.WriteException(error, "Input update result receipt"); }
    }

    internal static async Task StopForExitAsync()
    {
        var form = current;
        if (form == null || form.IsDisposed) return;
        form.exitRequested = true;
        form.cancellation?.Cancel();
        if (form.pending != null) await form.pending;
        form.Close();
    }

    internal UpgradeInputComponentForm()
    {
        Text = "小蟒 · 升级后更新输入法";
        Icon = ShareX.HelpersLib.ShareXResources.Icon;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = ProductUpdateStyle.Background; ForeColor = ProductUpdateStyle.Text;
        ClientSize = new Size(520, 170); MinimumSize = new Size(440, 200);
        StartPosition = FormStartPosition.CenterScreen; Padding = new Padding(18);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        footer.Controls.Add(action); Controls.Add(status); Controls.Add(footer);
        Shown += (_, _) => StartAttempt();
        action.Click += (_, _) =>
        {
            if (pending is { IsCompleted: false }) { cancellation.Cancel(); action.Enabled = false; status.Text = "主程序已更新。正在取消下载或等待组件安全恢复…"; }
            else if (retry) StartAttempt();
            else Close();
        };
        FormClosing += (_, e) =>
        {
            if (pending is { IsCompleted: false }) { e.Cancel = true; cancellation.Cancel(); }
        };
        FormClosed += (_, _) => { cancellation?.Dispose(); if (current == this) current = null; };
    }

    private void StartAttempt()
    {
        if (pending is { IsCompleted: false }) return;
        cancellation?.Dispose(); cancellation = new(); retry = false; attemptComplete = false;
        action.Text = "取消"; action.Enabled = true;
        status.Text = "主程序已更新，原数据已加载。\n正在检查已安装的输入法组件…";
        pending = RunAsync(cancellation.Token);
    }

    private async Task RunAsync(CancellationToken token)
    {
        var result = await flow.RunAsync(true, async cancel =>
        {
            var timer = Stopwatch.StartNew();
            while (File.Exists(Path.Combine(AppContext.BaseDirectory, ".xiaomang-update.json")))
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new IOException("主程序升级仍在收尾，请稍后重试组件更新。");
                await Task.Delay(200, cancel);
            }
        }, async cancel =>
        {
            if (PersonalDataManager.Busy) throw new IOException("正在处理个人数据，请完成后重试组件更新。");
            // Re-check: do not install a component the user removed while waiting.
            if (!XiaomangInputMethodManager.IsRegistered || !File.Exists(Path.Combine(XiaomangInputMethodManager.BundleDirectory, "XiaomangInputWorker.exe")))
                throw new IOException("输入法组件已移除，已停止自动更新。");
            var release = await InputComponentCatalog.ReadAsync(Program.ProductVersion, cancel);
            var progress = new Progress<int>(value =>
            {
                if (!IsDisposed && !attemptComplete && !cancel.IsCancellationRequested)
                    status.Text = "主程序已更新，原数据已加载。\n正在更新输入法组件 " + release.Version + "：" + value + "%\n下载期间可以正常输入；替换时会短暂暂停。";
            });
            return await Task.Run(() => InputComponentUpdater.UpdateAsync(release,
                Path.Combine(Program.PersonalFolder, "Cache", "Components"), progress, cancel));
        }, token);
        attemptComplete = true;
        WriteReceipt(receiptPath, result);
        if (IsDisposed || exitRequested) return;
        retry = result.Outcome is UpgradeInputOutcome.Failed or UpgradeInputOutcome.Cancelled;
        action.Text = retry ? "重试" : "关闭"; action.Enabled = true;
        status.Text = result.Outcome switch
        {
            UpgradeInputOutcome.Updated => "主程序与输入法组件均已更新。\n设置和学习记录已保留。\n若系统组件也有更新，已打开的应用可能需要重新打开。",
            UpgradeInputOutcome.Current => "主程序已更新。\n输入法组件已是最新版本。",
            UpgradeInputOutcome.Cancelled => "主程序已更新。\n输入法组件更新已取消，可稍后重试。",
            _ => "主程序已更新；输入法组件更新未完成。\n" + result.Error + "\n可重试，或在“全局设置 → 注册”中检查并更新。"
        };
    }
}
