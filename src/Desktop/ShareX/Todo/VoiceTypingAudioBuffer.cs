using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    // A live source must wait for another packet instead of returning zero: zero tells
    // Media Foundation to drain/reset its filter, which would introduce chunk seams.
    internal sealed class VoiceTypingAudioBuffer : IWaveProvider
    {
        private readonly object gate = new();
        private readonly Queue<byte[]> packets = new();
        private byte[] packet;
        private int packetOffset;
        private bool complete;
        private readonly List<float> samples = new(16000);
        private int sampleOrigin;
        private readonly Task conversion;
        private Exception error;
        public WaveFormat WaveFormat { get; }
        internal long AcceptedBytes { get; private set; }
        internal long ConvertedBytes { get; private set; }
        internal VoiceTypingAudioBuffer(WaveFormat format)
        {
            WaveFormat = format;
            conversion = Task.Run(Convert);
        }
        internal void Append(byte[] buffer, int count)
        {
            lock (gate)
            {
                if (complete || count == 0) return;
                byte[] copy = new byte[count]; Buffer.BlockCopy(buffer, 0, copy, 0, count);
                packets.Enqueue(copy); AcceptedBytes += count; Monitor.PulseAll(gate);
            }
        }
        public int Read(byte[] buffer, int offset, int count)
        {
            lock (gate)
            {
                while (packet == null && packets.Count == 0 && !complete) Monitor.Wait(gate);
                if (packet == null && packets.Count > 0) { packet = packets.Dequeue(); packetOffset = 0; }
                if (packet == null) return 0;
                int read = Math.Min(count, packet.Length - packetOffset);
                Buffer.BlockCopy(packet, packetOffset, buffer, offset, read); packetOffset += read;
                if (packetOffset == packet.Length) packet = null;
                ConvertedBytes += read;
                return read;
            }
        }
        private void Convert()
        {
            try
            {
                using var converter = new MediaFoundationResampler(this, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 };
                byte[] output = new byte[640]; // publish every 20ms, not a one-second buffer
                int count;
                while ((count = converter.Read(output, 0, output.Length)) > 0)
                {
                    lock (gate)
                        for (int i = 0; i + 1 < count; i += 2)
                            samples.Add((short)(output[i] | output[i + 1] << 8) / 32768f);
                }
            }
            catch (Exception failure) { lock (gate) { error = failure; complete = true; packets.Clear(); Monitor.PulseAll(gate); } }
        }
        internal void Complete()
        {
            lock (gate) { complete = true; Monitor.PulseAll(gate); }
        }
        internal Task FinishAsync() { Complete(); return conversion; }
        internal void Abort()
        {
            lock (gate) { complete = true; packets.Clear(); packet = null; Monitor.PulseAll(gate); }
        }
        internal float[] SnapshotFrom(int origin)
        {
            lock (gate)
            {
                if (error != null) throw new System.IO.IOException("录音格式转换失败：" + error.Message, error);
                if (origin < sampleOrigin || origin > sampleOrigin + samples.Count) throw new ArgumentOutOfRangeException(nameof(origin));
                int discard = origin - sampleOrigin;
                if (discard > 0) { samples.RemoveRange(0, discard); sampleOrigin = origin; }
                return samples.ToArray();
            }
        }
    }
}
