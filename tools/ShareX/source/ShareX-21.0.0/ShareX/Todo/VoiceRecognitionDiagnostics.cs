using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace ShareX
{
    // One bounded diagnostic scope per recording. Never logs samples or recognized text.
    internal sealed class VoiceRecognitionDiagnostics : IDisposable
    {
        private readonly object gate = new object();
        private readonly HashSet<string> entries = new HashSet<string>(StringComparer.Ordinal);
        private readonly Action<string> write;
        private readonly string id = Guid.NewGuid().ToString("N")[..8];
        private int suppressed;
        private bool completed;

        internal VoiceRecognitionDiagnostics() : this(message => DebugHelper.WriteLine(message)) { }
        internal VoiceRecognitionDiagnostics(Action<string> write) => this.write = write;

        internal void Event(string stage, string detail) => Record("event:" + stage, stage, detail);

        internal void Failure(string stage, string detail) => Record("failure:" + stage + ":" + detail, stage, detail);

        internal void Exception(string stage, Exception error) => Record(
            "exception:" + stage + ":" + error.GetType().FullName + ":" + error.HResult + ":" + error.Message,
            stage, $"type={error.GetType().FullName}; HRESULT=0x{error.HResult:X8}; {error}");

        private void Record(string key, string stage, string detail)
        {
            lock (gate)
            {
                if (completed) return;
                if (entries.Count >= 64 || !entries.Add(key)) { suppressed++; return; }
                Emit($"[VoiceRecognition:{id}] {stage}: {detail}");
            }
        }

        internal void Model(SenseVoiceModelFiles files, bool installed)
        {
            Event("model-location", $"installed={installed}; OS={Environment.OSVersion}; process64={Environment.Is64BitProcess}; " +
                $"model={files.ModelPath}; tokens={files.TokensPath}; threads={Math.Clamp(Environment.ProcessorCount / 2, 1, 4)}; provider=cpu");
            // Stat once per scope; never hash a 239 MB model on every partial result.
            lock (gate)
            {
                if (completed || entries.Contains("event:model-files")) return;
                try
                {
                    string version = Path.Combine(files.Folder, "model-version.txt");
                    Event("model-files", $"modelBytes={Size(files.ModelPath)}; tokensBytes={Size(files.TokensPath)}; versionFile={File.Exists(version)}");
                }
                catch (Exception error) { Exception("model-files", error); }
            }
        }

        private static long Size(string path) => File.Exists(path) ? new FileInfo(path).Length : -1;

        internal void AudioSummary(float[] samples)
            => AudioSliceSummary(samples, 0);

        internal void AudioSliceSummary(float[] samples, int sourceOffset)
        {
            double sum = 0, peak = 0;
            foreach (float sample in samples)
            {
                sum += (double)sample * sample;
                peak = Math.Max(peak, Math.Abs(sample));
            }
            Event("audio-final", $"samples={sourceOffset + samples.Length}; seconds={(sourceOffset + samples.Length) / 16000d:F2}; analyzedSamples={samples.Length}; " +
                $"rms={(samples.Length == 0 ? 0 : Math.Sqrt(sum / samples.Length)):F6}; peak={peak:F6}");
        }
        private void Emit(string message)
        {
            // Diagnostics must not interrupt recording, recognition or cancellation.
            try { write?.Invoke(message); } catch { }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (completed) return;
                completed = true;
                Emit($"[VoiceRecognition:{id}] end: unique={entries.Count}; repeatedOrLimited={suppressed}");
            }
        }
    }
}
