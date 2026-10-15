using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    public partial class HotkeySettingsForm
    {
        private void ConfigureMainMenuLayout()
        {
            MainMenuAppearance.Apply(this);
            MainMenuAppearance.SetPrimary(btnAdd);
            int S(int value) => MainMenuAppearance.Scale(this, value);
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
                btnHotkeysDisabled.Bounds = new Rectangle(S(16), S(54), S(220), S(30));
                int top = btnHotkeysDisabled.Visible ? S(94) : S(58);
                flpHotkeys.Bounds = new Rectangle(S(16), top, System.Math.Max(1, ClientSize.Width - S(32)), System.Math.Max(1, ClientSize.Height - top - S(16)));
            }
            SizeChanged += (s, e) => LayoutPage();
            btnHotkeysDisabled.VisibleChanged += (s, e) => LayoutPage();
            LayoutPage();
        }
    }
}
