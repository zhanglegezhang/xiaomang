using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private Control CreateVisibleModuleOptions()
        {
            var flow = new SettingsModuleOptions();
            Program.Settings.TodoSettings ??= new TodoSettings();
            foreach (var item in new[] { ("Todo", "待办"), ("Clipboard", "剪切板"), ("Favorites", "收藏夹"), ("News", "工具"), ("Diary", "日记") })
            {
                var check = new ModuleOption { Text = item.Item2, Tag = item.Item1, Name = "visibleModule_" + item.Item1,
                    Checked = Program.Settings.TodoSettings.IsModuleVisible(item.Item1), AutoCheck = true };
                check.AccessibleName = item.Item2;
                check.CheckedChanged += (_, _) =>
                {
                    Program.Settings.TodoSettings.VisibleModules = flow.Controls.OfType<ModuleOption>()
                        .Where(x => x.Checked).Select(x => (string)x.Tag).ToList();
                    Program.MainForm?.ApplyVisibleWidgetModules();
                    SettingManager.SaveApplicationConfigAsync();
                };
                flow.Controls.Add(check);
            }
            return flow;
        }

        private sealed class ModuleOption : CheckBox
        {
            private bool hover, pressed, keyboardFocus;
            private int S(int n) => (int)Math.Round(n * DeviceDpi / 96d);
            internal ModuleOption()
            {
                AutoSize = false; Appearance = Appearance.Button; Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Margin = Padding.Empty;
            }
            protected override void OnEnter(EventArgs e) { keyboardFocus = MouseButtons == MouseButtons.None; base.OnEnter(e); Invalidate(); }
            protected override void OnLeave(EventArgs e) { base.OnLeave(e); pressed = false; Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { keyboardFocus = false; pressed = AutoCheck && e.Button == MouseButtons.Left; base.OnMouseDown(e); Invalidate(); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
            protected override void OnKeyDown(KeyEventArgs e) { keyboardFocus = true; pressed = AutoCheck && e.KeyCode == Keys.Space; base.OnKeyDown(e); Invalidate(); }
            protected override void OnKeyUp(KeyEventArgs e) { pressed = false; base.OnKeyUp(e); Invalidate(); }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
                float r = S(4), w = Width - 1, h = Height - 1;
                using var path = new GraphicsPath();
                path.AddArc(0, 0, r * 2, r * 2, 180, 90); path.AddArc(w-r*2, 0, r*2, r*2, 270, 90);
                path.AddArc(w-r*2, h-r*2, r*2, r*2, 0, 90); path.AddArc(0, h-r*2, r*2, r*2, 90, 90); path.CloseFigure();
                Color background = Checked ? Color.FromArgb(66,48,36) : Color.FromArgb(34,46,54);
                if (hover) background = Checked ? Color.FromArgb(80,55,40) : Color.FromArgb(43,57,66);
                if (pressed) background = Checked ? Color.FromArgb(94,62,42) : Color.FromArgb(52,67,77);
                using var fill = new SolidBrush(background);
                using var border = new Pen(Checked ? Color.FromArgb(107,73,51) : hover ? Color.FromArgb(82,97,107) : background);
                g.FillPath(fill,path); g.DrawPath(border,path);
                Color foreground = Checked ? Color.FromArgb(255,154,85) : Color.FromArgb(184,197,206);
                int offset = pressed ? S(1) : 0;
                var textBounds = new Rectangle(0,offset,Width,Height-offset);
                if (!AutoCheck)
                {
                    int textWidth = TextRenderer.MeasureText(g,Text,Font,Size.Empty,TextFormatFlags.NoPadding).Width;
                    int lockWidth = S(10), gap = S(5), left = (Width-textWidth-lockWidth-gap)/2;
                    int top = (Height-S(11))/2;
                    using var pen = new Pen(foreground,Math.Max(1,DeviceDpi/96f));
                    g.DrawArc(pen,left+S(2),top, S(6),S(6),180,180);
                    g.DrawLine(pen,left+S(2),top+S(3),left+S(2),top+S(5));
                    g.DrawLine(pen,left+S(8),top+S(3),left+S(8),top+S(5));
                    g.DrawRectangle(pen,left,top+S(5),lockWidth,S(6));
                    textBounds = new Rectangle(left+lockWidth+gap,0,textWidth,Height);
                }
                TextRenderer.DrawText(g,Text,Font,textBounds,foreground,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding);
                if(Focused && keyboardFocus) { using var focus = new Pen(Color.FromArgb(255,172,112)); g.DrawRectangle(focus,S(3),S(3),Width-S(6)-1,Height-S(6)-1); }
            }
        }
    }
    internal sealed class SettingsModuleOptions : Panel
    {
        internal SettingsModuleOptions() { Margin = Padding.Empty; DoubleBuffered = true; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Controls.Count == 0) return;
            int gap = (int)Math.Round(6 * DeviceDpi / 96d);
            int available = Math.Max(Controls.Count, ClientSize.Width - gap * (Controls.Count - 1));
            for (int i=0;i<Controls.Count;i++)
            {
                int left = available*i/Controls.Count + gap*i;
                int right = available*(i+1)/Controls.Count + gap*i;
                Controls[i].SetBounds(left,0,right-left,ClientSize.Height);
            }
        }
    }
    public partial class MainForm
    {
        internal void ApplyVisibleWidgetModules() { if (todoWidget != null && !todoWidget.IsDisposed) todoWidget.ApplyVisibleModules(); }
    }
}
