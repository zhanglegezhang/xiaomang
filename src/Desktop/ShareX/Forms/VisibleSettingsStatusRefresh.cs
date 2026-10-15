using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    // Only one background read runs at a time; actions invalidate stale results.
    internal sealed class VisibleSettingsStatusRefresh<T> : IDisposable
    {
        private readonly Control owner;
        private readonly Func<bool> active;
        private readonly Func<T> read;
        private readonly Action<T> apply;
        private readonly Action<Exception> failed;
        private readonly Timer timer;
        private bool running, pending;
        private volatile bool disposed;
        private int revision;

        internal VisibleSettingsStatusRefresh(Control owner, Func<bool> active, Func<T> read, Action<T> apply, Action<Exception> failed)
        {
            this.owner = owner; this.active = active; this.read = read; this.apply = apply; this.failed = failed;
            timer = new Timer { Interval = 2000 };
            timer.Tick += (sender, e) => Request();
            owner.VisibleChanged += VisibilityChanged;
        }
        private bool IsActive => !disposed && !owner.IsDisposed && !owner.Disposing && owner.Visible && active();
        private void VisibilityChanged(object sender, EventArgs e) => UpdateVisibility();
        internal void UpdateVisibility()
        {
            if (disposed) return;
            if (IsActive) { timer.Start(); Request(); }
            else { timer.Stop(); revision++; pending = false; }
        }
        internal void Invalidate() { revision++; Request(); }
        private async void Request()
        {
            if (!IsActive) return;
            if (running) { pending = true; return; }
            running = true;
            int requestRevision = revision;
            T result = default;
            Exception error = null;
            try
            {
                result = await Task.Run(read).ConfigureAwait(false);
            }
            catch (Exception e) { error = e; }
            // Explicitly marshal back to the owner, including when the caller has
            // a generic SynchronizationContext (e.g. an embedded/test message loop).
            if (disposed) return;
            try { owner.BeginInvoke(new Action(() => Complete(result, error, requestRevision))); }
            catch (InvalidOperationException) { }
        }

        private void Complete(T result, Exception error, int requestRevision)
        {
            try
            {
                if (IsActive && requestRevision == revision)
                {
                    if (error == null) apply(result);
                    else failed(error);
                }
            }
            finally
            {
                running = false;
                if (pending) { pending = false; Request(); }
            }
        }
        public void Dispose()
        {
            disposed = true; revision++; pending = false;
            owner.VisibleChanged -= VisibilityChanged;
            timer.Dispose();
        }
    }
}
