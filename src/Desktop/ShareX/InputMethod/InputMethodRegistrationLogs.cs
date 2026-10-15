using ShareX.HelpersLib;
using System;
using System.IO;

namespace ShareX
{
    internal static class InputMethodRegistrationLogs
    {
        internal static string GetPath(bool voice) => Path.Combine(Path.GetTempPath(),
            voice ? "XiaomangVoiceInput-setup.log" : "XiaomangInputMethod-setup.log");

        internal static string Instructions(bool voice) =>
            "手动查找：按 Win + R，输入 %TEMP% 并回车，找到 " +
            (voice ? "XiaomangVoiceInput-setup.log" : "XiaomangInputMethod-setup.log") + "。\n" +
            "请将日志文件和报错截图发送给开发者：13711477110@139.com。\n" +
            "以上是当前账户的日志路径。若使用其他管理员账户授权，以安装器弹窗中的日志路径为准；授权前被拒绝或取消时，可能尚未生成日志。";

        internal static string OpenFolder(string path)
        {
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!Directory.Exists(folder)) return "日志目录不存在或无法访问。请按 Win + R 手动查找，或查看安装器弹窗中的日志路径。";
                bool found = File.Exists(path);
                bool opened = found ? FileHelpers.OpenFolderWithFile(path) : FileHelpers.OpenFolder(folder, false);
                if (!opened) return "无法打开日志文件夹。请按 Win + R 手动查找，或查看安装器弹窗中的日志路径。";
                return found ? "已打开文件夹并选中日志，请连同报错截图发送给开发者。" :
                    "已打开文件夹，当前目录未找到对应日志或日志不可访问。授权前被拒绝或取消时可能尚未生成；也请检查安装器弹窗中的路径。";
            }
            catch (Exception error)
            {
                return "打开日志文件夹失败：" + error.Message + " 请按 Win + R 手动查找，或查看安装器弹窗中的日志路径。";
            }
        }
    }
}
