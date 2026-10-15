using System;
using System.Threading;

namespace ShareX
{
    // Cancel and enter-commit share a gate: a request either wins before replacement,
    // or is refused once replacement must run to completion/rollback.
    internal sealed class PersonalDataTransferControl : IDisposable
    {
        private readonly object gate = new();
        private readonly CancellationTokenSource cancellation = new();
        private bool committing, requested;
        internal CancellationToken Token => cancellation.Token;
        internal bool CanCancel { get { lock (gate) return !committing && !requested; } }
        internal Func<string, (string Volume, long Available)> SpaceProbe = null;
        internal bool RequestCancel()
        {
            lock (gate) {
                if (committing || requested) return false;
                requested = true;
                cancellation.Cancel(); return true;
            }
        }
        internal void EnterCommit()
        {
            lock (gate) { Token.ThrowIfCancellationRequested(); committing = true; }
        }
        public void Dispose() => cancellation.Dispose();
    }
}
