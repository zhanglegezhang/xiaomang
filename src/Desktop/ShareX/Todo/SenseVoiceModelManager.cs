using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal sealed class SenseVoiceModelFiles
    {
        public string Folder { get; init; } = "";
        public string ModelPath { get; init; } = "";
        public string TokensPath { get; init; } = "";
        public string LicensePath { get; init; } = "";
    }

    internal sealed class SenseVoiceDownloadProgress
    {
        public string Stage { get; init; } = "";
        public int Percent { get; init; }
        public long BytesReceived { get; init; }
        public long? TotalBytes { get; init; }
    }

    internal static class SenseVoiceModelManager
    {
        public const string DisplayName = "SenseVoice-Small INT8";
        public const long ApproximateDownloadBytes = 155_902_217;

        public static SenseVoiceModelFiles GetModelFiles(string personalFolder)
        {
            string folder = Path.Combine(personalFolder, "SpeechModels", "SenseVoiceSmall");
            return new SenseVoiceModelFiles
            {
                Folder = folder,
                ModelPath = Path.Combine(folder, "model.int8.onnx"),
                TokensPath = Path.Combine(folder, "tokens.txt"),
                LicensePath = Path.Combine(folder, "LICENSE-SenseVoice.txt")
            };
        }

        public static bool HasLocalFiles(string personalFolder) => Directory.Exists(GetModelFiles(personalFolder).Folder);

        public static bool IsInstalled(string personalFolder)
        {
            return ComponentBundles.IsPresent(ComponentBundles.Voice, GetModelFiles(personalFolder).Folder);
        }

        public static long GetInstalledSize(string personalFolder)
        {
            SenseVoiceModelFiles files = GetModelFiles(personalFolder);
            if (!Directory.Exists(files.Folder)) return 0;
            long total = 0;
            try
            {
                foreach (string file in Directory.EnumerateFiles(files.Folder, "*", SearchOption.AllDirectories))
                    total += new FileInfo(file).Length;
            }
            catch
            {
            }
            return total;
        }

        public static async Task InstallAsync(string personalFolder, IProgress<SenseVoiceDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var bridge = new ModelProgress(progress);
            await ComponentBundles.EnsureAsync(ComponentBundles.Voice, GetModelFiles(personalFolder).Folder,
                Path.Combine(personalFolder, "Cache", "Components"), bridge, cancellationToken).ConfigureAwait(false);
        }
        private sealed class ModelProgress(IProgress<SenseVoiceDownloadProgress> target) : IProgress<int>
        {
            public void Report(int value) => target?.Report(new SenseVoiceDownloadProgress
            {
                Stage = value >= 100 ? "安装完成" : value >= 90 ? "正在校验并安装语音模型" : "正在下载语音模型",
                Percent = value,
                BytesReceived = value >= 90 ? ApproximateDownloadBytes : ApproximateDownloadBytes * value / 90,
                TotalBytes = ApproximateDownloadBytes
            });
        }
        public static void Remove(string personalFolder)
        {
            VoiceNoteTranscriber.ReleaseSenseVoice();
            SenseVoiceModelFiles files = GetModelFiles(personalFolder);
            if (Directory.Exists(files.Folder)) Directory.Delete(files.Folder, true);
        }

        public static Task RemoveAsync(string personalFolder) => Task.Run(() => Remove(personalFolder));

    }
}
