using ShareX.HelpersLib;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ShareX
{
    /// <summary>
    /// Reads the Windows clipboard on a dedicated STA thread so a busy clipboard owner
    /// cannot stall the main window. Native notifications wake the worker immediately;
    /// the timed wait is a recovery path for applications that omit notifications.
    /// </summary>
    internal sealed class ClipboardTextMonitor : IDisposable
    {
        private static readonly int[] RetryDelays = { 25, 50, 100, 150, 200, 300 };
        private readonly Action<ClipboardTextItem> resultCallback;
        private readonly AutoResetEvent wakeEvent = new(false);
        private readonly CancellationTokenSource cancellation = new();
        private Thread worker;
        private uint lastProcessedSequence;
        private bool disposed;

        public ClipboardTextMonitor(Action<ClipboardTextItem> resultCallback)
        {
            this.resultCallback = resultCallback ?? throw new ArgumentNullException(nameof(resultCallback));
        }

        public void Start()
        {
            if (worker != null || disposed) return;
            worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "xiaomangtongxue Clipboard Monitor"
            };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            wakeEvent.Set();
        }

        public void SignalClipboardChanged()
        {
            if (!disposed) wakeEvent.Set();
        }

        private void WorkerLoop()
        {
            WaitHandle[] waitHandles = { wakeEvent, cancellation.Token.WaitHandle };
            while (!cancellation.IsCancellationRequested)
            {
                int signaled = WaitHandle.WaitAny(waitHandles, TimeSpan.FromSeconds(2));
                if (signaled == 1 || cancellation.IsCancellationRequested) break;
                ReadCurrentClipboard();
            }
        }

        private void ReadCurrentClipboard()
        {
            uint sequence = GetClipboardSequenceNumber();
            if (sequence != 0 && sequence == lastProcessedSequence) return;

            for (int attempt = 0; attempt <= RetryDelays.Length && !cancellation.IsCancellationRequested; attempt++)
            {
                try
                {
                    IDataObject data = Clipboard.GetDataObject();
                    ClipboardTextItem item = ClipboardTextReader.TryReadItem(data, out ClipboardTextItem value) ? value : null;
                    lastProcessedSequence = sequence;
                    resultCallback(item);
                    return;
                }
                catch (ExternalException error)
                {
                    if (attempt >= RetryDelays.Length)
                    {
                        DebugHelper.WriteLine("Clipboard history read deferred after retries: " + error.Message);
                        return;
                    }

                    if (cancellation.Token.WaitHandle.WaitOne(RetryDelays[attempt])) return;
                }
                catch (Exception error)
                {
                    DebugHelper.WriteException(error, "Clipboard history background read failed.");
                    return;
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cancellation.Cancel();
            wakeEvent.Set();
            if (worker != null && worker.IsAlive) worker.Join(1000);
            wakeEvent.Dispose();
            cancellation.Dispose();
        }
    }
}
