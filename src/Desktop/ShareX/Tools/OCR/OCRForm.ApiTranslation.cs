using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX;
public partial class OCRForm
{
    private Panel apiPanel;
    private TextBox apiResult;
    private FlowLayoutPanel apiHeader;
    private LinkLabel openExternal;
    private Label apiFeedback;
    private string apiTarget = "zh";
    private CancellationTokenSource apiCancellation;
    private bool apiMode;
    private int apiResultRevision = -1;

    private void EnsureApiPanel()
    {
        if(apiPanel != null)return;
        apiPanel=new Panel{Dock=DockStyle.Fill,Padding=new Padding(12),BackColor=Color.FromArgb(38,48,57)};
        apiPanel.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(62,78,90)); e.Graphics.DrawRectangle(pen,0,0,apiPanel.Width-1,apiPanel.Height-1); };
        apiResult=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,
            BorderStyle=BorderStyle.None,Font=new Font("Microsoft YaHei UI",11),BackColor=Color.FromArgb(38,48,57),ForeColor=Color.FromArgb(229,236,242)};
        apiFeedback = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(600,0), ForeColor = Color.FromArgb(146,163,175), BackColor=Color.FromArgb(38,48,57) };
        var chinese = new Button { Text = "中文", AutoSize = true, FlatStyle = FlatStyle.Flat };
        var english = new Button { Text = "EN", AutoSize = true, FlatStyle = FlatStyle.Flat };
        var fallback = new Button { Text = "改用网页翻译", AutoSize = true, FlatStyle = FlatStyle.Flat };
        foreach (var button in new[] { chinese, english, fallback })
        {
            button.Cursor = Cursors.Hand;
            button.Padding = new Padding(6,0,6,0);
            button.Margin = new Padding(2,2,2,4);
            button.BackColor = Color.FromArgb(38,48,57);
            button.FlatAppearance.BorderColor = Color.FromArgb(62,78,90);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(63,74,83);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(75,85,94);
            button.SizeChanged += (_, _) =>
            {
                float d = 8f * button.DeviceDpi / 96f;
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                float w = button.Width, h = button.Height;
                path.AddArc(0,0,d,d,180,90);path.AddArc(w-d,0,d,d,270,90);
                path.AddArc(w-d,h-d,d,d,0,90);path.AddArc(0,h-d,d,d,90,90);path.CloseFigure();
                var old = button.Region;button.Region = new Region(path);old?.Dispose();
            };
        }
        void UpdateTarget()
        {
            foreach(var pair in new[]{(chinese,"zh"),(english,"en")})
            {
                pair.Item1.BackColor = apiTarget == pair.Item2 ? Color.FromArgb(255,135,62) : Color.FromArgb(38,48,57);
                pair.Item1.ForeColor = apiTarget == pair.Item2 ? Color.FromArgb(27,36,43) : Color.FromArgb(229,236,242);
            }
        }
        chinese.Click += async (_,_) => { if(apiTarget=="zh")return;apiTarget="zh";UpdateTarget();if(apiMode)await ShowTranslationAsync(); };
        english.Click += async (_,_) => { if(apiTarget=="en")return;apiTarget="en";UpdateTarget();if(apiMode)await ShowTranslationAsync(); };
        fallback.ForeColor = Color.FromArgb(229,236,242);
        apiHeader.Controls.AddRange(new Control[]{new Label{Text="百度 API 译文",AutoSize=true,Margin=new Padding(3,8,6,3),ForeColor=Color.FromArgb(229,236,242)},chinese,english,fallback});
        apiPanel.Controls.Add(apiResult);apiPanel.Controls.Add(apiFeedback);translationHost.Controls.Add(apiPanel);
        UpdateTarget();        fallback.Click+=async(_,_)=>await ShowWebTranslationAsync();
    }
    private void UpdateApiHeader()
    {
        apiHeader.Visible = apiMode;
        translationStatus.Visible = openExternal.Visible = !apiMode;
    }
    private void CancelApiTranslation()
    {
        apiCancellation?.Cancel(); apiResultRevision=-1;
        if(apiResult!=null)apiResult.Clear();
    }
    private async Task ShowTranslationAsync()
    {
        if(string.IsNullOrWhiteSpace(Result))return;
        BaiduTranslationCredentials credentials;
        try { credentials=BaiduTranslationApi.Load(); }
        catch(InvalidOperationException ex){CancelApiTranslation();translationRevision++;EnsureApiPanel();apiMode=true;UpdateApiHeader();apiPanel.Visible=true;apiPanel.BringToFront();if(translationBrowser!=null)translationBrowser.Visible=false;apiFeedback.Text=ex.Message;return;}
        if(credentials==null){await ShowWebTranslationAsync();return;}
        CancelApiTranslation();
        var cancellation=CancellationTokenSource.CreateLinkedTokenSource(ocrCancellation.Token);apiCancellation=cancellation;
        int revision=++translationRevision;
        EnsureApiPanel();apiMode=true;UpdateApiHeader();apiPanel.Visible=true;apiPanel.BringToFront();
        if(translationBrowser!=null){translationBrowser.CoreWebView2?.Stop();translationBrowser.Visible=false;}
        apiFeedback.Text="正在通过百度 API 翻译…";
        try
        {
            string text=await BaiduTranslationApi.TranslateAsync(credentials,Result,apiTarget,cancellation.Token);
            if(IsDisposed||Disposing||revision!=translationRevision||cancellation.IsCancellationRequested)return;
            apiResult.Text=text;apiResultRevision=revision;apiFeedback.Text="已完成";
        }
        catch(OperationCanceledException){if(!IsDisposed&&!Disposing&&revision==translationRevision&&!cancellation.IsCancellationRequested)apiFeedback.Text="翻译超时，可重试或改用网页翻译。";}
        catch(InvalidOperationException ex){if(!IsDisposed&&!Disposing&&revision==translationRevision)apiFeedback.Text=ex.Message;}
        catch{if(!IsDisposed&&!Disposing&&revision==translationRevision)apiFeedback.Text="翻译失败，可重试或改用网页翻译。";}
        finally{if(ReferenceEquals(apiCancellation,cancellation))apiCancellation=null;cancellation.Dispose();}
    }
}
