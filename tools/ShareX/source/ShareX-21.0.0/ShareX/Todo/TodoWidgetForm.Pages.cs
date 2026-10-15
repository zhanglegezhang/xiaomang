using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        public void SetNewsUpdateStatus(string message, bool isError) => toolsPage.SetNewsUpdateStatus(message, isError);
        internal void PreparePersonalDataSnapshot() => favoritesPage.PreparePersonalDataSnapshot();
        private static Color BlendColor(Color from, Color to, float progress)
        {
            progress = Math.Clamp(progress, 0f, 1f);
            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * progress),
                (int)Math.Round(from.G + (to.G - from.G) * progress),
                (int)Math.Round(from.B + (to.B - from.B) * progress));
        }
        // Pages are created once and retain editing, scrolling and row caches between visits.
        // The host supplies window-level operations and the current settings after a restore.
        private abstract class WidgetPageComponent : Panel
        {
            protected TodoWidgetForm Owner { get; }
            internal string Module { get; }
            protected WidgetPageComponent(TodoWidgetForm owner, string module)
            {
                Owner = owner;
                Module = module;
                Font = owner.Font;
                Dock = DockStyle.Fill;
                BackColor = WidgetBackgroundColor;
                Visible = false;
                owner.ConfigurePageNumber(this, module switch
                {
                    "Clipboard" => "2剪切板", "Favorites" => "3收藏夹", "News" => "4工具", "Diary" => "5日记", _ => "1待办"
                });
            }
            internal abstract void ArrangePage();
            internal abstract void RefreshPage();
        }
        private WidgetPageComponent GetActivePage() => Settings.ActiveModule switch
        {
            "Clipboard" => clipboardPage, "Favorites" => favoritesPage,
            "News" => toolsPage, "Diary" => diaryPage, _ => todoPage
        };
    }
}
