using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    internal sealed class PersonalDataTransferForm : Form
    {
        private readonly Dictionary<PersonalDataKind, CheckBox> checks = new();
        private readonly Label status;
        private readonly Button action, cancel;
        private readonly bool importing;
        private readonly string source;
        private readonly PersonalDataKind[] availableKinds;
        private bool working;
        private PersonalDataTransferControl operation;
        private void CancelOperation()
        {
            if (working) {
                if (operation?.RequestCancel() == true) status.Text = "正在取消，请稍候…";
                cancel.Enabled = false;
            } else { DialogResult = DialogResult.Cancel; Close(); }
        }
        private sealed class LimitedProgress : IProgress<string>
        {
            private readonly IProgress<string> target;
            private long last;
            internal LimitedProgress(IProgress<string> target) { this.target = target; }
            public void Report(string text) { long now = Environment.TickCount64; if (now - last < 150) return; last = now; target.Report(text); }
        }
        internal PersonalDataTransferForm(bool importing, string source = null, PersonalDataManifest manifest = null)
        {
            SuspendLayout();
            this.importing = importing; this.source = source;
            availableKinds = importing ? manifest.Categories.ToArray() : Enum.GetValues<PersonalDataKind>();
            Text = importing ? "导入个人数据" : "导出个人数据";
            Icon = ShareXResources.Icon; StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(470, 410);
            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 0, AutoScroll = true };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(stack);
            void Add(Control control) { stack.RowStyles.Add(new RowStyle(SizeType.AutoSize)); control.Margin = new Padding(0, 0, 0, 8); control.Anchor = AnchorStyles.Left | AnchorStyles.Right; stack.Controls.Add(control, 0, stack.RowCount++); }
            Add(new Label { AutoSize = true, MaximumSize = new Size(425, 0), Text = importing ? "备份时间：" + manifest.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "   版本：" + manifest.ProductVersion : "选择要备份的数据。备份文件可用于安装新包后恢复。" });
            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            int index = 0;
            foreach (var kind in Enum.GetValues<PersonalDataKind>()) {
                bool available = !importing || manifest.Categories.Contains(kind);
                var check = new CheckBox { Text = PersonalDataArchive.Label(kind), AutoSize = true, Enabled = available, Checked = available && kind != PersonalDataKind.Media, Margin = new Padding(0, 5, 6, 5), Tag = kind };
                checks.Add(kind, check); grid.Controls.Add(check, index % 2, index / 2); index++;
            }
            Add(grid);
            if (importing && manifest.MissingVoiceAttachments.Count > 0)
                Add(new Label { AutoSize = true, MaximumSize = new Size(425, 0), Text = $"此备份注明 {manifest.MissingVoiceAttachments.Count} 个录音附件已缺失。待办文字可恢复，这些录音无法恢复。" });
            if (importing && manifest.InvalidDiaryFiles.Count > 0)
                Add(new Label { AutoSize = true, MaximumSize = new Size(425, 0), Text = PersonalDataArchive.DiaryWarning(manifest.InvalidDiaryFiles) });
            Add(new Label { AutoSize = true, MaximumSize = new Size(425, 0), Text = importing ? "勾选的类别将整体替换，未勾选的保持不变。缺少文本组件时，输入法数据保留待恢复，其他数据照常导入。导入前自动备份，完成后重启。" : "截图和录屏默认不选。账号密钥、本机路径和程序文件不包含在备份中。" });
            Add(new Label { AutoSize = true, MaximumSize = new Size(425, 0), Text = "操作时会保存并关闭临时编辑窗口及所选的日记窗口，暂时暂停所选的输入法后台。" });
            status = new Label { AutoSize = true, MaximumSize = new Size(425, 0), Text = "请选择数据类别。" }; Add(status);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Padding = new Padding(12, 8, 12, 12) };
            action = new Button { Text = importing ? "导入并重启" : "导出…", AutoSize = true, MinimumSize = new Size(112, 32) };
            cancel = new Button { Text = "取消", AutoSize = true, MinimumSize = new Size(80, 32), DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(action); buttons.Controls.Add(cancel); Controls.Add(buttons); buttons.BringToFront();
            CancelButton = cancel; AcceptButton = action;
            action.Click += async (_, _) => await Execute();
            cancel.Click += (_, _) => CancelOperation();
            FormClosing += (_, e) => { if (working) { e.Cancel = true; CancelOperation(); } };
            ShareXResources.ApplyTheme(this, true);
            AutoScaleDimensions = new SizeF(96F, 96F);
            ResumeLayout(true);
        }
        private async Task Execute()
        {
            if (working) return;
            var selected = checks.Where(item => item.Value.Enabled && item.Value.Checked).Select(item => item.Key).ToArray();
            if (selected.Length == 0) { status.Text = "请至少选择一类数据。"; return; }
            string destination = null;
            if (!importing) {
                using var save = new SaveFileDialog { Filter = "小蟒个人数据备份 (*.xmbackup)|*.xmbackup", FileName = "小蟒个人数据_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xmbackup", AddExtension = true, OverwritePrompt = true };
                if (save.ShowDialog(this) != DialogResult.OK) return;
                destination = save.FileName;
                if (File.Exists(destination)) { status.Text = "请使用一个新的文件名，避免覆盖已有备份。"; return; }
            }
            operation = new PersonalDataTransferControl();
            working = true; action.Enabled = false; cancel.Enabled = true; cancel.DialogResult = DialogResult.None;
            foreach (var check in checks.Values) check.Enabled = false;
            var progress = new LimitedProgress(new Progress<string>(text => {
                if (working && !IsDisposed) { status.Text = operation.Token.IsCancellationRequested ? "正在取消，请稍候…" : text; cancel.Enabled = operation.CanCancel; }
            }));
            bool readyToImport = false;
            try {
                status.Text = "检查所需组件并准备数据…";
                if (importing) { await PersonalDataManager.QueueImport(this, selected, source, progress, operation); readyToImport = true; }
                else {
                    await PersonalDataManager.Export(this, selected, destination, progress, operation);
                    var exported = await Task.Run(() => PersonalDataArchive.Inspect(destination));
                    status.Text = "导出完成：" + destination + (exported.InvalidDiaryFiles.Count > 0 ? "\n\n" + PersonalDataArchive.DiaryWarning(exported.InvalidDiaryFiles) : "");
                    if (selected.Contains(PersonalDataKind.InputMethod) && !exported.Categories.Contains(PersonalDataKind.InputMethod))
                        status.Text += "\n\n本机没有输入法个人数据，本次备份未包含该类别。";
                    action.Visible = false; cancel.Text = "关闭";
                }
            } catch (OperationCanceledException) { status.Text = "已取消，原数据未替换。"; }
            catch (Exception error) { DebugHelper.WriteException(error, "Personal data transfer"); status.Text = "未完成：" + error.Message; }
            finally {
                working = false; action.Enabled = cancel.Enabled = true; ControlBox = true;
                operation.Dispose(); operation = null; cancel.DialogResult = DialogResult.Cancel;
                foreach (var item in checks) item.Value.Enabled = availableKinds.Contains(item.Key);
            }
            if (readyToImport) { DialogResult = DialogResult.OK; Close(); }
        }
        internal static async Task Open(Control owner, bool importing, string pendingSource = null)
        {
            string source = pendingSource; PersonalDataManifest manifest = null;
            if (importing) {
                using var open = new OpenFileDialog { Filter = "小蟒个人数据备份 (*.xmbackup)|*.xmbackup", CheckFileExists = true };
                if (source == null) {
                    if (open.ShowDialog(owner) != DialogResult.OK) return;
                    source = open.FileName;
                }
                try { manifest = await Task.Run(() => PersonalDataArchive.Inspect(source)); }
                catch (Exception error) { PersonalDataMessage.Show(owner, "无法读取备份：" + error.Message, "个人数据", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            }
            using var form = new PersonalDataTransferForm(importing, source, manifest);
            if (form.ShowDialog(Program.MainForm) == DialogResult.OK && importing) PersonalDataManager.RestartForImport();
        }
    }
}
