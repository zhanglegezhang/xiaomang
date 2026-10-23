using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    public partial class MainForm
    {
        private Button exitXiaomangButton;
        private bool mainAppearanceReady;
        private bool navigationDpiRefreshQueued;
        private readonly HashSet<SettingsDesignSurface> dpiLayoutSurfaces = new();
        private readonly HashSet<Control> dpiLayoutControls = new();
        private IntPtr dpiRedrawHandle;
        [DllImport("user32.dll", EntryPoint = "RedrawWindow")]
        private static extern bool RedrawDpiWindow(IntPtr window, IntPtr updateRect, IntPtr updateRegion, uint flags);

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            if (!mainAppearanceReady || !IsHandleCreated)
            {
                base.OnDpiChanged(e);
                return;
            }
            // The framework resumes every child's layout while scaling fonts.
            // Defer our measured settings rows until native scaling is complete.
            QueueMainMenuDpiLayout();
            try
            {
                base.OnDpiChanged(e);
            }
            catch
            {
                navigationDpiRefreshQueued = false;
                ResumeSettingsDpiLayout();
                throw;
            }
        }

        private void QueueMainMenuDpiLayout()
        {
            SuspendSettingsDpiLayout(this);
            if (navigationDpiRefreshQueued) return;
            navigationDpiRefreshQueued = true;
            if (Visible && IsHandleCreated)
            {
                dpiRedrawHandle = Handle;
                NativeMethods.SendMessage(dpiRedrawHandle, (int)WindowsMessages.SETREDRAW, 0, 0);
            }
            BeginInvoke((Action)(() =>
            {
                navigationDpiRefreshQueued = false;
                try
                {
                    if (!IsDisposed && !Disposing) RefreshMainMenuDpi();
                }
                finally { ResumeSettingsDpiLayout(); }
            }));
        }

        private void SuspendSettingsDpiLayout(Control root)
        {
            if (dpiLayoutControls.Add(root)) root.SuspendLayout();
            if (root is SettingsDesignSurface surface && dpiLayoutSurfaces.Add(surface)) surface.BeginDpiLayout();
            foreach (Control child in root.Controls) SuspendSettingsDpiLayout(child);
        }

        private void ResumeSettingsDpiLayout()
        {
            try
            {
                var controls = new List<Control>(dpiLayoutControls);
                dpiLayoutControls.Clear();
                for (int i = controls.Count - 1; i >= 0; i--)
                    if (!controls[i].IsDisposed) controls[i].ResumeLayout(false);
                foreach (Control control in controls)
                    if (!control.IsDisposed && control.Visible) control.PerformLayout();
                foreach (SettingsDesignSurface surface in dpiLayoutSurfaces)
                    if (!surface.IsDisposed) surface.EndDpiLayout();
                dpiLayoutSurfaces.Clear();
            }
            finally
            {
                IntPtr redrawHandle = dpiRedrawHandle;
                dpiRedrawHandle = IntPtr.Zero;
                if (redrawHandle != IntPtr.Zero && !IsDisposed && IsHandleCreated && Handle == redrawHandle)
                {
                    bool visible = Visible;
                    NativeMethods.SendMessage(redrawHandle, (int)WindowsMessages.SETREDRAW, 1, 0);
                    // WM_SETREDRAW restores WS_VISIBLE; retain a hide requested in
                    // the meantime instead of showing a window the user closed.
                    if (!visible) NativeMethods.ShowWindow(redrawHandle, 0);
                    else RedrawDpiWindow(redrawHandle, IntPtr.Zero, IntPtr.Zero, 0x0485); // invalidate, erase, frame, all children; no synchronous paint
                }
            }
        }

        private bool IsActiveNavigation(ToolStripItem item)
        {
            if (item == tsbHotkeySettings) return inlineSettingsKind is InlineSettingsKind.Hotkeys or InlineSettingsKind.HotkeyTask;
            if (item == tsbApplicationSettings) return inlineSettingsKind == InlineSettingsKind.Application;
            if (item == tsbAbout) return inlineSettingsKind == InlineSettingsKind.About;
            if (item.Name == "tsbInputMethod") return inlineSettingsKind == InlineSettingsKind.InputMethod;
            if (item == tsbScreenshotsFolder) return screenshotFolderPageActive;
            if (item.Text == "便签文件夹") return noteLogPageActive;
            if (item.Text == "临时文件夹") return temporaryFilePageActive;
            return false;
        }

        private void ConfigureMainMenuAppearance()
        {
            SuspendLayout();
            pToolbars.SuspendLayout();
            tsMain.SuspendLayout();
            try
            {
                mainAppearanceReady = true;
                if (exitXiaomangButton == null)
                {
                    exitXiaomangButton = new Button { Name = "exitXiaomangButton", Text = "退出小蟒", Dock = DockStyle.Bottom, FlatStyle = FlatStyle.Flat, TabStop = true };
                    exitXiaomangButton.FlatAppearance.BorderSize = 0;
                    exitXiaomangButton.Click += (_, _) => RequestUserExit();
                    bool hover = false, pressed = false;
                    exitXiaomangButton.MouseEnter += (_, _) => { hover = true; exitXiaomangButton.Invalidate(); };
                    exitXiaomangButton.MouseLeave += (_, _) => { hover = pressed = false; exitXiaomangButton.Invalidate(); };
                    exitXiaomangButton.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) pressed = true; exitXiaomangButton.Invalidate(); };
                    exitXiaomangButton.MouseUp += (_, _) => { pressed = false; exitXiaomangButton.Invalidate(); };
                    exitXiaomangButton.Paint += (_, e) =>
                    {
                        var button = exitXiaomangButton;
                        e.Graphics.Clear(button.BackColor);
                        int S(int value) => MainMenuAppearance.Scale(button, value);
                        if (hover || pressed)
                        {
                            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            using var path = MainMenuAppearance.Round(new Rectangle(S(6), 1, button.Width - S(12), button.Height - 2), S(5));
                            using var fill = new SolidBrush(pressed ? MainMenuAppearance.Edge : MainMenuAppearance.Selected);
                            e.Graphics.FillPath(fill, path);
                        }
                        MainMenuRenderer.DrawGlyph(e.Graphics, "power", new Rectangle(S(19), (button.Height - S(18)) / 2, S(18), S(18)), button.ForeColor);
                        Rectangle text = new Rectangle(S(46), 0, Math.Max(1, button.Width - S(52)), button.Height);
                        TextRenderer.DrawText(e.Graphics, button.Text, button.Font, text, button.ForeColor,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        if (button.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(button.ClientRectangle, -3, -3));
                    };
                    pToolbars.Controls.Add(exitXiaomangButton);
                    exitXiaomangButton.SendToBack();
                }
                exitXiaomangButton.Height = MainMenuAppearance.Scale(this, 38);
                exitXiaomangButton.Font = MainMenuAppearance.FontFor(tsMain, MainMenuAppearance.MenuFont);
                exitXiaomangButton.BackColor = MainMenuAppearance.Sidebar;
                exitXiaomangButton.ForeColor = MainMenuAppearance.Text;
                BackColor = MainMenuAppearance.Background;
                pMain.BackColor = MainMenuAppearance.Background;
                pToolbars.AutoSize = false;
                pToolbars.Width = MainMenuAppearance.Scale(this, 168);
                pToolbars.BackColor = MainMenuAppearance.Sidebar;
                tsMain.AutoSize = false;
                tsMain.MinimumSize = Size.Empty;
                tsMain.Width = MainMenuAppearance.Scale(this, 168);
                tsMain.Padding = new Padding(MainMenuAppearance.Scale(this, 6), MainMenuAppearance.Scale(this, 8), MainMenuAppearance.Scale(this, 6), 0);
                tsMain.Font = MainMenuAppearance.FontFor(tsMain, MainMenuAppearance.MenuFont);
                tsMain.DrawCustomBorder = false;
                tsMain.BackColor = MainMenuAppearance.Sidebar;
                tsMain.ForeColor = MainMenuAppearance.Text;
                tsMain.ImageScalingSize = new Size(MainMenuAppearance.Scale(this, 18), MainMenuAppearance.Scale(this, 18));
                tsMain.Renderer = new MainMenuRenderer(IsActiveNavigation);
                foreach (ToolStripItem item in tsMain.Items)
                {
                    item.AutoSize = false;
                    item.Size = new Size(MainMenuAppearance.Scale(this, 156), MainMenuAppearance.Scale(this, 38));
                    item.Margin = new Padding(0, 0, 0, MainMenuAppearance.Scale(this, 3));
                    item.Padding = Padding.Empty;
                    item.ForeColor = MainMenuAppearance.Text;
                }
                RefreshMainPagesAppearance();
            }
            finally
            {
                tsMain.ResumeLayout(false);
                pToolbars.ResumeLayout(false);
                ResumeLayout(true);
            }
            tsMain.PerformLayout();
            tsMain.Invalidate();
        }

        // Windows already scaled child controls. Do not reapply themes or visit cached
        // hidden pages when only the foreground window's monitor DPI changed.
        private void RefreshMainMenuDpi()
        {
            SuspendLayout(); pToolbars.SuspendLayout(); tsMain.SuspendLayout();
            inlineSettingsViewport?.SuspendLayout();
            try
            {
                pToolbars.Width = MainMenuAppearance.Scale(this, 168);
                tsMain.Width = MainMenuAppearance.Scale(this, 168);
                tsMain.Padding = new Padding(MainMenuAppearance.Scale(this, 6), MainMenuAppearance.Scale(this, 8), MainMenuAppearance.Scale(this, 6), 0);
                Font font = MainMenuAppearance.FontFor(tsMain, MainMenuAppearance.MenuFont);
                if (!tsMain.Font.Equals(font)) tsMain.Font = font;
                tsMain.ImageScalingSize = new Size(MainMenuAppearance.Scale(this, 18), MainMenuAppearance.Scale(this, 18));
                foreach (ToolStripItem item in tsMain.Items)
                {
                    item.Size = new Size(MainMenuAppearance.Scale(this, 156), MainMenuAppearance.Scale(this, 38));
                    item.Margin = new Padding(0, 0, 0, MainMenuAppearance.Scale(this, 3));
                }
                LayoutInlineSettingsForm();
                if (inlineSettingsHost?.Visible == true) MainMenuAppearance.ApplyDpi(inlineSettingsForm);
                if (noteLogPageActive) MainMenuAppearance.ApplyDpi(noteLogViewer);
                if (screenshotFolderPageActive) MainMenuAppearance.ApplyDpi(screenshotFolderViewer);
                if (temporaryFilePageActive) MainMenuAppearance.ApplyDpi(temporaryFileViewer);
            }
            finally
            {
                inlineSettingsViewport?.ResumeLayout(false);
                tsMain.ResumeLayout(false); pToolbars.ResumeLayout(false); ResumeLayout(true);
            }
            tsMain.PerformLayout();
            Invalidate(true);
        }

        private void RefreshMainPagesAppearance()
        {
            if (!mainAppearanceReady) return;
            if (inlineSettingsHost != null) inlineSettingsHost.BackColor = MainMenuAppearance.Background;
            if (inlineSettingsViewport != null) inlineSettingsViewport.BackColor = MainMenuAppearance.Background;
            if (inlineSettingsForm != null) MainMenuAppearance.Apply(inlineSettingsForm);
            if (cachedApplicationSettingsForm != null && !cachedApplicationSettingsForm.IsDisposed && cachedApplicationSettingsForm != inlineSettingsForm)
                MainMenuAppearance.Apply(cachedApplicationSettingsForm);
            if (cachedInputMethodForm != null && !cachedInputMethodForm.IsDisposed && cachedInputMethodForm != inlineSettingsForm)
                MainMenuAppearance.Apply(cachedInputMethodForm);
            if (suspendedHotkeySettingsForm != null) MainMenuAppearance.Apply(suspendedHotkeySettingsForm);
            if (cachedHotkeySettingsForm != null && cachedHotkeySettingsForm != inlineSettingsForm && cachedHotkeySettingsForm != suspendedHotkeySettingsForm)
                MainMenuAppearance.Apply(cachedHotkeySettingsForm);
            if (noteLogViewer != null) MainMenuAppearance.Apply(noteLogViewer);
            if (screenshotFolderViewer != null) MainMenuAppearance.Apply(screenshotFolderViewer);
            if (temporaryFileViewer != null) MainMenuAppearance.Apply(temporaryFileViewer);
            tsMain.Invalidate();
        }
    }
}


