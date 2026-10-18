using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ShareX;

internal static class ManagedComponentRemoval
{
    // Callers supply only their own component manifest, never a user-selected directory.
    internal static void RemoveFiles(string root, IEnumerable<string> names, CancellationToken token)
    {
        var paths = names.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => ComponentBundles.SafePath(root, name)).ToArray();
        token.ThrowIfCancellationRequested();
        var leases = new List<FileStream>();
        var moved = new List<(string Original, string Backup)>();
        string stage = null;
        bool committed = false;
        try
        {
            var existing = new List<string>();
            foreach (string path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(path)) throw new IOException("组件文件被同名目录替代，请先检查组件目录。");
                if (!File.Exists(path)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                    throw new IOException("组件文件为只读，未移除任何文件。");
                // A running executable or loaded DLL cannot grant this write lease.
                // Delete sharing lets us rename our own locked files as one short commit.
                leases.Add(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
                existing.Add(path);
            }
            token.ThrowIfCancellationRequested();
            if (existing.Count == 0) return;
            stage = ComponentBundles.SafePath(root, ".component-remove-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            // Cancellation stops before commit; rename failures restore the originals.
            foreach (string path in existing)
            {
                string backup = ComponentBundles.SafePath(stage, moved.Count + ".removed");
                ComponentBundles.SafePath(root, Path.GetRelativePath(root, path));
                File.Move(path, backup);
                moved.Add((path, backup));
            }
            committed = true;
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            foreach (var file in moved.AsEnumerable().Reverse())
                try { File.Move(file.Backup, file.Original); }
                catch (Exception restore) { failures.Add(restore); }
            if (failures.Count > 0)
                throw new IOException("移除未完成，部分组件无法恢复；恢复文件保留在：" + stage,
                    new AggregateException(new[] { error }.Concat(failures)));
            throw;
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
            if (committed)
            {
                try { foreach (var file in moved) File.Delete(file.Backup); }
                catch (Exception error)
                {
                    throw new IOException("组件已停用，但部分文件未能清理；残留文件保留在：" + stage, error);
                }
            }
            // Do not recursively delete anything: unrelated files always stay in place.
            if (stage != null && Directory.Exists(stage) && !Directory.EnumerateFileSystemEntries(stage).Any())
                Directory.Delete(stage);
        }
    }
}
