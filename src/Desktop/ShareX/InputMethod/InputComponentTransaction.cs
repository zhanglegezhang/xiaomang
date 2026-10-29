using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX;

// Stage and validate before pausing input. Keep backups until registration and startup succeed.
internal static class InputComponentTransaction
{
    internal static async Task CommitAsync(ComponentBundle bundle, string staged, string destination,
        Func<Task> pause, Func<Task> activate, Func<Task> restore, CancellationToken token)
    {
        if (!await ComponentBundles.IsValidAsync(bundle, staged, token)) throw new IOException("待安装组件校验失败。");
        string backup = destination + ".update-backup-" + Guid.NewGuid().ToString("N");
        var changes = new List<(string Target, string Backup)>();
        bool paused = false, retain = false;
        try
        {
            token.ThrowIfCancellationRequested();
            paused = true;
            await pause();
            token.ThrowIfCancellationRequested();
            // Do not cancel midway through commit or activation; either finish or restore.
            foreach (var file in bundle.Files)
            {
                string target = ComponentBundles.SafePath(destination, file.Path);
                string saved = null;
                if (File.Exists(target))
                {
                    saved = ComponentBundles.SafePath(backup, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved));
                    File.Copy(target, saved);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                changes.Add((target, saved));
                File.Copy(ComponentBundles.SafePath(staged, file.Path), target, true);
            }
            if (!await ComponentBundles.IsValidAsync(bundle, destination, CancellationToken.None)) throw new IOException("安装后组件校验失败。");
            await activate();
        }
        catch (Exception original)
        {
            var errors = new List<Exception>();
            if (paused)
            {
                try { await pause(); } catch (Exception e) { errors.Add(e); }
                foreach (var change in changes.AsEnumerable().Reverse())
                    try { if (change.Backup != null) File.Copy(change.Backup, change.Target, true); else File.Delete(change.Target); }
                    catch (Exception e) { errors.Add(e); }
                try { await restore(); } catch (Exception e) { errors.Add(e); }
            }
            if (errors.Count > 0)
            {
                retain = true;
                throw new IOException("更新失败且恢复未完成，程序备份保留在：" + backup, new AggregateException(new[] { original }.Concat(errors)));
            }
            throw;
        }
        finally
        {
            if (!retain && Directory.Exists(backup)) try { Directory.Delete(backup, true); } catch { }
        }
    }
}
