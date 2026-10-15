using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace ShareX
{
    internal static class PersonalDataInputPreflight
    {
        internal const string MissingComponent = "迁移输入法个人数据需要文本输入法组件。请先到“全局设置 → 注册”下载或修复文本输入组件，或取消勾选“输入法个人数据”后重试。";

        private static bool ConverterPresent(string bundleDirectory)
        {
            // Configuration conversion exits before engine initialization. It only
            // needs the worker, not dictionaries, settings UI or TSF registration.
            try { return new FileInfo(Path.Combine(bundleDirectory, "XiaomangInputWorker.exe")).Length > 0; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static void RequireComponent(string bundleDirectory, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!ConverterPresent(bundleDirectory)) throw new IOException(MissingComponent);
        }

        internal static PersonalDataKind[] ForExport(string inputDirectory, PersonalDataKind[] selected, string bundleDirectory, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!selected.Contains(PersonalDataKind.InputMethod) || ConverterPresent(bundleDirectory)) return selected;

            // A lightweight installation may never have used text input. Only omit a
            // provably empty category; existing or unreadable data must not be lost silently.
            if (HasData(inputDirectory, token)) throw new IOException(MissingComponent);
            var effective = selected.Where(kind => kind != PersonalDataKind.InputMethod).ToArray();
            if (effective.Length == 0) throw new IOException("本机没有可导出的输入法个人数据，请选择其他数据类别。");
            return effective;
        }

        private static bool HasData(string root, CancellationToken token)
        {
            PersonalDataArchive.SafePath(root);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(root); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            if ((attributes & FileAttributes.Directory) == 0) throw new IOException("输入法个人数据目录无效，请检查存储位置。");

            var files = PersonalDataArchive.InputFiles.Concat(PersonalDataArchive.GlossaryFiles).Append("config.toml").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFileSystemEntries(root))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileName(path);
                if (files.Contains(name))
                {
                    PersonalDataArchive.SafePath(path);
                    if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new IOException("输入法个人数据文件位置被目录占用，请检查后重试。");
                    return true;
                }
                if (name.Equals("dicts", StringComparison.OrdinalIgnoreCase) || name.Equals("codes", StringComparison.OrdinalIgnoreCase))
                {
                    PersonalDataArchive.SafePath(path);
                    if ((File.GetAttributes(path) & FileAttributes.Directory) == 0) throw new IOException("输入法个人词库目录无效，请检查后重试。");
                    // A child directory is also exported, even when it contains no files.
                    if (Directory.EnumerateFileSystemEntries(path).Any()) return true;
                }
            }
            token.ThrowIfCancellationRequested();
            return false;
        }
    }
}
