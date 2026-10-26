using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX
{
    // Reparents the real editor: its data bindings, validation, native drop-down,
    // keyboard navigation, password mode and variable menu remain intact.
    internal sealed class SettingsDesignField : Panel
    {
        internal Control Editor { get; }
        internal bool Flexible => Editor is TextBoxBase;
        private readonly Panel viewport;
        private readonly DropArrow arrow;
        private readonly ComboWheelGuard wheelGuard;
        private readonly bool plain;
        private bool arranging;
        private int S(int n) => (int)Math.Round(n * DeviceDpi / 96d);

        internal SettingsDesignField(Control editor)
        {
            Editor = editor;
            Name = "frame_" + editor.Name;
            plain = editor is TextBoxBase text && text.ReadOnly;
            DoubleBuffered = true;
            Size = new Size(S(300), S(32));
            viewport = new Panel { BackColor = plain ? SettingsDesignSurface.Canvas : SettingsDesignSurface.Input };
            Controls.Add(viewport);
            editor.Dock = DockStyle.None;
            editor.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            editor.MaximumSize = editor.MinimumSize = Size.Empty;
            if (editor is TextBoxBase input)
            {
                input.BorderStyle = BorderStyle.None;
                input.AutoSize = false;
                input.BackColor = viewport.BackColor;
            }
            viewport.Controls.Add(editor);
            if (editor is ComboBox combo)
            {
                arrow = new DropArrow(this, combo);
                Controls.Add(arrow);
                wheelGuard = new ComboWheelGuard(this, combo);
                combo.EnabledChanged += Changed;
            }
            editor.Enter += Changed;
            editor.Leave += Changed;
            editor.EnabledChanged += Changed;
            editor.LocationChanged += EditorLayoutChanged;
            editor.SizeChanged += EditorLayoutChanged;
        }
        private void EditorLayoutChanged(object sender,EventArgs e){if(!arranging&&!Disposing&&!IsDisposed)PerformLayout();}
        private void Changed(object sender, EventArgs e) { Invalidate(); arrow?.Invalidate(); }
        private void ScrollPage(int delta)
        {
            for (Control parent = Parent; parent != null; parent = parent.Parent)
                if (parent is SettingsDesignSurface surface)
                {
                    surface.ScrollFromNumber(new MouseEventArgs(MouseButtons.None, 0, 0, 0, delta));
                    break;
                }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Editor is ComboBox combo && !combo.DroppedDown)
            {
                if (e is HandledMouseEventArgs handled) handled.Handled = true;
                ScrollPage(e.Delta);
            }
            else base.OnMouseWheel(e);
        }
        internal int MeasureHeight() => Editor is TextBoxBase text && text.Multiline ? S(84) : S(32);
        internal void Restyle(){viewport.BackColor=plain?SettingsDesignSurface.Canvas:SettingsDesignSurface.Input;Editor.BackColor=viewport.BackColor;if(Editor is TextBoxBase text)text.BorderStyle=BorderStyle.None;}
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (viewport == null || arranging) return;
            arranging=true;
            try {
            bool combo = Editor is ComboBox;
            bool compact = Editor.Name == "cbToastWindowPlacement";
            int pad = plain ? 0 : S(9);
            viewport.SetBounds(combo ? S(3) : pad, S(3), Math.Max(1, Width - (combo ? S(compact ? 20 : 33) : pad * 2)), Math.Max(1, Height - S(6)));
            if (combo)
            {
                Editor.SetBounds(-S(2), (viewport.Height - Editor.PreferredSize.Height) / 2,
                    Math.Max(1,Width-S(2)), Editor.PreferredSize.Height);
                arrow?.SetBounds(Math.Max(0, Width - S(compact ? 19 : 28)), S(3), S(compact ? 16 : 25), Math.Max(1, Height - S(6)));
                arrow?.BringToFront();
            }
            else
            {
                var text = (TextBoxBase)Editor;
                int height = text.Multiline ? viewport.Height : Math.Min(viewport.Height, text.PreferredSize.Height);
                text.SetBounds(0, Math.Max(0, (viewport.Height - height) / 2), viewport.Width, height);
            }
            }finally{arranging=false;}
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SettingsDesignSurface.Canvas);
            if (plain || Width < 3 || Height < 3) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = MainMenuAppearance.Round(new Rectangle(0, 0, Width - 1, Height - 1), S(5));
            using var fill = new SolidBrush(SettingsDesignSurface.Input);
            using var edge = new Pen(ContainsFocus ? SettingsDesignSurface.Orange : SettingsDesignSurface.Border);
            e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(edge, path);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { wheelGuard?.Dispose();Editor.LocationChanged-=EditorLayoutChanged;Editor.SizeChanged-=EditorLayoutChanged;Editor.Enter -= Changed; Editor.Leave -= Changed; Editor.EnabledChanged -= Changed; if(Editor is ComboBox combo)combo.EnabledChanged -= Changed; }
            base.Dispose(disposing);
        }
        // Intercept before the native combo changes selection. The hook follows
        // handle recreation and only belongs to this settings editor.
        private sealed class ComboWheelGuard : NativeWindow, IDisposable
        {
            private readonly SettingsDesignField owner;
            private readonly ComboBox combo;
            internal ComboWheelGuard(SettingsDesignField owner, ComboBox combo)
            {
                this.owner = owner; this.combo = combo;
                combo.HandleCreated += HandleCreated;
                combo.HandleDestroyed += HandleDestroyed;
                if (combo.IsHandleCreated) AssignHandle(combo.Handle);
            }
            private void HandleCreated(object sender, EventArgs e) => AssignHandle(combo.Handle);
            private void HandleDestroyed(object sender, EventArgs e) { if (Handle != IntPtr.Zero) ReleaseHandle(); }
            protected override void WndProc(ref Message m)
            {
                const int WM_MOUSEWHEEL = 0x020A;
                if (m.Msg == WM_MOUSEWHEEL && !combo.DroppedDown)
                {
                    owner.ScrollPage(unchecked((short)(m.WParam.ToInt64() >> 16)));
                    m.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref m);
            }
            public void Dispose()
            {
                combo.HandleCreated -= HandleCreated;
                combo.HandleDestroyed -= HandleDestroyed;
                if (Handle != IntPtr.Zero) ReleaseHandle();
            }
        }
        private sealed class DropArrow : Control
        {
            private readonly SettingsDesignField owner;
            private readonly ComboBox combo;
            internal DropArrow(SettingsDesignField owner, ComboBox combo) { this.owner = owner; this.combo = combo; DoubleBuffered = true; Cursor = Cursors.Hand; TabStop = false; }
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (!combo.DroppedDown)
                {
                    if (e is HandledMouseEventArgs handled) handled.Handled = true;
                    owner.ScrollPage(e.Delta);
                }
                else base.OnMouseWheel(e);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(SettingsDesignSurface.Input); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int d = Math.Max(3, (int)Math.Round(3 * DeviceDpi / 96d)), x = Width / 2, y = Height / 2;
                using var pen = new Pen(combo.Enabled ? SettingsDesignSurface.Muted : Color.FromArgb(70, 84, 95), Math.Max(1, DeviceDpi / 96f));
                e.Graphics.DrawLines(pen, new[] { new Point(x - d, y - d / 2), new Point(x, y + d / 2), new Point(x + d, y - d / 2) });
            }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if(e.Button == MouseButtons.Left && combo.Enabled) { combo.Focus(); combo.DroppedDown = !combo.DroppedDown; } }
        }
    }
}
