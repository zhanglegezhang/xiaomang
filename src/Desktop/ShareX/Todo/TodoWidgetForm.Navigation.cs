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
            bool isExpanded = expanded;
            string module = Settings.ActiveModule;
            moduleSidebar.Visible = isExpanded;
            todoPage.Visible = isExpanded && module == "Todo";
            clipboardPage.Visible = isExpanded && module == "Clipboard";
            favoritesPage.Visible = isExpanded && module == "Favorites";
            toolsPage.Visible = isExpanded && module == "News";
            diaryPage.Visible = isExpanded && module == "Diary";
            // Keep all five stickers available, including the current page.
            todoModuleButton.Visible = isExpanded;
            clipboardModuleButton.Visible = isExpanded;
            favoritesModuleButton.Visible = isExpanded;
            newsModuleButton.Visible = isExpanded;
            diaryModuleButton.Visible = isExpanded;
            UpdateCompactText();
        }

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
