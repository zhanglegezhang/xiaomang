using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class XiaomangInputMethodPageForm : Form
    {
        private readonly Label status, operation;
        private readonly Button install, uninstall, settings, retry;
        private readonly VisibleSettingsStatusRefresh<(string Text, bool Registered)> refresh;
        private bool registered;
        private bool busy;

        public XiaomangInputMethodPageForm() : this(true) { }
        public XiaomangInputMethodPageForm(bool registrationMode)
        {
            Text = "小蟒输入法"; Font = MainMenuAppearance.BodyFont; AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true; Size = new Size(780, 550);
            var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(24) };
            content.SuspendLayout();
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(content);
            void Add(Control item) { content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.Controls.Add(item, 0, content.RowCount++); }
            Label Note(string text) => new() { AutoSize = true, Text = text, Margin = new Padding(0, 0, 0, 16) };
            Add(Note("小蟒文本输入法拥有独立的输入法后台，退出小蟒后仍可打字\n小蟒文本输入法会跟随小蟒开机自启动设置，不单独设置后台自启动\n小蟒语音输入法独立运行，会跟随小蟒退出\n在「主菜单 → 快捷键」配置「切换到小蟒输入法」，初始按键为 Ctrl + Shift + J\n已在使用小蟒输入法时保持中英文状态不变\n也可按 Win + 空格切换输入法"));
            if (!registrationMode)
            {
                var settingButton = new Button { Name="openXiaomangInputSettings",Text="输入法设置",AutoSize=true,MinimumSize=new Size(144,34) };
                settingButton.Click += (_,_) => { try { XiaomangInputMethodManager.OpenSettings(); } catch(Exception error) { MessageBox.Show(this,error.Message,"输入法设置"); } };
                var jump = new Button {Name="goToTextRegistration",Text="转到注册页",AutoSize=true,MinimumSize=new Size(144,34)};
                jump.Click += (_,_) => Program.MainForm.OpenRegistrationPage("text");
                var summaryActions = new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true,WrapContents=true };summaryActions.Controls.AddRange(new Control[]{settingButton,jump});Add(summaryActions);
                ClientSizeChanged += (_,_) => { foreach(Control c in content.Controls) if(c is Label label) label.MaximumSize=new Size(Math.Max(1,ClientSize.Width-64),0); };
                AutoScaleDimensions=new SizeF(96,96);PerformAutoScale();content.ResumeLayout(true);
                return;
            }
            Add(Note("「注册/修复」首次使用需写入注册表；\n「重试后台」会重启引擎；\n「撤销注册」会停止后台，保留设置与学习词库；"));
            status = Note("正在检查输入法状态…"); status.Name = "xiaomangInputStatus"; Add(status);
            Button Button(string text, string name) => new() { Text = text, Name = name, Size = new Size(144, 34), Margin = new Padding(0, 0, 10, 10) };
            install = Button("注册 / 修复", "installXiaomangInputMethod");
            uninstall = Button("撤销注册", "uninstallXiaomangInputMethod");
            settings = Button("输入法设置", "openXiaomangInputSettings");
            retry = Button("重试后台", "retryXiaomangInputWorker");
            string logPath = InputMethodRegistrationLogs.GetPath(false);
            var openLog = Button("打开日志文件夹", "openXiaomangInputSetupLogFolder");
            openLog.Click += (_, _) => operation.Text = InputMethodRegistrationLogs.OpenFolder(logPath);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            actions.Controls.AddRange(new Control[] { install, settings, retry, uninstall, openLog }); Add(actions);
            operation = Note(""); operation.Name = "xiaomangInputOperation"; Add(operation);
            Add(Note("注册失败排查 · 安装日志"));
            Add(new TextBox { Name = "xiaomangInputSetupLogPath", Text = logPath, ReadOnly = true, Dock = DockStyle.Fill });
            install.Click += async (_, _) => await Register(true);
            uninstall.Click += async (_, _) => await Register(false);
            settings.Click += (_, _) => { try { XiaomangInputMethodManager.OpenSettings(); } catch (Exception e) { operation.Text = e.Message; } };
            retry.Click += async (_, _) =>
            {
                if (busy) return;
                busy = true; RefreshState();
                try { await Task.Run(XiaomangInputMethodManager.Retry); }
                finally { busy = false; if (!IsDisposed) RefreshState(); }
            };
            void Wrap()
            {
                int width = Math.Max(120, ClientSize.Width - 64);
                content.SuspendLayout();
                try { foreach (Control item in content.Controls) if (item is Label label) label.MaximumSize = new Size(width, 0); }
                finally { content.ResumeLayout(true); }
            }
            ClientSizeChanged += (_, _) => Wrap();
            Shown += (_, _) =>
            {
                content.SuspendLayout();
                try { MainMenuAppearance.Apply(this); Wrap(); }
                finally { content.ResumeLayout(true); }
            };
            refresh = new VisibleSettingsStatusRefresh<(string Text, bool Registered)>(this, () => true,
                () => (XiaomangInputMethodManager.Status, XiaomangInputMethodManager.IsRegistered), result =>
                { status.Text = result.Text; registered = result.Registered; UpdateActions(); },
                e => { status.Text = "读取状态失败：" + e.Message; registered = false; UpdateActions(); });
            AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale(); RefreshState();
            content.ResumeLayout(true);
        }

        private void RefreshState()
        {
            UpdateActions();
            refresh?.Invalidate();
        }

        private void UpdateActions()
        {
            uninstall.Enabled = !busy && registered;
            install.Enabled = settings.Enabled = retry.Enabled = !busy;
        }

        private async Task Register(bool enabled)
        {
            if (busy) return;
            busy = true; RefreshState(); operation.Text = "等待系统授权及注册结果…";
            try { await XiaomangInputMethodManager.RegisterAsync(enabled); if (!IsDisposed) operation.Text = enabled ? "注册完成，请在 Windows 添加键盘并切换。" : "已撤销注册，学习数据保留。"; }
            catch (Exception e) { if (!IsDisposed) operation.Text = "操作未完成：" + e.Message; }
            finally { busy = false; if (!IsDisposed) RefreshState(); }
        }

        protected override void Dispose(bool disposing) { if (disposing) refresh?.Dispose(); base.Dispose(disposing); }
    }
}

