using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ShareX
{
    public partial class HotkeySelectionControl
    {
        private void ConfigureCardAppearance()
        {
            MainMenuAppearance.Apply(this);
            int S(int value) => MainMenuAppearance.Scale(this, value);
            Height = S(40);
            Margin = new System.Windows.Forms.Padding(0, 0, 0, S(4));
            void LayoutCard()
            {
                int inset = S(8), verticalInset = S(4), gap = S(8), height = Height - verticalInset * 2;
                int keyWidth = Math.Max(S(130), Math.Min(S(184), Width / 3));
                btnHotkey.Bounds = new Rectangle(Width - inset - keyWidth, verticalInset, keyWidth, height);
                btnTask.Bounds = new Rectangle(inset, verticalInset, Math.Max(S(80), btnHotkey.Left - gap - inset), height);
            }
            SizeChanged += (s, e) => LayoutCard();
            Paint += (s, e) =>
            {
                // The two buttons already define the row; reserve the outer outline for selection.
                if (!Selected || Width < 3 || Height < 3) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using GraphicsPath path = MainMenuAppearance.Round(new Rectangle(0, 0, Width - 1, Height - 1), S(6));
                using Pen pen = new(MainMenuAppearance.Orange);
                e.Graphics.DrawPath(pen, path);
            };
            LayoutCard();
        }
    }
}
