using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX;

internal enum UpgradeInputOutcome { Skipped, Current, Updated, Cancelled, Failed }
internal sealed record UpgradeInputResult(UpgradeInputOutcome Outcome, string Error = null);

// One attempt at a time; a failed component must never invalidate a successful app upgrade.
internal sealed class UpgradeInputComponentFlow
{
    private int running;
    internal async Task<UpgradeInputResult> RunAsync(bool installed, Func<CancellationToken, Task> waitForCommit,
        Func<CancellationToken, Task<bool>> update, CancellationToken token)
    {
        if (!installed) return new(UpgradeInputOutcome.Skipped);
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            return new(UpgradeInputOutcome.Failed, "输入法组件正在更新，请等待当前操作完成。");
        try
        {
            await waitForCommit(token);
            token.ThrowIfCancellationRequested();
            return new(await update(token) ? UpgradeInputOutcome.Updated : UpgradeInputOutcome.Current);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return new(UpgradeInputOutcome.Cancelled); }
        catch (Exception error) { return new(UpgradeInputOutcome.Failed, error.Message); }
        finally { Interlocked.Exchange(ref running, 0); }
    }
}
