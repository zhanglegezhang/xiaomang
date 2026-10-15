using System;
using System.IO;

namespace ShareX
{
    // 先记录再禁用；崩溃在两步之间也可在下一次启动恢复。只恢复本程序暂停过的条目。
    internal sealed class InputProfilePauseState
    {
        private const string Marker = "xiaomang-input-profile-paused-v1";
        private readonly string path;

        internal InputProfilePauseState(string path) => this.path = path;

        internal void Pause(Func<bool> isEnabled, Action<bool> setEnabled)
        {
            if (!isEnabled()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, Marker);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            setEnabled(false);
            if (isEnabled()) throw new InvalidOperationException("小蟒输入法没有成功暂停。");
        }

        internal bool Resume(bool backendReady, Func<bool> isEnabled, Action<bool> setEnabled)
        {
            if (!backendReady || !File.Exists(path)) return false;
            if (File.ReadAllText(path) != Marker) throw new InvalidOperationException("输入法暂停记录无法识别；未改变启用状态。");
            if (!isEnabled()) setEnabled(true);
            if (!isEnabled()) throw new InvalidOperationException("小蟒输入法没有成功恢复。");
            File.Delete(path);
            return true;
        }
    }
}
