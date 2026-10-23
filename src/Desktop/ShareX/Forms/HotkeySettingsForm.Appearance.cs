using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    public partial class HotkeySettingsForm
    {
        private CheckBox fullscreenToggleSource;
        private SettingsDesignSwitch fullscreenToggle;
        private Label fullscreenToggleLabel;

        private void ConfigureMainMenuLayout()
        {
            MainMenuAppearance.Apply(this);
            MainMenuAppearance.SetPrimary(btnAdd);
            int S(int value) => MainMenuAppearance.Scale(this, value);
            fullscreenToggleSource = new CheckBox
            {
                Name = "disableHotkeysOnFullscreen", Text = "全屏时禁用快捷键",
                Checked = Program.Settings.DisableHotkeysOnFullscreen, Visible = false
            };
            fullscreenToggle = new SettingsDesignSwitch(fullscreenToggleSource);
            fullscreenToggleLabel = new Label
            {
                Text = fullscreenToggleSource.Text, AutoSize = true, Font = btnAdd.Font,
                ForeColor = MainMenuAppearance.Text
            };
            fullscreenToggleSource.CheckedChanged += (_, _) =>
            {
                Program.Settings.DisableHotkeysOnFullscreen = fullscreenToggleSource.Checked;
                manager.RefreshFullscreenPolicy();
                SettingManager.SaveApplicationConfigAsync();
            };
            Controls.AddRange(new Control[] { fullscreenToggleSource, fullscreenToggleLabel, fullscreenToggle });
            MinimumSize = new Size(S(560), S(280));
            void LayoutPage()
            {
                int x = S(16), y = S(16);
                foreach (Button button in new[] { btnAdd, btnRemove, btnDuplicate, btnMoveUp, btnMoveDown, btnReset })
                {
                    int width = button == btnMoveUp || button == btnMoveDown ? S(32) : S(76);
                    button.Bounds = new Rectangle(x, y, width, S(30));
                    x += width + S(6);
                }
                int switchWidth = S(40), groupWidth = fullscreenToggleLabel.PreferredWidth + S(10) + switchWidth;
                int groupLeft = ClientSize.Width - S(16) - groupWidth;
                int switchTop = groupLeft >= x + S(12) ? y : S(54);
                fullscreenToggleLabel.Location = new Point(groupLeft, switchTop + (S(30) - fullscreenToggleLabel.PreferredHeight) / 2);
                fullscreenToggle.Bounds = new Rectangle(ClientSize.Width - S(16) - switchWidth, switchTop + S(3), switchWidth, S(24));
                int contentTop = switchTop + S(42);
                btnHotkeysDisabled.Bounds = new Rectangle(S(16), contentTop, S(220), S(30));
                int top = btnHotkeysDisabled.Visible ? contentTop + S(40) : contentTop;
                flpHotkeys.Bounds = new Rectangle(S(16), top, System.Math.Max(1, ClientSize.Width - S(32)), System.Math.Max(1, ClientSize.Height - top - S(16)));
            }
            SizeChanged += (s, e) => LayoutPage();
            btnAdd.FontChanged += (_, _) =>
            {
                fullscreenToggleLabel.Font = btnAdd.Font;
                LayoutPage();
            };
            btnHotkeysDisabled.VisibleChanged += (s, e) => LayoutPage();
            LayoutPage();
        }

        private void RefreshFullscreenToggle()
        {
            if (fullscreenToggleSource != null)
                fullscreenToggleSource.Checked = Program.Settings.DisableHotkeysOnFullscreen;
        }
    }
}
