#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ShareX.ImageEffectsLib
{
    public static class ImageEffectPackager
    {
        private const string ConfigFileName = "Config.json";

        public static string Package(string outputFilePath, string configJson, string assetsFolderPath)
        {
            if (!string.IsNullOrEmpty(outputFilePath))
            {
                List<ZipEntryInfo> entries = new List<ZipEntryInfo>();

                byte[] bytes = Encoding.UTF8.GetBytes(configJson);
                MemoryStream ms = new MemoryStream(bytes);
                entries.Add(new ZipEntryInfo(ms, ConfigFileName));

                if (!string.IsNullOrEmpty(assetsFolderPath) && Directory.Exists(assetsFolderPath))
                {
                    string parentFolderPath = Directory.GetParent(assetsFolderPath).FullName;
                    int entryNamePosition = parentFolderPath.Length + 1;

                    foreach (string assetPath in Directory.EnumerateFiles(assetsFolderPath, "*.*", SearchOption.AllDirectories).Where(x => FileHelpers.IsImageFile(x)))
                    {
                        string entryName = assetPath.Substring(entryNamePosition);
                        entries.Add(new ZipEntryInfo(assetPath, entryName));
                    }
                }

                ZipManager.Compress(outputFilePath, entries);

                return outputFilePath;
            }

            return null;
        }

        public static string ExtractPackage(string packageFilePath, string destination)
        {
            if (string.IsNullOrWhiteSpace(destination)) throw new InvalidDataException("未设置特效资源目录。");
            using var archive = ZipFile.OpenRead(packageFilePath);
            if (archive.Entries.Sum(entry => entry.Length) > 100_000_000)
                throw new InvalidDataException("特效包解压后超过 100 MB，无法导入。");
            var configs = archive.Entries.Where(entry => entry.FullName.Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (configs.Length != 1) throw new InvalidDataException("特效包必须包含唯一的 Config.json 配置文件。");
            string configJson;
            using (var buffer = new MemoryStream())
            {
                CopyEntry(configs[0], buffer);
                buffer.Position = 0;
                using var reader = new StreamReader(buffer, Encoding.UTF8);
                configJson = reader.ReadToEnd();
            }
            ImageEffectImport.Parse(configJson);

            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
            string staging = Path.Combine(Path.GetTempPath(), "XiaomangEffectImport-" + Guid.NewGuid().ToString("N"));
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<(string Staged, string Target, string Backup)>();
            var written = new List<(string Target, string Backup)>();
            bool keepBackup = false;
            try
            {
                Directory.CreateDirectory(staging);
                foreach (var entry in archive.Entries.Where(entry => FileHelpers.IsImageFile(entry.Name)))
                {
                    string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.Contains(':') || !targets.Add(target))
                        throw new InvalidDataException("特效包包含越界或重复的资源路径。");
                    EnsureOrdinaryPath(target);
                    string staged = Path.Combine(staging, files.Count + ".asset");
                    using (var output = File.Create(staged)) CopyEntry(entry, output);
                    string backup = null;
                    if (File.Exists(target))
                    {
                        backup = Path.Combine(staging, files.Count + ".backup");
                        File.Copy(target, backup);
                    }
                    files.Add((staged, target, backup));
                }
                foreach (var file in files)
                {
                    EnsureOrdinaryPath(file.Target);
                    Directory.CreateDirectory(Path.GetDirectoryName(file.Target));
                    // Record before copying so a partially written file is restored too.
                    written.Add((file.Target, file.Backup));
                    File.Copy(file.Staged, file.Target, true);
                }
                return configJson;
            }
            catch (Exception error)
            {
                var failures = new List<Exception> { error };
                foreach (var file in written.AsEnumerable().Reverse())
                {
                    try
                    {
                        EnsureOrdinaryPath(file.Target);
                        if (file.Backup != null) File.Copy(file.Backup, file.Target, true);
                        else if (File.Exists(file.Target)) File.Delete(file.Target);
                    }
                    catch (Exception rollbackError) { failures.Add(rollbackError); }
                }
                if (failures.Count > 1)
                {
                    keepBackup = true;
                    try { File.WriteAllLines(Path.Combine(staging, "recovery.txt"), files.Select(file => file.Backup + " => " + file.Target)); }
                    catch (Exception manifestError) { failures.Add(manifestError); }
                    throw new AggregateException("特效资源恢复未完成，备份保留在：" + staging, failures);
                }
                throw;
            }
            finally
            {
                if (!keepBackup && Directory.Exists(staging))
                {
                    try { Directory.Delete(staging, true); }
                    catch (Exception cleanupError) { DebugHelper.WriteException(cleanupError); }
                }
            }
        }

        private static void EnsureOrdinaryPath(string path)
        {
            for (string current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("特效资源路径不能包含符号链接或目录联接。");
        }

        private static void CopyEntry(ZipArchiveEntry entry, Stream output)
        {
            using var input = entry.Open();
            byte[] buffer = new byte[81920];
            long copied = 0;
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                copied += count;
                if (copied > entry.Length || copied > 100_000_000) throw new InvalidDataException("特效包资源大小异常。");
                output.Write(buffer, 0, count);
            }
            if (copied != entry.Length) throw new InvalidDataException("特效包资源不完整。");
        }
    }
}
