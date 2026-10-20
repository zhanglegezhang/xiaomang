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
        public void OpenPanel()
        {
            if (animatingGenie) FinishGenieAnimation();
            Show();
            SetExpanded(true);
            Activate();
            if (Settings.ActiveModule == "Favorites") favoritesPage.favoriteSearchBox.Focus();
            else if (Settings.ActiveModule == "Todo") todoPage.todoTextBox.Focus();
        }

        private void OpenPanelGenie()
        {
            if (animatingGenie || expanded || !Visible || IsDisposed)
            {
                OpenPanel();
                return;
            }
            try
            {
                genieStartBounds = new Rectangle(Right - D(CollapsedWidth), Top,
                    D(CollapsedWidth), D(CollapsedHeight));
                using Bitmap compactImage = new Bitmap(genieStartBounds.Width,
                    genieStartBounds.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(compactImage))
                {
                    graphics.Clear(Color.Transparent);
                    compactLabel.DrawContent(graphics, compactImage.Size);
                }

                genieAnimationForm = new GenieAnimationForm(this, (Bitmap)compactImage.Clone());
                genieAnimationForm.UpdateFrame(genieStartBounds, 0f);
                genieAnimationForm.Show();
                animatingGenie = true;
                collapseTimer.Stop();
                Opacity = 0;
                SetExpanded(true);
                genieTargetBounds = Bounds;
                Control panel = GetActiveModulePanel();
                genieContentSnapshot = new Bitmap(Math.Max(1, panel.Width),
                    Math.Max(1, panel.Height), PixelFormat.Format32bppArgb);
                panel.DrawToBitmap(genieContentSnapshot, new Rectangle(Point.Empty, genieContentSnapshot.Size));
                Button[] buttons = new[] { todoModuleButton, clipboardModuleButton,
                    favoritesModuleButton, newsModuleButton, diaryModuleButton }.Where(button => button.Visible).ToArray();
                genieAnimationForm.SetContent(genieContentSnapshot, buttons, genieTargetBounds);
                genieAnimationForm.UpdateFrame(genieStartBounds, 0f);
                genieAnimationClock = Stopwatch.StartNew();
                genieAnimationTimer ??= new Timer { Interval = 16 };
                genieAnimationTimer.Tick -= GenieAnimationTick;
                genieAnimationTimer.Tick += GenieAnimationTick;
                genieAnimationTimer.Start();
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "贴边浮标神奇效果失败，已直接打开窗口。");
                FinishGenieAnimation();
                if (!expanded && !IsDisposed) OpenPanel();
            }
        }

        private void GenieAnimationTick(object sender, EventArgs e)
        {
            if (!animatingGenie || genieAnimationForm == null || genieAnimationForm.IsDisposed)
            {
                FinishGenieAnimation();
                return;
            }
            try
            {
                float progress = Math.Clamp((float)genieAnimationClock.Elapsed.TotalMilliseconds / 320f, 0f, 1f);
                if (progress >= 1f)
                {
                    FinishGenieAnimation();
                    return;
                }
                float eased = progress * progress * (3f - 2f * progress);
                int width = Math.Max(1, (int)Math.Round(genieStartBounds.Width +
                    (genieTargetBounds.Width - genieStartBounds.Width) * eased));
                int height = Math.Max(1, (int)Math.Round(genieStartBounds.Height +
                    (genieTargetBounds.Height - genieStartBounds.Height) * eased));
                double centerY = genieStartBounds.Top + genieStartBounds.Height / 2d +
                    (genieTargetBounds.Top + genieTargetBounds.Height / 2d -
                    genieStartBounds.Top - genieStartBounds.Height / 2d) * eased;
                Rectangle bounds = new Rectangle(genieTargetBounds.Right - width,
                    (int)Math.Round(centerY - height / 2d), width, height);
                genieAnimationForm.UpdateFrame(bounds, progress);
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "贴边浮标神奇效果中断，已直接打开窗口。");
                FinishGenieAnimation();
            }
        }

        private void FinishGenieAnimation()
        {
            genieAnimationTimer?.Stop();
            genieAnimationClock?.Stop();
            animatingGenie = false;
            Opacity = 1;
            GenieAnimationForm animation = genieAnimationForm;
            genieAnimationForm = null;
            animation?.Hide();
            animation?.Dispose();
            genieContentSnapshot?.Dispose();
            genieContentSnapshot = null;
            if (IsDisposed || Disposing || closingWidget) return;
            SyncExpandedFrameCorners();
            SyncStickerOverlay();
            SyncCompactOverlay();
            if (expanded)
            {
                Activate();
                if (Settings.ActiveModule == "Favorites") favoritesPage.favoriteSearchBox.Focus();
                else if (Settings.ActiveModule == "Todo") todoPage.todoTextBox.Focus();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ReleaseOcrPanelHold();
            StopSearchMenuOutsideClickWatch();
            searchMenu?.Dispose();
            searchMenu = null;
            searchMenuOutsideClickTimer?.Dispose();
            searchMenuOutsideClickTimer = null;
            noteLogStore.WriteFailed -= NoteLogWriteFailed;
            genieAnimationTimer?.Dispose();
            genieAnimationClock?.Stop();
            genieAnimationForm?.Dispose();
            genieContentSnapshot?.Dispose();
            stickerOverlay?.Dispose();
            compactOverlay?.Dispose();
            if (frameCornerOverlays != null)
                foreach (FrameCornerOverlayForm overlay in frameCornerOverlays) overlay.Dispose();
            compactLabel.Image = null;
            StopVoicePlayback();
            if (voiceRecorder != null)
            {
                _ = VoiceNoteRecordingService.StopAsync(voiceRecorder, voiceRecorder.Elapsed);
                voiceRecorder = null;
            }
            reminderTimer.Dispose();
            collapseTimer.Dispose();
            noteSaveTimer.Dispose();
            compactLongPressTimer.Dispose();
            voiceRecordingTimer.Dispose();
            voicePlaybackTimer.Dispose();
            HideCompactHint();
            compactHintTimer.Dispose();
            compactHint?.Dispose();
            reminderService.Dispose();
            titleLogoImage.Dispose();
            compactLogoImage.Dispose();
            sharedDeleteIcon.Dispose();
            recordTip.Dispose();
            sharedSpeakerIcon.Dispose();
            sharedStopIcon.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closingWidget = true;
            if (compactRecording || stoppingVoiceRecording)
            {
                e.Cancel = true;
                if (!recordingClosePending) _ = FinishRecordingAndCloseAsync();
                base.OnFormClosing(e);
                e.Cancel = true;
                return;
            }
            favoritesPage.CommitPendingFavorites();
            noteSaveTimer.Stop();
            RememberExpandedBounds();
            // The main form may already be disposing, so the queued save worker can
            // no longer dispatch a write. Flush the last bounds and pending notes here.
            if (!SettingManager.SaveApplicationConfig()) SettingManager.SaveApplicationConfigAsync();
            base.OnFormClosing(e);
            if (e.Cancel) closingWidget = false;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetExpanded(expanded);
            // Initial native window placement can reapply Windows' minimum track width.
            // Restore the compact bounds once the first Show operation has completed.
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed && !expanded) SetExpanded(false);
            }));
            CheckReminders();
            if (!clipboardMetricsWarmed)
            {
                clipboardMetricsWarmed = true;
                BeginInvoke(new Action(clipboardPage.WarmClipboardMetrics));
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            favoritesPage.CommitPendingFavorites();
            base.OnDeactivate(e);
            if (!ocrPanelHeldOpen && expanded && !animatingGenie && !closingWidget && !showingWidgetOverlay && !todoPage.choosingSchedule && !ContainsWidgetPoint(Cursor.Position))
            {
                SetExpanded(false);
            }
        }

        private void Widget_MouseEnter(object sender, EventArgs e)
        {
            collapseTimer.Stop();
        }

        private void Widget_MouseLeave(object sender, EventArgs e)
        {
            if (expanded && !animatingGenie && !resizing && !titleDragging && !compactDragging && !HasEditorFocus()) collapseTimer.Start();
        }

        private bool HasEditorFocus()
        {
            if (todoPage.todoTextBox.ContainsFocus || favoritesPage.favoriteSearchBox.ContainsFocus ||
                clipboardPage.clipboardPreviewLinesInput.ContainsFocus || todoPage.schedulePanel.ContainsFocus) return true;
            return ContainsFocusedTextEditor(todoPage.todoList) || ContainsFocusedTextEditor(clipboardPage.clipboardList) ||
                ContainsFocusedTextEditor(favoritesPage.favoritesList);
        }

        private static bool ContainsFocusedTextEditor(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is TextBoxBase editor && editor.ContainsFocus) return true;
                if (child.HasChildren && ContainsFocusedTextEditor(child)) return true;
            }

            return false;
        }

        private void ScheduleCollapseIfPointerOutside()
        {
            if (expanded && !ContainsWidgetPoint(Cursor.Position))
            {
                collapseTimer.Stop();
                collapseTimer.Start();
            }
        }

        private void CompactSurface_MouseEnter(Control surface)
        {
            if (expanded || compactDragging || compactRecording || !Visible) return;
            HideCompactHint();
            compactHintSurface = surface;
            compactHintTimer.Interval = 500;
            compactHintTimer.Start();
        }

        private void CompactSurface_MouseLeave(Control surface)
        {
            if (compactHintSurface == surface) HideCompactHint();
        }

        private void CompactHintTimer_Tick(object sender, EventArgs e)
        {
            compactHintTimer.Stop();
            Control surface = compactHintSurface;
            if (compactHint?.Visible == true || expanded || compactDragging || compactRecording || !Visible ||
                surface == null || surface.IsDisposed || !surface.Visible ||
                !GetCompactSurfaceBounds(surface).Contains(Cursor.Position))
            {
                HideCompactHint();
                return;
            }
            UpdateCompactHintWindow();
            compactHintTimer.Interval = 6000;
            compactHintTimer.Start();
        }

        private void UpdateCompactHintWindow()
        {
            Control surface = compactHintSurface;
            if (surface == null || surface.IsDisposed || expanded || compactDragging || compactRecording || !Visible) return;
            compactHint ??= new CompactHintForm { Owner = this };
            Rectangle anchor = GetCompactSurfaceBounds(surface);
            Size hintSize = compactHint.UpdateText(compactHintText, surface.DeviceDpi);
            Rectangle area = Screen.FromRectangle(anchor).WorkingArea;
            compactHint.Bounds = CalculateCompactHintBounds(anchor, hintSize, area, D(6));
            if (!compactHint.Visible) compactHint.Show();
        }

        private static Rectangle GetCompactSurfaceBounds(Control surface) => surface is CompactOverlayForm overlay
            ? overlay.VisualBounds : surface.RectangleToScreen(surface.ClientRectangle);

        private static Rectangle CalculateCompactHintBounds(Rectangle anchor, Size hintSize, Rectangle area, int gap)
        {
            int width = Math.Min(hintSize.Width, area.Width);
            int height = Math.Min(hintSize.Height, area.Height);
            int x = anchor.Left + (anchor.Width - width) / 2;
            int y = anchor.Top - gap - height;
            if (y < area.Top)
            {
                x = anchor.Left - gap - width;
                y = anchor.Top + (anchor.Height - height) / 2;
            }
            return new Rectangle(Math.Clamp(x, area.Left, area.Right - width),
                Math.Clamp(y, area.Top, area.Bottom - height), width, height);
        }

        private void HideCompactHint()
        {
            compactHintTimer?.Stop();
            compactHintSurface = null;
            compactHint?.Hide();
        }

        private void TitleLabel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            titleDragging = true;
            collapseTimer.Stop();
            titleDragControl = sender as Control ?? todoPage.titleLabel;
            titleDragStartMouseY = titleDragControl.PointToScreen(e.Location).Y;
            titleDragStartWindowY = Location.Y;
            titleDragControl.Capture = true;
        }

        private void TitleLabel_MouseMove(object sender, MouseEventArgs e)
        {
            if (!titleDragging) return;

            Rectangle workingArea = dockScreen.WorkingArea;
            Control handle = titleDragControl ?? todoPage.titleLabel;
            int y = titleDragStartWindowY + handle.PointToScreen(e.Location).Y - titleDragStartMouseY;
            y = Math.Max(workingArea.Top + 4, Math.Min(workingArea.Bottom - Height - 4, y));
            Location = new Point(workingArea.Right - Width, y);
        }

        private void TitleLabel_MouseUp(object sender, MouseEventArgs e)
        {
            if (!titleDragging) return;

            titleDragging = false;
            if (titleDragControl != null) titleDragControl.Capture = false;
            titleDragControl = null;
            Settings.WidgetY = Location.Y;
            SettingManager.SaveApplicationConfigAsync();
        }

        private void ConfigureTitleDrag(Control title)
        {
            title.Cursor = Cursors.SizeNS;
            title.MouseDown += TitleLabel_MouseDown;
            title.MouseMove += TitleLabel_MouseMove;
            title.MouseUp += TitleLabel_MouseUp;
        }

        private PictureBox CreateTitleLogo(Panel panel)
        {
            PictureBox logo = new RecordingTitleLogo
            {
                Name = "TitleLogo",
                Image = titleLogoImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                AccessibleName = "小蟒同学标志",
                TabStop = false
            };
            ConfigureTitleDrag(logo);
            logo.MouseDoubleClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                titleDragging = false;logo.Capture = false;titleDragControl = null;
                Program.MainForm.ShowMainMenu("widget-logo");
            };
            panel.Controls.Add(logo);
            return logo;
        }

        private void LayoutPageTitle(PictureBox logo, Label title, int titleWidth)
        {
            logo.Bounds = new Rectangle(D(8), D(12), D(20), D(20));
            int textWidth = TextRenderer.MeasureText(title.Text, title.Font).Width + D(8);
            title.Bounds = new Rectangle(logo.Right + D(6), D(8), Math.Min(titleWidth, textWidth), D(28));
            // Keep the layout anchor for the controls below; paint the heading beside the logo.
            title.Visible = false;
        }

        private void ConfigurePageNumber(Panel panel, string number)
        {
            panel.AccessibleName = number;
            panel.MouseDown += (sender, e) =>
            {
                int top = D(8) + panel.AutoScrollPosition.Y;
                if (e.Y >= top && e.Y < top + D(28)) TitleLabel_MouseDown(sender, e);
            };
            panel.MouseMove += TitleLabel_MouseMove;
            panel.MouseUp += TitleLabel_MouseUp;
            // Paint on the panel itself: no extra hit target, tab stop or layout row.
            panel.Paint += (sender, e) =>
            {
                float left = D(34); // Logo: 8 + 20; text gap: 6 logical pixels.
                float right = panel.ClientSize.Width - D(8);
                int scrollY = panel.AutoScrollPosition.Y;
                foreach (Control control in panel.Controls)
                {
                    bool reservesSaveStatus = ReferenceEquals(control, favoritesPage.favoriteSaveStatusLabel);
                    if (((control.Visible && control is Button) || reservesSaveStatus) &&
                        control.Top < D(36) + scrollY && control.Bottom > D(8) + scrollY && control.Left >= left)
                        right = Math.Min(right, control.Left - D(6));
                }
                RectangleF bounds = new RectangleF(left, D(8) + scrollY, Math.Max(1, right - left), D(28));
                using Font numberFont = new Font("Microsoft YaHei UI", D(20), FontStyle.Bold, GraphicsUnit.Pixel);
                using Brush brush = new SolidBrush(Color.FromArgb(38, Color.White));
                using StringFormat format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float measuredWidth = e.Graphics.MeasureString(number, numberFont, PointF.Empty, format).Width;
                using Font fittedFont = new Font(numberFont.FontFamily, numberFont.Size * Math.Min(1f, bounds.Width / measuredWidth), FontStyle.Bold, GraphicsUnit.Pixel);
                e.Graphics.DrawString(number, fittedFont, brush, bounds, format);
            };
            panel.SizeChanged += (sender, e) => panel.Invalidate();
        }

        private void TodoWidgetForm_ResizeEnd(object sender, EventArgs e)
        {
            if (!expanded) return;

            // Horizontal resizing on the Other page must not turn its temporary
            // content-fit height into the user's preferred height.
            if (heightBeforeNewsAutoExpand.HasValue && Height != resizeStartBounds.Height)
                heightBeforeNewsAutoExpand = null;
            RememberExpandedBounds();
            MoveToScreenEdge();
            LayoutContent();
            if (Settings.ActiveModule == "Todo") todoPage.RenderItems();
            SettingManager.SaveApplicationConfigAsync();
        }

        private void TodoWidgetForm_SizeChanged(object sender, EventArgs e)
        {
            if (changingExpansion) return;
            if (ready && expanded)
            {
                LayoutContent();
                if (!resizing && Settings.ActiveModule == "Todo") todoPage.RenderItems();
            }
            else if (ready) SyncCompactOverlay();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (changingExpansion) return;
            HideCompactHint();
            SyncStickerOverlay();
            SyncCompactOverlay();
            SyncExpandedFrameCorners();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (changingExpansion) return;
            if (!Visible) HideCompactHint();
            SyncStickerOverlay();
            SyncCompactOverlay();
            SyncExpandedFrameCorners();
        }

        private int D(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        private void CreateResizeGrips()
        {
            // Dedicated child controls receive mouse input; a parent's WM_NCHITTEST cannot
            // resize through a dock-filled child panel.
            foreach (string edge in new[] { "L", "R", "T", "B", "LT", "RT", "LB", "RB" })
            {
                Panel grip = new Panel
                {
                    Tag = edge,
                    AccessibleName = "调整待办大小 " + edge,
                    BackColor = BackColor,
                    Cursor = edge.Length == 2 ? (edge == "LT" || edge == "RB" ? Cursors.SizeNWSE : Cursors.SizeNESW)
                        : (edge == "L" || edge == "R" ? Cursors.SizeWE : Cursors.SizeNS)
                };
                grip.MouseDown += (sender, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    BeginGripResize(grip, grip.PointToScreen(e.Location));
                };
                grip.MouseMove += (sender, e) =>
                {
                    if (!resizing || !grip.Capture) return;
                    Point mousePosition = grip.PointToScreen(e.Location);
                    Point delta = new Point(mousePosition.X - resizeStartMouse.X, mousePosition.Y - resizeStartMouse.Y);
                    ResizeFromDelta(edge, delta);
                };
                grip.MouseUp += (sender, e) => { if (e.Button == MouseButtons.Left) grip.Capture = false; };
                grip.MouseCaptureChanged += (sender, e) =>
                {
                    if (!resizing || grip.Capture) return;
                    resizing = false;
                    TodoWidgetForm_ResizeEnd(this, EventArgs.Empty);
                };
                grip.MouseEnter += (sender, e) => collapseTimer.Stop();
                grip.MouseLeave += Widget_MouseLeave;
                Controls.Add(grip);
                resizeGrips.Add(grip);
            }
            bottomResizeHitFilter = new BottomResizeHitFilter(this);
            Disposed += (_, _) => bottomResizeHitFilter.SetEnabled(false);
        }

        private void ResizeFromDelta(string edge, Point delta)
        {
            Rectangle area = dockScreen.WorkingArea;
            int left = resizeStartBounds.Left, right = resizeStartBounds.Right;
            int top = resizeStartBounds.Top, bottom = resizeStartBounds.Bottom;
            if (edge.Contains("L")) left = Math.Clamp(left + delta.X, area.Left, right - MinimumSize.Width);
            if (edge.Contains("R")) right = Math.Clamp(right + delta.X, left + MinimumSize.Width, area.Right);
            if (edge.Contains("T")) top = Math.Clamp(top + delta.Y, area.Top, bottom - MinimumSize.Height);
            if (edge.Contains("B")) bottom = Math.Clamp(bottom + delta.Y, top + MinimumSize.Height, area.Bottom);
            Bounds = Rectangle.FromLTRB(left, top, right, bottom);
        }

        private void LayoutResizeGrips()
        {
            int b = D(2), corner = D(5), w = ClientSize.Width, h = ClientSize.Height;
            int left = expanded ? moduleSidebar.Right : 0;
            foreach (Panel grip in resizeGrips)
            {
                grip.Bounds = (string)grip.Tag switch
                {
                    "L" => new Rectangle(left, corner, b, h - corner * 2),
                    "R" => new Rectangle(w - b, corner, b, h - corner * 2),
                    "T" => new Rectangle(left + corner, 0, w - left - corner * 2, b),
                    "B" => new Rectangle(left + corner, h - b, w - left - corner * 2, b),
                    "LT" => new Rectangle(left, 0, corner, corner),
                    "RT" => new Rectangle(w - corner, 0, corner, corner),
                    "LB" => new Rectangle(left, h - corner, corner, corner),
                    _ => new Rectangle(w - corner, h - corner, corner, corner)
                };
                grip.BringToFront();
            }
        }

        private Size GetSavedExpandedSize()
        {
            int width = Math.Min(dockScreen.WorkingArea.Width, Math.Max(MinimumSize.Width, Settings.WidgetWidth > 0 ? Settings.WidgetWidth : D(ExpandedWidth)));
            int height = Math.Min(dockScreen.WorkingArea.Height, Math.Max(MinimumSize.Height, Settings.WidgetHeight > 0 ? Settings.WidgetHeight : D(ExpandedHeight)));
            return new Size(width, height);
        }

        private bool RememberExpandedBounds()
        {
            if (!ready || !expanded) return false;
            int height = heightBeforeNewsAutoExpand ?? Height;
            bool changed = Settings.WidgetWidth != Width || Settings.WidgetHeight != height || Settings.WidgetY != Top;
            Settings.WidgetWidth = Width;
            Settings.WidgetHeight = height;
            Settings.WidgetY = Top;
            return changed;
        }

        private int GetNewsRequiredHeight()
        {
            int titleHeight = D(28);
            int rows = toolsPage.ToolRowCount;
            return D(8) + titleHeight + D(8) +
                rows * D(31) + (rows - 1) * D(16) + D(8) + D(28) + D(8);
        }

        private void EnsureNewsFits()
        {
            int requiredHeight = GetNewsRequiredHeight();
            int availableHeight = Math.Max(D(310), dockScreen.WorkingArea.Height);
            int minimumHeight = Math.Min(requiredHeight, availableHeight);
            if (Height < minimumHeight)
            {
                heightBeforeNewsAutoExpand ??= Height;
                MinimumSize = new Size(D(280), minimumHeight);
                Height = minimumHeight;
                MoveToScreenEdge();
            }
            else
            {
                MinimumSize = new Size(D(280), minimumHeight);
            }
            toolsPage.AutoScroll = minimumHeight < requiredHeight;
            toolsPage.AutoScrollMinSize = new Size(0, requiredHeight);
        }

        private void RestoreHeightBeforeNews()
        {
            MinimumSize = new Size(D(280), D(310));
            toolsPage.AutoScroll = false;
            if (heightBeforeNewsAutoExpand.HasValue)
            {
                Height = heightBeforeNewsAutoExpand.Value;
                heightBeforeNewsAutoExpand = null;
                MoveToScreenEdge();
            }
        }

        private void MoveToScreenEdge()
        {
            dockScreen = Screen.AllScreens.FirstOrDefault(x => x.DeviceName == dockScreen.DeviceName) ?? Screen.PrimaryScreen;
            Rectangle area = dockScreen.WorkingArea;
            int defaultY = area.Top + (area.Height - Height) / 2;
            int requestedY = Settings.WidgetY >= area.Top && Settings.WidgetY <= area.Bottom - Height ? Settings.WidgetY : defaultY;
            int y = Math.Max(area.Top + 4, Math.Min(area.Bottom - Height - 4, requestedY));
            Location = new Point(area.Right - Width, y);
        }

        private void LayoutContent()
        {
            if (!ready || !expanded) return;
            WidgetPageComponent activePage = GetActivePage();
            Point scrollPosition = activePage.AutoScrollPosition;
            LayoutModuleSidebar();
            if (!changingExpansion)
            {
                UpdateExpandedWindowRegion();
                SyncExpandedFrameCorners();
                SyncStickerOverlay();
            }
            activePage.ArrangePage();
            LayoutResizeGrips();
            // Overlay refresh can restore focus and scroll a short page back to its first button.
            if (activePage.AutoScroll)
                activePage.AutoScrollPosition = new Point(-scrollPosition.X, -scrollPosition.Y);
        }

        private void LayoutModuleSidebar()
        {
            int gap = D(6), y = 0, width = moduleSidebar.ClientSize.Width;
            Button[] visibleButtons = new[] { todoModuleButton, clipboardModuleButton, favoritesModuleButton, newsModuleButton, diaryModuleButton }.Where(x => x.Visible).ToArray();
            int height = Math.Min(D(72), Math.Max(D(55), (moduleSidebar.ClientSize.Height - gap * Math.Max(0, visibleButtons.Length - 1)) / Math.Max(1, visibleButtons.Length)));
            foreach (Button button in visibleButtons)
            {
                bool shapeChanged = button.Width != width || button.Height != height || button.Region == null;
                button.Bounds = new Rectangle(0, y, width, height);
                if (shapeChanged) ApplyStickerShape(button);
                y = button.Bottom + gap;
            }
        }

        private static void ApplyStickerShape(Button button)
        {
            int slant = Math.Max(3, button.Height / 5);
            float radius = Math.Max(3f, 5f * button.DeviceDpi / 96f);
            GraphicsPath path = CreateRoundedTrapezoidPath(button.Width, button.Height, slant, radius);
            Region oldRegion = button.Region;
            button.Region = new Region(path);
            oldRegion?.Dispose();
            path.Dispose();
        }

        private Rectangle expandedRegionBounds;
        private float expandedRegionRadius;
        private Region expandedWindowRegion;
        private void UpdateExpandedWindowRegion()
        {
            // The solid content stays in this window; the tabs use per-pixel alpha in their
            // own layered window so their exposed rounded edges are not clipped by Region.
            float radius = Math.Max(3f, D(5));
            Rectangle bounds = new Rectangle(moduleSidebar.Right, 0, ClientSize.Width - moduleSidebar.Right, ClientSize.Height);
            float effectiveRadius = expandedFrameFailed ? radius : radius + D(2);
            if (ReferenceEquals(Region, expandedWindowRegion) && Region != null && expandedRegionBounds == bounds && expandedRegionRadius == effectiveRadius) return;
            using GraphicsPath contentPath = CreateRoundedRectanglePath(
                bounds, effectiveRadius, true);
            Region newRegion = new Region(contentPath);
            Region oldRegion = Region;
            Region = newRegion;
            expandedWindowRegion = newRegion;
            expandedRegionBounds = bounds;
            expandedRegionRadius = effectiveRadius;
            oldRegion?.Dispose();
        }

        private bool ContainsWidgetPoint(Point point) => Bounds.Contains(point) ||
            (stickerOverlay != null && !stickerOverlay.IsDisposed && stickerOverlay.Visible &&
             stickerOverlay.ContainsStickerPoint(point));

        private void SyncStickerOverlay()
        {
            if (!ready || IsDisposed || Disposing) return;
            if (animatingGenie || !expanded || !Visible || !IsHandleCreated || !moduleSidebar.Visible)
            {
                stickerOverlay?.Hide();
                return;
            }

            stickerOverlay ??= new StickerOverlayForm(this);
            Button[] buttons = new[] { todoModuleButton, clipboardModuleButton, favoritesModuleButton, newsModuleButton, diaryModuleButton }
                .Where(button => button.Visible).ToArray();
            Point location = PointToScreen(new Point(moduleSidebar.Left, moduleSidebar.Top));
            stickerOverlay.UpdateStickers(buttons, location, moduleSidebar.ClientSize.Width);
            if (!stickerOverlay.Visible)
            {
                showingWidgetOverlay = true;
                try { stickerOverlay.Show(this); }
                finally { showingWidgetOverlay = false; }
            }
        }

        private void SyncExpandedFrameCorners()
        {
            if (!ready || IsDisposed || Disposing) return;
            if (animatingGenie || !expanded || !Visible || !IsHandleCreated || !moduleSidebar.Visible || expandedFrameFailed)
            {
                if (frameCornerOverlays != null)
                    foreach (FrameCornerOverlayForm overlay in frameCornerOverlays) overlay.Hide();
                return;
            }

            try
            {
                frameCornerOverlays ??= new[]
                {
                    new FrameCornerOverlayForm(FrameCorner.TopRight),
                    new FrameCornerOverlayForm(FrameCorner.BottomRight),
                    new FrameCornerOverlayForm(FrameCorner.BottomLeft)
                };
                float radius = Math.Max(3f, D(5));
                int patchSize = (int)Math.Ceiling(radius) + D(3);
                Size contentSize = new Size(ClientSize.Width - moduleSidebar.Right, ClientSize.Height);
                Point contentLocation = PointToScreen(new Point(moduleSidebar.Right, 0));
                foreach (FrameCornerOverlayForm overlay in frameCornerOverlays)
                {
                    overlay.UpdateCorner(contentLocation, contentSize, radius, patchSize);
                    if (!overlay.Visible)
                    {
                        showingWidgetOverlay = true;
                        try { overlay.Show(this); }
                        finally { showingWidgetOverlay = false; }
                    }
                }
            }
            catch (Exception error)
            {
                expandedFrameFailed = true;
                if (frameCornerOverlays != null)
                    foreach (FrameCornerOverlayForm overlay in frameCornerOverlays) overlay.Hide();
                UpdateExpandedWindowRegion();
                DebugHelper.WriteException(error, "展开窗口圆角透明绘制失败，已恢复原轮廓。");
            }
        }

        private void SyncCompactOverlay()
        {
            if (!ready || IsDisposed || Disposing) return;
            if (animatingGenie || expanded || !Visible || !IsHandleCreated)
            {
                compactOverlay?.Hide();
                return;
            }
            if (compactOverlayFailed)
            {
                ApplyOriginalCompactRegion();
                return;
            }

            try
            {
                compactOverlay ??= new CompactOverlayForm(this);
                Size size = new Size(D(CollapsedWidth), D(CollapsedHeight));
                Point location = PointToScreen(new Point(ClientSize.Width - size.Width, 0));
                compactOverlay.UpdateCompact(location, size);
                if (!compactOverlay.Visible) compactOverlay.Show(this);
            }
            catch (Exception error)
            {
                RecoverCompactOverlay(error);
            }
        }

        private void RefreshCompactOverlay()
        {
            if (compactOverlayFailed || compactOverlay == null || compactOverlay.IsDisposed ||
                !compactOverlay.Visible || expanded) return;
            try { compactOverlay.RefreshVisual(); }
            catch (Exception error) { RecoverCompactOverlay(error); }
        }

        private void RecoverCompactOverlay(Exception error)
        {
            compactOverlayFailed = true;
            compactOverlay?.Hide();
            if (!expanded) ApplyOriginalCompactRegion();
            DebugHelper.WriteException(error, "收起贴片透明绘制失败，已恢复原轮廓。");
        }

        private void ApplyOriginalCompactRegion()
        {
            PointF[] corners =
            {
                new PointF(0, 0), new PointF(ClientSize.Width, 0),
                new PointF(ClientSize.Width, ClientSize.Height), new PointF(0, ClientSize.Height)
            };
            using GraphicsPath path = CreateRoundedPolygonPath(corners,
                new[] { (float)D(10), 0f, 0f, (float)D(10) });
            Region oldRegion = Region;
            Region = new Region(path);
            oldRegion?.Dispose();
        }





        private void LayoutSlimScrollBar(VirtualClipboardPanel list, SlimListScrollBar scrollBar)
        {
            scrollBar.Bounds = new Rectangle(list.ClientSize.Width - scrollBar.Width, 0,
                scrollBar.Width, list.ClientSize.Height);
            scrollBar.BringToFront();
            scrollBar.RefreshFromList();
        }



        private void AttachHoverEvents(Control control)
        {
            foreach (Control child in control.Controls)
            {
                child.MouseEnter += Widget_MouseEnter;
                child.MouseLeave += Widget_MouseLeave;
                AttachHoverEvents(child);
            }
        }

        private void SetExpanded(bool value)
        {
            if (!value) RestoreSearchCompletedVisibility();
            if (!value) ReleaseOcrPanelHold();
            HideCompactHint();
            if (!value && animatingGenie) FinishGenieAnimation();
            collapseTimer.Stop();
            if (!value)
            {
                stickerOverlay?.Hide();
                if (frameCornerOverlays != null)
                    foreach (FrameCornerOverlayForm overlay in frameCornerOverlays) overlay.Hide();
            }
            else
            {
                compactOverlay?.Hide();
                compactLabel.PointerInside = false;
            }
            if (expanded && !value)
            {
                favoritesPage.CommitPendingFavorites();
                if (RememberExpandedBounds()) SettingManager.SaveApplicationConfigAsync();
                heightBeforeNewsAutoExpand = null;
            }
            Control activePanel = GetActiveModulePanel();
            changingExpansion = true;
            SuspendLayout();
            activePanel.SuspendLayout();
            moduleSidebar.SuspendLayout();
            try
            {
                expanded = value;
                bottomResizeHitFilter.SetEnabled(value);
                Padding = Padding.Empty;
                if (!value)
                {
                    Region oldRegion = Region;
                    Region = null;
                    oldRegion?.Dispose();
                    MinimumSize = Size.Empty;
                    Size = new Size(D(CollapsedWidth), D(CollapsedHeight));
                }
                else
                {
                    // Apply the saved size before the minimum: setting the minimum first
                    // expands the compact form twice and lays out every row twice.
                    Size target = GetSavedExpandedSize();
                    target = new Size(Math.Max(D(280), target.Width), Math.Max(D(310), target.Height));
                    if (Settings.ActiveModule == "News")
                    {
                        int newsHeight = Math.Min(GetNewsRequiredHeight(), Math.Max(D(310), dockScreen.WorkingArea.Height));
                        if (target.Height < newsHeight)
                        {
                            heightBeforeNewsAutoExpand ??= target.Height;
                            target.Height = newsHeight;
                        }
                    }
                    Size = target;
                    MinimumSize = new Size(D(280), D(310));
                }
                MoveToScreenEdge();
                compactLabel.Visible = !value;
                UpdateModuleVisibility();
                foreach (Panel grip in resizeGrips) grip.Visible = value;
                if (value)
                {
                    if (Settings.ActiveModule == "News") EnsureNewsFits();
                }
            }
            finally
            {
                moduleSidebar.ResumeLayout(true);
                activePanel.ResumeLayout(true);
                ResumeLayout(true);
                changingExpansion = false;
            }
            if (value)
            {
                // Docked panels have their final client rectangles only after ResumeLayout.
                // Measuring while suspended leaves compact-width buttons in the expanded form.
                LayoutContent();
                if (Settings.ActiveModule == "Todo") todoPage.RenderItems();
                if (Settings.ActiveModule == "Todo") todoPage.UpdateTodoViewport();
                else if (Settings.ActiveModule == "Clipboard") clipboardPage.UpdateClipboardViewport();
                else if (Settings.ActiveModule == "Favorites") favoritesPage.UpdateFavoritesViewport();
                RefreshActiveModuleSurface();
            }
            else
            {
                if (compactOverlayFailed)
                {
                    ApplyOriginalCompactRegion();
                    return;
                }
                // Keep an opaque interior for the regular child control, entirely under
                // the per-pixel overlay. No binary Region pixels reach its soft outline.
                int left = Math.Max(0, ClientSize.Width - D(CollapsedWidth));
                int top = D(2);
                Region oldRegion = Region;
                Region = new Region(new Rectangle(left + D(10), top,
                    Math.Max(1, D(CollapsedWidth) - D(10)),
                    Math.Max(1, D(CollapsedHeight) - top * 2)));
                oldRegion?.Dispose();
                SyncCompactOverlay();
            }
        }
    }
}
