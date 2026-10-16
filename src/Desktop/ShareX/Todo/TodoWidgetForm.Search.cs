using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private ContextMenuStrip searchMenu;
        private System.Windows.Forms.Timer searchMenuOutsideClickTimer;
        private HookProc searchMenuMouseHookProc;
        private IntPtr searchMenuMouseHookHandle;
        private volatile Rectangle[] searchMenuScreenBounds = Array.Empty<Rectangle>();
        private volatile bool searchMenuOutsideClickPending;

        private void ShowSearchMenu()
        {
            if (IsDisposed || Disposing || closingWidget) return;
            if (searchMenu == null)
            {
                searchMenu = new ContextMenuStrip { AutoClose = true };
                searchMenu.Items.Add("全局搜索", null, (_, _) => UnifiedSearchForm.Open());
                searchMenu.Items.Add("主菜单", null, (_, _) => Program.MainForm.ShowMainMenu("widget-context"));
                searchMenuOutsideClickTimer = new System.Windows.Forms.Timer { Interval = 15 };
                searchMenuOutsideClickTimer.Tick += (_, _) =>
                {
                    if (!searchMenuOutsideClickPending) return;
                    searchMenuOutsideClickPending = false;
                    searchMenu.Close(ToolStripDropDownCloseReason.AppClicked);
                };
                searchMenu.Opened += (_, _) => StartSearchMenuOutsideClickWatch();
                searchMenu.Closed += (_, _) => StopSearchMenuOutsideClickWatch();
                searchMenu.Disposed += (_, _) => StopSearchMenuOutsideClickWatch();
                searchMenu.LocationChanged += (_, _) => RefreshSearchMenuScreenBounds();
                searchMenu.SizeChanged += (_, _) => RefreshSearchMenuScreenBounds();
            }
            // Closed runs before WinForms finishes processing a menu item click.
            // Keep the menu alive until its owning widget closes.
            CompactContextMenuStyle.Apply(searchMenu);
            searchMenu.Show(Cursor.Position);
        }

        private void RefreshSearchMenuScreenBounds()
        {
            searchMenuScreenBounds = searchMenu?.Visible == true
                ? new[] { searchMenu.RectangleToScreen(searchMenu.ClientRectangle) }
                : Array.Empty<Rectangle>();
        }

        private void StartSearchMenuOutsideClickWatch()
        {
            RefreshSearchMenuScreenBounds();
            searchMenuOutsideClickPending = false;
            if (searchMenuMouseHookHandle != IntPtr.Zero) return;
            searchMenuMouseHookProc ??= SearchMenuMouseHookProc;
            using Process process = Process.GetCurrentProcess();
            using ProcessModule module = process.MainModule;
            searchMenuMouseHookHandle = NativeMethods.SetWindowsHookEx(NativeConstants.WH_MOUSE_LL,
                searchMenuMouseHookProc, NativeMethods.GetModuleHandle(module.ModuleName), 0);
            if (searchMenuMouseHookHandle == IntPtr.Zero)
                DebugHelper.WriteLine("Unable to install widget menu outside-click hook.");
            else searchMenuOutsideClickTimer.Start();
        }

        private IntPtr SearchMenuMouseHookProc(int code, IntPtr message, IntPtr dataPointer)
        {
            int buttonMessage = message.ToInt32();
            if (code >= 0 && (buttonMessage == (int)WindowsMessages.LBUTTONDOWN ||
                buttonMessage == (int)WindowsMessages.RBUTTONDOWN ||
                buttonMessage == (int)WindowsMessages.MBUTTONDOWN ||
                buttonMessage == (int)WindowsMessages.XBUTTONDOWN))
            {
                // The native callback only marks an outside click; the UI timer closes the menu.
                // Always pass the click on so the window under the pointer handles it normally.
                POINT point = Marshal.PtrToStructure<POINT>(dataPointer);
                if (!searchMenuScreenBounds.Any(bounds => bounds.Contains(point.X, point.Y)))
                    searchMenuOutsideClickPending = true;
            }
            return NativeMethods.CallNextHookEx(searchMenuMouseHookHandle, code, message, dataPointer);
        }

        private void StopSearchMenuOutsideClickWatch()
        {
            searchMenuOutsideClickTimer?.Stop();
            searchMenuOutsideClickPending = false;
            searchMenuScreenBounds = Array.Empty<Rectangle>();
            if (searchMenuMouseHookHandle == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(searchMenuMouseHookHandle);
            searchMenuMouseHookHandle = IntPtr.Zero;
        }

        internal void LocateSearchResult(SearchHit hit)
        {
            bool previousOverlay = showingWidgetOverlay;
            showingWidgetOverlay = true;
            try
            {
            FinishGenieAnimation();
            collapseTimer.Stop();
            if (hit.Source == "待办")
            {
                if (!Settings.Items.Any(x => x.Id == hit.Id)) throw new InvalidOperationException("该待办已删除。");
                todoPage.showCompleted = true; SelectModule("Todo"); OpenPanel(); LayoutContent(); todoPage.RenderItems();
                int i = Array.FindIndex(todoPage.todoViewItems, x => x.Id == hit.Id);
                if (i >= 0 && i < todoPage.todoRowOffsets.Length) { todoPage.todoList.AutoScrollPosition = new Point(0, todoPage.todoRowOffsets[i]); todoPage.UpdateTodoViewport(); }
            }
            else if (hit.Source == "手动收藏")
            {
                if (!Settings.FavoriteItems.Any(x => x.Id == hit.Id)) throw new InvalidOperationException("该收藏已删除。");
                favoritesPage.favoriteSearchBox.Clear(); SelectModule("Favorites"); OpenPanel(); LayoutContent(); favoritesPage.RenderFavorites();
                int i = Array.FindIndex(favoritesPage.favoriteViewItems, x => x.Id == hit.Id);
                if (i >= 0 && i < favoritesPage.favoriteRowOffsets.Length) { favoritesPage.favoritesList.AutoScrollPosition = new Point(0, favoritesPage.favoriteRowOffsets[i]); favoritesPage.UpdateFavoritesViewport(); }
            }
            else
            {
                if (!Settings.ClipboardItems.Any(x => x.Id == hit.Id)) throw new InvalidOperationException("该剪切板记录已删除。");
                SelectModule("Clipboard"); OpenPanel(); LayoutContent(); clipboardPage.RenderClipboard();
                int i = Array.FindIndex(clipboardPage.clipboardViewItems, x => x.Id == hit.Id);
                if (i >= 0 && i < clipboardPage.clipboardRowOffsets.Length) { clipboardPage.clipboardList.AutoScrollPosition = new Point(0, clipboardPage.clipboardRowOffsets[i]); clipboardPage.UpdateClipboardViewport(); }
            }
            Activate();
            }
            finally { showingWidgetOverlay = previousOverlay; }
        }
    }
}
