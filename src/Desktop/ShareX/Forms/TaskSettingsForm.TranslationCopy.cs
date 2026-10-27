using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX;

public partial class TaskSettingsForm
{
    private FlowLayoutPanel translationCopyChoices;

    private void InitializeTranslationCopyChoices(OCROptions options)
    {
        cbCaptureOCRAutoCopy.Text = "自动复制翻译结果";
        translationCopyChoices = new FlowLayoutPanel
        {
            Name = "translationCopyChoices", AutoSize = true, WrapContents = false,
            Location = new Point(cbCaptureOCRAutoCopy.Left, cbCaptureOCRAutoCopy.Bottom + 8),
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        foreach (var pair in new[] { ("中文", TranslationCopyContent.Chinese), ("英文", TranslationCopyContent.English),
            ("中文+英文", TranslationCopyContent.ChineseAndEnglish) })
        {
            var radio = new TranslationCopyRadioButton { Text = pair.Item1, AutoSize = true, Checked = options.TranslationCopyContent == pair.Item2,
                Margin = new Padding(0, 4, 16, 4) };
            radio.CheckedChanged += (_, _) => { if (radio.Checked) options.TranslationCopyContent = pair.Item2; };
            translationCopyChoices.Controls.Add(radio);
        }
        tpOCR.Controls.Add(translationCopyChoices);
        void UpdateEnabled() => translationCopyChoices.Enabled = options.AutoCopy && !options.Silent;
        cbCaptureOCRAutoCopy.CheckedChanged += (_, _) => UpdateEnabled();
        cbCaptureOCRSilent.CheckedChanged += (_, _) => UpdateEnabled();
        UpdateEnabled();
    }
}

internal sealed class TranslationCopyRadioButton : RadioButton
{
    private bool hovered;
    private int ScaleValue(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    internal TranslationCopyRadioButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return new Size(ScaleValue(24) + text.Width + Padding.Horizontal,
            Math.Max(ScaleValue(20), text.Height + ScaleValue(4)) + Padding.Vertical);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        // RadioButton's themed background can replay the parent caption into this surface.
        // This control paints its own opaque background before its glyph and label.
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float diameter = 16f * DeviceDpi / 96f;
        float stroke = 1.5f * DeviceDpi / 96f;
        float x = Padding.Left + stroke / 2, y = (Height - diameter) / 2f + stroke / 2;
        Color color = !Enabled ? Color.FromArgb(83,99,110) : Checked || hovered ? Color.FromArgb(255,135,62) : Color.FromArgb(146,163,175);
        using var pen = new Pen(color, stroke);
        e.Graphics.DrawEllipse(pen, x, y, diameter - stroke, diameter - stroke);
        if (Checked)
        {
            float dot = 8f * DeviceDpi / 96f;
            using var brush = new SolidBrush(color);
            e.Graphics.FillEllipse(brush, Padding.Left + (diameter-dot)/2, (Height-dot)/2, dot, dot);
        }
        var textBounds = new Rectangle(Padding.Left + ScaleValue(24), Padding.Top,
            Math.Max(0, Width-Padding.Horizontal-ScaleValue(24)), Height-Padding.Vertical);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : SystemColors.GrayText,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, textBounds, ForeColor, BackColor);
    }
}
