using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class InputMethodPageForm : Form
    {
        private readonly TabControl pages;
        private readonly TabPage voicePage, qingjianPage;
        private readonly TabToTreeView navigation;
        private readonly VoiceInputSetupCoordinator setup;
        private readonly Func<Form> createQingjian;
        private Form qingjian;
        private readonly Label status, operation;
        private readonly Button register, repair, uninstall;
        private readonly VisibleSettingsStatusRefresh<VoiceInputRegistrationState> refresh;
        private VoiceInputRegistrationState? lastState;
        private bool voicePageActive = true;

        internal const string VoiceDescription = "按住 Ctrl + Alt 说话，松开提交；说话时持续显示并修正文字\n首次需管理员授权注册系统组件，用于跨应用输入\n与文本输入法独立，无需安装或切换键盘输入法";
        public InputMethodPageForm() : this(false) { }
        public InputMethodPageForm(bool registrationMode) : this(VoiceInputSetupCoordinator.Default, () => new XiaomangInputMethodPageForm(registrationMode), registrationMode) { }
        internal InputMethodPageForm(VoiceInputSetupCoordinator setup, Func<Form> createQingjian) : this(setup,createQingjian,true) { }
        internal InputMethodPageForm(VoiceInputSetupCoordinator setup, Func<Form> createQingjian, bool registrationMode)
        {
            this.setup = setup; this.createQingjian = createQingjian;
            Text = "输入法"; Font = MainMenuAppearance.BodyFont; AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(780, 550); MinimumSize = new Size(580, 440);
            pages = new TabControl();
            voicePage = new TabPage("语音输入") { Name = "tpVoiceInput", AutoScroll = true, Padding = new Padding(24) };
            qingjianPage = new TabPage("文本输入") { Name = "tpQingjianInputMethod" };
            pages.TabPages.AddRange(new[] { voicePage, qingjianPage });
            navigation = new TabToTreeView { Name = "inputMethodNavigation", Dock = DockStyle.Fill, TreeViewSize = 150 };
            navigation.TabChanged += SelectPage;
            navigation.MainTabControl = pages;
            Controls.Add(navigation);

            if (!registrationMode)
            {
                var summary = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
                var description = new Label { Text = VoiceDescription, AutoSize = true, Margin = new Padding(0,0,0,16) };
                var jump = new Button { Text = "转到注册页", Name = "goToVoiceRegistration", AutoSize = true, MinimumSize = new Size(144,34) };
                jump.Click += (_,_) => Program.MainForm.OpenRegistrationPage("voice");
                summary.Controls.Add(description,0,0);summary.Controls.Add(jump,0,1);voicePage.Controls.Add(summary);
                voicePage.SizeChanged += (_,_) => description.MaximumSize = new Size(Math.Max(1,voicePage.ClientSize.Width-voicePage.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth),0);
                void LayoutSummary()
                {
                    navigation.TreeViewSize=MainMenuAppearance.Scale(this,150);
                    voicePage.Padding=new Padding(MainMenuAppearance.Scale(this,24));
                    jump.MinimumSize=new Size(MainMenuAppearance.Scale(this,144),MainMenuAppearance.Scale(this,34));
                    description.Margin=new Padding(0,0,0,MainMenuAppearance.Scale(this,16));
                }
                ClientSizeChanged+=(_,_)=>LayoutSummary();
                navigation.NavigateToTabPage(voicePage);
                AutoScaleDimensions=new SizeF(96,96);PerformAutoScale();
                Shown += (_,_)=>{MainMenuAppearance.Apply(this);LayoutSummary();};
                return;
            }

            TableLayoutPanel content = new() { Name = "voiceInputContent", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            content.SuspendLayout();
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            void Add(Control control)
            {
                content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.Controls.Add(control, 0, content.RowCount++);
            }
            Label Label(string text, bool heading = false) => new()
            {
                Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 16),
                Font = heading ? new Font(Font.FontFamily, 16, FontStyle.Bold) : Font
            };
            var explanation = Label("按住 Ctrl + Alt 说话，松开提交；说话时持续显示并修正文字\n首次需管理员授权注册系统组件，用于跨应用输入");
            explanation.Name = "voiceInputRegistrationExplanation"; Add(explanation);
            var independence = Label("与文本输入法独立，无需安装或切换键盘输入法");
            independence.Name = "voiceInputIndependenceExplanation"; Add(independence);
            status = Label("组件状态：正在检查…"); status.Name = "voiceInputRegistrationStatus"; Add(status);
            register = new Button { Name = "registerVoiceInputComponent", Text = "正在检查…", Enabled = false, Size = new Size(144, 34), Margin = new Padding(0, 0, 0, 16) };
            register.Click += async (sender, e) =>
            {
                try { await setup.EnsureRegisteredAsync(false); }
                finally { SetupChanged(); }
            };
            repair = new Button { Name = "repairVoiceInputComponent", Text = "修复注册", Enabled = false, Size = new Size(144, 34), Margin = new Padding(8, 0, 0, 0) };
            uninstall = new Button { Name = "uninstallVoiceInputComponent", Text = "卸载组件", Enabled = false, Size = new Size(144, 34), Margin = new Padding(8, 0, 0, 0) };
            repair.Click += async (sender, e) =>
            {
                try { await setup.RepairAsync(); }
                finally { SetupChanged(); }
            };
            uninstall.Click += async (sender, e) =>
            {
                try { await setup.UninstallAsync(); }
                finally { SetupChanged(); }
            };
            FlowLayoutPanel actions = new() { Name = "voiceInputMaintenanceActions", Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 16) };
            register.Margin = new Padding(0);
            actions.Controls.AddRange(new Control[] { register, repair, uninstall }); Add(actions);
            operation = Label(""); operation.Name = "voiceInputRegistrationOperation"; Add(operation);
            Add(Label("注册失败排查 · 安装日志"));
            string logPath = InputMethodRegistrationLogs.GetPath(true);
            Add(new TextBox { Name = "voiceInputSetupLogPath", Text = logPath, ReadOnly = true, Dock = DockStyle.Fill });
            var openLog = new Button { Name = "openVoiceInputSetupLogFolder", Text = "打开日志文件夹", Size = new Size(144, 34) };
            openLog.Click += (sender, e) => operation.Text = InputMethodRegistrationLogs.OpenFolder(logPath);
            Add(openLog);
            Add(Label("换包或移动程序通常无需重新注册；组件与当前包不一致时，请更新并修复。已注册仍可主动修复。卸载只移除语音组件，保留模型和个人数据"));
            voicePage.Controls.Add(content);
            void Wrap()
            {
                int width = Math.Max(1, voicePage.ClientSize.Width - voicePage.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                content.SuspendLayout();
                try { foreach (Control control in content.Controls) if (control is Label label) label.MaximumSize = new Size(width, 0); }
                finally { content.ResumeLayout(true); }
            }
            voicePage.SizeChanged += (sender, e) => Wrap();
            setup.Changed += SetupChanged;
            refresh = new VisibleSettingsStatusRefresh<VoiceInputRegistrationState>(this, () => voicePageActive,
                () => setup.State, state => { lastState = state; RenderRegistration(); }, e =>
                { status.Text = "读取组件状态失败：" + e.Message; register.Enabled = repair.Enabled = uninstall.Enabled = false; });
            voicePage.VisibleChanged += (sender, e) => refresh.UpdateVisibility();
            void LayoutNavigation()
            {
                int S(int value) => MainMenuAppearance.Scale(this, value);
                content.SuspendLayout();
                try
                {
                    navigation.TreeViewSize = S(150);
                    voicePage.Padding = new Padding(S(24));
                    register.Size = new Size(S(144), S(34));
                    repair.Size = uninstall.Size = register.Size;
                    openLog.Size = register.Size;
                    repair.Margin = uninstall.Margin = new Padding(S(8), 0, 0, S(8));
                    foreach (Control control in content.Controls) control.Margin = new Padding(0, 0, 0, S(16));
                }
                finally { content.ResumeLayout(true); }
            }
            ClientSizeChanged += (sender, e) => LayoutNavigation();
            Shown += (sender, e) =>
            {
                content.SuspendLayout();
                try { MainMenuAppearance.Apply(this); LayoutNavigation(); Wrap(); }
                finally { content.ResumeLayout(true); }
                refresh.UpdateVisibility();
            };
            // Attach the default page before scaling: detached TabPages are otherwise
            // omitted by WinForms, leaving their buttons at half size on a 200% display.
            navigation.NavigateToTabPage(voicePage);
            AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
            content.ResumeLayout(true);
        }

        private void SelectPage(TabPage page)
        {
            voicePageActive = page == voicePage;
            refresh?.UpdateVisibility();
            if (voicePageActive) { qingjian?.Hide(); return; }
            if (page != qingjianPage) return;
            if (qingjian != null) { qingjian.Show(); return; }
            // Keep Qingjian's settings, timers and network checks scoped to its own page.
            qingjian = createQingjian();
            qingjian.TopLevel = false; qingjian.FormBorderStyle = FormBorderStyle.None;
            qingjian.MinimumSize = Size.Empty; qingjian.Dock = DockStyle.Fill;
            qingjianPage.Controls.Add(qingjian); qingjian.Show(); MainMenuAppearance.Apply(qingjian);
        }

        internal void SelectInputPage(bool text) { var page=text?qingjianPage:voicePage;navigation.NavigateToTabPage(page);SelectPage(page); }

        private void SetupChanged()
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(SetupChanged)); } catch (InvalidOperationException) { }
                return;
            }
            RenderRegistration();
            refresh.Invalidate();
        }

        private void RenderRegistration()
        {
            if (register == null || IsDisposed || Disposing) return;
            var state = lastState;
            status.Text = state switch
            {
                VoiceInputRegistrationState.Registered => "组件状态：已注册，可以使用 Ctrl + Alt 语音打字。",
                VoiceInputRegistrationState.NeedsRepair => "组件状态：需要修复注册。检测到组件残留、文件缺失或指向开发目录的旧注册项。",
                VoiceInputRegistrationState.NeedsUpdate => "组件状态：已注册，但与当前包的组件不一致，请更新并修复。",
                VoiceInputRegistrationState.BundleIncomplete => "组件状态：随包组件缺失或无法读取，请重新获取完整使用包。",
                VoiceInputRegistrationState.Unsupported => "组件状态：当前系统架构不支持此组件。",
                VoiceInputRegistrationState.Unregistered => "组件状态：未注册。请先注册组件。",
                _ => "组件状态：正在检查…"
            };
            register.Text = setup.IsBusy ? "处理中…" : state is VoiceInputRegistrationState.Registered or VoiceInputRegistrationState.NeedsUpdate ? "已注册" :
                state == VoiceInputRegistrationState.NeedsRepair ? "需修复" : state == null ? "正在检查…" : "注册组件";
            register.Enabled = !setup.IsBusy && state == VoiceInputRegistrationState.Unregistered;
            repair.Text = state == VoiceInputRegistrationState.NeedsUpdate ? "更新并修复" : "修复注册";
            repair.Enabled = uninstall.Enabled = !setup.IsBusy && state is VoiceInputRegistrationState.Registered or
                VoiceInputRegistrationState.NeedsRepair or VoiceInputRegistrationState.NeedsUpdate;
            operation.Text = setup.Message;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                setup.Changed -= SetupChanged; refresh?.Dispose();
                qingjian?.Dispose(); voicePage.Dispose(); qingjianPage.Dispose(); pages.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
