using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ShareX
{
    // Presentation only: existing editors and their event handlers are retained.
    // Rows are measured once per width/font/DPI change, never by polling.
    internal sealed class SettingsDesignSurface : Panel
    {
        internal static readonly Color Canvas = Color.FromArgb(25,34,41), TextColor = Color.FromArgb(228,234,240),
            Muted = Color.FromArgb(153,169,181), Border = Color.FromArgb(51,65,77), Input = Color.FromArgb(32,43,51), Orange = Color.FromArgb(255,135,62);
        private readonly Panel content = new() { Name = "settingsDesignContent" };
        private readonly List<(Control Control, Func<int,int> Measure)> rows = new();
        private bool fitting, initialized, layoutQueued;
        private int dpiLayoutDepth;
        private readonly HashSet<ComboBox> styledCombos = new();
        private readonly ToolTip tips = new();
        private readonly List<Font> ownedFonts = new();
        private readonly HashSet<Control> hiddenRows = new();
        private bool hasSection;
        internal bool RightAlignedToggles { get; set; }
        internal bool RightAlignedActions { get; set; }
        internal int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        internal SettingsDesignSurface(string title)
        {
            Name="settingsDesign"; Dock=DockStyle.Fill; AutoScroll=true; BackColor=Canvas;
            Font=new Font("Microsoft YaHei UI",8f); ForeColor=TextColor;
            ownedFonts.Add(Font);
            DoubleBuffered=true; content.BackColor=Canvas; Controls.Add(content);
            AddText(title,11,true,10);
        }
        internal static IEnumerable<Control> All(Control root) => new[]{root}.Concat(root.Controls.Cast<Control>().SelectMany(All));
        internal void Finish() {
            Style(content);
            foreach(var frame in All(content).OfType<SettingsDesignField>())frame.Restyle();
            foreach(var button in All(content).OfType<Button>()){MainMenuAppearance.Apply(button);button.Font=Font;}
            foreach(var combo in All(content).OfType<ComboBox>())if(styledCombos.Add(combo)){
                combo.DrawMode=DrawMode.OwnerDrawFixed;
                combo.ItemHeight=Math.Max(combo.Font.Height+S(6),S(24));
                combo.DrawItem+=(_,e)=>{
                    using var brush=new SolidBrush((e.State&DrawItemState.Selected)!=0?Border:Input);e.Graphics.FillRectangle(brush,e.Bounds);
                    string text=e.Index>=0&&e.Index<combo.Items.Count?combo.GetItemText(combo.Items[e.Index]):combo.Text;
                    var bounds=e.Bounds;if((e.State&DrawItemState.ComboBoxEdit)!=0)bounds.Width=Math.Min(bounds.Width,Math.Max(1,combo.Width-S(combo.Name=="cbToastWindowPlacement"?21:34)));
                    TextRenderer.DrawText(e.Graphics,text,combo.Font,bounds,combo.Enabled?TextColor:Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
                    e.DrawFocusRectangle();
                };
            }
            initialized=true;Fit();
        }
        internal void Hint(string text) => AddText(text,8,false,8);
        internal void Section(string text)
        {
            var heading=new SectionHeading{Text=text,Font=new Font(Font.FontFamily,10f,FontStyle.Bold),First=!hasSection};
            ownedFonts.Add(heading.Font);hasSection=true;
            Add(heading,width=>TextRenderer.MeasureText(text,heading.Font,new Size(width,int.MaxValue),TextFormatFlags.WordBreak).Height+S(heading.First?22:32));
        }
        private void AddText(string text,float size,bool bold,int gap)
        {
            var label=new Label { Text=text,AutoSize=false,ForeColor=bold?TextColor:Muted, BackColor=Canvas,
                Font=new Font(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular) };
            ownedFonts.Add(label.Font);
            Add(label,width=>Math.Max(S(24),TextRenderer.MeasureText(text,label.Font,new Size(width,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height)+S(gap));
        }
        private void Add(Control control,Func<int,int> measure)
        { control.TabIndex=rows.Count;content.Controls.Add(control); rows.Add((control,measure)); }
        private Control Prepare(Control editor)
        {
            if(editor is NumericUpDown number)return new SettingsDesignNumber(number);
            if(editor is TextBoxBase || editor is ComboBox)return new SettingsDesignField(editor);
            return editor;
        }
        internal Control Editor(string title,Control editor,string hint=null,int width=300,string description=null)
        {
            if(editor is NumericUpDown)width=104;
            editor=Prepare(editor);
            var row=new SettingRow(this,title,editor,width,description); Add(row,row.Measure);
            if(hint!=null)tips.SetToolTip(row.Caption,hint);
            if(editor is Label || editor is SettingsDesignStatus)editor.TextChanged+=(_,_)=>RequestFit();
            foreach(var text in All(editor).OfType<TextBoxBase>().Where(t=>t.ReadOnly)){
                tips.SetToolTip(text,text.Text);text.TextChanged+=(_,_)=>tips.SetToolTip(text,text.Text);
            }
            return row;
        }
        internal void Toggle(string title,CheckBox source,string hint=null)
        {
            Editor(title,new SettingsDesignSwitch(source),null,54);
            var row=(SettingRow)rows[^1].Control;LinkEnabled(row.Caption,source);
            tips.SetToolTip(row.Caption,hint??source.Text.Replace("&",""));
        }
        internal void Action(string title,Button button,string text=null)
        { if(text!=null)button.Text=text;button.AutoSize=false;Editor(title,button,null,Math.Max(112,TextRenderer.MeasureText(button.Text,Font).Width*96/DeviceDpi+28)); }
        internal void Related(string title,CheckBox source,params Control[] editors)
        {
            var field=Inline(new[]{(Control)new SettingsDesignSwitch(source)}.Concat(editors).ToArray());
            var row=(SettingRow)Editor(title,field);LinkEnabled(row.Caption,source);
        }
        internal void Pair(string leftTitle,Control left,string rightTitle,Control right)
        {
            var pair=new PairRow(this,new SettingRow(this,leftTitle,Prepare(left),260),new SettingRow(this,rightTitle,Prepare(right),260));
            Add(pair,pair.Measure);
        }
        internal void SetRowVisible(Control row,bool visible)
        {
            if(IsDisposed)return;if(visible)hiddenRows.Remove(row);else hiddenRows.Add(row);
            row.Visible=visible;RequestFit();
        }
        internal Control CompactPair(string leftTitle,Control left,string rightTitle,Control right)
        {
            left=Prepare(left);right=Prepare(right);
            var row=new Panel{BackColor=Canvas,Name="notificationPair"};
            var a=Unit(leftTitle);var b=Unit(rightTitle);
            a.ForeColor=b.ForeColor=TextColor;
            row.Controls.AddRange(new[]{a,left,b,right});
            foreach(var field in new[]{left,right}){
                foreach(var number in All(field).OfType<SettingsDesignNumber>())number.Width=S(62);
                if(field is FlowLayoutPanel flow){flow.WrapContents=false;foreach(Control child in flow.Controls)child.Margin=new Padding(0,S(child is Label?10:3),S(5),0);}
            }
            Add(row,width=>{
                int half=(width-S(18))/2,label=S(66),fieldWidth=Math.Max(S(80),half-label);
                a.SetBounds(0,S(10),label,S(24));b.SetBounds(half+S(18),S(10),label,S(24));
                left.SetBounds(label,S(4),left is SettingsDesignField ? S(62) : fieldWidth,S(38));right.SetBounds(half+S(18)+label,S(4),fieldWidth,S(38));
                return S(47);
            });
            return row;
        }
        internal FlowLayoutPanel Inline(params Control[] editors)
        {
            var panel=new FlowLayoutPanel { WrapContents=true,AutoSize=false,BackColor=Canvas,Margin=Padding.Empty };
            foreach(var original in editors){var editor=Prepare(original);editor.Dock=DockStyle.None;editor.Anchor=AnchorStyles.Top|AnchorStyles.Left;editor.Margin=new Padding(0,S(3),S(8),S(3));
                if(editor is SettingsDesignField frame)editor.Size=new Size(S(220),frame.MeasureHeight());
                else if(editor is SettingsDesignNumber)editor.Size=new Size(S(104),S(32));
                else if(editor is SettingsDesignSwitch)editor.Size=new Size(S(54),S(28));
                else if(editor is Label label){label.AutoSize=true;label.MaximumSize=Size.Empty;label.Margin=new Padding(0,S(7),S(8),0);}
                else if(editor is Button button){button.AutoSize=false;button.Size=new Size(S(Math.Max(84,button.Text.Length*14+24)),S(32));}
                panel.Controls.Add(editor);
            }
            panel.Layout+=(_,_)=>{if(initialized&&!fitting)RequestFit();};
            return panel;
        }
        internal Label Unit(string text)=>new(){Text=text,ForeColor=Muted,AutoSize=true};
        internal void MirrorCaption(Label source)
        {
            var caption=((SettingRow)rows[^1].Control).Caption;caption.Text=source.Text;
            EventHandler update=(_,_)=>{caption.Text=source.Text;RequestFit();};source.TextChanged+=update;
            caption.Disposed+=(_,_)=>source.TextChanged-=update;
        }
        internal void LinkEnabled(Control field,Control originalGroup)
        {
            field.Enabled=originalGroup.Enabled;
            EventHandler changed=(_,_)=>{if(!field.IsDisposed)field.Enabled=originalGroup.Enabled;};
            originalGroup.EnabledChanged+=changed;
            field.Disposed+=(_,_)=>originalGroup.EnabledChanged-=changed;
        }
        private void RequestFit()
        {
            if(layoutQueued||dpiLayoutDepth>0||!Visible||!IsHandleCreated||IsDisposed)return;
            layoutQueued=true;
            BeginInvoke((Action)(()=>{layoutQueued=false;if(!IsDisposed)PerformLayout();}));
        }
        protected override void OnLayout(LayoutEventArgs e){if(Visible&&dpiLayoutDepth==0)Fit();base.OnLayout(e);}
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) RequestFit();
        }
        internal void BeginDpiLayout() => dpiLayoutDepth++;
        internal void EndDpiLayout()
        {
            if (dpiLayoutDepth > 0 && --dpiLayoutDepth == 0 && Visible) PerformLayout();
        }
        internal void ScrollFromNumber(MouseEventArgs e) => OnMouseWheel(new MouseEventArgs(e.Button,e.Clicks,e.X,e.Y,e.Delta));
        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            if (Visible) RefreshDpiLayout();
        }
        internal void RefreshDpiLayout()
        {
            SuspendLayout(); content.SuspendLayout();
            try
            {
                foreach (var button in All(content).OfType<Button>())
                    if (!button.Font.Equals(Font)) button.Font = Font;
                foreach (var combo in styledCombos)
                    if (!combo.IsDisposed) combo.ItemHeight = Math.Max(combo.Font.Height + S(6), S(24));
            }
            finally { content.ResumeLayout(false); ResumeLayout(false); }
            RequestFit();
        }
        private void Fit()
        {
            if(!initialized||fitting||content==null||ClientSize.Width<80)return;fitting=true;
            try {
                int margin=S(22),width=Math.Max(1,(Parent is SettingsDesignHost host?host.ClientSize.Width:ClientSize.Width)-margin*2-S(12));
                int y=0;
                foreach(var row in rows){if(hiddenRows.Contains(row.Control))continue;int height=row.Measure(width);row.Control.SetBounds(0,y,width,height);y+=height;}
                content.Bounds=new Rectangle(margin+AutoScrollPosition.X,S(14)+AutoScrollPosition.Y,width,y);
                AutoScrollMinSize=new Size(0,y+S(30));
                (Parent as SettingsDesignHost)?.RefreshRail();
            }finally{fitting=false;}
        }
        internal static void Style(Control root)
        {
            foreach(var control in All(root)) {
                control.ForeColor=control.Tag as string=="settings-help"||control is Label label&&label.Font.Style==FontStyle.Regular&&control.Parent is not SettingRow?Muted:TextColor;
                if(control is TextBoxBase text){text.BackColor=text.ReadOnly?Canvas:Input;text.BorderStyle=BorderStyle.None;}
                else if(control is ComboBox combo){combo.BackColor=Input;combo.ForeColor=TextColor;combo.FlatStyle=FlatStyle.Flat;}
                else if(control is NumericUpDown number){number.BackColor=Input;number.ForeColor=TextColor;number.BorderStyle=BorderStyle.FixedSingle;}
                else if(control is Button button){button.BackColor=Input;button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Border;button.FlatAppearance.BorderSize=1;button.UseVisualStyleBackColor=false;}
                else control.BackColor=control is SettingsDesignNumber?Input:Canvas;
            }
        }
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){tips.Dispose();foreach(var font in ownedFonts)font.Dispose();ownedFonts.Clear();}}
        private sealed class SettingRow:Panel
        {
            private readonly SettingsDesignSurface surface;private readonly Control field;private readonly int desired;
            private readonly Label below;
            internal bool Compact;
            internal Label Caption {get;}
            internal SettingRow(SettingsDesignSurface surface,string title,Control field,int desired,string description=null)
            {
                this.surface=surface;this.field=field;this.desired=field is SettingsDesignNumber?104:desired;
                Caption=new Label{Text=title,AutoSize=false,ForeColor=TextColor};
                foreach(var control in All(field)){control.Font=surface.Font;if(control is SettingsDesignNumber number)number.SetCaption(title);}
                field.Dock=DockStyle.None;field.Anchor=AnchorStyles.Top|AnchorStyles.Left;field.MaximumSize=Size.Empty;field.MinimumSize=Size.Empty;
                if(field is Label label){label.AutoSize=false;label.AutoEllipsis=false;}
                Controls.Add(Caption);Controls.Add(field);DoubleBuffered=true;
                if(description!=null){below=new Label{Text=description,ForeColor=Muted,Tag="settings-help"};Controls.Add(below);}
            }
            internal int Measure(int width)
            {
                int S(int v)=>surface.S(v);bool toggle=field is SettingsDesignSwitch;
                bool stacked=!toggle&&width<S(Compact?300:460);bool empty=string.IsNullOrEmpty(Caption.Text);
                if(field is SettingsModuleOptions) stacked = width-Math.Min(S(245),(int)(width*.37))-S(14)<S(400);
                int labelWidth=empty?0:toggle&&surface.RightAlignedToggles?Math.Max(1,width-S(76)):stacked?width:Math.Min(S(245),(int)(width*.37));
                int labelHeight=Math.Max(S(23),TextRenderer.MeasureText(Caption.Text,Font,new Size(labelWidth,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height);
                Caption.Visible=!empty;Caption.Bounds=new Rectangle(0,S(8),labelWidth,labelHeight);
                int available=stacked||empty?width:Math.Max(1,width-labelWidth-S(14));
                int target=field is SettingsDesignSwitch?S(54):field is SettingsDesignField frame?(frame.Flexible?available:Math.Min(available,S(desired))):field is TextBoxBase||field is Label||field is FlowLayoutPanel||field is ProgressBar||field is SettingsDesignStatus?available:Math.Min(available,S(desired));
                if(field is SettingsModuleOptions) target = stacked ? available : S(400);
                field.MaximumSize=Size.Empty;
                if(field is FlowLayoutPanel inline){
                    inline.Width=target;
                    var texts=inline.Controls.OfType<SettingsDesignField>().Where(f=>f.Flexible).ToArray();
                    if(texts.Length==1){int other=inline.Controls.Cast<Control>().Where(c=>c!=texts[0]).Sum(c=>c.Width+c.Margin.Horizontal);texts[0].Width=Math.Max(S(70),target-other-texts[0].Margin.Horizontal);}
                    var number=inline.Controls.OfType<SettingsDesignNumber>().FirstOrDefault();
                    foreach(var label in inline.Controls.OfType<Label>()){
                        label.MaximumSize=new Size(Math.Max(1,target-label.Margin.Horizontal),0);
                        if(number!=null)label.Margin=new Padding(0,Math.Max(S(3),number.Margin.Top+(number.Height-label.PreferredHeight)/2),S(8),0);
                    }
                    inline.PerformLayout();
                }
                int h=field is SettingsModuleOptions?S(30):field is SettingsDesignSwitch?S(28):field is SettingsDesignNumber?S(32):field is SettingsDesignField framed?framed.MeasureHeight():field is TextBoxBase text&&text.Multiline?S(84):field is FlowLayoutPanel flow?Math.Max(S(32),flow.GetPreferredSize(new Size(target,0)).Height):field is SettingsDesignStatus?Math.Max(S(28),field.GetPreferredSize(new Size(target,0)).Height):field is Label l?Math.Max(S(26),TextRenderer.MeasureText(l.Text,l.Font,new Size(target,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height):S(32);
                int x=toggle&&surface.RightAlignedToggles||field is Button&&surface.RightAlignedActions?width-target:stacked||empty?0:labelWidth+S(14),y=stacked&&!empty?Caption.Bottom+S(4):S(4);
                field.Bounds=new Rectangle(x,y,target,h);

                int bottom=Math.Max(empty?0:Caption.Bottom,field.Bottom);
                if(below!=null){int bh=TextRenderer.MeasureText(below.Text,below.Font,new Size(target,int.MaxValue),TextFormatFlags.WordBreak).Height;below.Bounds=new Rectangle(x,field.Bottom+S(5),target,bh);bottom=below.Bottom;}
                return Math.Max(S(38),bottom+S(5));
            }
        }
        private sealed class PairRow:Panel
        {
            private readonly SettingsDesignSurface surface;private readonly SettingRow left,right;
            internal PairRow(SettingsDesignSurface surface,SettingRow left,SettingRow right){this.surface=surface;this.left=left;this.right=right;left.Compact=right.Compact=true;Controls.Add(left);Controls.Add(right);}
            internal int Measure(int width){int gap=surface.S(24);bool columns=width>=surface.S(680);int w=columns?(width-gap)/2:width;int lh=left.Measure(w),rh=right.Measure(w);left.SetBounds(0,0,w,lh);right.SetBounds(columns?w+gap:0,columns?0:lh,w,rh);return columns?Math.Max(lh,rh):lh+rh;}
        }
        private sealed class SectionHeading:Control
        {
            internal bool First;
            protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);int top=(int)Math.Round((First?1:10)*DeviceDpi/96d);using var pen=new Pen(Border);e.Graphics.DrawLine(pen,0,top,Width,top);TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(0,top+(int)Math.Round(10*DeviceDpi/96d),Width,Height-top),TextColor,TextFormatFlags.Top|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);}
        }
    }

    internal sealed class SettingsDesignSwitch:CheckBox
    {
        private readonly CheckBox source;private bool syncing;
        internal SettingsDesignSwitch(CheckBox source)
        {
            this.source=source;Name="switch_"+source.Name;AccessibleName=source.Text;AccessibleRole=AccessibleRole.CheckButton;
            Appearance=Appearance.Button;AutoSize=false;FlatStyle=FlatStyle.Flat;Cursor=Cursors.Hand;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            Checked=source.Checked;Enabled=source.Enabled;
            CheckedChanged+=Push;source.CheckedChanged+=Pull;source.EnabledChanged+=Pull;
        }
        private void Push(object s,EventArgs e){if(syncing)return;try{syncing=true;source.Checked=Checked;Enabled=source.Enabled;}finally{syncing=false;}Invalidate();}
        private void Pull(object s,EventArgs e){if(syncing)return;try{syncing=true;Checked=source.Checked;Enabled=source.Enabled;}finally{syncing=false;}Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SettingsDesignSurface.Canvas);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            int pad=Math.Max(2,Height/10),height=Height-pad*2,width=Math.Min(Width-2,height*2);
            Rectangle bounds=new(1,pad,width,height);using var shape=new GraphicsPath();shape.AddArc(bounds.X,bounds.Y,height,height,90,180);shape.AddArc(bounds.Right-height,bounds.Y,height,height,270,180);shape.CloseFigure();
            using var fill=new SolidBrush(!Enabled?Color.FromArgb(48,57,64):Checked?SettingsDesignSurface.Orange:Color.FromArgb(65,82,94));e.Graphics.FillPath(fill,shape);
            int knob=height-6;using var knobBrush=new SolidBrush(Enabled?Color.White:SettingsDesignSurface.Muted);e.Graphics.FillEllipse(knobBrush,Checked?bounds.Right-knob-3:bounds.X+3,bounds.Y+3,knob,knob);
            if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle,SettingsDesignSurface.TextColor,SettingsDesignSurface.Canvas);
        }
        protected override void Dispose(bool disposing){if(disposing){source.CheckedChanged-=Pull;source.EnabledChanged-=Pull;}base.Dispose(disposing);}
    }
}


