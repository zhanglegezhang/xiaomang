using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal static class VideoThumbnailLoader
    {
        internal static async Task<Image> LoadAsync(string ffmpegPath, string videoPath, CancellationToken cancellation)
        {
            if (!File.Exists(ffmpegPath)) throw new FileNotFoundException("视频缩略图组件不可用。", ffmpegPath);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                // A seek beyond the end of a short recording produces no frame. Retry at the start.
                foreach (string seek in new[] { "1", "0" })
                {
                    byte[] frame = await ExtractAsync(ffmpegPath, videoPath, seek, deadline.Token).ConfigureAwait(false);
                    if (frame.Length == 0) continue;
                    using var stream = new MemoryStream(frame);
                    using Image image = Image.FromStream(stream);
                    return new Bitmap(image);
                }
                throw new IOException("视频暂时无法读取，可能尚未录制完成或文件已损坏。");
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new TimeoutException("读取视频缩略图超时。");
            }
        }

        private static async Task<byte[]> ExtractAsync(string ffmpegPath, string videoPath, string seek, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(ffmpegPath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error",
                "-threads", "1", "-ss", seek, "-protocol_whitelist", "file,pipe", "-i", videoPath,
                "-map", "0:v:0", "-an", "-sn", "-dn", "-vf", "scale=78:48:force_original_aspect_ratio=decrease",
                "-frames:v", "1", "-threads", "1", "-c:v", "png", "-f", "image2pipe", "pipe:1" })
                start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            process.Start();
            // Killing the child also unblocks its pipes on navigation, shutdown, or timeout.
            using var registration = cancellation.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });
            using var output = new MemoryStream();
            try
            {
                await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(output, cancellation),
                    process.StandardError.ReadToEndAsync(cancellation), process.WaitForExitAsync(cancellation)).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new IOException("视频无法解码。");
                return output.ToArray();
            }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(true); }
                    catch (InvalidOperationException) { } // The cancellation callback may already have stopped it.
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
