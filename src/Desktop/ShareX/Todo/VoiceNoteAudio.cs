using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SherpaOnnx;

namespace ShareX
{
    internal sealed class VoiceNoteRecorder : IDisposable
    {
        private readonly Stopwatch stopwatch = new Stopwatch();
        private readonly ManualResetEventSlim recordingStopped = new ManualResetEventSlim(false);
        private IWaveIn capture;
        private readonly Func<IWaveIn> createCapture;
        private readonly Action<string, string> convert;
        private WaveFileWriter writer;
        private Exception recordingError;
        private string temporaryPath;
        private bool recordingCommitted;

        public string OutputPath { get; private set; }
        public TimeSpan Elapsed => stopwatch.Elapsed;

        internal VoiceNoteRecorder() : this(() => new WasapiCapture(), ConvertToSpeechWave) { }
        internal VoiceNoteRecorder(Func<IWaveIn> createCapture, Action<string, string> convert)
        {
            this.createCapture = createCapture ?? throw new ArgumentNullException(nameof(createCapture));
            this.convert = convert ?? throw new ArgumentNullException(nameof(convert));
        }

        public void Start(string outputPath)
        {
            if (capture != null) throw new InvalidOperationException("录音已经开始。");
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("录音文件路径不能为空。", nameof(outputPath));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            OutputPath = outputPath;
            temporaryPath = outputPath + ".recording";
            try
            {
                // NAudio otherwise captures the WinForms synchronization context and posts
                // RecordingStopped back to the UI thread. Stop() waits for that event, so keep
                // capture callbacks on the audio worker thread to avoid a UI-thread deadlock.
                SynchronizationContext context = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                    capture = createCapture();
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(context);
                }
                writer = new WaveFileWriter(temporaryPath, capture.WaveFormat);
                capture.DataAvailable += Capture_DataAvailable;
                capture.RecordingStopped += Capture_RecordingStopped;
                capture.StartRecording();
                stopwatch.Restart();
            }
            catch
            {
                DisposeCapture();
                VoiceNoteStorage.TryDelete(temporaryPath);
                throw;
            }
        }

        public TimeSpan Stop()
        {
            if (capture == null) return stopwatch.Elapsed;
            stopwatch.Stop();
            try
            {
                capture.StopRecording();
                if (!recordingStopped.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("停止录音超时。");
                if (recordingError != null) throw new InvalidOperationException("录音设备返回错误。", recordingError);
                convert(temporaryPath, OutputPath);
                recordingCommitted = true;
                return stopwatch.Elapsed;
            }
            catch (Exception error)
            {
                throw new IOException("录音处理失败，原始录音已保留：" + temporaryPath, error);
            }
            finally
            {
                DisposeCapture();
                if (recordingCommitted) VoiceNoteStorage.TryDelete(temporaryPath);
            }
        }

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            writer?.Write(e.Buffer, 0, e.BytesRecorded);
            writer?.Flush();
        }

        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            recordingError = e.Exception;
            writer?.Dispose();
            writer = null;
            recordingStopped.Set();
        }

        private static void ConvertToSpeechWave(string inputPath, string outputPath)
        {
            string staging = outputPath + ".converting";
            try
            {
            using WaveFileReader reader = new WaveFileReader(inputPath);
            using MediaFoundationResampler resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1))
            {
                ResamplerQuality = 60
            };
            WaveFileWriter.CreateWaveFile(staging, resampler);
            File.Move(staging, outputPath, false);
            }
            finally { VoiceNoteStorage.TryDelete(staging); }
        }

        private void DisposeCapture()
        {
            if (capture != null)
            {
                capture.DataAvailable -= Capture_DataAvailable;
                capture.RecordingStopped -= Capture_RecordingStopped;
                capture.Dispose();
                capture = null;
            }
            writer?.Dispose();
            writer = null;
        }

        public void Dispose()
        {
            stopwatch.Stop();
            DisposeCapture();
            recordingStopped.Dispose();
            if (recordingCommitted) VoiceNoteStorage.TryDelete(temporaryPath);
        }
    }

    internal sealed class VoiceTranscriptionResult
    {
        public bool Succeeded { get; init; }
        public string Text { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
        public string WarningMessage { get; init; } = "";
        public bool NoSpeech { get; init; }
    }

    internal static class VoiceNoteTranscriber
    {
        private static readonly object RecognizerLock = new object();
        private static readonly Timer ReleaseTimer = new Timer(_ => ReleaseIdleRecognizer(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        private static SenseVoiceNativeRecognizer senseVoiceRecognizer;
        private static string loadedModelPath = "";
        private static DateTime lastSenseVoiceUseUtc;

        public static VoiceTranscriptionResult TranscribeChinese(string wavePath, string personalFolder)
        {
            using VoiceRecognitionDiagnostics diagnostics = new VoiceRecognitionDiagnostics();
            diagnostics.Model(SenseVoiceModelManager.GetModelFiles(personalFolder), SenseVoiceModelManager.IsInstalled(personalFolder));
            string senseVoiceFailure = "";
            if (SenseVoiceModelManager.IsInstalled(personalFolder))
            {
                try
                {
                    VoiceTranscriptionResult result = TranscribeWithSenseVoice(wavePath, personalFolder);
                    if (result.Succeeded) return result;
                    senseVoiceFailure = result.ErrorMessage;
                }
                catch (Exception e)
                {
                    diagnostics.Exception("voice-note-recognition", e);
                    senseVoiceFailure = "SenseVoice 转写失败：" + e.Message;
                }
            }

            VoiceTranscriptionResult fallback = TranscribeWithWindows(wavePath, diagnostics);
            if (fallback.Succeeded)
            {
                return new VoiceTranscriptionResult
                {
                    Succeeded = true,
                    Text = fallback.Text,
                    WarningMessage = SenseVoiceModelManager.IsInstalled(personalFolder) ?
                        "SenseVoice 不可用，已改用 Windows 识别" :
                        "尚未安装 SenseVoice，已使用 Windows 识别"
                };
            }

            if (!string.IsNullOrWhiteSpace(senseVoiceFailure))
                return Failure(senseVoiceFailure + "；" + fallback.ErrorMessage);
            return Failure("尚未安装 SenseVoice；" + fallback.ErrorMessage);
        }

        internal static void ReleaseSenseVoice()
        {
            lock (RecognizerLock)
            {
                senseVoiceRecognizer?.Dispose();
                senseVoiceRecognizer = null;
                loadedModelPath = "";
                ReleaseTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        internal static bool PreloadTypingModel(string personalFolder)
        {
            using VoiceRecognitionDiagnostics diagnostics = new VoiceRecognitionDiagnostics();
            try
            {
                bool installed = SenseVoiceModelManager.IsInstalled(personalFolder);
                diagnostics.Model(SenseVoiceModelManager.GetModelFiles(personalFolder), installed);
                if (!installed) { diagnostics.Failure("model-preload", "语音模型缺失或不完整，预加载跳过。"); return false; }
                lock (RecognizerLock)
                {
                    GetOrCreateRecognizer(SenseVoiceModelManager.GetModelFiles(personalFolder));
                    lastSenseVoiceUseUtc = DateTime.UtcNow;
                    ReleaseTimer.Change(TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
                }
                diagnostics.Event("model-preload", "ready");
                return true;
            }
            catch (Exception error)
            {
                diagnostics.Exception("model-preload", error);
                // Preloading is optional. Actual dictation retains its existing local
                // fallback and error handling if a model cannot be loaded.
                return false;
            }
        }

        // Voice typing already segments live audio. Keep its samples in memory rather than
        // writing dictation into the persistent voice-note library.
        internal static VoiceTranscriptionResult TranscribeTypingSegment(float[] samples, string personalFolder,
            CancellationToken cancellationToken)
        {
            using VoiceRecognitionDiagnostics diagnostics = new VoiceRecognitionDiagnostics();
            return TranscribeTypingSegmentWithDiagnostics(samples, personalFolder, cancellationToken, diagnostics);
        }

        internal static VoiceTranscriptionResult TranscribeTypingSegmentWithDiagnostics(float[] samples, string personalFolder,
            CancellationToken cancellationToken, VoiceRecognitionDiagnostics diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool installed = SenseVoiceModelManager.IsInstalled(personalFolder);
            SenseVoiceModelFiles files = SenseVoiceModelManager.GetModelFiles(personalFolder);
            diagnostics.Model(files, installed);
            return RecognizeWithFallback(() =>
            {
                if (!installed) return Failure("语音模型缺失或不完整，请到全局设置 → 语音转写检查模型。");
                lock (RecognizerLock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SenseVoiceNativeRecognizer recognizer;
                    try { recognizer = GetOrCreateRecognizer(files); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error)
                    {
                        diagnostics.Exception("model-load", error);
                        return Failure("语音模型加载失败：" + error.Message);
                    }
                    diagnostics.Event("model-load", "ready");
                    using SenseVoiceNativeRecognizer.RecognitionStream stream = recognizer.CreateStream();
                    stream.AcceptWaveform(16000, samples);
                    recognizer.Decode(stream);
                    cancellationToken.ThrowIfCancellationRequested();
                    lastSenseVoiceUseUtc = DateTime.UtcNow;
                    ReleaseTimer.Change(TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
                    string text = stream.ResultText.Trim();
                    return string.IsNullOrWhiteSpace(text) ? Failure("没有识别到清晰语音，请检查麦克风后重试。", true) :
                        new VoiceTranscriptionResult { Succeeded = true, Text = text };
                }
            }, () =>
            {
                using MemoryStream wave = new MemoryStream();
                using (WaveFileWriter writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(wave), new WaveFormat(16000, 16, 1)))
                    writer.WriteSamples(samples, 0, samples.Length);
                wave.Position = 0;
                cancellationToken.ThrowIfCancellationRequested();
                VoiceTranscriptionResult result = TranscribeWithWindowsDiagnostics(wave, diagnostics, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }, diagnostics, cancellationToken);
        }

        // Preserve the primary failure when the local fallback also fails. A normal empty
        // hypothesis stays retryable and must not trigger a different recognizer mid-phrase.
        internal static VoiceTranscriptionResult RecognizeWithFallback(Func<VoiceTranscriptionResult> offline,
            Func<VoiceTranscriptionResult> fallback, VoiceRecognitionDiagnostics diagnostics, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            VoiceTranscriptionResult primary;
            try { primary = offline(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                diagnostics.Exception("offline-recognition", error);
                primary = Failure("离线语音识别失败：" + error.Message);
            }
            token.ThrowIfCancellationRequested();
            if (primary.Succeeded || primary.NoSpeech) return primary;
            diagnostics.Failure("offline-recognition", primary.ErrorMessage);
            diagnostics.Event("windows-fallback", "started");
            VoiceTranscriptionResult result;
            try { result = fallback(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                diagnostics.Exception("windows-fallback", error);
                result = Failure("Windows 备用识别失败：" + error.Message);
            }
            token.ThrowIfCancellationRequested();
            if (result.Succeeded) { diagnostics.Event("windows-result", "success"); return result; }
            diagnostics.Failure("windows-result", result.ErrorMessage);
            return Failure(primary.ErrorMessage + "；Windows 备用识别：" + result.ErrorMessage);
        }

        private static VoiceTranscriptionResult TranscribeWithSenseVoice(string wavePath, string personalFolder)
        {
            SenseVoiceModelFiles files = SenseVoiceModelManager.GetModelFiles(personalFolder);
            float[] samples = ReadMonoSamples(wavePath, out int sampleRate);
            if (sampleRate != 16000) return Failure("SenseVoice 需要 16kHz 录音");

            List<ArraySegment<float>> segments = VoiceActivitySegmenter.Split(samples, sampleRate);
            if (segments.Count == 0) return Failure("没有检测到清晰语音");

            lock (RecognizerLock)
            {
                SenseVoiceNativeRecognizer recognizer = GetOrCreateRecognizer(files);
                List<string> phrases = new List<string>();
                foreach (ArraySegment<float> segment in segments)
                {
                    float[] chunk = new float[segment.Count];
                    Array.Copy(segment.Array, segment.Offset, chunk, 0, segment.Count);
                    using SenseVoiceNativeRecognizer.RecognitionStream stream = recognizer.CreateStream();
                    stream.AcceptWaveform(sampleRate, chunk);
                    recognizer.Decode(stream);
                    string text = stream.ResultText.Trim();
                    if (!string.IsNullOrWhiteSpace(text)) phrases.Add(text);
                }

                lastSenseVoiceUseUtc = DateTime.UtcNow;
                ReleaseTimer.Change(TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
                string combined = string.Join(Environment.NewLine, phrases).Trim();
                return string.IsNullOrWhiteSpace(combined) ? Failure("SenseVoice 没有识别到清晰语音") :
                    new VoiceTranscriptionResult { Succeeded = true, Text = combined };
            }
        }

        private static SenseVoiceNativeRecognizer GetOrCreateRecognizer(SenseVoiceModelFiles files)
        {
            if (senseVoiceRecognizer != null && string.Equals(loadedModelPath, files.ModelPath, StringComparison.OrdinalIgnoreCase))
                return senseVoiceRecognizer;

            senseVoiceRecognizer?.Dispose();
            senseVoiceRecognizer = null;
            loadedModelPath = "";
            OfflineRecognizerConfig config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = 16000;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Tokens = files.TokensPath;
            config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.Debug = 0;
            config.ModelConfig.SenseVoice.Model = files.ModelPath;
            config.ModelConfig.SenseVoice.Language = "auto";
            config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            config.DecodingMethod = "greedy_search";
            senseVoiceRecognizer = new SenseVoiceNativeRecognizer(config);
            loadedModelPath = files.ModelPath;
            lastSenseVoiceUseUtc = DateTime.UtcNow;
            return senseVoiceRecognizer;
        }

        private static void ReleaseIdleRecognizer()
        {
            lock (RecognizerLock)
            {
                if (senseVoiceRecognizer == null) return;
                TimeSpan idle = DateTime.UtcNow - lastSenseVoiceUseUtc;
                if (idle < TimeSpan.FromMinutes(5))
                {
                    ReleaseTimer.Change(TimeSpan.FromMinutes(5) - idle, Timeout.InfiniteTimeSpan);
                    return;
                }
                senseVoiceRecognizer.Dispose();
                senseVoiceRecognizer = null;
                loadedModelPath = "";
            }
        }

        private static float[] ReadMonoSamples(string wavePath, out int sampleRate)
        {
            using WaveFileReader reader = new WaveFileReader(wavePath);
            sampleRate = reader.WaveFormat.SampleRate;
            ISampleProvider provider = reader.ToSampleProvider();
            List<float> samples = new List<float>((int)Math.Min(int.MaxValue, reader.Length / 2));
            float[] buffer = new float[16000];
            int read;
            while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++) samples.Add(buffer[i]);
            }
            return samples.ToArray();
        }

        private static VoiceTranscriptionResult TranscribeWithWindows(string wavePath, VoiceRecognitionDiagnostics diagnostics)
        {
            try
            {
                using Stream wave = File.OpenRead(wavePath);
                return TranscribeWithWindowsDiagnostics(wave, diagnostics);
            }
            catch (Exception e) { diagnostics.Exception("windows-wave-read", e); return Failure("录音读取失败：" + e.Message); }
        }

        private static VoiceTranscriptionResult TranscribeWithWindows(Stream wave)
        {
            using VoiceRecognitionDiagnostics diagnostics = new VoiceRecognitionDiagnostics();
            return TranscribeWithWindowsDiagnostics(wave, diagnostics);
        }

        private static VoiceTranscriptionResult TranscribeWithWindowsDiagnostics(Stream wave, VoiceRecognitionDiagnostics diagnostics,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                RecognizerInfo recognizer = SpeechRecognitionEngine.InstalledRecognizers()
                    .FirstOrDefault(x => string.Equals(x.Culture.Name, "zh-CN", StringComparison.OrdinalIgnoreCase));
                if (recognizer == null)
                {
                    diagnostics.Failure("windows-availability", "未安装 Windows 中文语音识别组件");
                    return Failure("未安装 Windows 中文语音识别组件");
                }

                using SpeechRecognitionEngine engine = new SpeechRecognitionEngine(recognizer);
                engine.LoadGrammar(new DictationGrammar());
                engine.SetInputToWaveStream(wave);
                List<string> phrases = new List<string>();
                TaskCompletionSource<Exception> completed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                engine.SpeechRecognized += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Result.Text)) phrases.Add(e.Result.Text.Trim());
                };
                engine.RecognizeCompleted += (_, e) =>
                {
                    completed.TrySetResult(e.Error);
                };
                // Multiple consumes the entire wave and completes once at end of input.
                // Calling synchronous Recognize again after EOF instead throws "no input"
                // and used to discard phrases recognized successfully on earlier calls.
                engine.RecognizeAsync(RecognizeMode.Multiple);
                double seconds = wave.CanSeek ? wave.Length / 32000d : 60d;
                Exception recognitionError;
                try
                {
                    recognitionError = completed.Task.WaitAsync(
                        TimeSpan.FromSeconds(Math.Clamp(seconds * 2 + 15, 15, 180)), cancellationToken).GetAwaiter().GetResult();
                }
                catch (TimeoutException)
                {
                    engine.RecognizeAsyncCancel();
                    throw new TimeoutException("Windows 备用识别未在规定时间内结束。");
                }
                catch (OperationCanceledException)
                {
                    engine.RecognizeAsyncCancel();
                    throw;
                }
                if (recognitionError != null) throw new InvalidOperationException("Windows 备用识别返回错误。", recognitionError);

                string text = string.Join(Environment.NewLine, phrases).Trim();
                return string.IsNullOrWhiteSpace(text) ? Failure("没有识别到清晰语音，请检查麦克风后重试。", true) :
                    new VoiceTranscriptionResult { Succeeded = true, Text = text };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                diagnostics.Exception("windows-recognition", e);
                return Failure("本地语音识别失败：" + e.Message);
            }
        }

        private static VoiceTranscriptionResult Failure(string message, bool noSpeech = false) => new VoiceTranscriptionResult
        {
            Succeeded = false,
            ErrorMessage = message,
            NoSpeech = noSpeech
        };
    }

    internal static class VoiceActivitySegmenter
    {
        private const int FrameMilliseconds = 20;
        private const int MinimumSpeechMilliseconds = 300;
        private const int MinimumTypingSpeechMilliseconds = 100;
        private const int MaximumSilenceMilliseconds = 600;
        private const int PaddingMilliseconds = 180;
        private const int MaximumSegmentSeconds = 25;

        public static List<ArraySegment<float>> Split(float[] samples, int sampleRate)
            => SplitCore(samples, sampleRate, 0.008d, false);

        // Live microphones can be far quieter than a saved or normalized recording.
        // Keep the noise-relative gate, but do not trim intelligible quiet syllables
        // merely because they fall below the voice-note absolute energy floor.
        internal static List<ArraySegment<float>> SplitForTyping(float[] samples, int sampleRate)
            => SplitCore(samples, sampleRate, 0.0003d, true);

        private static List<ArraySegment<float>> SplitCore(float[] samples, int sampleRate,
            double minimumEnergy, bool requireSpeechFrames)
        {
            List<ArraySegment<float>> result = new List<ArraySegment<float>>();
            if (samples == null || samples.Length == 0 || sampleRate <= 0) return result;

            int frameSize = Math.Max(1, sampleRate * FrameMilliseconds / 1000);
            int frameCount = (samples.Length + frameSize - 1) / frameSize;
            double[] energies = new double[frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                int start = frame * frameSize;
                int end = Math.Min(samples.Length, start + frameSize);
                double squareSum = 0;
                for (int i = start; i < end; i++) squareSum += samples[i] * samples[i];
                energies[frame] = Math.Sqrt(squareSum / Math.Max(1, end - start));
            }

            double[] sorted = energies.OrderBy(x => x).ToArray();
            double noiseFloor = sorted[Math.Min(sorted.Length - 1, sorted.Length / 5)];
            double threshold = Math.Clamp(noiseFloor * 3d, minimumEnergy, 0.04d);
            int allowedSilentFrames = Math.Max(1, MaximumSilenceMilliseconds / FrameMilliseconds);
            int minimumSpeechFrames = Math.Max(1, MinimumSpeechMilliseconds / FrameMilliseconds);
            int minimumVoicedFrames = Math.Max(1, MinimumTypingSpeechMilliseconds / FrameMilliseconds);
            int paddingFrames = Math.Max(1, PaddingMilliseconds / FrameMilliseconds);
            int startFrame = -1;
            int lastSpeechFrame = -1;
            int speechFrames = 0;

            for (int frame = 0; frame < frameCount; frame++)
            {
                if (energies[frame] >= threshold)
                {
                    if (startFrame < 0) startFrame = frame;
                    lastSpeechFrame = frame;
                    speechFrames++;
                }

                if (startFrame >= 0 && lastSpeechFrame >= 0 && frame - lastSpeechFrame > allowedSilentFrames)
                {
                    if (!requireSpeechFrames || speechFrames >= minimumVoicedFrames)
                        AddSegment(result, samples, sampleRate, frameSize,
                            Math.Max(0, startFrame - paddingFrames), Math.Min(frameCount, lastSpeechFrame + paddingFrames + 1),
                            minimumSpeechFrames);
                    startFrame = -1;
                    lastSpeechFrame = -1;
                    speechFrames = 0;
                }
            }

            if (startFrame >= 0 && lastSpeechFrame >= 0 && (!requireSpeechFrames || speechFrames >= minimumVoicedFrames))
            {
                AddSegment(result, samples, sampleRate, frameSize,
                    Math.Max(0, startFrame - paddingFrames), Math.Min(frameCount, lastSpeechFrame + paddingFrames + 1),
                    minimumSpeechFrames);
            }
            return result;
        }

        private static void AddSegment(List<ArraySegment<float>> result, float[] samples, int sampleRate, int frameSize,
            int startFrame, int endFrame, int minimumSpeechFrames)
        {
            if (endFrame - startFrame < minimumSpeechFrames) return;
            int start = Math.Min(samples.Length, startFrame * frameSize);
            int end = Math.Min(samples.Length, endFrame * frameSize);
            int maximumSamples = sampleRate * MaximumSegmentSeconds;
            while (start < end)
            {
                int count = Math.Min(maximumSamples, end - start);
                result.Add(new ArraySegment<float>(samples, start, count));
                start += count;
            }
        }
    }

    internal static class VoiceNoteStorage
    {
        private const string FolderName = "VoiceNotes";

        public static string CreateRecordingPath(string personalFolder, DateTime now)
        {
            string folder = Path.Combine(personalFolder, FolderName, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, Guid.NewGuid().ToString("N") + ".wav");
        }

        public static string ToRelativePath(string personalFolder, string fullPath) =>
            Path.GetRelativePath(personalFolder, fullPath);

        public static string ResolvePath(string personalFolder, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return "";
            string root = Path.GetFullPath(personalFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? fullPath : "";
        }

        public static int GetDurationSeconds(string wavePath, TimeSpan fallback)
        {
            try
            {
                using BinaryReader reader = new BinaryReader(File.OpenRead(wavePath));
                if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException();
                reader.ReadInt32();
                if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException();
                int bytesPerSecond = 0;
                int dataSize = 0;
                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string chunk = new string(reader.ReadChars(4));
                    int size = reader.ReadInt32();
                    long next = Math.Min(reader.BaseStream.Length, reader.BaseStream.Position + size + (size & 1));
                    if (chunk == "fmt " && size >= 12)
                    {
                        reader.ReadInt16();
                        reader.ReadInt16();
                        reader.ReadInt32();
                        bytesPerSecond = reader.ReadInt32();
                    }
                    else if (chunk == "data") dataSize = size;
                    reader.BaseStream.Position = next;
                }
                if (bytesPerSecond > 0 && dataSize > 0)
                    return Math.Clamp((int)Math.Ceiling((double)dataSize / bytesPerSecond), 1, 60);
            }
            catch
            {
            }
            return Math.Clamp((int)Math.Ceiling(fallback.TotalSeconds), 1, 60);
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
