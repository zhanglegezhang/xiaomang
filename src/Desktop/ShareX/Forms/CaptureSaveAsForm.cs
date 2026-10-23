using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class CaptureSaveAsForm : Form
    {
        private readonly TextBox fileName = new() { Dock = DockStyle.Fill };
        private readonly TextBox folder = new() { Dock = DockStyle.Fill };
        private readonly Button save = new() { Text = "保存", AutoSize = false, Size = new Size(84, 30) };
        private readonly Bitmap image;
        private readonly TaskSettings settings;
        private bool saving, saveSucceeded, successCloseAllowed;
        private readonly System.Windows.Forms.Timer successTimer = new() { Interval = 500 };
        private readonly Label status = new() { AutoSize = true, Text = "", Margin = new Padding(3, 7, 12, 0) };
        private CaptureSaveAsForm(Bitmap image, TaskSettings settings)
        {
            this.image = image; this.settings = settings;
            Text = "另存为"; ShowInTaskbar = false; MinimizeBox = MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(520, 180); Padding = new Padding(16);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "文件名", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            layout.Controls.Add(fileName, 1, 0); layout.SetColumnSpan(fileName, 2);
            layout.Controls.Add(new Label { Text = "保存位置", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            layout.Controls.Add(folder, 1, 1);
            var browse = new Button { Text = "浏览…", Dock = DockStyle.Top, Height = 28 };
            browse.Click += (_, _) => { using var picker = new FolderBrowserDialog { SelectedPath = folder.Text }; if (picker.ShowDialog(this) == DialogResult.OK) folder.Text = picker.SelectedPath; };
            layout.Controls.Add(browse, 2, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(84, 30) };
            actions.Controls.Add(save); actions.Controls.Add(cancel); actions.Controls.Add(status); layout.Controls.Add(actions, 0, 2); layout.SetColumnSpan(actions, 3);
            Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
            folder.Text = Program.Settings?.CaptureSaveAsDirectory;
            if (!Directory.Exists(folder.Text)) folder.Text = TaskHelpers.GetScreenshotsFolder(settings);
            if (!Directory.Exists(folder.Text)) folder.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            fileName.Text = "小蟒截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            MainMenuAppearance.Apply(this); MainMenuAppearance.SetPrimary(save);
            status.ForeColor = Color.FromArgb(95, 199, 133);
            successTimer.Tick += (_, _) =>
            {
                successTimer.Stop(); successCloseAllowed = true;
                DialogResult = DialogResult.OK; Close();
            };
            FormClosing += (_, e) => { if (saveSucceeded && !successCloseAllowed) e.Cancel = true; };
            save.Click += (_, _) => SaveImage();
            AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
        }
        private void SaveImage()
        {
            if (saving || saveSucceeded) return;

            try
            {
                string name = fileName.Text.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name is "." or "..") throw new IOException("请输入有效的文件名。");
                string directory = Path.GetFullPath(folder.Text.Trim());
                if (!Directory.Exists(directory)) throw new IOException("保存位置不存在，请重新选择。");
                saving = true; save.Enabled = false; UseWaitCursor = true;
                using var data = TaskHelpers.PrepareImage(image, settings);
                string extension = "." + data.ImageFormat.GetDescription().ToLowerInvariant();
                string path = Path.Combine(directory, name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension);
                bool overwrite = File.Exists(path);
                if (overwrite && MessageBox.Show(this, "文件已存在，是否覆盖？", "另存为", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                WriteImage(data.ImageStream, path, overwrite);
                saveSucceeded = true;
                if (Program.Settings != null)
                {
                    Program.Settings.CaptureSaveAsDirectory = directory;
                    SettingManager.SaveApplicationConfigAsync();
                }
                fileName.Enabled = folder.Enabled = false;
                foreach (Control control in save.Parent.Controls)
                    if (control is Button) control.Enabled = false;
                status.Text = "✓ 保存成功";
                successTimer.Start();
            }
            catch (Exception ex) { MessageBox.Show(this, "保存失败：" + ex.Message, "另存为", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { saving = false; save.Enabled = !saveSucceeded; UseWaitCursor = false; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) successTimer.Dispose();
            base.Dispose(disposing);
        }
        internal static void WriteImage(Stream image, string path, bool overwrite)
        {
            string temp = Path.Combine(Path.GetDirectoryName(path), ".xiaomang-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { image.Position = 0; image.CopyTo(output); output.Flush(true); }
                File.Move(temp, path, overwrite);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static bool Save(IWin32Window owner, Bitmap image, TaskSettings settings)
        {
            using var dialog = new CaptureSaveAsForm(image, settings);
            // A capture overlay spans the virtual desktop. Center on the screen
            // where the toolbar was clicked, not on the multi-monitor owner.
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.TopMost = true;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            dialog.Location = new Point(area.Left + Math.Max(0, (area.Width - dialog.Width) / 2),
                area.Top + Math.Max(0, (area.Height - dialog.Height) / 2));
            dialog.Shown += (_, _) =>
            {
                dialog.Location = new Point(area.Left + Math.Max(0, (area.Width - dialog.Width) / 2),
                    area.Top + Math.Max(0, (area.Height - dialog.Height) / 2));
                dialog.Activate(); dialog.fileName.Focus(); dialog.fileName.SelectAll();
            };
            return dialog.ShowDialog(owner) == DialogResult.OK;
        }
    }
}







