using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ShareX
{
    internal static class TemporaryFileStorage
    {
        private static readonly object migrationLock = new object();
        private static bool migrationComplete;

        internal static string RootFolder
        {
            get
            {
                string root = Path.Combine(Program.PersonalFolder, "临时文件夹");
                if (migrationComplete) return root;
                lock (migrationLock)
                {
                    if (!migrationComplete)
                    {
                        string legacy = Path.Combine(Program.PersonalFolder, "TemporaryFiles");
                        if (Directory.Exists(legacy)) MigrateLegacyDirectory(legacy, root);
                        migrationComplete = true;
                    }
                }
                return root;
            }
        }

        internal static void MigrateLegacyDirectory(string legacy, string destination)
        {
            if (!Directory.Exists(legacy)) return;
            if (File.Exists(destination)) destination += "-迁移-" + Guid.NewGuid().ToString("N")[..8];
            if (!Directory.Exists(destination))
            {
                Directory.Move(legacy, destination);
                return;
            }

            foreach (string sourceDirectory in Directory.GetDirectories(legacy))
            {
                string targetDirectory = Path.Combine(destination, Path.GetFileName(sourceDirectory));
                MigrateLegacyDirectory(sourceDirectory, targetDirectory);
            }
            foreach (string sourceFile in Directory.GetFiles(legacy))
            {
                string targetFile = Path.Combine(destination, Path.GetFileName(sourceFile));
                if (File.Exists(targetFile) || Directory.Exists(targetFile))
                {
                    string name = Path.GetFileNameWithoutExtension(sourceFile);
                    string extension = Path.GetExtension(sourceFile);
                    do
                    {
                        targetFile = Path.Combine(destination, name + "-迁移-" + Guid.NewGuid().ToString("N")[..8] + extension);
                    } while (File.Exists(targetFile) || Directory.Exists(targetFile));
                }
                File.Move(sourceFile, targetFile);
            }
            Directory.Delete(legacy);
        }

        private static string CurrentMonthFolder
        {
            get
            {
                string folder = Path.Combine(RootFolder, DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static string NewPath(string extension)
        {
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            return Path.Combine(CurrentMonthFolder, name + "-" + Guid.NewGuid().ToString("N")[..8] + extension);
        }

        internal static string NewMarkdownPath() => NewPath(".md");
        internal static string NewTextPath() => NewPath(".txt");

        internal static string CreateEmptyTextFile()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string path = NewPath(".txt");
                try
                {
                    using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
                    return path;
                }
                catch (IOException) when (File.Exists(path))
                {
                }
            }
            throw new IOException("无法创建唯一的临时文本文件。");
        }

        internal static void WriteMarkdown(string path, string content)
        {
            string folder = Path.GetDirectoryName(path);
            Directory.CreateDirectory(folder);
            string staging = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(staging, content, new UTF8Encoding(false));
                File.Move(staging, path, true);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }

        internal static string GetFileFingerprint(string path)
        {
            if (!File.Exists(path)) return null;
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        internal static string WriteMarkdownChecked(string path, string content, string expectedFingerprint, out bool conflict)
            => WriteDocumentChecked(path, content, expectedFingerprint, new UTF8Encoding(false, true), out conflict);

        internal static byte[] EncodeDocument(string content, Encoding encoding)
        {
            byte[] preamble = encoding.GetPreamble(), body = encoding.GetBytes(content);
            byte[] bytes = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
            return bytes;
        }

        internal static string DecodeDocument(byte[] bytes, out Encoding encoding)
        {
            encoding = new UTF8Encoding(false, true);
            int skip = 0;
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { encoding = new UTF8Encoding(true, true); skip = 3; }
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) { encoding = new UTF32Encoding(false, true, true); skip = 4; }
            else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) { encoding = new UTF32Encoding(true, true, true); skip = 4; }
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, true, true); skip = 2; }
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, true, true); skip = 2; }
            try { return encoding.GetString(bytes, skip, bytes.Length - skip); }
            catch (DecoderFallbackException) when (skip == 0)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                encoding = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                return encoding.GetString(bytes);
            }
        }

        internal static string WriteDocumentChecked(string path, string content, string expectedFingerprint, Encoding encoding, out bool conflict)
            => WriteDocumentChecked(path, content, expectedFingerprint, encoding, TimeSpan.FromSeconds(3), CancellationToken.None, out conflict);

        internal static string WriteDocumentChecked(string path, string content, string expectedFingerprint, Encoding encoding,
            TimeSpan lockTimeout, CancellationToken cancellationToken, out bool conflict)
        {
            conflict = false;
            cancellationToken.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            string lockName = "Local\\XiaomangDocument-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
            using Mutex saveLock = new Mutex(false, lockName);
            bool acquired = false;
            try
            {
                int milliseconds = (int)Math.Clamp(lockTimeout.TotalMilliseconds, 0, int.MaxValue);
                try
                {
                    int wait = WaitHandle.WaitAny(new WaitHandle[] { saveLock, cancellationToken.WaitHandle }, milliseconds);
                    acquired = wait == 0;
                    if (wait == 1) cancellationToken.ThrowIfCancellationRequested();
                    if (!acquired) throw new TimeoutException("文件正在被其他操作保存，请稍后重试。原文件和编辑内容均已保留。");
                }
                catch (AbandonedMutexException) { acquired = true; }
                cancellationToken.ThrowIfCancellationRequested();
                // Acquire, write and release stay on the same worker thread: a Mutex is thread-owned.
                return WriteDocumentLocked(path, content, expectedFingerprint, encoding, out conflict);
            }
            finally { if (acquired) saveLock.ReleaseMutex(); }
        }

        private static string WriteDocumentLocked(string path, string content, string expectedFingerprint, Encoding encoding, out bool conflict)
        {
            string folder = Path.GetDirectoryName(path);
            Directory.CreateDirectory(folder);
            string staging = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
            conflict = false;
            try
            {
                File.WriteAllBytes(staging, EncodeDocument(content, encoding));
                conflict = !string.Equals(GetFileFingerprint(path), expectedFingerprint, StringComparison.Ordinal);
                if (conflict)
                {
                    string stem = Path.GetFileNameWithoutExtension(path) + "-冲突副本-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                    for (int index = 0; ; index++)
                    {
                        string candidate = Path.Combine(folder, stem + (index == 0 ? "" : $"-{index}") + Path.GetExtension(path));
                        try { File.Move(staging, candidate, false); return candidate; }
                        catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
                    }
                }
                if (expectedFingerprint == null) File.Move(staging, path, false);
                else
                {
                    // The replacement captures any external write that raced the fingerprint check.
                    string displaced = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".displaced");
                    File.Replace(staging, path, displaced);
                    if (GetFileFingerprint(displaced) == expectedFingerprint) File.Delete(displaced);
                    else
                    {
                        conflict = true;
                        string recovered = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-外部修改-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(path));
                        File.Move(displaced, recovered);
                    }
                }
                return path;
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }
    }
}
