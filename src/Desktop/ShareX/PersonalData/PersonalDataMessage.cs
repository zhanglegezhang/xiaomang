using System;
using System.Drawing;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    // Independent of MainForm: restore notifications also run after it has exited.
    internal sealed class PersonalDataMessage : Form
    {
        private readonly Bitmap statusImage;

        internal PersonalDataMessage(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon kind)
        {
            SuspendLayout();
            Text = caption; Icon = ShareXResources.Icon;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 220);
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
            var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(440, 0), Location = new Point(72, 20) };
            body.Controls.Add(label);
            System.Drawing.Icon symbol = kind switch {
                MessageBoxIcon.Error => SystemIcons.Error,
                MessageBoxIcon.Warning => SystemIcons.Warning,
                MessageBoxIcon.Question => SystemIcons.Question,
                MessageBoxIcon.Information => SystemIcons.Information,
                _ => null
            };
            if (symbol != null) {
                statusImage = symbol.ToBitmap();
                body.Controls.Add(new PictureBox { Image = statusImage, SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(20, 20), Size = new Size(32, 32) });
            } else { label.Location = new Point(20, 20); label.MaximumSize = new Size(496, 0); }
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft };
            Button Add(string title, DialogResult result) {
                var button = new Button { Text = title, DialogResult = result, Size = new Size(88, 32) };
                footer.Controls.Add(button); return button;
            }
            if (buttons == MessageBoxButtons.YesNo) {
                CancelButton = Add("否", DialogResult.No);
                AcceptButton = Add("是", DialogResult.Yes);
                FormClosing += (_, _) => { if (DialogResult == DialogResult.Cancel || DialogResult == DialogResult.None) DialogResult = DialogResult.No; };
            } else if (buttons == MessageBoxButtons.OK) {
                var ok = Add("确定", DialogResult.OK); AcceptButton = CancelButton = ok;
            } else { throw new ArgumentOutOfRangeException(nameof(buttons)); }
            Controls.Add(body); Controls.Add(footer);
            // Measure after DPI scaling, otherwise startup-thread DPI can leave a large empty body.
            Load += (_, _) => ClientSize = new Size(ClientSize.Width,
                Math.Min(Math.Max(label.Bottom + body.Padding.Bottom, 90 * DeviceDpi / 96) + footer.Height,
                    Screen.FromControl(this).WorkingArea.Height * 3 / 4));
            ShareXResources.ApplyTheme(this, true);
            ResumeLayout(false);
        }

        internal static DialogResult Show(string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
            => Show(null, text, caption, buttons, icon);

        internal static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
        {
            using var form = new PersonalDataMessage(text, caption, buttons, icon);
            if (owner is Control control && control.IsDisposed) owner = null;
            form.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
            form.ShowInTaskbar = owner == null;
            return owner == null ? form.ShowDialog() : form.ShowDialog(owner);
        }

        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) statusImage?.Dispose(); }
    }
}
