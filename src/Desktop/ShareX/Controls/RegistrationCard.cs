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
            using var path=MainMenuAppearance.Round(new Rectangle(0,0,Width-1,Height-1),MainMenuAppearance.Scale(this,4));
            using var brush=new SolidBrush(Ready?Color.FromArgb(27,53,40):MainMenuAppearance.Selected);
            e.Graphics.FillPath(brush,path);
            TextRenderer.DrawText(e.Graphics,(Ready?"✓ ":"")+Text,Font,ClientRectangle,Ready?Color.FromArgb(170,231,181):MainMenuAppearance.Text,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class RegistrationGlyph : Control
    {
        internal string Glyph {get;set;}
        internal RegistrationGlyph(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Tag="registration-surface";TabStop=false;}
        protected override void OnPaint(PaintEventArgs e)
        {
            if(Glyph!="mouse"){MainMenuRenderer.DrawGlyph(e.Graphics,Glyph,ClientRectangle,MainMenuAppearance.Text);return;}
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using var pen=new Pen(MainMenuAppearance.Text,Math.Max(1,Width/16f));
            using var path=MainMenuAppearance.Round(new Rectangle(Width/5,1,Width*3/5,Height-3),Width/4);
            e.Graphics.DrawPath(pen,path);e.Graphics.DrawLine(pen,Width/2,2,Width/2,Height/3);
        }
    }

    internal sealed class RegistrationHelp : Control
    {
        internal RegistrationHelp(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Tag="registration-surface";Cursor=Cursors.Default;TabStop=false;}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            int size=Math.Max(4,Math.Min(Width,Height)-4-MainMenuAppearance.Scale(this,3)),x=(Width-size)/2,y=(Height-size)/2;
            using var pen=new Pen(MainMenuAppearance.Muted,Math.Max(1,DeviceDpi/96f));
            e.Graphics.DrawEllipse(pen,x,y,size,size);
            TextRenderer.DrawText(e.Graphics,"!",Font,new Rectangle(x,y,size,size),MainMenuAppearance.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        }
    }
    // Keep status and primary actions aligned; narrow pages wrap actions below the name.
    internal sealed class RegistrationComponentRow : Panel
    {
        private readonly Control glyph,title,status,actions,help;
        private bool arranging;
        internal RegistrationComponentRow(Control glyph,Control title,Control status,FlowLayoutPanel actions,Control help)
        {
            this.glyph=glyph;this.title=title;this.status=status;this.actions=actions;this.help=help;
            Tag="registration-surface";DoubleBuffered=true;
            Controls.AddRange(new[]{glyph,title,status,actions,help});
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);if(arranging||Width<80)return;
            arranging=true;
            try
            {
                int S(int n)=>MainMenuAppearance.Scale(this,n);
                bool narrow=Width<S(540);
                int actionWidth=narrow?Width:S(270),statusWidth=S(88),nameStart=S(34);
                int statusLeft=narrow?Width-statusWidth:Width-actionWidth-statusWidth-S(16);
                glyph.SetBounds(0,S(10),S(23),S(23));
                int available=Math.Max(1,statusLeft-nameStart-S(8));
                int textWidth=Math.Min(Math.Max(1,available-S(22)),TextRenderer.MeasureText(title.Text,title.Font).Width);
                title.SetBounds(nameStart,0,textWidth,S(44));
                help.SetBounds(title.Right+S(2),S(12),S(18),S(20));
                status.SetBounds(statusLeft,S(8),statusWidth,S(28));
                actions.MaximumSize=new Size(actionWidth,0);
                actions.Width=actionWidth;
                int actionHeight=Math.Max(S(34),actions.GetPreferredSize(new Size(actionWidth,0)).Height);
                actions.SetBounds(narrow?0:Width-actionWidth,narrow?S(44):S(5),actionWidth,actionHeight);
                Height=Math.Max(S(52),actions.Bottom+S(8));
            }
            finally{arranging=false;}
        }
    }
}
