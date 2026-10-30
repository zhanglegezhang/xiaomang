using ShareX;
using System.Security.Cryptography;
using System.Text.Json;

int checks = 0;
void Check(bool passed, string label) { if (!passed) throw new Exception(label); checks++; }
var root = Path.GetFullPath(args.Length > 0 ? args[0] : "build/upgrade-input-auto-tests");
Directory.CreateDirectory(root);
foreach (bool residual in new[] { false, true })
{
    var result = await new UpgradeInputComponentFlow().RunAsync(false,
        _ => throw new Exception("Must not wait"), _ => throw new Exception("Must not download"), default);
    Check(result.Outcome == UpgradeInputOutcome.Skipped, "missing/residual component skipped");
}
foreach (string mode in new[] { "current", "updated", "network", "incompatible", "denied", "cancelled", "helper-failed" })
{
    using var cts = new CancellationTokenSource();
    bool committed = false; int updates = 0;
    var result = await new UpgradeInputComponentFlow().RunAsync(true, _ =>
    {
        if (mode == "helper-failed") throw new IOException("Application transaction still pending");
        committed = true; return Task.CompletedTask;
    }, token =>
    {
        Check(committed, "must wait for app commit"); updates++;
        if (mode == "network") throw new HttpRequestException("offline");
        if (mode == "incompatible") throw new InvalidDataException("minimum app version");
        if (mode == "denied") throw new System.ComponentModel.Win32Exception(1223);
        if (mode == "cancelled") { cts.Cancel(); token.ThrowIfCancellationRequested(); }
        return Task.FromResult(mode == "updated");
    }, cts.Token);
    var expected = mode switch { "current" => UpgradeInputOutcome.Current, "updated" => UpgradeInputOutcome.Updated,
        "cancelled" => UpgradeInputOutcome.Cancelled, _ => UpgradeInputOutcome.Failed };
    Check(result.Outcome == expected, mode);
    if (mode == "helper-failed") Check(updates == 0, "do not start component before application committed");
}
var shared = new UpgradeInputComponentFlow();
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var first = shared.RunAsync(true, _ => release.Task, _ => Task.FromResult(true), default);
var second = await shared.RunAsync(true, _ => Task.CompletedTask, _ => throw new Exception("duplicate update"), default);
Check(second.Outcome == UpgradeInputOutcome.Failed, "duplicate update rejected");
release.SetResult(); Check((await first).Outcome == UpgradeInputOutcome.Updated, "first attempt completed");
Check((await shared.RunAsync(true, _ => Task.CompletedTask, _ => Task.FromResult(false), default)).Outcome == UpgradeInputOutcome.Current, "retry permitted");

foreach (string mode in new[] { "success", "activation-failed", "cancel-before-commit", "locked" })
{
    string dir = Path.Combine(root, mode + "-" + Guid.NewGuid().ToString("N"));
    string stage = Path.Combine(dir, "stage"), installed = Path.Combine(dir, "installed");
    Directory.CreateDirectory(stage); Directory.CreateDirectory(installed);
    File.WriteAllText(Path.Combine(stage, "worker.exe"), "new");
    File.WriteAllText(Path.Combine(installed, "worker.exe"), "old");
    File.WriteAllText(Path.Combine(installed, "config.toml"), "shortcut=Ctrl+M");
    File.WriteAllText(Path.Combine(installed, "user.tsv"), "learned-word\t100");
    var bundle = new ComponentBundle("text", "test.zip", 1, new string('0', 64),
        [new("worker.exe", 3, Convert.ToHexString(SHA256.HashData("new"u8)), true)]);
    using var cts = new CancellationTokenSource();
    bool paused = false, resumed = false;
    using var held = mode == "locked" ? new FileStream(Path.Combine(installed, "worker.exe"), FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    var result = await new UpgradeInputComponentFlow().RunAsync(true, _ => Task.CompletedTask, async token =>
    {
        if (mode == "cancel-before-commit") cts.Cancel();
        await InputComponentTransaction.CommitAsync(bundle, stage, installed,
            () => { paused = true; return Task.CompletedTask; },
            () => mode == "activation-failed" ? Task.FromException(new IOException("not ready")) : Task.CompletedTask,
            () => { resumed = true; return Task.CompletedTask; }, token);
        return true;
    }, cts.Token);
    Check(File.ReadAllText(Path.Combine(installed, "config.toml")) == "shortcut=Ctrl+M", mode + " settings retained");
    Check(File.ReadAllText(Path.Combine(installed, "user.tsv")) == "learned-word\t100", mode + " learning retained");
    Check(File.ReadAllText(Path.Combine(installed, "worker.exe")) == (mode == "success" ? "new" : "old"), mode + " runtime retention");
    Check(result.Outcome == (mode == "success" ? UpgradeInputOutcome.Updated : mode == "cancel-before-commit" ? UpgradeInputOutcome.Cancelled : UpgradeInputOutcome.Failed), mode + " result");
    if (mode == "activation-failed") Check(resumed, "failure invokes old worker restore");
    if (mode == "cancel-before-commit") Check(!paused, "download cancellation never pauses input");
}
File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks }));
Console.WriteLine($"PASS {checks} auto-update coordination and transaction checks");
namespace ShareX { internal static class Program { public static string ProductVersion => "3.14"; } }
