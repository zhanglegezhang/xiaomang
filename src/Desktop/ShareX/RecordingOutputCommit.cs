using System;
using System.IO;

namespace ShareX
{
    internal static class RecordingOutputCommit
    {
        public static string Encode(string input, string output, Func<string, bool> encode, bool deleteInput)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(output));
            Directory.CreateDirectory(folder);
            string temporary = Path.Combine(folder, ".encoding-" + Guid.NewGuid().ToString("N") + Path.GetExtension(output));
            try
            {
                if (!encode(temporary) || !File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                    throw new IOException("录屏编码失败，原始录制文件已保留。");

                string destination = output;
                for (int suffix = 1; ; suffix++)
                {
                    try
                    {
                        // File.Move without overwrite also protects against concurrent writers.
                        File.Move(temporary, destination);
                        break;
                    }
                    catch (IOException) when (File.Exists(destination) || Directory.Exists(destination))
                    {
                        destination = Path.Combine(folder, Path.GetFileNameWithoutExtension(output) +
                            " (" + suffix + ")" + Path.GetExtension(output));
                    }
                }
                if (deleteInput && !Path.GetFullPath(input).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    File.Delete(input);
                return destination;
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
