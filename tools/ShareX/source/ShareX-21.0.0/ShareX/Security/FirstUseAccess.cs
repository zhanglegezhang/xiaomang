using System;
using System.IO;
using System.Security;
using System.Windows.Forms;

namespace ShareX
{
    internal static class FirstUseAccess
    {
        internal static bool IsVerified()
        {
            try { return new FirstUseAccessStore().HasAccess(); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is SecurityException)
            {
                return false;
            }
        }

        internal static bool EnsureAuthorized()
        {
            FirstUseAccessStore store;
            try { store = new FirstUseAccessStore(); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is SecurityException)
            {
                MessageBox.Show("无法读取当前用户的本地数据目录，请检查本机文件权限后重新启动。",
                    Program.ProductName + " - 首次使用验证", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (store.HasAccess()) return true;
            using var form = new FirstUsePasswordForm(store);
            return form.ShowDialog() == DialogResult.OK && store.HasAccess();
        }
    }
}
