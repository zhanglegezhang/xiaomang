using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class FirstUsePasswordForm : Form
    {
        private readonly FirstUseAccessStore store;
        private readonly TextBox passwordInput;
        private readonly Label status;
        private readonly Button verifyButton;

        internal FirstUsePasswordForm(FirstUseAccessStore store)
        {
            SuspendLayout();
            this.store = store;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(420, 172);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            Text = $"{Program.ProductName}{Program.ProductVersion} - 首次使用验证";
            Icon = ShareXResources.Icon;
            Font = new Font("Microsoft YaHei", 9f);
            BackColor = Color.FromArgb(28, 35, 41);
            ForeColor = Color.FromArgb(231, 233, 234);

            var instruction = new Label
            {
                Text = "请输入提供给你的使用密码。",
                Bounds = new Rectangle(24, 12, 372, 20),
                Font = new Font(Font.FontFamily, 10f, FontStyle.Bold)
            };
            var explanation = new Label
            {
                Text = "验证成功后，本机当前用户以后无需再输入。",
                Bounds = new Rectangle(24, 34, 372, 20),
                ForeColor = Color.FromArgb(172, 183, 192)
            };
            var passwordLabel = new Label
            {
                Text = "使用密码",
                Bounds = new Rectangle(24, 60, 72, 30),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var passwordFrame = new Panel
            {
                Bounds = new Rectangle(96, 60, 300, 30),
                BackColor = Color.FromArgb(34, 43, 51),
                TabIndex = 0
            };
            passwordFrame.Paint += (sender, e) =>
            {
                int borderWidth = LogicalToDeviceUnits(1);
                Color borderColor = Color.FromArgb(85, 98, 108);
                ControlPaint.DrawBorder(e.Graphics, passwordFrame.ClientRectangle,
                    borderColor, borderWidth, ButtonBorderStyle.Solid,
                    borderColor, borderWidth, ButtonBorderStyle.Solid,
                    borderColor, borderWidth, ButtonBorderStyle.Solid,
                    borderColor, borderWidth, ButtonBorderStyle.Solid);
            };
            passwordInput = new TextBox
            {
                UseSystemPasswordChar = true,
                MaxLength = 256,
                AccessibleName = "使用密码",
                BackColor = Color.FromArgb(34, 43, 51),
                ForeColor = ForeColor,
                BorderStyle = BorderStyle.None,
                TabIndex = 0
            };
            passwordFrame.Controls.Add(passwordInput);
            passwordFrame.Resize += (sender, e) => CenterPasswordInput(passwordFrame);
            passwordInput.FontChanged += (sender, e) => CenterPasswordInput(passwordFrame);
            status = new Label
            {
                Bounds = new Rectangle(24, 94, 372, 34),
                ForeColor = Color.FromArgb(255, 157, 99)
            };
            verifyButton = CreateButton("验证并进入", new Rectangle(182, 132, 112, 30), 1);
            verifyButton.Click += (sender, e) => SubmitPassword();
            var cancel = CreateButton("退出", new Rectangle(306, 132, 90, 30), 2);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { instruction, explanation, passwordLabel, passwordFrame, status, verifyButton, cancel });
            AcceptButton = verifyButton;
            CancelButton = cancel;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ResumeLayout(false);
            PerformLayout();
            CenterPasswordInput(passwordFrame);
        }

        private void CenterPasswordInput(Panel frame)
        {
            int padding = LogicalToDeviceUnits(8);
            passwordInput.Bounds = new Rectangle(padding, (frame.ClientSize.Height - passwordInput.PreferredHeight) / 2,
                Math.Max(1, frame.ClientSize.Width - padding * 2), passwordInput.PreferredHeight);
        }

        private Button CreateButton(string text, Rectangle bounds, int tabIndex)
        {
            var button = new Button
            {
                Text = text, Bounds = bounds, TabIndex = tabIndex,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(34, 43, 51),
                ForeColor = ForeColor
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(58, 73, 84);
            return button;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            passwordInput.Focus();
        }

        private void SubmitPassword()
        {
            if (passwordInput.Text.Length == 0)
            {
                status.Text = "请输入使用密码。";
                passwordInput.Focus();
                return;
            }
            verifyButton.Enabled = false;
            UseWaitCursor = true;
            try
            {
                FirstUseValidationResult result = store.ValidateAndSave(passwordInput.Text);
                passwordInput.Clear();
                if (result == FirstUseValidationResult.Authorized)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    status.Text = result == FirstUseValidationResult.InvalidPassword
                        ? "密码不正确，请重新输入。"
                        : "无法保存验证记录，请检查本机可用空间和文件权限后重试。";
                    passwordInput.Focus();
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    UseWaitCursor = false;
                    verifyButton.Enabled = true;
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            passwordInput.Clear();
            base.OnFormClosed(e);
        }
    }
}
