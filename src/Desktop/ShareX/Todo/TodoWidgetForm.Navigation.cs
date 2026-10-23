using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;
using ShareX.Properties;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private void SelectModule(string module, bool expand = true)
        {
            if (module != "Clipboard" && module != "Favorites" && module != "News" && module != "Diary") module = "Todo";
            if (!Settings.IsModuleVisible(module)) module = Settings.FirstVisibleModule();
            if (string.IsNullOrEmpty(module))
            {
                // Keep the robot and its menu available while every tab is hidden.
                if (Settings.ActiveModule == "Favorites") favoritesPage.CommitPendingFavorites();
                Settings.ActiveModule = string.Empty;
                SetExpanded(false);
                SettingManager.SaveApplicationConfigAsync();
                return;
            }
            if (module != "Todo") RestoreSearchCompletedVisibility();
            string previousModule = Settings.ActiveModule;
            if (Settings.ActiveModule == "Favorites" && module != "Favorites") favoritesPage.CommitPendingFavorites();
            Settings.ActiveModule = module;
            if (expanded && previousModule == "News" && module != "News") RestoreHeightBeforeNews();
            if (expand && !expanded) SetExpanded(true);

            SuspendLayout();
            try
            {
                UpdateModuleVisibility();
            }
            finally
            {
                ResumeLayout(true);
            }

            if (expanded && module == "News") EnsureNewsFits();

            if (expanded)
            {
                // Recalculate after docking has consumed the sidebar width. Measuring while
                // layout is suspended would use the previous page's stale client rectangle.
                LayoutContent();
                // The clipboard and favorites layout methods also size their variable-height
                // rows, so they already render the active list. Avoid rebuilding them twice.
                if (module == "Todo") todoPage.RenderItems();
            }

            RefreshActiveModuleSurface();
            SettingManager.SaveApplicationConfigAsync();
        }

        private Control GetActiveModulePanel()
        {
            return Settings.ActiveModule switch
            {
                "Clipboard" => clipboardPage,
                "Favorites" => favoritesPage,
                "News" => toolsPage,
                "Diary" => diaryPage,
                _ => todoPage
            };
        }

        private void UpdateModuleVisibility()
        {
            bool isExpanded = expanded && Settings.IsModuleVisible(Settings.ActiveModule);
            string module = Settings.ActiveModule;
            moduleSidebar.Visible = isExpanded;
            todoPage.Visible = isExpanded && module == "Todo";
            clipboardPage.Visible = isExpanded && module == "Clipboard";
            favoritesPage.Visible = isExpanded && module == "Favorites";
            toolsPage.Visible = isExpanded && module == "News";
            diaryPage.Visible = isExpanded && module == "Diary";
            int number = 0;
            foreach (var entry in new[] {
                ("Todo", "待办", todoModuleButton, (Panel)todoPage),
                ("Clipboard", "剪切板", clipboardModuleButton, (Panel)clipboardPage),
                ("Favorites", "收藏夹", favoritesModuleButton, (Panel)favoritesPage),
                ("News", "工具", newsModuleButton, (Panel)toolsPage),
                ("Diary", "日记", diaryModuleButton, (Panel)diaryPage) })
            {
                bool enabled = Settings.IsModuleVisible(entry.Item1);
                entry.Item3.Visible = isExpanded && enabled;
                if (!enabled) continue;
                string caption = (++number).ToString() + entry.Item2;
                entry.Item3.Text = string.Join("\n", caption.ToCharArray());
                entry.Item4.AccessibleName = caption;
            }
            UpdateCompactText();
        }

        internal void ApplyVisibleModules() => SelectModule(Settings.ActiveModule, false);

        private void RefreshActiveModuleSurface()
        {
            if (!expanded || IsDisposed || Disposing || !IsHandleCreated) return;
            if (Settings.ActiveModule == "Diary") diaryPage.RefreshPage();

            Control activePanel = GetActiveModulePanel();
            activePanel.Invalidate(true);
            moduleSidebar.Invalidate(true);
            // Paint the newly visible page without forcing every hidden child to repaint.
            activePanel.Update();
            moduleSidebar.Update();
        }

    }
}
