using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX;

internal sealed class BaiduTranslationSettingsForm : Form
{
    internal const string CertificationUrl = "https://fanyi-api.baidu.com/manage/developer";
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBox id, secret;
    private readonly Label status;
    private readonly RegistrationPopupButton reveal, save, test, clear;
    private readonly Font headingFont, bodyFont;
    private readonly Image logo;
    private bool cancellationDisposed;

    internal BaiduTranslationSettingsForm() : this(
        () => { var value = BaiduTranslationApi.Load(); return (value?.AppId, value?.Secret); },
        BaiduTranslationApi.Save, BaiduTranslationApi.Clear,
        (appId, key, token) => BaiduTranslationApi.TranslateAsync(new(appId, key), "Hello", "zh", token),
        () => Process.Start(new ProcessStartInfo(CertificationUrl) { UseShellExecute = true })) { }

    // Delegates keep UI verification independent of real credentials and API quota.
    internal BaiduTranslationSettingsForm(Func<(string AppId, string Secret)> load,
        Action<string, string> saveCredentials, Action clearCredentials,
        Func<string, string, CancellationToken, Task> testConnection, Action openCertification)
    {
        SuspendLayout();
        Name = "baiduTranslationSettings"; Text = "百度翻译 API 配置";
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(560, 438); MinimumSize = new Size(480, 438);
        BackColor = RegistrationPopupStyle.Background; ForeColor = RegistrationPopupStyle.Text;
        bodyFont = new Font("Microsoft YaHei UI", 10); headingFont = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
        Font = bodyFont; Icon = ShareXResources.Icon; DoubleBuffered = true; Padding = new Padding(1);
        logo = ShareXResources.Icon.ToBitmap();

        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var icon = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom };
        var title = new Label { Text = Text, Font = headingFont, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        var close = new RegistrationPopupButton { Name = "baiduClose", Glyph = "close", AccessibleName = "关闭", TabStop = false, Quiet = true, BackColor = BackColor };
        close.Click += (_, _) => Close();
        foreach (Control handle in new Control[] { header, icon, title }) handle.MouseDown += DragHeader;
        header.Controls.AddRange(new Control[] { icon, title, close });
        header.Layout += (_, _) =>
        {
            int S(int value) => RegistrationPopupStyle.S(header, value);
            icon.SetBounds(S(22), S(16), S(32), S(32));
            close.SetBounds(header.Width - S(52), S(14), S(36), S(36));
            title.SetBounds(S(66), 0, Math.Max(S(100), close.Left - S(78)), header.Height - S(1));
        };
        header.Paint += (_, e) => { using var pen = new Pen(RegistrationPopupStyle.Edge); e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };

        id = new TextBox { Name = "baiduAppId", BorderStyle = BorderStyle.None, BackColor = RegistrationPopupStyle.Surface,
            ForeColor = ForeColor, PlaceholderText = "输入 APP ID", AccessibleName = "APP ID", TabIndex = 0 };
        secret = new TextBox { Name = "baiduSecret", BorderStyle = BorderStyle.None, BackColor = RegistrationPopupStyle.Surface,
            ForeColor = ForeColor, UseSystemPasswordChar = true, AccessibleName = "密钥", TabIndex = 1 };
        reveal = new RegistrationPopupButton { Name = "baiduRevealSecret", Glyph = "eye", AccessibleName = "显示密钥", Quiet = true, TabIndex = 2 };
        reveal.Click += (_, _) => { secret.UseSystemPasswordChar = !secret.UseSystemPasswordChar; UpdateReveal(); };
        Deactivate += (_, _) => { secret.UseSystemPasswordChar = true; UpdateReveal(); };

        var body = new TableLayoutPanel { Name = "baiduConfigurationBody", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8,
            Padding = new Padding(22, 16, 22, 0), Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 24, 42, 34, 42, 16, 42, 16 }) body.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Label Label(string text) => new() { Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.BottomLeft };
        body.Controls.Add(Label("APP ID"), 0, 0); body.Controls.Add(new CredentialField(id), 0, 1);
        var secretLabel = Label("密钥"); secretLabel.Padding = new Padding(0, 9, 0, 4); body.Controls.Add(secretLabel, 0, 2);
        body.Controls.Add(new CredentialField(secret, reveal), 0, 3);
        var auxiliary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        auxiliary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); auxiliary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var authenticate = new RegistrationPopupButton { Name = "baiduOpenCertification", Text = "前往百度认证", Glyph = "external",
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 7, 0), TabIndex = 3 };
        clear = new RegistrationPopupButton { Name = "baiduClearCredentials", Text = "清空当前认证", Glyph = "trash", Danger = true,
            Dock = DockStyle.Fill, Margin = new Padding(7, 0, 0, 0), TabIndex = 4 };
        authenticate.Click += (_, _) =>
        {
            try { openCertification(); }
            catch { SetStatus("无法打开浏览器，请稍后重试。", true); }
        };
        auxiliary.Controls.Add(authenticate, 0, 0); auxiliary.Controls.Add(clear, 1, 0); body.Controls.Add(auxiliary, 0, 5);
        var description = new Label { Name = "baiduConfigurationHint", Text = "配置后使用个人 API 额度；未配置时使用网页翻译。\n凭据仅本机加密保存。",
            ForeColor = RegistrationPopupStyle.Muted, Dock = DockStyle.Fill, Margin = Padding.Empty };
        body.Controls.Add(description, 0, 7);

        status = new Label { Name = "baiduConfigurationStatus", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(22, 0, 22, 0), Margin = Padding.Empty, ForeColor = RegistrationPopupStyle.Muted, AutoEllipsis = false };
        var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(22, 12, 22, 12) };
        footer.Paint += (_, e) => { using var pen = new Pen(RegistrationPopupStyle.Edge); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        test = new RegistrationPopupButton { Name = "baiduTestConnection", Text = "测试连接", Size = new Size(120, 40), Margin = new Padding(0, 0, 10, 0), TabIndex = 5 };
        save = new RegistrationPopupButton { Name = "baiduSaveCredentials", Text = "保存", Primary = true, Size = new Size(110, 40), Margin = Padding.Empty, TabIndex = 6 };
        buttons.Controls.AddRange(new Control[] { test, save }); footer.Controls.Add(buttons); AcceptButton = save;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.Controls.Add(header, 0, 0); layout.Controls.Add(body, 0, 1); layout.Controls.Add(status, 0, 2); layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
        try { var value = load(); id.Text = value.AppId ?? ""; secret.Text = value.Secret ?? ""; }
        catch { SetStatus("无法读取认证，请重新填写或清空。", true); }
        save.Click += (_, _) =>
        {
            if (!ValidateFields()) return;
            try { saveCredentials(id.Text.Trim(), secret.Text.Trim()); SetStatus("已保存，下次翻译使用 API。"); }
            catch { SetStatus("保存失败，请检查本机存储权限。", true); }
        };
        clear.Click += (_, _) =>
        {
            try { clearCredentials(); id.Clear(); secret.Clear(); secret.UseSystemPasswordChar = true; UpdateReveal(); SetStatus("已清空，下次翻译使用网页。"); }
            catch { SetStatus("清空失败，请检查本机存储权限。", true); }
        };
        test.Click += async (_, _) =>
        {
            if (!ValidateFields()) return;
            test.Enabled = save.Enabled = clear.Enabled = id.Enabled = secret.Enabled = reveal.Enabled = false;
            SetStatus("测试中（仅翻译 Hello）…");
            try { await testConnection(id.Text.Trim(), secret.Text.Trim(), cancellation.Token); if (!IsDisposed) SetStatus("测试成功，保存后生效。"); }
            catch (OperationCanceledException) { if (!IsDisposed) SetStatus("测试已取消或超时。", true); }
            catch (InvalidOperationException error) { if (!IsDisposed) SetStatus(error.Message, true); }
            catch { if (!IsDisposed) SetStatus("测试失败，请稍后重试。", true); }
            finally { if (!IsDisposed) test.Enabled = save.Enabled = clear.Enabled = id.Enabled = secret.Enabled = reveal.Enabled = true; }
        };
        ResumeLayout(true);
    }
    private bool ValidateFields()
    {
        if (string.IsNullOrWhiteSpace(id.Text) || !System.Linq.Enumerable.All(id.Text.Trim(), char.IsAsciiDigit) || string.IsNullOrWhiteSpace(secret.Text))
        { SetStatus("请填写有效的 APP ID 和密钥。", true); return false; }
        return true;
    }
    private void SetStatus(string text, bool error = false) { status.Text = text; status.ForeColor = error ? RegistrationPopupStyle.Danger : RegistrationPopupStyle.Muted; }
    private void UpdateReveal() { reveal.Glyph = secret.UseSystemPasswordChar ? "eye" : "eye-off"; reveal.AccessibleName = secret.UseSystemPasswordChar ? "显示密钥" : "隐藏密钥"; reveal.Invalidate(); }
    private void DragHeader(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, 2, 0);
    }
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); id.Select(); }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int corner = 2; NativeMethods.DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int));
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 2 || Height < 2) return;
        using var path = MainMenuAppearance.Round(new Rectangle(0, 0, Width, Height), RegistrationPopupStyle.S(this, 10));
        var old = Region; Region = new Region(path); old?.Dispose();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = MainMenuAppearance.Round(new Rectangle(0, 0, Width - 1, Height - 1), RegistrationPopupStyle.S(this, 10));
        using var edge = new Pen(RegistrationPopupStyle.Edge); e.Graphics.DrawPath(edge, path);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !cancellationDisposed) { cancellationDisposed = true; cancellation.Cancel(); cancellation.Dispose(); }
        base.Dispose(disposing);
        if (disposing) { logo.Dispose(); headingFont.Dispose(); bodyFont.Dispose(); }
    }
    private sealed class CredentialField : Panel
    {
        private readonly TextBox editor;
        private readonly Button reveal;
        internal CredentialField(TextBox editor, Button reveal = null)
        {
            this.editor = editor; this.reveal = reveal; Dock = DockStyle.Fill; Margin = new Padding(0, 3, 0, 0);
            BackColor = RegistrationPopupStyle.Background; DoubleBuffered = true;
            Controls.Add(editor); if (reveal != null) Controls.Add(reveal);
            editor.GotFocus += (_, _) => Invalidate(); editor.LostFocus += (_, _) => Invalidate();
            MouseDown += (_, _) => editor.Focus();
            SizeChanged += (_, _) => Arrange();
        }
        private void Arrange()
        {
            int inset = RegistrationPopupStyle.S(this, 12), buttonWidth = reveal == null ? 0 : RegistrationPopupStyle.S(this, 40);
            editor.SetBounds(inset, (Height - editor.PreferredHeight) / 2, Math.Max(1, Width - inset * 2 - buttonWidth), editor.PreferredHeight);
            if (reveal != null) reveal.SetBounds(Width - buttonWidth - RegistrationPopupStyle.S(this, 5), RegistrationPopupStyle.S(this, 4), buttonWidth, Height - RegistrationPopupStyle.S(this, 8));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = MainMenuAppearance.Round(new Rectangle(0, 0, Width - 1, Height - 1), RegistrationPopupStyle.S(this, 7));
            using var fill = new SolidBrush(RegistrationPopupStyle.Surface); e.Graphics.FillPath(fill, path);
            using var pen = new Pen(editor.Focused ? RegistrationPopupStyle.Orange : RegistrationPopupStyle.Edge); e.Graphics.DrawPath(pen, path);
        }
    }
}
