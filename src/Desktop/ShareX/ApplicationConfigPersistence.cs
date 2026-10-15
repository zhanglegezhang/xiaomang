using ShareX.HelpersLib;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    // One ordered writer for both ordinary autosaves and operations requiring confirmation.
    internal sealed class ApplicationConfigPersistence
    {
        private sealed class Request : IDisposable
        {
            internal long Revision;
            internal ApplicationConfig Owner;
            internal string Path;
            internal SettingsObjectSnapshot<ApplicationConfig> Snapshot;
            internal byte[] Bytes;
            internal readonly TaskCompletionSource<bool> Attempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Dispose() => Snapshot?.Dispose();
        }
        private readonly object gate = new();
        private readonly object writeGate = new();
        private Request pending;
        private Task worker;
        private long nextRevision, savedRevision;
        private readonly Func<ApplicationConfig> current;
        internal ApplicationConfigPersistence(Func<ApplicationConfig> current) => this.current = current;

        private Request Capture(ApplicationConfig config, string path)
        {
            config.ApplicationVersion = Helpers.GetApplicationVersion();
            return new Request { Revision = Interlocked.Increment(ref nextRevision), Owner = config,
                Path = path, Snapshot = SettingsObjectSnapshot<ApplicationConfig>.Capture(config) };
        }
        internal Task<bool> Queue(ApplicationConfig config, string path)
        {
            var request = Capture(config, path);
            lock (gate)
            {
                if (pending != null) { _ = CompleteSupersededAsync(pending.Attempt, request.Attempt.Task); pending.Dispose(); }
                pending = request;
                if (worker == null || worker.IsCompleted) worker = Task.Run(ProcessAsync);
            }
            return request.Attempt.Task;
        }
        private static async Task CompleteSupersededAsync(TaskCompletionSource<bool> previous, Task<bool> replacement)
            => previous.TrySetResult(await replacement.ConfigureAwait(false));
        internal bool Save(ApplicationConfig config, string path)
        {
            using var request = Capture(config, path);
            return Write(request);
        }
        private bool Write(Request request)
        {
            lock (writeGate)
            {
                if (request.Revision <= savedRevision || !ReferenceEquals(request.Owner, current())) return true;
                try
                {
                    request.Bytes ??= request.Snapshot.Value.CaptureSaveSnapshot();
                    bool saved = request.Owner.SaveSnapshot(request.Path, request.Bytes);
                    if (saved) savedRevision = request.Revision;
                    return saved;
                }
                catch (Exception error) { DebugHelper.WriteException(error, "ApplicationConfig snapshot save failed."); return false; }
            }
        }
        private async Task ProcessAsync()
        {
            int failures = 0;
            while (true)
            {
                await Task.Delay(failures == 0 ? 250 : Math.Min(60_000, 1000 << Math.Min(failures - 1, 6))).ConfigureAwait(false);
                Request request;
                lock (gate)
                {
                    request = pending; pending = null;
                    if (request == null) { worker = null; return; }
                }
                bool saved = Write(request);
                request.Attempt.TrySetResult(saved);
                lock (gate)
                {
                    if (!saved && pending == null) { pending = request; failures++; }
                    else { request.Dispose(); failures = 0; }
                    if (pending == null) { worker = null; return; }
                }
            }
        }
        internal async Task FlushAsync(ApplicationConfig config, string path)
        {
            // Queue a fresh snapshot so a prior failed attempt cannot report success.
            if (!await Queue(config, path).ConfigureAwait(false))
                throw new IOException("主配置保存失败，修改已保留，请重试保存。");
        }
    }
}
