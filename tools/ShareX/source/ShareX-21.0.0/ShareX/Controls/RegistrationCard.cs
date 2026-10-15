using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class RegistrationCard : TableLayoutPanel
    {
        internal RegistrationCard()
        {
            DoubleBuffered=true; AutoSize=true; AutoSizeMode=AutoSizeMode.GrowAndShrink;
            ColumnCount=1; ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            Tag="registration-surface";
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(MainMenuAppearance.Background);
            if(Width<2||Height<2)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using var path=MainMenuAppearance.Round(new Rectangle(0,0,Width-1,Height-1),MainMenuAppearance.Scale(this,8));
            using var brush=new SolidBrush(MainMenuAppearance.Surface);
            using var pen=new Pen(MainMenuAppearance.Edge);
            e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);
        }
    }

    internal sealed class RegistrationStatus : Label
    {
        internal bool Ready {get;private set;}
        internal RegistrationStatus(){AutoSize=false;Tag="registration-surface";TextAlign=ContentAlignment.MiddleCenter;}
        internal void UpdateState(string text,bool ready){Text=text;Ready=ready;Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(MainMenuAppearance.Background);
            if(Width<2||Height<2)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using var path=MainMenuAppearance.Round(new Rectangle(0,0,Width-1,Height-1),Height/2);
            using var brush=new SolidBrush(Ready?Color.FromArgb(39,78,54):MainMenuAppearance.Selected);
            e.Graphics.FillPath(brush,path);
            TextRenderer.DrawText(e.Graphics,(Ready?"✓ ":"")+Text,Font,ClientRectangle,Ready?Color.FromArgb(170,231,181):MainMenuAppearance.Text,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class RegistrationGlyph : Control
    {
        internal string Glyph {get;set;}
        internal RegistrationGlyph(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Tag="registration-surface";TabStop=false;}
        protected override void OnPaint(PaintEventArgs e)=>MainMenuRenderer.DrawGlyph(e.Graphics,Glyph,ClientRectangle,MainMenuAppearance.Text);
    }

    // Keep status and primary actions aligned; narrow pages wrap actions below the name.
    internal sealed class RegistrationComponentRow : Panel
    {
        private readonly Control glyph,title,status,actions;
        private bool arranging;
        internal RegistrationComponentRow(Control glyph,Control title,Control status,FlowLayoutPanel actions)
        {
            this.glyph=glyph;this.title=title;this.status=status;this.actions=actions;
            Tag="registration-surface";DoubleBuffered=true;
            Controls.AddRange(new[]{glyph,title,status,actions});
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);if(arranging||Width<80)return;
            arranging=true;
            try
            {
                int S(int n)=>MainMenuAppearance.Scale(this,n);
                bool narrow=Width<S(470);
                int actionWidth=narrow?Width:S(280),statusWidth=S(88),nameStart=S(34);
                int statusLeft=narrow?Width-statusWidth:Width-actionWidth-statusWidth-S(16);
                glyph.SetBounds(0,S(10),S(23),S(23));
                title.SetBounds(nameStart,0,Math.Max(1,statusLeft-nameStart-S(8)),S(44));
                status.SetBounds(statusLeft,S(8),statusWidth,S(28));
                actions.MaximumSize=new Size(actionWidth,0);
                actions.Width=actionWidth;
                int actionHeight=Math.Max(S(34),actions.GetPreferredSize(new Size(actionWidth,0)).Height);
                actions.SetBounds(narrow?0:Width-actionWidth,narrow?S(44):S(5),actionWidth,actionHeight);
                Height=Math.Max(S(40),actions.Bottom+S(2));
            }
            finally{arranging=false;}
        }
    }
}
