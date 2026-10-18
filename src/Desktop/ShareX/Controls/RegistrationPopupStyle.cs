using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX;

// Scoped to registration popups; the tray and sticker menus keep their existing style.
internal static class RegistrationPopupStyle
{
    internal static readonly Color Background = Color.FromArgb(27, 36, 43);
    internal static readonly Color Surface = Color.FromArgb(38, 48, 57);
    internal static readonly Color Edge = Color.FromArgb(64, 80, 92);
    internal static readonly Color Text = Color.FromArgb(224, 231, 238);
    internal static readonly Color Muted = Color.FromArgb(163, 179, 192);
    internal static readonly Color Orange = Color.FromArgb(255, 135, 62);
    internal static readonly Color Danger = Color.FromArgb(235, 146, 146);
    internal static int S(Control owner, int value) => MainMenuAppearance.Scale(owner, value);

    internal static void Glyph(Graphics graphics, string name, Rectangle bounds, Color color)
    {
        if (name is "sliders" or "document") { MainMenuRenderer.DrawGlyph(graphics, name, bounds, color); return; }
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (name)
        {
            case "trash":
                graphics.DrawLines(pen, new[] { new Point(6, 8), new Point(7, 21), new Point(18, 21), new Point(19, 8) });
                graphics.DrawLine(pen, 4, 6, 21, 6); graphics.DrawRectangle(pen, 9, 2, 7, 4);
                graphics.DrawLine(pen, 10, 10, 10, 17); graphics.DrawLine(pen, 15, 10, 15, 17); break;
            case "remove":
                graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawLine(pen, 8, 12, 16, 12); break;
            case "retry":
                graphics.DrawArc(pen, 3, 3, 18, 18, 0, 295);
                graphics.DrawLines(pen, new[] { new Point(13, 2), new Point(19, 2), new Point(19, 8) }); break;
            case "external":
                graphics.DrawLines(pen, new[] { new Point(10, 4), new Point(4, 4), new Point(4, 20), new Point(20, 20), new Point(20, 14) });
                graphics.DrawLines(pen, new[] { new Point(14, 3), new Point(21, 3), new Point(21, 10) });
                graphics.DrawLine(pen, 11, 13, 21, 3); break;
            case "eye": case "eye-off":
                using (var eye = new GraphicsPath())
                {
                    eye.AddBezier(2, 12, 7, 3, 17, 3, 22, 12); eye.AddBezier(22, 12, 17, 21, 7, 21, 2, 12);
                    graphics.DrawPath(pen, eye);
                }
                graphics.DrawEllipse(pen, 9, 9, 6, 6);
                if (name == "eye-off") graphics.DrawLine(pen, 3, 3, 21, 21); break;
            case "close": graphics.DrawLine(pen, 6, 6, 18, 18); graphics.DrawLine(pen, 18, 6, 6, 18); break;
        }
        graphics.Restore(state);
    }
}

internal sealed class RegistrationMaintenanceMenu : ContextMenuStrip
{
    private readonly Font menuFont = new("Microsoft YaHei UI", 9.5f);
    internal RegistrationMaintenanceMenu()
    {
        Name = "registrationMaintenancePopup";
        Font = menuFont; ShowImageMargin = ShowCheckMargin = false; DropShadowEnabled = true;
        BackColor = RegistrationPopupStyle.Surface; ForeColor = RegistrationPopupStyle.Text;
        Renderer = new RendererStyle();
        SizeChanged += (_, _) => Round();
    }
    internal static bool Destructive(string name) => name is "uninstallVoiceInputComponent" or "uninstallXiaomangInputMethod" or "removeModernFileMenu";
    internal static bool Destructive(ToolStripItem item) => Destructive(item.Name);
    protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
    {
        // Parent theme refreshes may replace the renderer; restore only this popup.
        Renderer = new RendererStyle(); Font = menuFont;
        int S(int value) => RegistrationPopupStyle.S(this, value);
        int width = S(140), height = 0;
        foreach (ToolStripItem item in Items)
            if (item is not ToolStripSeparator)
                width = Math.Max(width, TextRenderer.MeasureText(item.Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + S(56));
        Padding = new Padding(S(6));
        foreach (ToolStripItem item in Items)
        {
            item.AutoSize = false; item.Margin = Padding.Empty; item.Padding = Padding.Empty;
            item.Size = new Size(width, S(item is ToolStripSeparator ? 9 : 36));
            height += item.Height;
        }
        AutoSize = false; Size = new Size(width + Padding.Horizontal, height + Padding.Vertical);
        base.OnOpening(e); Round();
    }
    private void Round()
    {
        if (Width < 2 || Height < 2) return;
        using var path = MainMenuAppearance.Round(new Rectangle(0, 0, Width, Height), RegistrationPopupStyle.S(this, 9));
        var old = Region; Region = new Region(path); old?.Dispose();
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) menuFont.Dispose(); }

    private sealed class RendererStyle : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(RegistrationPopupStyle.Surface);
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = MainMenuAppearance.Round(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), RegistrationPopupStyle.S(e.ToolStrip, 9));
            using var pen = new Pen(RegistrationPopupStyle.Edge); e.Graphics.DrawPath(pen, path);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = MainMenuAppearance.Round(new Rectangle(0, 0, e.Item.Width - 1, e.Item.Height - 1), RegistrationPopupStyle.S(e.ToolStrip, 6));
            using var fill = new SolidBrush(RegistrationMaintenanceMenu.Destructive(e.Item) ? Color.FromArgb(65, 49, 53) : Color.FromArgb(65, 55, 47));
            e.Graphics.FillPath(fill, path);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(RegistrationPopupStyle.Edge);
            int inset = RegistrationPopupStyle.S(e.ToolStrip, 8);
            e.Graphics.DrawLine(pen, inset, e.Item.Height / 2, e.Item.Width - inset, e.Item.Height / 2);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            int S(int value) => RegistrationPopupStyle.S(e.ToolStrip, value);
            bool danger = RegistrationMaintenanceMenu.Destructive(e.Item);
            Color text = !e.Item.Enabled ? RegistrationPopupStyle.Muted : danger ? RegistrationPopupStyle.Danger : RegistrationPopupStyle.Text;
            Color icon = !e.Item.Enabled ? RegistrationPopupStyle.Muted : danger ? RegistrationPopupStyle.Danger : e.Item.Selected ? RegistrationPopupStyle.Orange : RegistrationPopupStyle.Muted;
            string glyph = e.Item.Name switch
            {
                "configureBaiduTranslation" => "sliders", "retryXiaomangInputWorker" => "retry",
                "openXiaomangInputSetupLogFolder" => "document", "removeModernFileMenu" => "remove", _ => "trash"
            };
            RegistrationPopupStyle.Glyph(e.Graphics, glyph, new Rectangle(S(10), (e.Item.Height - S(18)) / 2, S(18), S(18)), icon);
            TextRenderer.DrawText(e.Graphics, e.Item.Text, e.TextFont, new Rectangle(S(38), 0, e.Item.Width - S(48), e.Item.Height), text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}

internal sealed class RegistrationPopupButton : Button
{
    internal bool Primary { get; set; }
    internal bool Danger { get; set; }
    internal bool Quiet { get; set; }
    internal string Glyph { get; set; }
    private bool hover, down;
    internal RegistrationPopupButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
        BackColor = RegistrationPopupStyle.Surface; ForeColor = RegistrationPopupStyle.Text;
        Cursor = Cursors.Hand; SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        MouseEnter += (_, _) => { hover = true; Invalidate(); }; MouseLeave += (_, _) => { hover = down = false; Invalidate(); };
        MouseDown += (_, e) => { down = e.Button == MouseButtons.Left; Invalidate(); }; MouseUp += (_, _) => { down = false; Invalidate(); };
        GotFocus += (_, _) => Invalidate(); LostFocus += (_, _) => Invalidate(); EnabledChanged += (_, _) => Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Quiet ? BackColor : Parent?.BackColor ?? RegistrationPopupStyle.Background); g.SmoothingMode = SmoothingMode.AntiAlias;
        int S(int value) => RegistrationPopupStyle.S(this, value);
        using var path = MainMenuAppearance.Round(new Rectangle(0, 0, Width - 1, Height - 1), S(7));
        Color fill = Primary && Enabled ? RegistrationPopupStyle.Orange : down && Enabled ? Color.FromArgb(65, 55, 47) : hover && Enabled ? Color.FromArgb(47, 62, 73) : BackColor;
        Color foreground = !Enabled ? RegistrationPopupStyle.Muted : Primary ? Color.White : Danger ? RegistrationPopupStyle.Danger : ForeColor;
        using var brush = new SolidBrush(fill); using var edge = new Pen(Focused ? RegistrationPopupStyle.Orange : Danger ? Color.FromArgb(94, 68, 73) : RegistrationPopupStyle.Edge);
        g.FillPath(brush, path); if ((!Primary || Focused) && !Quiet) g.DrawPath(edge, path);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        int textWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags).Width;
        int iconSize = string.IsNullOrEmpty(Glyph) ? 0 : S(18), gap = iconSize > 0 && textWidth > 0 ? S(8) : 0;
        int left = (Width - textWidth - iconSize - gap) / 2;
        if (iconSize > 0) RegistrationPopupStyle.Glyph(g, Glyph, new Rectangle(left, (Height - iconSize) / 2, iconSize, iconSize), foreground);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(left + iconSize + gap, 0, textWidth, Height), foreground,
            flags | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -S(4), -S(4)), foreground, fill);
    }
}
