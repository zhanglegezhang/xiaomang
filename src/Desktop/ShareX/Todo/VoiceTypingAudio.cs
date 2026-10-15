using NAudio.Wave;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    // A bounded, single-session in-memory recording. Never writes dictation to notes or logs.
    internal sealed class VoiceTypingAudio : IDisposable
    {
        private readonly object gate = new object();
        private VoiceTypingAudioBuffer audio;
        private long receivedBytes;
        private readonly TaskCompletionSource<bool> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Func<IWaveIn> createCapture;
        private IWaveIn capture;
        private WaveFormat format;
        private Exception error;
        private bool stopping, disposed;
        internal Task Ended => ended.Task;
        internal Task Ready => ready.Task;
        internal VoiceRecognitionDiagnostics Diagnostics { get; set; }

        internal VoiceTypingAudio() : this(() => new WasapiCapture()) { }

        internal VoiceTypingAudio(Func<IWaveIn> createCapture)
        {
            this.createCapture = createCapture ?? throw new ArgumentNullException(nameof(createCapture));
        }

        internal void Start()
        {
            try
            {
                capture = createCapture();
                format = capture.WaveFormat;
                audio = new VoiceTypingAudioBuffer(format);
                Diagnostics?.Event("microphone-start", $"capture={capture.GetType().FullName}; format={format}; bytesPerSecond={format.AverageBytesPerSecond}");
                capture.DataAvailable += Receive;
                capture.RecordingStopped += (_, e) =>
                {
                    if (e.Exception != null) error = e.Exception;
                    if (error != null) Diagnostics?.Exception("microphone-stopped", error);
                    Diagnostics?.Event("microphone-stopped", $"requested={stopping}; error={error != null}");
                    audio.Complete();
                    ended.TrySetResult(true);
                };
                capture.StartRecording();
            }
            catch (Exception failure)
            {
                Diagnostics?.Exception("microphone-start", failure);
                throw new IOException("麦克风录音启动失败：" + failure.Message, failure);
            }
        }

        private void Receive(object sender, WaveInEventArgs e)
        {
            lock (gate)
            {
                if (disposed) return;
                int limit = checked(format.AverageBytesPerSecond * 60);
                int count = (int)Math.Min(e.BytesRecorded, limit - receivedBytes);
                count -= count % format.BlockAlign;
                if (count > 0)
                {
                    audio.Append(e.Buffer, count);
                    receivedBytes += count;
                    // Readiness means the device has actually delivered audio, including
                    // silence. Creating/starting the capture object is not sufficient.
                    ready.TrySetResult(true);
                    Diagnostics?.Event("microphone-data", "received");
                }
            }
        }

        internal async Task StopAsync()
        {
            if (capture == null) return;
            stopping = true;
            try
            {
                capture.StopRecording();
                await ended.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                Diagnostics?.Exception("microphone-stop", failure);
                throw new IOException("麦克风录音停止失败：" + failure.Message, failure);
            }
            await audio.FinishAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            lock (gate) Diagnostics?.Event("microphone-summary", $"bytes={receivedBytes}; seconds={(double)receivedBytes / format.AverageBytesPerSecond:F2}");
            if (error != null) throw new IOException("麦克风录音中断：" + error.Message, error);
        }

        internal float[] Snapshot() => SnapshotFrom(0);

        internal float[] SnapshotFrom(int origin)
        {
            if (error != null) throw new IOException("麦克风录音中断：" + error.Message, error);
            float[] samples = audio?.SnapshotFrom(origin) ?? Array.Empty<float>();
            Diagnostics?.Event("audio-converted", $"samples={samples.Length}; origin={origin}; sampleRate=16000; channels=1");
            return samples;
        }

        internal static float[] Resample(byte[] bytes, WaveFormat format)
        {
            if (bytes.Length == 0) return Array.Empty<float>();
            using MemoryStream memory = new MemoryStream(bytes, false);
            using RawSourceWaveStream source = new RawSourceWaveStream(memory, format);
            using MediaFoundationResampler converter = new MediaFoundationResampler(source, new WaveFormat(16000, 16, 1))
            { ResamplerQuality = 60 };
            List<float> samples = new List<float>();
            byte[] block = new byte[32000];
            int read;
            while ((read = converter.Read(block, 0, block.Length)) > 0)
                for (int i = 0; i + 1 < read; i += 2)
                    samples.Add((short)(block[i] | block[i + 1] << 8) / 32768f);
            return samples.ToArray();
        }

        public void Dispose()
        {
            if (capture != null)
            {
                if (!stopping) { try { capture.StopRecording(); } catch { } }
                capture.DataAvailable -= Receive;
                capture.Dispose();
                capture = null;
            }
            lock (gate) { disposed = true; audio?.Abort(); }
        }
    }

    // Recognition hypotheses may revise old words. Keep the canonical result separate
    // from append-only output so a correction cannot stall subsequent audio segments.
    internal sealed class VoiceTypingTranscript
    {
        private readonly Func<float[], CancellationToken, VoiceTranscriptionResult> recognize;
        private readonly List<string> committed = new List<string>();
        private readonly List<string> committedStable = new List<string>();
        private int consumed;
        private int partialAudioStart = -1, partialAudioEnd;
        private string partial = "";
        private string stablePartial = "";
        internal string Error { get; private set; } = "";
        internal int RecognitionCount { get; private set; }
        internal int AudioOffset => consumed;
        internal string Text => Normalize(string.Join(" ", committed.Append(partial)));
        internal string StableText => Normalize(string.Join(" ", committedStable.Append(stablePartial)));

        internal VoiceTypingTranscript(Func<float[], CancellationToken, VoiceTranscriptionResult> recognize) => this.recognize = recognize;

        internal void Update(float[] allSamples, bool final, CancellationToken token)
            => UpdateSlice(allSamples, 0, final, token);

        internal void UpdateSlice(float[] samples, int sourceOffset, bool final, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int totalSamples = checked(sourceOffset + samples.Length);
            if (totalSamples <= consumed) return;
            if (sourceOffset > consumed) throw new ArgumentOutOfRangeException(nameof(sourceOffset));
            int origin = consumed;
            float[] tail = sourceOffset == origin ? samples : samples.AsSpan(origin - sourceOffset).ToArray();
            List<ArraySegment<float>> segments = ContinueAudioSegment(tail, origin);
            foreach (ArraySegment<float> segment in segments)
            {
                token.ThrowIfCancellationRequested();
                int end = origin + segment.Offset + segment.Count;
                bool complete = final || end <= totalSamples - 16000 * 0.65 || segment.Count >= 25 * 16000;
                VoiceTranscriptionResult result = recognize(segment.AsSpan().ToArray(), token);
                RecognitionCount++;
                token.ThrowIfCancellationRequested();
                string next = result.Succeeded ? NormalizeSegment(result.Text) : "";
                if (complete)
                {
                    if (result.Succeeded)
                    {
                        if (next.Length > 0) committed.Add(next);
                        stablePartial = ExtendStable(stablePartial, next);
                    }
                    else Error = result.ErrorMessage;
                    if (stablePartial.Length > 0) committedStable.Add(stablePartial);
                    consumed = end;
                    partial = stablePartial = "";
                    partialAudioStart = -1;
                    partialAudioEnd = 0;
                }
                else if (result.Succeeded)
                {
                    string stable = StablePrefix(partial, next);
                    stablePartial = ExtendStable(stablePartial, stable);
                    partial = next;
                    partialAudioStart = origin + segment.Offset;
                    partialAudioEnd = end;
                }
            }
        }

        private List<ArraySegment<float>> ContinueAudioSegment(float[] tail, int origin)
        {
            List<ArraySegment<float>> detected = VoiceActivitySegmenter.SplitForTyping(tail, 16000);
            int anchorStart = partialAudioStart, anchorEnd = partialAudioEnd;
            // A soft initial syllable can fall below the later, louder speech's
            // adaptive gate. Keep the recorded beginning of the first utterance
            // when it is within 500ms of the detected range. This cannot resurrect
            // already committed audio or turn noise-only input into a speech segment.
            if (origin == 0 && anchorStart < 0 && detected.Count > 0 && detected[0].Offset <= 16000 / 2)
            {
                anchorStart = 0;
                anchorEnd = detected[0].Offset + detected[0].Count;
            }
            if (anchorStart < origin) return detected;

            // The adaptive energy gate can move a previously decoded endpoint backward
            // when louder speech arrives. Committing that shorter range would leave its
            // already displayed quiet tail in the next segment and replay it. Anchor the
            // active range, merging detections that overlap its observed source audio.
            int start = anchorStart - origin;
            int end = Math.Min(tail.Length, anchorEnd - origin);
            int following = 0;
            while (following < detected.Count && detected[following].Offset <= end)
            {
                ArraySegment<float> range = detected[following++];
                end = Math.Max(end, range.Offset + range.Count);
            }
            var result = new List<ArraySegment<float>>();
            while (start < end)
            {
                int count = Math.Min(25 * 16000, end - start);
                result.Add(new ArraySegment<float>(tail, start, count));
                start += count;
            }
            result.AddRange(detected.Skip(following));
            return result;
        }

        internal static string StablePrefix(string previous, string current)
        {
            int length = 0;
            while (length < previous.Length && length < current.Length && previous[length] == current[length]) length++;
            length = Math.Max(0, length - 2); // Keep a small revisable tail until the next recognition.
            if (length > 0 && char.IsHighSurrogate(current[length - 1])) length--;
            while (length > 0 && length < current.Length && IsWord(current[length - 1]) && IsWord(current[length])) length--;
            return current.Substring(0, length).TrimEnd();
        }
        private static bool IsWord(char c) => c < 128 && char.IsLetterOrDigit(c);

        private static string ExtendStable(string previous, string current)
        {
            if (current.StartsWith(previous, StringComparison.Ordinal)) return current;
            if (current.Length == 0 || previous.StartsWith(current, StringComparison.Ordinal)) return previous;

            // Align already displayed words to a prefix of the revised hypothesis. Ignore
            // punctuation only for alignment; retain the actual suffix, including English
            // spaces. Never edit the user's field or replay the revised prefix.
            var oldWords = new List<char>();
            var newWords = new List<char>();
            var ends = new List<int>();
            for (int i = 0; i < previous.Length; i++)
                if (!char.IsWhiteSpace(previous[i]) && !char.IsPunctuation(previous[i]))
                    oldWords.Add(char.ToUpperInvariant(previous[i]));
            for (int i = 0; i < current.Length; i++)
                if (!char.IsWhiteSpace(current[i]) && !char.IsPunctuation(current[i]))
                {
                    newWords.Add(char.ToUpperInvariant(current[i]));
                    ends.Add(i + 1);
                }
            if (oldWords.Count == 0 || newWords.Count == 0) return previous;

            int[] row = Enumerable.Range(0, newWords.Count + 1).ToArray();
            for (int i = 1; i <= oldWords.Count; i++)
            {
                int diagonal = row[0];
                row[0] = i;
                for (int j = 1; j <= newWords.Count; j++)
                {
                    int above = row[j];
                    row[j] = Math.Min(diagonal + (oldWords[i - 1] == newWords[j - 1] ? 0 : 1),
                        Math.Min(above + 1, row[j - 1] + 1));
                    diagonal = above;
                }
            }
            int matched = 1;
            for (int j = 2; j <= newWords.Count; j++)
                if (row[j] < row[matched] || (row[j] == row[matched] &&
                    Math.Abs(j - oldWords.Count) < Math.Abs(matched - oldWords.Count))) matched = j;
            // Unrelated hypotheses have no reliable append boundary. Hold this segment's
            // output, retain its canonical result, and allow the next segment to proceed.
            int tolerance = oldWords.Count < 6 ? 0 : Math.Max(1, oldWords.Count / 4);
            if (row[matched] > tolerance) return previous;
            int end = ends[matched - 1];
            if (end < current.Length && char.IsLowSurrogate(current[end])) end++;
            while (end < current.Length && IsWord(current[end - 1]) && IsWord(current[end])) end++;
            if (char.IsPunctuation(previous[previous.Length - 1]))
                while (end < current.Length && char.IsPunctuation(current[end])) end++;
            return previous + current.Substring(end);
        }

        // Clean each dictation hypothesis before stability checks and commits. Keep
        // punctuation inside the segment and leave the shared voice-note result alone.
        private static string NormalizeSegment(string text)
        {
            string normalized = Normalize(text);
            int end = normalized.Length;
            while (end > 0 && (normalized[end - 1] == '\u3002' || normalized[end - 1] == '\uFF1F' ||
                normalized[end - 1] == '.' || normalized[end - 1] == '?' || char.IsWhiteSpace(normalized[end - 1]))) end--;
            return normalized.Substring(0, end);
        }

        // Do not inject Enter into chat clients, including when ASR supplies line breaks.
        internal static string Normalize(string text) => string.Join(" ", (text ?? "").Split(
            new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
