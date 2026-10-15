using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.Collections.Generic;

namespace ShareX
{
    // Auto-sized rows keep translated captions and system font scaling from clipping.
    internal sealed class SettingsPageLayout : System.IDisposable
    {
        private readonly Control owner;
        private readonly float labelColumnPercent;
        private readonly bool deferLayout;
        private readonly List<Control> pendingLayouts = new();
        public int Scale(int value) => (int)System.Math.Round(value * owner.DeviceDpi / 96f);
        public static string Caption(string chinese, string english) =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? chinese : english;

        public SettingsPageLayout(Control owner, float labelColumnPercent = 42, bool deferLayout = false)
        {
            this.owner = owner;
            this.labelColumnPercent = labelColumnPercent;
            this.deferLayout = deferLayout;
        }

        public void Defer(Control control)
        {
            if (!deferLayout) return;
            control.SuspendLayout();
            pendingLayouts.Add(control);
        }

        public void Complete()
        {
            for (int i = pendingLayouts.Count - 1; i >= 0; i--) pendingLayouts[i].ResumeLayout(false);
            pendingLayouts.Clear();
        }

        public void Dispose() => Complete();

        public TableLayoutPanel Stack()
        {
            var table = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 0, Dock = DockStyle.Top,
                Margin = Padding.Empty, Padding = Padding.Empty, Size = new Size(1, 1)
            };
            Defer(table);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        public void Add(TableLayoutPanel stack, Control control)
        {
            int row = stack.RowCount++;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.TabIndex = row;
            control.Margin = new Padding(0, Scale(4), 0, Scale(4));
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            if (control is CheckBox check) check.AutoSize = true;
            stack.Controls.Add(control, 0, row);
        }

        public void Heading(TableLayoutPanel stack, string chinese, string english)
        {
            var label = new Label { Text = Caption(chinese, english), AutoSize = true };
            Add(stack, label);
            label.Margin = new Padding(0, Scale(stack.RowCount == 1 ? 0 : 14), 0, Scale(8));
        }

        public Panel Row(Control label, Control field, bool stackNarrow = true)
        {
            var row = new Panel { Dock = DockStyle.Top, Margin = Padding.Empty, Size = new Size(1, 1) };
            Defer(row);
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            label.Margin = new Padding(0, Scale(4), Scale(12), Scale(4));
            field.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            field.Margin = new Padding(0, Scale(2), 0, Scale(2));
            row.Controls.Add(label);
            row.Controls.Add(field);
            bool adjusting = false;
            void FitRow()
            {
                if (adjusting || row.ClientSize.Width < Scale(80)) return;
                adjusting = true;
                try
                {
                    int width = row.ClientSize.Width - row.Padding.Horizontal;
                    bool stacked = stackNarrow && width < Scale(520);
                    int labelColumn = stacked ? width : (int)(width * labelColumnPercent / 100);
                    int fieldColumn = stacked ? width : width - labelColumn;
                    int labelWidth = System.Math.Max(1, labelColumn - label.Margin.Horizontal);
                    int fieldWidth = System.Math.Max(1, fieldColumn - field.Margin.Horizontal);
                    var labelMaximum = new Size(labelWidth, 0);
                    var fieldMaximum = new Size(fieldWidth, 0);
                    if (label.MaximumSize != labelMaximum) label.MaximumSize = labelMaximum;
                    if (field.MaximumSize != fieldMaximum) field.MaximumSize = fieldMaximum;
                    int labelHeight = label.GetPreferredSize(labelMaximum).Height;
                    int fieldHeight = System.Math.Max(field.MinimumSize.Height, field.GetPreferredSize(fieldMaximum).Height);
                    label.Bounds = new Rectangle(row.Padding.Left, row.Padding.Top + label.Margin.Top, labelWidth, labelHeight);
                    int fieldTop = stacked ? label.Bottom + label.Margin.Bottom + field.Margin.Top : row.Padding.Top + field.Margin.Top;
                    field.Bounds = new Rectangle(row.Padding.Left + (stacked ? 0 : labelColumn), fieldTop, fieldWidth, fieldHeight);
                    int height = System.Math.Max(label.Bottom + label.Margin.Bottom, field.Bottom + field.Margin.Bottom) + row.Padding.Bottom;
                    if (row.Height != height) row.Height = height;
                }
                finally { adjusting = false; }
            }
            row.SizeChanged += (sender, e) => FitRow();
            row.Layout += (sender, e) => FitRow();
            label.FontChanged += (sender, e) => FitRow();
            field.FontChanged += (sender, e) => FitRow();
            return row;
        }
        public FlowLayoutPanel Inline(params Control[] controls)
        {
            var flow = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true, Margin = Padding.Empty, Size = new Size(1, 1)
            };
            Defer(flow);
            foreach (Control control in controls)
            {
                control.Margin = new Padding(0, Scale(2), Scale(6), Scale(2));
                control.Anchor = AnchorStyles.Left;
                if (control is Label) { control.AutoSize = true; control.Margin = new Padding(0, Scale(5), Scale(6), 0); }
                if (control is NumericUpDown) control.Width = Scale(72);
                flow.Controls.Add(control);
            }
            return flow;
        }

        public void Install(ScrollableControl parent, TableLayoutPanel stack)
        {
            parent.AutoScroll = true;
            parent.Padding = new Padding(Scale(16));
            bool fitting = false;
            void FitContent()
            {
                if (fitting) return;
                fitting = true;
                try
                {
                    int width = System.Math.Max(1, parent.ClientSize.Width - parent.Padding.Horizontal - (parent.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
                    Size maximum = new Size(width, 0);
                    if (stack.MaximumSize != maximum) stack.MaximumSize = maximum;
                    if (stack.Width != width) stack.Width = width;
                }
                finally { fitting = false; }
            }
            parent.SizeChanged += (sender, e) => FitContent();
            parent.Layout += (sender, e) => FitContent();
            parent.Controls.Add(stack);
            FitContent();
        }
    }
}
