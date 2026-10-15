using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace ShareX
{
    internal static class ClipboardTextReader
    {
        public static bool TryRead(IDataObject data, out string text)
        {
            bool success = TryReadItem(data, out ClipboardTextItem item);
            text = item?.Text;
            return success;
        }

        public static bool TryReadItem(IDataObject data, out ClipboardTextItem item)
        {
            item = null;
            if (data == null || data.GetDataPresent(VoiceTypingClipboard.HistoryExclusionFormat, false)) return false;
            if (data.GetDataPresent(DataFormats.FileDrop, false))
            {
                if (data.GetData(DataFormats.FileDrop, false) is not string[] paths || paths.Length == 0) return false;

                try
                {
                    if (paths.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))) return false;
                    paths = paths.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).ToArray();
                    if (paths.Length == 1)
                    {
                        item = CreateFileItem(paths, paths[0]);
                        return true;
                    }

                    string[] parentFolders = paths.Select(Path.GetDirectoryName).ToArray();
                    if (parentFolders.Any(string.IsNullOrEmpty) ||
                        parentFolders.Any(folder => !string.Equals(folder, parentFolders[0], StringComparison.OrdinalIgnoreCase))) return false;

                    item = CreateFileItem(paths, parentFolders[0]);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            if (data.GetDataPresent(DataFormats.Bitmap, false)) return false;

            string[] formats = data.GetFormats(false) ?? Array.Empty<string>();
            if (formats.Any(format => format.IndexOf("Excel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                format.IndexOf("XML Spreadsheet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                format.IndexOf("Biff", StringComparison.OrdinalIgnoreCase) >= 0)) return false;

            // Some applications expose their plain representation only through automatic
            // conversion or StringFormat. Read the resulting string, never the rich payload.
            foreach (string format in new[] { DataFormats.UnicodeText, DataFormats.Text, DataFormats.StringFormat })
            {
                if (!data.GetDataPresent(format, true)) continue;
                if (data.GetData(format, true) is string value)
                {
                    value = Normalize(value);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        item = new ClipboardTextItem { Text = value };
                        return true;
                    }
                }
            }

            return false;
        }

        private static ClipboardTextItem CreateFileItem(string[] paths, string recordedPath)
        {
            string[] kinds = paths.Select(GetPathKind).ToArray();
            string kind = kinds.All(value => value == kinds[0]) ? kinds[0] : "项目";
            string label = paths.Length == 1 ? kind : $"{paths.Length}个{kind}的所在目录";
            // Keep the actual selection identity even though a multi-selection displays
            // only its parent folder. Sorting makes selection order irrelevant.
            string identity = string.Join("\n", paths.Select((path, index) =>
                kinds[index] + ":" + path.ToUpperInvariant()).OrderBy(value => value, StringComparer.Ordinal));
            return new ClipboardTextItem
            {
                Text = recordedPath,
                SourceLabel = label,
                SourceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            };
        }

        private static string GetPathKind(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.Directory) != 0 ? "文件夹" : "文件";
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            // The item may have moved or become inaccessible after being copied.
            return "项目";
        }

        public static string Normalize(string text)
        {
            // History is a reusable copy of the original payload, including its
            // newline convention and trailing whitespace. Compact only its preview.
            return text ?? string.Empty;
        }
    }
}
