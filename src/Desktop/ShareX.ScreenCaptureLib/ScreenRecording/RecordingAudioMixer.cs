using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.ScreenCaptureLib
{
    // One bounded, clocked PCM stream feeds FFmpeg. Silence must advance the clock:
    // WASAPI loopback sends no packets when the output device is idle.
    internal sealed class RecordingAudioMixer : IDisposable
    {
        internal static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        private const int BlockBytes = 48000 * 2 * 4 / 50; // 20 ms
        private readonly Func<bool, IWaveIn> createCapture;
        private readonly Action failed;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly object gate = new object();
        private readonly TcpListener listener;
        private TcpClient client;
        private readonly BufferedWaveProvider[] buffers = new BufferedWaveProvider[2];
        private readonly IWaveIn[] captures = new IWaveIn[2];
        private Task pump;
        private bool accepting;
        private int stopRequested;
        private bool stopping => Volatile.Read(ref stopRequested) != 0;
        private Exception error;
        private int disposed;
        private long writtenBlocks;
        private readonly long[] capturedBytes = new long[2];

        internal string InputUrl { get; }
        internal Exception Error => Volatile.Read(ref error);
        internal long WrittenBlocks => Interlocked.Read(ref writtenBlocks);
        internal long CapturedSystemBytes => Interlocked.Read(ref capturedBytes[0]);
        internal long CapturedMicrophoneBytes => Interlocked.Read(ref capturedBytes[1]);

        internal RecordingAudioMixer(Action failed, Func<bool, IWaveIn> createCapture = null)
        {
            this.failed = failed;
            this.createCapture = createCapture ?? CreateDefaultCapture;
            // Bind only to this computer. No audio is sent to the network or a server.
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            InputUrl = "tcp://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private sealed class LoopbackCapture : WasapiLoopbackCapture
        {
            protected override AudioClientStreamFlags GetAudioClientStreamFlags() =>
                base.GetAudioClientStreamFlags() | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality;
        }

        private static IWaveIn CreateDefaultCapture(bool systemAudio)
        {
            IWaveIn capture = systemAudio ? new LoopbackCapture() : new WasapiCapture();
            capture.WaveFormat = MixFormat;
            return capture;
        }

        internal void Start()
        {
            if (pump != null || captures[0] != null) throw new InvalidOperationException("录屏采音已经启动。");
            try
            {
                // Avoid callbacks posting to a WinForms context while Stop waits for them.
                SynchronizationContext context = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                    for (int i = 0; i < captures.Length; i++)
                    {
                        int index = i;
                        try
                        {
                            captures[i] = createCapture(i == 0);
                            WaveFormat format = captures[i].WaveFormat;
                            if (format.SampleRate != 48000 || format.Channels != 2 || format.BitsPerSample != 32 ||
                                format.Encoding != WaveFormatEncoding.IeeeFloat)
                                throw new NotSupportedException("音频设备不支持共享模式的 48 kHz 双声道采集。");
                            buffers[i] = new BufferedWaveProvider(MixFormat) { BufferDuration = TimeSpan.FromSeconds(3), ReadFully = true };
                            captures[i].DataAvailable += (_, e) => AddSamples(index, e);
                            captures[i].RecordingStopped += (_, e) =>
                            {
                                if (!stopping) Fail(new IOException((index == 0 ? "电脑声音" : "麦克风") +
                                    "采集已中断，请检查设备连接及 Windows 麦克风权限。", e.Exception));
                            };
                            captures[i].StartRecording();
                        }
                        catch (Exception e)
                        {
                            throw new IOException((i == 0 ? "无法采集电脑声音" : "无法启动麦克风") +
                                "，请检查系统默认设备、设备连接及 Windows 麦克风权限。", e);
                        }
                    }
                }
                finally { SynchronizationContext.SetSynchronizationContext(context); }
                // Encoding waits on a process on the thread pool. A dedicated audio clock
                // must not depend on thread-pool timer continuations during that wait.
                pump = Task.Factory.StartNew(Pump, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch { Dispose(); throw; }
        }

        private void AddSamples(int index, WaveInEventArgs e)
        {
            try
            {
                lock (gate)
                {
                    if (!accepting || stopping) return;
                    Interlocked.Add(ref capturedBytes[index], e.BytesRecorded);
                    buffers[index].AddSamples(e.Buffer, 0, e.BytesRecorded);
                }
            }
            catch (Exception failure) { Fail(new IOException("录屏音频处理跟不上设备采集，已停止并保留录制文件。", failure)); }
        }

        private void Pump()
        {
            try
            {
                using (CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
                {
                    connection.CancelAfter(TimeSpan.FromSeconds(15));
                    try { client = listener.AcceptTcpClientAsync(connection.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) when (!stopping)
                    { throw new TimeoutException("录屏音频连接超时，请检查 FFmpeg 组件。"); }
                }
                listener.Stop();
                client.NoDelay = true;
                client.SendBufferSize = BlockBytes * 32;
                using NetworkStream stream = client.GetStream();
                stream.WriteTimeout = 3000;
                lock (gate) { accepting = true; }
                byte[] system = new byte[BlockBytes], microphone = new byte[BlockBytes], output = new byte[BlockBytes];
                float[] a = new float[BlockBytes / 4], b = new float[BlockBytes / 4];
                Stopwatch clock = Stopwatch.StartNew();
                long blocks = 0;
                while (!stopping)
                {
                    int wait = (int)((blocks + 1) * 20 - clock.ElapsedMilliseconds);
                    if (wait > 0 && cancellation.Token.WaitHandle.WaitOne(wait)) break;
                    if (clock.ElapsedMilliseconds - blocks * 20 > 3000)
                        throw new IOException("录屏音频输出持续阻塞，已停止并保留录制文件。");
                    lock (gate)
                    {
                        buffers[0].Read(system, 0, system.Length);
                        buffers[1].Read(microphone, 0, microphone.Length);
                    }
                    Buffer.BlockCopy(system, 0, a, 0, BlockBytes);
                    Buffer.BlockCopy(microphone, 0, b, 0, BlockBytes);
                    MixSamples(a, b);
                    Buffer.BlockCopy(a, 0, output, 0, BlockBytes);
                    stream.Write(output, 0, output.Length);
                    blocks++;
                    Interlocked.Exchange(ref writtenBlocks, blocks);
                }
            }
            catch (OperationCanceledException) when (stopping) { }
            catch (ObjectDisposedException) when (stopping) { }
            catch (IOException) when (stopping || client?.Client?.Poll(0, SelectMode.SelectRead) == true)
            {
                // FFmpeg closes its input on fixed duration, stop, or encoder failure.
                // The caller checks its exit code; a normal EOF is not a device failure.
            }
            catch (Exception e) { if (!stopping) Fail(e); }
            finally { RequestStop(); }
        }

        internal static void MixSamples(float[] system, float[] microphone)
        {
            for (int i = 0; i < system.Length; i++)
            {
                float value = (system[i] + microphone[i]) * 0.5f;
                system[i] = float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0f;
            }
        }

        private void Fail(Exception failure)
        {
            if (Interlocked.CompareExchange(ref error, failure, null) == null)
            {
                RequestStop();
                try { failed?.Invoke(); } catch { /* FFmpeg may have just exited. */ }
            }
        }

        internal void RequestStop()
        {
            if (Interlocked.Exchange(ref stopRequested, 1) != 0) return;
            cancellation.Cancel();
            // Closing the local transport unblocks FFmpeg during input probing too.
            listener.Stop();
            client?.Dispose();
            foreach (IWaveIn capture in captures)
            {
                try { capture?.StopRecording(); } catch { }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            RequestStop();
            if (pump != null && !pump.Wait(TimeSpan.FromSeconds(5)))
                Interlocked.CompareExchange(ref error, new TimeoutException("停止录屏采音超时。"), null);
            foreach (IWaveIn capture in captures)
            {
                try { capture?.Dispose(); }
                catch (Exception failure) { Interlocked.CompareExchange(ref error, new IOException("释放录屏音频设备失败。", failure), null); }
            }
            cancellation.Dispose();
        }
    }
}
