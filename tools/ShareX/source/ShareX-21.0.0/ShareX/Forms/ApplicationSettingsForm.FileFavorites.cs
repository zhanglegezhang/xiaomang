using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private void AddFileFavoritesMenuSettings(SettingsPageLayout layout, TableLayoutPanel content)
        {
            layout.Heading(content, "文件收藏右键菜单", "File favorites context menu");
            var status = new Label { Name = "fileFavoritesMenuStatus", AutoSize = true, Text = FileFavoritesMenu.Status() };
            var hint = new Label { AutoSize = true, Text = "启用、修复或移除需要管理员授权，入口对本机所有用户可见。\n日常收藏无需授权；移动程序位置后，请重新修复入口。" };
            void Wrap(Label label)
            {
                if (content.ClientSize.Width < layout.Scale(80)) return;
                var maximum = new Size(content.ClientSize.Width, 0);
                if (label.MaximumSize != maximum) label.MaximumSize = maximum;
            }
            content.SizeChanged += (_, _) => { Wrap(status); Wrap(hint); };
            layout.Add(content, status);
            layout.Add(content, hint);
            var install = new Button { Name = "enableFileFavoritesMenu", Text = "启用 / 修复", AutoSize = true, MinimumSize = new Size(layout.Scale(120), layout.Scale(32)) };
            var remove = new Button { Name = "removeFileFavoritesMenu", Text = "移除入口", AutoSize = true, MinimumSize = new Size(layout.Scale(120), layout.Scale(32)) };
            async void Change(bool enabled)
            {
                install.Enabled = remove.Enabled = false;
                status.Text = "等待管理员授权并更新菜单…";
                try
                {
                    await FileFavoritesMenu.ChangeAsync(enabled);
                    if (!IsDisposed) status.Text = enabled ? FileFavoritesMenu.Status() : "右键入口已移除，已有收藏和文件保持不变。";
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    if (!IsDisposed) status.Text = "已取消授权，菜单未更改。\n" + FileFavoritesMenu.Status();
                }
                catch (Exception ex)
                {
                    ShareX.HelpersLib.DebugHelper.WriteException(ex);
                    if (!IsDisposed) status.Text = ex.Message;
                }
                finally { if (!IsDisposed) install.Enabled = remove.Enabled = true; }
            }
            install.Click += (_, _) => Change(true);
            remove.Click += (_, _) => Change(false);
            layout.Add(content, layout.Inline(install, remove));
        }
    }
}
