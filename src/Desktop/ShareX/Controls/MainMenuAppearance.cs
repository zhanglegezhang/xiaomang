using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace ShareX
{
    // Scoped to the main-window pages: changing this palette must not restyle capture overlays or notes.
    internal static class MainMenuAppearance
    {
        internal static Color Background => ShareXResources.IsDarkTheme ? Color.FromArgb(25, 33, 39) : Color.FromArgb(245, 247, 250);
        internal static Color Sidebar => ShareXResources.IsDarkTheme ? Color.FromArgb(29, 39, 47) : Color.FromArgb(234, 239, 244);
        internal static Color Surface => ShareXResources.IsDarkTheme ? Color.FromArgb(34, 43, 51) : Color.White;
        internal static Color Input => ShareXResources.IsDarkTheme ? Color.FromArgb(23, 32, 39) : Color.White;
        internal static Color Edge => ShareXResources.IsDarkTheme ? Color.FromArgb(53, 67, 77) : Color.FromArgb(203, 214, 223);
        internal static Color Text => ShareXResources.IsDarkTheme ? Color.FromArgb(226, 234, 240) : Color.FromArgb(32, 43, 53);
        internal static Color StorageHeading => ShareXResources.IsDarkTheme ? Color.FromArgb(248, 250, 252) : Color.FromArgb(15, 23, 32);
        internal static Color Muted => ShareXResources.IsDarkTheme ? Color.FromArgb(151, 168, 181) : Color.FromArgb(91, 108, 121);
        internal static Color Selected => ShareXResources.IsDarkTheme ? Color.FromArgb(47, 67, 83) : Color.FromArgb(220, 233, 245);
        internal static Color Active => ShareXResources.IsDarkTheme ? Color.FromArgb(65, 48, 39) : Color.FromArgb(255, 230, 211);
        internal static readonly Color Orange = Color.FromArgb(255, 139, 67);
        internal static readonly Font BodyFont = new Font("Microsoft YaHei UI", 9f);
        internal static readonly Font MenuFont = new Font("Microsoft YaHei UI", 9.5f);
        // WinForms scales point fonts relative to the process initial DPI. Reapplying
        // an unscaled shared font after a monitor change would undo that scaling.
        private static readonly int FontBaselineDpi = GetFontBaselineDpi();
        private static readonly System.Collections.Generic.Dictionary<(Font, int), Font> dpiFonts = new();
        private static int GetFontBaselineDpi() { using var control = new Control(); return control.DeviceDpi; }
        internal static Font FontFor(Control control, Font basis)
        {
            int dpi = control.DeviceDpi;
            if (dpi == FontBaselineDpi) return basis;
            var key = (basis, dpi);
            if (!dpiFonts.TryGetValue(key, out Font font))
                dpiFonts[key] = font = new Font(basis.FontFamily, basis.SizeInPoints * dpi / FontBaselineDpi, basis.Style, GraphicsUnit.Point);
            return font;
        }
        private sealed class State { internal bool Hovered, Down, Primary, Selected; }
        private static readonly ConditionalWeakTable<Control, State> styled = new();
        internal static int Scale(Control c, int value) => (int)Math.Round(value * c.DeviceDpi / 96f);

        internal static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath path = new();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void RefreshIfStyled(Control root)
        {
            if (styled.TryGetValue(root, out _)) Apply(root);
        }

        internal static void Apply(Control root)
        {
            if (root == null || root.IsDisposed) return;
            if (root is SettingsDesignHost host) { host.BackColor = SettingsDesignSurface.Canvas;host.Surface.Finish();return; }
            if (root is SettingsDesignSurface design) { design.Finish(); return; }
            bool first = !styled.TryGetValue(root, out State state);
            if (first)
            {
                state = new State();
                styled.Add(root, state);
                root.ControlAdded += (s, e) => Apply(e.Control);
                if (root is Button button && (button.GetType() == typeof(Button) || button.GetType() == typeof(MenuButton)))
                {
                    button.Paint += PaintButton;
                    button.MouseEnter += (s, e) => { state.Hovered = true; button.Invalidate(); };
                    button.MouseLeave += (s, e) => { state.Hovered = state.Down = false; button.Invalidate(); };
                    button.MouseDown += (s, e) => { state.Down = e.Button == MouseButtons.Left; button.Invalidate(); };
                    button.MouseUp += (s, e) => { state.Down = false; button.Invalidate(); };
                    button.GotFocus += (s, e) => button.Invalidate();
                    button.LostFocus += (s, e) => button.Invalidate();
                    button.EnabledChanged += (s, e) => button.Invalidate();
                }
                if (root is GroupBox group) group.Paint += PaintGroup;
                if (root is TreeView tree)
                {
                    tree.DrawNode += PaintTreeNode;
                    tree.MouseDown += SelectSettingsRow;
                }
                if (root is TabControl tabs) tabs.DrawItem += PaintTab;
                if (root is CheckBox check && check.Appearance == Appearance.Normal) check.Paint += PaintCheckBox;
            }
            root.ForeColor = root is Label { Tag: "storage-section-heading" } ? StorageHeading : Text;
            if (root is Button b)
            {
                b.UseVisualStyleBackColor = false;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = Surface;
                b.Font = FontFor(b, BodyFont);
                if (b is MenuButton menuButton) ApplyMenu(menuButton.Menu);
            }
            else if (root is DataGridView grid) ApplyGrid(grid);
            else if (root is TextBoxBase text)
            {
                text.BackColor = Input;
                text.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (root is ComboBox combo)
            {
                combo.BackColor = Input;
                combo.FlatStyle = FlatStyle.Flat;
            }
            else if (root is NumericUpDown numeric) { numeric.BackColor = Input; numeric.BorderStyle = BorderStyle.FixedSingle; }
            else if (root is CheckBox check) { check.BackColor = Background; check.FlatStyle = FlatStyle.Flat; check.FlatAppearance.CheckedBackColor = Active; }
            else if (root is RadioButton radio) { radio.BackColor = Background; radio.FlatStyle = FlatStyle.Flat; }
            else if (root is TreeView tree)
            {
                tree.BackColor = Sidebar;
                tree.Font = FontFor(tree, MenuFont);
                tree.BorderStyle = BorderStyle.None;
                tree.DrawMode = TreeViewDrawMode.OwnerDrawAll;
                tree.FullRowSelect = true;
                tree.HideSelection = false;
                tree.ShowLines = tree.ShowPlusMinus = tree.ShowRootLines = false;
                tree.ItemHeight = Math.Max(Scale(tree, tree.FindForm() is ApplicationSettingsForm ? 30 : 29), tree.Font.Height + Scale(tree, 8));
                tree.Indent = Scale(tree, 13);
                foreach (TreeNode node in tree.Nodes) ApplyNodePages(node);
                if (tree.SelectedNode == null && tree.Nodes.Count > 0) tree.SelectedNode = tree.Nodes[0];
            }
            else if (root is TabControl tabs) { tabs.DrawMode = TabDrawMode.OwnerDrawFixed; tabs.BackColor = Background; }
            else if (root is PropertyGrid property)
            {
                property.ViewBackColor = Input; property.ViewForeColor = Text;
                property.HelpBackColor = Surface; property.HelpForeColor = Text;
                property.LineColor = Edge; property.CategoryForeColor = Text;
                property.CommandsBackColor = Surface; property.CommandsForeColor = Text;
            }
            else if (root is LinkLabel link) { link.BackColor = Background; link.LinkColor = Color.FromArgb(111, 190, 255); link.ActiveLinkColor = Orange; }
            else if (root is ListView list) { list.BackColor = Input; list.BorderStyle = BorderStyle.None; }
            else if (root is Form || root is Panel || root is UserControl || root is TabPage || root is Label || root is GroupBox || root is PictureBox)
                root.BackColor = Background;
            if (root.Tag is string registrationTag && registrationTag == "registration-surface") root.BackColor = Background;
            if (root.ContextMenuStrip != null) ApplyMenu(root.ContextMenuStrip);
            foreach (Control child in root.Controls) Apply(child);
            if (root is TabToTreeView navigation)
            {
                navigation.LeftPanelBackColor = Sidebar;
                navigation.SeparatorColor = Background;
                navigation.TreeViewFont = FontFor(navigation, MenuFont);
            }
            root.Invalidate();
        }

        private static void ApplyNodePages(TreeNode node)
        {
            if (node.Tag is TabPage page) Apply(page);
            foreach (TreeNode child in node.Nodes) ApplyNodePages(child);
        }

        internal static void SetPrimary(Button button) { Apply(button); styled.GetValue(button, _ => new State()).Primary = true; button.Invalidate(); }
        internal static void SetSelected(Button button, bool selected) { Apply(button); styled.GetValue(button, _ => new State()).Selected = selected; button.Invalidate(); }

        internal static void LayoutPager(Panel footer, Label status, Button previous, Button next)
        {
            status.Dock = previous.Dock = next.Dock = DockStyle.None;
            void Arrange()
            {
                int height = Scale(footer, 30), gap = Scale(footer, 6);
                int top = Math.Max(0, footer.Height - height);
                next.SetBounds(footer.Width - next.Width, top, next.Width, height);
                previous.SetBounds(next.Left - gap - previous.Width, top, previous.Width, height);
                status.SetBounds(0, top, Math.Max(1, previous.Left - gap), height);
            }
            footer.SizeChanged += (s, e) => Arrange();
            Arrange();
        }

        internal static void ApplyMenu(ToolStrip menu)
        {
            if (menu == null || menu.IsDisposed) return;
            menu.Renderer = new MainMenuRenderer();
            menu.BackColor = Sidebar;
            menu.ForeColor = Text;
            menu.Font = FontFor(menu, MenuFont);
            menu.ImageScalingSize = new Size(Scale(menu, 16), Scale(menu, 16));
            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = Text;
                item.Padding = new Padding(Scale(menu, 4), Scale(menu, 5), Scale(menu, 8), Scale(menu, 5));
                if (item is ToolStripDropDownItem parent && parent.HasDropDownItems) ApplyMenu(parent.DropDown);
            }
        }

        private static void ApplyGrid(DataGridView grid)
        {
            grid.BackgroundColor = Background;
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.GridColor = Edge;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Sidebar;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Sidebar;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.Font = FontFor(grid, BodyFont);
            var cells = grid.DefaultCellStyle.Clone();
            cells.BackColor = Surface;
            cells.ForeColor = Text;
            cells.SelectionBackColor = Selected;
            cells.SelectionForeColor = Text;
            grid.DefaultCellStyle = cells;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Sidebar;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Selected;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Text;
        }

        private static void PaintButton(object sender, PaintEventArgs e)
        {
            Button b = (Button)sender;
            State state = styled.GetValue(b, _ => new State());
            if (b.Width < 3 || b.Height < 3) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(b.Parent?.BackColor ?? Background);
            Rectangle bounds = new(0, 0, b.Width - 1, b.Height - 1);
            using GraphicsPath path = Round(bounds, Scale(b, 5));
            Color fill = state.Primary && b.Enabled ? Orange : state.Selected ? Active : state.Hovered && b.Enabled ? Selected : Surface;
            if (state.Down && b.Enabled) fill = Active;
            using SolidBrush brush = new(fill);
            using Pen edge = new(b.Focused || state.Selected ? Orange : Edge);
            g.FillPath(brush, path); g.DrawPath(edge, path);
            Color fg = !b.Enabled ? Muted : state.Primary ? Color.FromArgb(25, 33, 39) : state.Selected ? Orange : Text;
            if(b.Enabled && b.Tag as string == "settings-danger")fg=Color.FromArgb(238,113,104);
            Rectangle textBounds = new(Scale(b, 7), 0, Math.Max(1, b.Width - Scale(b, 14)), b.Height);
            bool hotkey = b.Parent is HotkeySelectionControl;
            if (b.Image != null)
            {
                int size = Scale(b, 16);
                int x = string.IsNullOrWhiteSpace(b.Text) ? (b.Width - size) / 2 : b.ImageAlign == ContentAlignment.MiddleRight ? b.Width - size - Scale(b, 10) : Scale(b, 9);
                if (hotkey && b.Name == "btnTask")
                {
                    var job = ((HotkeySelectionControl)b.Parent).HotkeySettings.TaskSettings.Job;
                    string icon = job == HotkeyType.RectangleRegion ? "capture" : job == HotkeyType.ScreenRecorder ? "record" : job == HotkeyType.ActiveWindowTopMost ? "pin" : job == HotkeyType.ScreenColorPicker ? "picker" : null;
                    if (icon == null) g.DrawImage(b.Image, new Rectangle(x, (b.Height - size) / 2, size, size));
                    else MainMenuRenderer.DrawGlyph(g, icon, new Rectangle(x, (b.Height - size) / 2, size, size), icon == "record" || icon == "pin" ? Color.FromArgb(244, 107, 109) : fg);
                }
                else if (hotkey && b.Name == "btnHotkey")
                {
                    var status = ((HotkeySelectionControl)b.Parent).HotkeySettings.HotkeyInfo.Status;
                    using SolidBrush dot = new(status == HotkeyStatus.Registered ? Color.FromArgb(92, 210, 129) : status == HotkeyStatus.Failed ? Color.FromArgb(244, 107, 109) : Color.FromArgb(238, 187, 83));
                    int diameter = Scale(b, 6);
                    g.FillEllipse(dot, x + (size - diameter) / 2, (b.Height - diameter) / 2, diameter, diameter);
                }
                else g.DrawImage(b.Image, new Rectangle(x, (b.Height - size) / 2, size, size));
                if (!string.IsNullOrWhiteSpace(b.Text))
                {
                    if (b.ImageAlign == ContentAlignment.MiddleRight) textBounds.Width -= size + Scale(b, 8);
                    else { textBounds.X += size + Scale(b, 8); textBounds.Width -= size + Scale(b, 8); }
                }
            }
            if (b is MenuButton) textBounds.Width -= Scale(b, 16);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            flags |= b.TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            if (!b.UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            if (state.Primary && b.Name == "btnAdd")
                TextRenderer.DrawText(g, "+ " + b.Text.TrimEnd('.'), b.Font, textBounds, fg, flags);
            else TextRenderer.DrawText(g, b.Text, b.Font, textBounds, fg, flags);
            if (b.Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(bounds, -Scale(b, 4), -Scale(b, 4)), fg, fill);
        }

        private static void PaintGroup(object sender, PaintEventArgs e)
        {
            GroupBox group = (GroupBox)sender;
            if (group.Width < 3 || group.Height < group.Font.Height + 3) return;
            e.Graphics.Clear(Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = Round(new Rectangle(0, group.Font.Height / 2, group.Width - 1, group.Height - group.Font.Height / 2 - 1), Scale(group, 6));
            using Pen pen = new(Edge);
            e.Graphics.DrawPath(pen, path);
            int width = TextRenderer.MeasureText(group.Text, group.Font).Width;
            Rectangle title = new(Scale(group, 10), 0, width + Scale(group, 8), group.Font.Height);
            using SolidBrush brush = new(Background);
            e.Graphics.FillRectangle(brush, title);
            TextRenderer.DrawText(e.Graphics, group.Text, group.Font, title, Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private static void SelectSettingsRow(object sender, MouseEventArgs e)
        {
            TreeView tree = (TreeView)sender;
            if (e.Button != MouseButtons.Left || tree.FindForm() is not (ApplicationSettingsForm or InputMethodPageForm)) return;
            // Match the full painted row, including the icon and the former child indentation.
            for (TreeNode node = tree.TopNode; node != null; node = node.NextVisibleNode)
            {
                if (e.Y >= node.Bounds.Top && e.Y < node.Bounds.Top + tree.ItemHeight)
                {
                    tree.SelectedNode = node;
                    break;
                }
            }
        }

        internal static string SettingsPageGlyph(TreeNode node) => (node.Tag as TabPage)?.Name switch
        {
            "tpGeneral" => "settings",
            "tpNotifications" => "bell",
            "tpPaths" => "folder",
            "tpAdvanced" => "sliders",
            "tpDailyNews" => "news",
            "tpVoiceTranscription" => "microphone",
            "tpVoiceInput" => "microphone",
            "tpQingjianInputMethod" => "keyboard",
            "tpCapture" or "tpCaptureGeneral" => "image",
            "tpRegionCapture" => "capture",
            "tpScreenRecorder" => "video",
            "tpOCR" => "ocr",
            "tpTools" or "tpToolsGeneral" => "picker",
            _ => "tasks"
        };

        private static void PaintTreeNode(object sender, DrawTreeNodeEventArgs e)
        {
            TreeView tree = (TreeView)sender;
            if (e.Bounds.Height <= 0) return;
            bool active = tree.SelectedNode == e.Node;
            Rectangle row = new(Scale(tree, 4), e.Bounds.Top + Scale(tree, 1), Math.Max(1, tree.ClientSize.Width - Scale(tree, 8)), tree.ItemHeight - Scale(tree, 2));
            using SolidBrush background = new(Sidebar);
            e.Graphics.FillRectangle(background, new Rectangle(0, e.Bounds.Top, tree.ClientSize.Width, tree.ItemHeight));
            if (active)
            {
                using GraphicsPath path = Round(row, Scale(tree, 5));
                using SolidBrush brush = new(Active);
                e.Graphics.FillPath(brush, path);
                using SolidBrush accent = new(Orange);
                e.Graphics.FillRectangle(accent, row.Left, row.Top + Scale(tree, 6), Scale(tree, 2), row.Height - Scale(tree, 12));
            }
            int indent = Scale(tree, 12) + e.Node.Level * Scale(tree, 13);
            if (tree.FindForm() is ApplicationSettingsForm or InputMethodPageForm)
            {
                indent = Scale(tree, 12);
                int iconSize = Scale(tree, 16);
                MainMenuRenderer.DrawGlyph(e.Graphics, SettingsPageGlyph(e.Node),
                    new Rectangle(indent, row.Top + (row.Height - iconSize) / 2, iconSize, iconSize), active ? Orange : Muted);
                indent += iconSize + Scale(tree, 10);
            }
            else if (e.Node.Level == 0)
            {
                bool isColorPickerPage = e.Node.Tag is TabPage page && (page.Name == "tpTools" || page.Name == "tpToolsGeneral");
                string icon = isColorPickerPage ? "tools" : e.Node.Text.Contains("路径") ? "folder" : e.Node.Text.Contains("图") ? "image" :
                    e.Node.Text.Contains("快捷") ? "keyboard" : e.Node.Text.Contains("工具") ? "tools" : e.Node.Text.Contains("常规") || e.Node.Text.Contains("主题") ? "settings" : "tasks";
                MainMenuRenderer.DrawGlyph(e.Graphics, icon, new Rectangle(indent, row.Top + (row.Height - Scale(tree, 14)) / 2, Scale(tree, 14), Scale(tree, 14)), active ? Orange : Muted);
                indent += Scale(tree, 23);
            }
            TextRenderer.DrawText(e.Graphics, e.Node.Text, tree.Font, new Rectangle(indent, row.Top, Math.Max(1, row.Right - indent - Scale(tree, 4)), row.Height), active ? Orange : Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawDefault = false;
        }

        private static void PaintTab(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = (TabControl)sender;
            if (e.Index < 0 || e.Index >= tabs.TabCount) return;
            bool active = tabs.SelectedIndex == e.Index;
            using SolidBrush brush = new(active ? Active : Sidebar);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, active ? Orange : Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static void PaintCheckBox(object sender, PaintEventArgs e)
        {
            CheckBox check = (CheckBox)sender;
            // Keep the native checkbox hit target and keyboard behavior, only draw its visual state.
            if (check.CheckAlign != ContentAlignment.MiddleLeft || check.RightToLeft == RightToLeft.Yes || check.Image != null) return;
            Graphics g = e.Graphics;
            g.Clear(check.BackColor);
            int size = Scale(check, 13);
            Rectangle mark = new(1, (check.Height - size) / 2, size, size);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = Round(mark, Scale(check, 2));
            using SolidBrush fill = new(check.Checked ? (check.Enabled ? Orange : Edge) : Input);
            using Pen pen = new(check.Checked && check.Enabled ? Orange : Muted);
            g.FillPath(fill, path); g.DrawPath(pen, path);
            if (check.CheckState != CheckState.Unchecked)
            {
                using Pen tick = new(check.Enabled ? Color.FromArgb(25, 33, 39) : Text, Math.Max(1.5f, check.DeviceDpi / 64f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                if (check.CheckState == CheckState.Indeterminate) g.DrawLine(tick, mark.Left + size * .25f, mark.Top + size * .5f, mark.Left + size * .75f, mark.Top + size * .5f);
                else g.DrawLines(tick, new[] { new PointF(mark.Left + size * .22f, mark.Top + size * .5f), new PointF(mark.Left + size * .43f, mark.Top + size * .72f), new PointF(mark.Left + size * .79f, mark.Top + size * .27f) });
            }
            Rectangle text = new(mark.Right + Scale(check, 5), 0, Math.Max(1, check.Width - mark.Right - Scale(check, 5)), check.Height);
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            flags |= check.Height >= check.Font.Height * 2 ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine;
            if (!check.UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, check.Text, check.Font, text, check.Enabled ? Text : Muted, flags);
            if (check.Focused) ControlPaint.DrawFocusRectangle(g, text, Text, check.BackColor);
        }
    }
}

