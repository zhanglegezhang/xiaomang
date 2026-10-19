using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class SettingsDesignStatus : Control
    {
        private readonly Label source;
        private bool installed;
        internal SettingsDesignStatus(Label source)
        {
            this.source=source;installed=source.ForeColor.G>source.ForeColor.R;Text=source.Text;Name="settingsModelStatus";DoubleBuffered=true;
            source.TextChanged+=Changed;source.ForeColorChanged+=Changed;
        }
        private void Changed(object sender,EventArgs e){if(!IsDisposed){Text=source.Text;Parent?.PerformLayout();Invalidate();}}
        internal void SetInstalled(bool value){installed=value;Invalidate();}
        public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width,TextRenderer.MeasureText(source.Text,Font,new Size(Math.Max(1,proposedSize.Width-(int)Math.Round(14*DeviceDpi/96d)),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+(int)Math.Round(8*DeviceDpi/96d));
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SettingsDesignSurface.Canvas);int d=(int)Math.Round(7*DeviceDpi/96d),pad=(int)Math.Round(14*DeviceDpi/96d);
            using var brush=new SolidBrush(installed?Color.FromArgb(88,190,115):SettingsDesignSurface.Muted);e.Graphics.FillEllipse(brush,0,Math.Max(0,(Font.Height-d)/2),d,d);
            TextRenderer.DrawText(e.Graphics,source.Text,Font,new Rectangle(pad,0,Math.Max(1,Width-pad),Height),SettingsDesignSurface.TextColor,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        }
        protected override void Dispose(bool disposing){if(disposing){source.TextChanged-=Changed;source.ForeColorChanged-=Changed;}base.Dispose(disposing);}
    }
}
