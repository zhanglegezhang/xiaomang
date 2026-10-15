using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    // The main window has saved and exited. This modal message loop keeps preparation
    // cancellable while the replacement worker retains the existing transfer locks.
    internal sealed class PersonalDataRestoreProgressForm : Form
    {
        private readonly PersonalDataTransferControl operation = new();
        private readonly Label status;
        private readonly Button cancel;
        private bool working = true;
        internal string Backup { get; private set; }
        internal Exception Failure { get; private set; }
        internal PersonalDataRestoreProgressForm(Func<PersonalDataTransferControl, IProgress<string>, Task<string>> apply)
        {
            Text = "导入个人数据"; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            Font = new Font("Microsoft YaHei UI", 9F); ClientSize = new Size(470, 150);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            status = new Label { AutoSize = true, MaximumSize = new Size(425, 0), Text = "正在准备恢复…", Location = new Point(20, 20) };
            cancel = new Button { Text = "取消", AutoSize = true, MinimumSize = new Size(80, 32), Location = new Point(360, 100), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            Controls.Add(status); Controls.Add(cancel); CancelButton = cancel;
            void Cancel() {
                if (operation.RequestCancel()) status.Text = "正在取消，请稍候…";
                cancel.Enabled = false;
                if (!operation.Token.IsCancellationRequested) status.Text = "正在恢复，请稍候…";
            }
            cancel.Click += (_, _) => Cancel();
            FormClosing += (_, e) => { if (working) { e.Cancel = true; Cancel(); } };
            Shown += async (_, _) => {
                long last = 0;
                var progress = new Progress<string>(text => {
                    if (!working || IsDisposed) return;
                    cancel.Enabled = operation.CanCancel;
                    if (operation.Token.IsCancellationRequested) status.Text = "正在取消，请稍候…";
                    else if (!operation.CanCancel) status.Text = "正在恢复，请稍候…";
                    else if (Environment.TickCount64 - last >= 150) { last = Environment.TickCount64; status.Text = text; }
                });
                try { Backup = await Task.Run(() => apply(operation, progress)); }
                catch (Exception error) { Failure = error; }
                finally { working = false; Close(); }
            };
            ShareXResources.ApplyTheme(this, true);
        }
        protected override void Dispose(bool disposing) { if (disposing) operation.Dispose(); base.Dispose(disposing); }
    }
}
