using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace ShareX
{
    // The original editor remains the authority for ranges, precision, enablement
    // and ValueChanged handlers. Wheel messages never reach that editor.
    internal sealed class SettingsDesignNumber : UserControl
    {
        private readonly NumericUpDown source;
        private readonly NumberTextBox input;
        private readonly bool ownsSource;
        private readonly ToolTip feedback = new();
        private bool syncing, invalid, dirty, disposing;
        private int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);

        internal SettingsDesignNumber(NumericUpDown source)
        {
            this.source = source;
            ownsSource = source.Parent == null;
            Name = "number_" + source.Name;
            AccessibleName = source.AccessibleName ?? source.Name;
            AccessibleRole = AccessibleRole.SpinButton;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = SettingsDesignSurface.Input;
            Size = new Size(S(104), S(32));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            input = new NumberTextBox(this)
            {
                Name = "numberText_" + source.Name,
                BorderStyle = BorderStyle.None,
                BackColor = SettingsDesignSurface.Input,
                ForeColor = SettingsDesignSurface.TextColor,
                ReadOnly = source.ReadOnly,
                AccessibleName = AccessibleName
            };
            Controls.Add(input);
            input.KeyDown += InputKeyDown;
            input.TextChanged += (_, _) => { if (!syncing) { dirty = true; invalid = false; Invalidate(); } };
            input.Enter += (_, _) => { input.SelectAll(); Invalidate(); };
            input.Leave += (_, _) => Invalidate();
            source.ValueChanged += Pull;
            source.EnabledChanged += Pull;
            Pull(this, EventArgs.Empty);
        }

        private string FormatValue(decimal value) => value.ToString(
            (source.ThousandsSeparator ? "N" : "F") + source.DecimalPlaces, CultureInfo.CurrentCulture);

        internal void SetCaption(string caption) => input.AccessibleName = AccessibleName = caption;

        private void Pull(object sender, EventArgs e)
        {
            if (syncing || disposing || IsDisposed || source.IsDisposed) return;
            syncing = true;
            try
            {
                input.Text = FormatValue(source.Value);
                Enabled = source.Enabled;
                input.ForeColor = Enabled ? SettingsDesignSurface.TextColor : SettingsDesignSurface.Muted;
                invalid = false;
                dirty = false;
            }
            finally { syncing = false; }
            Invalidate();
        }

        private bool Commit(bool restoreOnError)
        {
            if (disposing || IsDisposed || source.IsDisposed || !Enabled) return false;
            if (!dirty) { invalid = false; feedback.Hide(this); Invalidate(); return true; }
            string text = input.Text.Trim();
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal value) &&
                value >= source.Minimum && value <= source.Maximum &&
                value == decimal.Round(value, source.DecimalPlaces))
            {
                // Pull after assigning even if the original handler clamps the value.
                source.Value = value;
                Pull(this, EventArgs.Empty);
                feedback.Hide(this);
                return true;
            }
            string message = $"请输入 {FormatValue(source.Minimum)}–{FormatValue(source.Maximum)} 范围内、最多 {source.DecimalPlaces} 位小数的数字；原设置未改动。";
            if (restoreOnError) Pull(this, EventArgs.Empty);
            invalid = true;
            feedback.SetToolTip(this, message);
            if (IsHandleCreated && Visible) feedback.Show(message, this, 0, Height, 4000);
            Invalidate();
            return false;
        }

        private void Step(int direction)
        {
            if (!Enabled || !Commit(false)) return;
            decimal remaining = direction > 0 ? source.Maximum - source.Value : source.Value - source.Minimum;
            decimal change = Math.Min(source.Increment, remaining);
            source.Value += direction * change;
            Pull(this, EventArgs.Empty);
        }

        private void InputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers != Keys.None) return;
            switch (e.KeyCode)
            {
                case Keys.Enter: Commit(false); break;
                case Keys.Up: Step(1); break;
                case Keys.Down: Step(-1); break;
                case Keys.Escape: Pull(this, EventArgs.Empty); feedback.Hide(this); break;
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
        }

        protected override void OnLeave(EventArgs e)
        {
            Commit(true);
            base.OnLeave(e);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (input == null) return;
            input.SetBounds(S(9), Math.Max(S(3), (Height - input.PreferredHeight) / 2),
                Math.Max(1, Width - S(36)), input.PreferredHeight);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            input.Focus();
            if (e.X >= Width - S(26)) Step(e.Y < Height / 2 ? 1 : -1);
        }

        private void ScrollPage(MouseEventArgs e)
        {
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
            for (Control parent = Parent; parent != null; parent = parent.Parent)
                if (parent is SettingsDesignSurface surface) { surface.ScrollFromNumber(e); break; }
        }

        protected override void OnMouseWheel(MouseEventArgs e) => ScrollPage(e);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? SettingsDesignSurface.Canvas);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = MainMenuAppearance.Round(new Rectangle(0, 0, Width - 1, Height - 1), S(5));
            using var fill = new SolidBrush(SettingsDesignSurface.Input);
            e.Graphics.FillPath(fill, outline);
            Color border = invalid ? Color.FromArgb(238, 113, 104) : ContainsFocus ? SettingsDesignSurface.Orange : SettingsDesignSurface.Border;
            using var edge = new Pen(border, Math.Max(1, DeviceDpi / 96f));
            e.Graphics.DrawPath(edge, outline);
            using var arrow = new Pen(Enabled ? SettingsDesignSurface.Muted : Color.FromArgb(77, 94, 105), Math.Max(1, DeviceDpi / 96f))
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            int x = Width - S(13), d = S(3), up = Height / 3, down = Height * 2 / 3;
            e.Graphics.DrawLines(arrow, new[] { new Point(x - d, up + d / 2), new Point(x, up - d / 2), new Point(x + d, up + d / 2) });
            e.Graphics.DrawLines(arrow, new[] { new Point(x - d, down - d / 2), new Point(x, down + d / 2), new Point(x + d, down - d / 2) });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.disposing = true;
                source.ValueChanged -= Pull;
                source.EnabledChanged -= Pull;
                feedback.Dispose();
                if (ownsSource) source.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class NumberTextBox : TextBox
        {
            private readonly SettingsDesignNumber owner;
            internal NumberTextBox(SettingsDesignNumber owner) { this.owner = owner; }
            protected override void OnMouseWheel(MouseEventArgs e) => owner.ScrollPage(e);
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData is Keys.Enter or Keys.Escape or Keys.Up or Keys.Down)
                {
                    owner.InputKeyDown(this, new KeyEventArgs(keyData));
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }
    }
}
