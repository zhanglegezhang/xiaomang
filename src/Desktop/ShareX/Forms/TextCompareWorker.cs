using DiffPlex.DiffBuilder.Model;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    // DiffPlex cannot interrupt an active edit-graph calculation. Isolate it so cancellation
    // actually stops CPU work. Text travels through anonymous pipes, never temporary files.
    internal static class TextCompareWorker
    {
        private const string Command = "--local-text-diff-worker";
        private sealed record Request(string Left, string Right, bool Whitespace, bool Casing);
        private sealed record Response(SideBySideDiffModel Model, string Error);

        internal static bool TryRun(string[] args)
        {
            if (args.Length != 3 || args[0] != Command) return false;
            if (!FirstUseAccess.IsVerified())
            {
                Environment.ExitCode = 1;
                return true;
            }
            try
            {
                using Process parent = Process.GetProcessById(int.Parse(args[1]));
                if (parent.StartTime.ToUniversalTime().Ticks != long.Parse(args[2])) return true;
                // Also avoid orphan computations if the main application crashes or is force-closed.
                _ = parent.WaitForExitAsync().ContinueWith(_ => Environment.Exit(0));
                using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
                using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
                Response response;
                try
                {
                    Request request = JsonConvert.DeserializeObject<Request>(input.ReadLine());
                    response = new Response(LocalTextDiff.Compare(request.Left, request.Right, request.Whitespace, request.Casing), null);
                }
                catch (Exception ex) { response = new Response(null, ex.Message); }
                output.WriteLine(JsonConvert.SerializeObject(response));
            }
            catch { Environment.ExitCode = 1; }
            return true;
        }

        internal static Task<SideBySideDiffModel> CompareAsync(string left, string right, bool whitespace, bool casing, CancellationToken token)
            => Task.Run(() => RunAsync(new Request(left, right, whitespace, casing), token), token);

        private static void Stop(Process process)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static async Task<SideBySideDiffModel> RunAsync(Request request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using Process parent = Process.GetCurrentProcess();
            // Use the product launcher beside our assembly, including calls from a regression harness.
            string directory = Path.GetDirectoryName(typeof(TextCompareWorker).Assembly.Location);
            string executable = Path.Combine(directory, "xiaomang.exe");
            if (!File.Exists(executable)) throw new IOException("找不到小蟒启动程序，请完整解压或恢复 xiaomang.exe。");
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = directory,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false)
            };
            info.ArgumentList.Add(Command);
            info.ArgumentList.Add(parent.Id.ToString());
            info.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString());
            using Process worker = Process.Start(info) ?? throw new IOException("无法启动文本对比进程。");
            using CancellationTokenRegistration registration = token.Register(() => Stop(worker));
            Task<string> output = worker.StandardOutput.ReadToEndAsync();
            Task<string> errors = worker.StandardError.ReadToEndAsync();
            try
            {
                await worker.StandardInput.WriteLineAsync(JsonConvert.SerializeObject(request)).ConfigureAwait(false);
                worker.StandardInput.Close();
                await worker.WaitForExitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                string json = await output.ConfigureAwait(false);
                string error = await errors.ConfigureAwait(false);
                if (worker.ExitCode != 0 || string.IsNullOrEmpty(json)) throw new IOException("文本对比进程异常退出。" + error);
                Response response = JsonConvert.DeserializeObject<Response>(json);
                if (response.Error != null) throw new ArgumentException(response.Error);
                token.ThrowIfCancellationRequested();
                return response.Model ?? throw new IOException("文本对比结果为空。");
            }
            catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
            finally
            {
                Stop(worker);
                await worker.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(output, errors).ConfigureAwait(false);
            }
        }
    }
}
