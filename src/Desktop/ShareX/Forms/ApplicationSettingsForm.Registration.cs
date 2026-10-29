using Microsoft.Win32;
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private TabPage registrationTab;
        private CancellationTokenSource registrationCancellation;
        private bool registrationBusy;
        private readonly List<Button> registrationActions = new();
        private Button registrationAll,registrationCancel,recordingDownloadButton;
        private ProgressBar registrationProgress;
        private Label registrationMessage;
        private Label registrationSummary;
        private Control recordingRow,voiceRegistrationRow,textRegistrationRow,fileRegistrationRow,mixedOcrRow;
        private VisibleSettingsStatusRefresh<RegistrationSnapshot> registrationRefresh;
        private sealed record RegistrationSnapshot(bool Recording,VoiceInputRegistrationState Voice,bool VoiceFiles,bool Text,bool TextFiles,string Modern,bool Mixed,bool MixedFiles);

        private void InitializeRegistrationTab()
        {
            registrationTab=new TabPage("注册"){Name="tpRegistration",BackColor=tpGeneral.BackColor};
            RegisterSettingsPage(registrationTab,BuildRegistrationPage);
            tcSettings.TabPages.Insert(1,registrationTab);
        }
        private static bool ModernMenuReady()=>ModernFileFavoritesMenu.Status().StartsWith("外层菜单已注册",StringComparison.Ordinal);
        private static string VoiceStatus(VoiceInputRegistrationState state)=>state switch
        {
            VoiceInputRegistrationState.Registered=>"已注册",VoiceInputRegistrationState.Unregistered=>"未注册",
            VoiceInputRegistrationState.Unsupported=>"系统不支持",VoiceInputRegistrationState.BundleIncomplete=>"组件缺失",
            _=>"需要修复"
        };
        private void BuildRegistrationPage()
        {
            using var layout=new SettingsPageLayout(this,45);
            var content=layout.Stack();content.Name="registrationContent";content.SuspendLayout();
            TableLayoutPanel group=content;
            Button Action(string text,string name,Func<CancellationToken,Task> run,bool managesComponent=true)
            {
                var button=new Button{Text=text,Name=name,AutoSize=true,MinimumSize=new Size(layout.Scale(94),layout.Scale(30))};
                button.Click+=async(_,_)=>
                {
                    if(managesComponent)await RunRegistrationAction(run);
                    else
                    {
                        try{await run(CancellationToken.None);}
                        catch(Exception error){DebugHelper.WriteException(error);registrationMessage.Text="无法打开配置窗口，请重试。";}
                    }
                };
                registrationActions.Add(button);return button;
            }
            var helpTips=new ToolTip { AutoPopDelay=10000, InitialDelay=300, ReshowDelay=100, ShowAlways=true }; content.Disposed+=(_,_)=>helpTips.Dispose();
            Label Status(string name)=>new RegistrationStatus{Name=name,Text="检查中…"};
            Control Row(string title,Label status,params Control[] buttons)
            {
                var visible=buttons.Take(2).ToList();
                if(buttons.Length>visible.Count)
                {
                    var more=new Button{Text="⋯",Name=title+"Maintenance",AccessibleName=title+"更多操作",AutoSize=false,Size=new Size(layout.Scale(34),layout.Scale(30))};
                    var menu=new RegistrationMaintenanceMenu();
                    foreach(var control in buttons.Skip(visible.Count).OrderBy(control=>RegistrationMaintenanceMenu.Destructive(control.Name)))
                    {
                        var action=(Button)control;
                        var item=new ToolStripMenuItem(action.Text){Name=action.Name};
                        item.Click+=(_,_)=>action.PerformClick();
                        if(RegistrationMaintenanceMenu.Destructive(item) && menu.Items.Count>0)menu.Items.Add(new ToolStripSeparator());
                        menu.Items.Add(item);
                    }
                    more.ContextMenuStrip=menu;
                    more.Click+=(_,_)=>{if(!registrationBusy)menu.Show(more,new Point(more.Width,more.Height),ToolStripDropDownDirection.BelowLeft);};
                    more.Disposed+=(_,_)=>{menu.Dispose();foreach(var c in buttons.Skip(visible.Count))c.Dispose();};
                    registrationActions.Add(more);visible.Add(more);
                }
                var actions=layout.Inline(visible.ToArray());actions.Tag="registration-surface";
                var label=new Label{Text=title,TextAlign=ContentAlignment.MiddleLeft,Tag="registration-surface"};
                var glyph=new RegistrationGlyph{Glyph=title switch{"OCR 识别组件"=>"ocr","鼠标右键菜单"=>"mouse","FFmpeg"=>"video","语音输入"=>"microphone","文本输入"=>"keyboard",_=>"folder"}};
                var help=new RegistrationHelp{Name=status.Name+"Help"};

                string description=title switch{"OCR 识别组件"=>"提取截图中的文字","FFmpeg"=>"支持屏幕录制","语音输入"=>"语音打字，长按添加待办","文本输入"=>"启用小蟒输入法",_=>"右键收藏文件和文件夹"};
                help.AccessibleName=title+"用途";help.AccessibleDescription=description;helpTips.SetToolTip(help,description);
                var row=new RegistrationComponentRow(glyph,label,status,actions,help);
                layout.Add(group,row);return row;
            }
            registrationAll=Action("一键注册全部","registerAllComponents",RunRegistrationBatch);
            registrationAll.MinimumSize=new Size(layout.Scale(112),layout.Scale(32));MainMenuAppearance.SetPrimary(registrationAll);
            registrationCancel=new Button{Text="取消",Name="cancelRegistrationBatch",AutoSize=true,Visible=false,MinimumSize=new Size(layout.Scale(80),layout.Scale(30))};
            registrationCancel.Click+=(_,_)=>{registrationCancellation?.Cancel();registrationMessage.Text="正在取消下载；当前注册结束后停止后续步骤…";};
            var summary=layout.Stack();layout.Add(content,summary);summary.Margin=Padding.Empty;
            registrationSummary=new Label{Name="registrationSummary",Text="正在检查组件…",AutoSize=true,Tag="registration-surface"};
            var summaryActions=layout.Inline(registrationAll,registrationCancel,registrationSummary);summaryActions.Tag="registration-surface";
            var downloadPage=new LinkLabel{Text="组件下载 ↗",Name="componentReleasePage",AutoSize=true,Anchor=AnchorStyles.Right,LinkColor=MainMenuAppearance.Text,ActiveLinkColor=MainMenuAppearance.Text};
            downloadPage.LinkClicked+=(_,_)=>{try{URLHelpers.OpenURL("https://github.com/zhanglegezhang/xiaomang_component/releases");}catch(Exception error){registrationMessage.Text="无法打开组件发布页面："+error.Message;}};
            var summaryRow=new TableLayoutPanel{AutoSize=true,ColumnCount=2,Tag="registration-surface",Margin=Padding.Empty};
            summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            summaryActions.Dock=DockStyle.Fill;summaryRow.Controls.Add(summaryActions,0,0);summaryRow.Controls.Add(downloadPage,1,0);
            layout.Add(summary,summaryRow);
            registrationProgress=new ProgressBar{Name="registrationOverallProgress",Visible=false,Height=layout.Scale(6),Minimum=0,Maximum=100};layout.Add(summary,registrationProgress);
            registrationMessage=new Label{Name="registrationOperationMessage",AutoSize=true,Tag="registration-surface",Text="",Visible=false};layout.Add(summary,registrationMessage);registrationMessage.TextChanged+=(_,_)=>registrationMessage.Visible=!string.IsNullOrWhiteSpace(registrationMessage.Text);

            group=content;
            var mixedStatus=Status("mixedOcrStatus");
            mixedOcrRow=Row("OCR 识别组件",mixedStatus,Action("下载 / 修复","downloadMixedOCR",async token=>
            {
                await MixedOcrComponent.DownloadAsync(new Progress<int>(value=>{if(!IsDisposed){registrationProgress.Value=value;registrationMessage.Text=$"OCR识别组件下载、校验：{value}%";}}),token);
                if(!IsDisposed){mixedStatus.Text="已就绪";registrationMessage.Text="OCR识别组件已就绪，无需注册；识别全程离线。";}
            }), Action("移除组件", "removeMixedOCR", token => RemoveDownloadedComponent(true, token)),
                Action("百度翻译 API 配置", "configureBaiduTranslation", token =>
                {
                    BaiduTranslationSettingsForm.Open(this);
                    return Task.CompletedTask;
                },managesComponent:false));
            var recordingStatus=Status("recordingComponentStatus");
            recordingDownloadButton=Action("下载录屏组件","downloadRecordingComponent",async token=>
            {
                var progress=new Progress<int>(value=>{if(!IsDisposed && registrationBusy){registrationProgress.Value=Math.Clamp(value,0,100);registrationMessage.Text=value==100?"录屏组件已就绪。":value<90?$"录屏组件下载：{value*100/90}%":"校验并安装录屏组件…";}});
                await RecordingComponent.DownloadAsync(progress,token);
            });
            recordingRow=Row("FFmpeg",recordingStatus,recordingDownloadButton,
                Action("移除组件", "removeRecordingComponent", token => RemoveDownloadedComponent(false, token)));


            var voiceStatus=Status("voiceInputRegistrationStatus");
            voiceRegistrationRow=Row("语音输入",voiceStatus,
                Action("注册 / 修复","registerVoiceInputComponent",token=>PrepareInputSingle(true,token)),
                Action("打开日志","openVoiceInputSetupLogFolder",_=>OpenRegistrationLog(true)),
                Action("卸载","uninstallVoiceInputComponent",_=>ChangeVoice(false,false)));
            var textStatus=Status("xiaomangInputStatus");
            textRegistrationRow=Row("文本输入",textStatus,
                Action("检查并更新","updateXiaomangInputMethod",UpdateTextComponent),
                Action("注册 / 修复","installXiaomangInputMethod",token=>PrepareInputSingle(false,token)),
                Action("输入法设置","openXiaomangInputSettings",_=>{XiaomangInputMethodManager.OpenSettings();return Task.CompletedTask;}),
                Action("重试后台","retryXiaomangInputWorker",_=>Task.Run(XiaomangInputMethodManager.Retry)),
                Action("撤销注册","uninstallXiaomangInputMethod",_=>XiaomangInputMethodManager.RegisterAsync(false)),
                Action("打开日志","openXiaomangInputSetupLogFolder",_=>OpenRegistrationLog(false)));


            var modernStatus=Status("modernFileMenuStatus");
            var certificate=new Label{Name="modernFileMenuFingerprint",AutoSize=true,Visible=false};
            var certificateDetails=new Button{Text="证书详情",Name="modernCertificateDetails",AutoSize=true,MinimumSize=new Size(layout.Scale(94),layout.Scale(30))};
            certificateDetails.Click+=(_,_)=>
            {
                if(!certificate.Visible)
                {
                    try{certificate.Text="证书只加入本机“受信任的人”，不加入根证书库。\n信任影响本机所有用户；移除菜单保留证书信任。\nSHA-256："+ModernFileFavoritesMenu.CertificateFingerprint();}
                    catch(Exception error){certificate.Text=error.Message;}
                }
                certificate.Visible=!certificate.Visible;
            };
            registrationActions.Add(certificateDetails);
            fileRegistrationRow=Row("鼠标右键菜单",modernStatus,
                Action("注册 / 修复","installModernFileMenu",_=>ModernFileFavoritesMenu.ChangeAsync(true,true)),
                certificateDetails,Action("移除入口","removeModernFileMenu",_=>ModernFileFavoritesMenu.ChangeAsync(false,false)));
            certificate.Tag="registration-surface";layout.Add(group,certificate);
            content.SizeChanged+=(_,_)=>
            {
                if(content.ClientSize.Width<layout.Scale(80))return;
                foreach(var label in new[]{registrationMessage,certificate})label.MaximumSize=new Size(content.ClientSize.Width,0);
            };
            content.ResumeLayout(false);layout.Install(registrationTab,content);registrationTab.Padding=new Padding(layout.Scale(16),0,layout.Scale(16),layout.Scale(8));
            registrationRefresh=new VisibleSettingsStatusRefresh<RegistrationSnapshot>(this,()=>registrationTab.Visible && !registrationBusy,
                ()=>new RegistrationSnapshot(RecordingComponent.IsValidAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult(),VoiceInputComponent.RegistrationState,
                    SenseVoiceModelManager.IsInstalled(Program.PersonalFolder),XiaomangInputMethodManager.IsRegistered,
                    ComponentBundles.IsPresent(ComponentBundles.Text,XiaomangInputMethodManager.BundleDirectory),ModernFileFavoritesMenu.Status(),
                    MixedOcrComponent.IsValidAsync(CancellationToken.None).GetAwaiter().GetResult(),MixedOcrComponent.HasFiles),
                state=>
                {
                    recordingStatus.Text=state.Recording?"已就绪":"未下载（约65MB）";recordingDownloadButton.Enabled=state.Recording?false:!registrationBusy;
                    voiceStatus.Text=VoiceStatus(state.Voice);textStatus.Text=state.Text?"已注册":"未注册或需修复";
                    modernStatus.Text=state.Modern.StartsWith("外层菜单已注册")?"已注册":"未注册或需修复";
                    bool voiceReady=state.Voice==VoiceInputRegistrationState.Registered && state.VoiceFiles,
                        textReady=state.Text && state.TextFiles,modernReady=state.Modern.StartsWith("外层菜单已注册");
                    ((RegistrationStatus)recordingStatus).UpdateState(state.Recording?"已就绪":RecordingComponent.IsInstalled?"需修复":"未下载",state.Recording);
                    ((RegistrationStatus)voiceStatus).UpdateState(!state.VoiceFiles?"待下载模型":VoiceStatus(state.Voice),voiceReady);
                    ((RegistrationStatus)textStatus).UpdateState(!state.TextFiles?"待下载组件":state.Text?"已注册":"待注册",textReady);
                    ((RegistrationStatus)modernStatus).UpdateState(modernReady?"已注册":"待准备",modernReady);
                    int count=(state.Recording?1:0)+(voiceReady?1:0)+(textReady?1:0)+(modernReady?1:0);
                    bool mixedReady=state.Mixed;
                    ((RegistrationStatus)mixedStatus).UpdateState(mixedReady?"已就绪":state.MixedFiles?"需修复":"未下载",mixedReady);
                    if(mixedReady)count++;
                    registrationSummary.ForeColor=count==5?Color.FromArgb(127,218,153):MainMenuAppearance.Text;registrationSummary.Text=count==5?"✓ 全部已就绪   5 / 5 项":$"组件准备   {count} / 5 项";
                },error=>{if(!registrationBusy)registrationMessage.Text="状态检查失败："+error.Message;});
            registrationTab.VisibleChanged+=(_,_)=>registrationRefresh.UpdateVisibility();
            Disposed+=(_,_)=>{registrationCancellation?.Cancel();registrationRefresh.Dispose();};
            registrationRefresh.UpdateVisibility();
        }
        private static Task OpenRegistrationLog(bool voice)
        {
            string result=InputMethodRegistrationLogs.OpenFolder(InputMethodRegistrationLogs.GetPath(voice));
            if(result.StartsWith("无法")||result.StartsWith("失败"))throw new IOException(result);
            return Task.CompletedTask;
        }
        private async Task RemoveDownloadedComponent(bool ocr, CancellationToken token)
        {
            string title = ocr ? "OCR 识别组件" : "FFmpeg 录屏组件";
            string effect = ocr ? "移除后将使用 Windows OCR，中英混合识别效果可能下降。" :
                "移除后，依赖 FFmpeg 的录屏、视频转换等功能需要重新下载组件才能使用。";
            if (MessageBox.Show(this, effect + "\n只移除小蟒管理的组件文件，保留个人数据。\n是否移除？",
                "移除" + title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                registrationProgress.Visible = false;
                registrationMessage.Text = "已取消移除，组件保留。";
                return;
            }
            registrationMessage.Text = "正在移除" + title + "…";
            if (ocr) await MixedOcrComponent.RemoveAsync(token); else await RecordingComponent.RemoveAsync(token);
            if (!IsDisposed)
            {
                registrationProgress.Value = 100;
                registrationMessage.Text = ocr ? "OCR 组件已移除，将使用 Windows OCR；可随时重新下载。" :
                    "FFmpeg 组件已移除；需要录屏或转换时可重新下载。";
            }
        }
        private static async Task ChangeVoice(bool install,bool repair)
        {
            var setup=VoiceInputSetupCoordinator.Default;
            var result=await (install?repair?setup.RepairAsync():setup.EnsureRegisteredAsync(false):setup.UninstallAsync());
            if(result.Cancelled)throw new OperationCanceledException(result.Message);
            if(install?!result.Ready:setup.State!=VoiceInputRegistrationState.Unregistered)throw new IOException(result.Message);
        }
        private sealed class ComponentProgress(IProgress<int> target) : IProgress<int>
        {
            public void Report(int value)=>target?.Report(Math.Min(value,95));
        }
        private static string InputDestination(bool voice)=>voice?SenseVoiceModelManager.GetModelFiles(Program.PersonalFolder).Folder:XiaomangInputMethodManager.BundleDirectory;
        private async Task UpdateTextComponent(CancellationToken token)
        {
            registrationMessage.Text="正在检查文本输入法组件更新…";
            var release=await InputComponentCatalog.ReadAsync(Program.ProductVersion,token);
            var progress=new Progress<int>(value=>{if(!IsDisposed){registrationProgress.Value=Math.Min(value,95);registrationMessage.Text="正在准备输入法组件 "+release.Version+"："+value+"%";}});
            bool changed=await Task.Run(()=>InputComponentUpdater.UpdateAsync(release,Path.Combine(Program.PersonalFolder,"Cache","Components"),progress,token));
            if(!IsDisposed){registrationProgress.Value=100;registrationMessage.Text=changed?"输入法组件 "+release.Version+" 更新完成，设置与学习记录已保留。已打开的应用可能需重新打开以加载新系统组件。":"输入法组件 "+release.Version+" 已是最新版本。";}
        }
        private static async Task<bool> InputReady(bool voice,CancellationToken token)
        {
            if(voice?VoiceInputComponent.RegistrationState!=VoiceInputRegistrationState.Registered:!XiaomangInputMethodManager.IsRegistered)return false;
            return await ComponentBundles.IsValidAsync(voice?ComponentBundles.Voice:ComponentBundles.Text,InputDestination(voice),token);
        }
        private static async Task PrepareInput(bool voice,bool repair,IProgress<int> progress,CancellationToken token)
        {
            if(!voice)
            {
                var latest=await InputComponentCatalog.ReadAsync(Program.ProductVersion,token);
                await Task.Run(()=>InputComponentUpdater.UpdateAsync(latest,Path.Combine(Program.PersonalFolder,"Cache","Components"),progress,token));
                progress?.Report(100);PersonalDataManager.NotifyDeferredInput();return;
            }
            await ComponentBundles.EnsureAsync(voice?ComponentBundles.Voice:ComponentBundles.Text,InputDestination(voice),
                Path.Combine(Program.PersonalFolder,"Cache","Components"),new ComponentProgress(progress),token);
            token.ThrowIfCancellationRequested();progress?.Report(96);
            if(voice)await ChangeVoice(true,repair);else await XiaomangInputMethodManager.RegisterAsync(true);
            if(!await InputReady(voice,token))throw new IOException("组件已下载，但注册未通过检查，请重试注册。");
            progress?.Report(100);
            if (!voice) PersonalDataManager.NotifyDeferredInput();
        }
        private async Task PrepareInputSingle(bool voice,CancellationToken token)
        {
            string name=voice?"语音输入":"文本输入";
            var progress=new Progress<int>(value=>{if(!IsDisposed && registrationBusy){registrationProgress.Value=value;registrationMessage.Text=value<90?$"{name}下载：{value*100/90}%":value<96?$"{name}：校验并安装组件…":value<100?$"{name}：正在注册，请确认系统授权…":$"{name}已就绪。";}});
            await PrepareInput(voice,true,progress,token);
            if(!IsDisposed){registrationProgress.Value=100;registrationMessage.Text=name+"已就绪。";}
        }
        private async Task RunRegistrationBatch(CancellationToken cancellation)
        {
            var steps=new[]
            {
                new RegistrationStep("录屏组件",()=>RecordingComponent.IsValidAsync(cancellation),RecordingComponent.DownloadAsync),
                new RegistrationStep("OCR 识别组件",()=>MixedOcrComponent.IsValidAsync(cancellation),MixedOcrComponent.DownloadAsync),
                new RegistrationStep("语音输入",()=>InputReady(true,cancellation),(p,t)=>PrepareInput(true,false,p,t)),
                new RegistrationStep("文本输入",()=>InputReady(false,cancellation),(p,t)=>PrepareInput(false,false,p,t)),
                new RegistrationStep("外层收藏菜单",()=>Task.Run(ModernMenuReady),(_,_)=>ModernFileFavoritesMenu.ChangeAsync(true,true))
            };
            var progress=new Progress<RegistrationProgress>(value=>{if(!IsDisposed && registrationBusy){registrationProgress.Value=value.Percent;registrationMessage.Text=value.Message;}});
            var result=await RegistrationBatch.RunAsync(steps,progress,cancellation);
            if(IsDisposed)return;
            string summary=$"{(result.Cancelled?"已取消":"已完成")}：准备 {result.Prepared} 项，已就绪 {result.Skipped} 项，失败 {result.Errors.Count} 项。";
            registrationMessage.Text=result.Errors.Count==0?summary:summary+"\n"+string.Join("\n",result.Errors);
            if(!result.Cancelled)registrationProgress.Value=100;
        }
        private async Task RunRegistrationAction(Func<CancellationToken,Task> action)
        {
            if(registrationBusy)return;
            registrationBusy=true;registrationCancellation=new CancellationTokenSource();registrationRefresh.UpdateVisibility();
            foreach(var button in registrationActions)button.Enabled=false;
            registrationCancel.Visible=true;registrationProgress.Visible=true;registrationProgress.Value=0;registrationMessage.Text="准备中；请确认可能出现的 Windows 管理员授权…";
            try
            {
                await action(registrationCancellation.Token);
                if(!IsDisposed && registrationCancellation.IsCancellationRequested && !registrationMessage.Text.StartsWith("已取消"))registrationMessage.Text="已取消后续步骤，已完成内容保留。";
                if(!IsDisposed && registrationMessage.Text.StartsWith("准备中")){registrationProgress.Value=100;registrationMessage.Text=registrationCancellation.IsCancellationRequested?"已取消后续步骤，已完成内容保留。":"操作完成。";}
                else if(!IsDisposed && registrationMessage.Text.StartsWith("校验并安装")){registrationProgress.Value=100;registrationMessage.Text="录屏组件已就绪。";}
            }
            catch(OperationCanceledException){if(!IsDisposed)registrationMessage.Text="已取消，已有组件和注册保留。";}
            catch(System.ComponentModel.Win32Exception error)when(error.NativeErrorCode==1223){if(!IsDisposed)registrationMessage.Text="已取消管理员授权，原状态保留。";}
            catch(Exception error){DebugHelper.WriteException(error);if(!IsDisposed)registrationMessage.Text="操作失败："+error.Message;}
            finally
            {
                registrationCancellation.Dispose();registrationCancellation=null;registrationBusy=false;
                if(!IsDisposed){foreach(var button in registrationActions)button.Enabled=true;registrationCancel.Visible=false;registrationRefresh.UpdateVisibility();}
            }
        }
        internal void SelectRegistrationPage(string section)
        {
            tttvMain.NavigateToTabPage(registrationTab);
            if(!EnsureSettingsPage(registrationTab))return;
            registrationTab.ScrollControlIntoView(RegistrationRow(section));
        }

        private Control RegistrationRow(string section) => section switch
        {
            "ocr" => mixedOcrRow, "recording" => recordingRow,
            "voice" => voiceRegistrationRow, "text" => textRegistrationRow, _ => fileRegistrationRow
        };

        internal void PrepareRegistrationDialog(string section)
        {
            SelectRegistrationPage(section);
            // The OCR dialog owns a modal loop; reuse this registration page in an
            // owned window so the main window does not remain disabled behind it.
            tttvMain.Visible = false;
            registrationTab.Parent?.Controls.Remove(registrationTab);
            var tabs = new TablessControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(registrationTab);
            Controls.Add(tabs);
            tabs.BringToFront();
            registrationTab.Show();
            Name = "registrationComponentsDialog";
            Text = "小蟒 · 注册组件";
            Icon = ShareXResources.Icon;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(MainMenuAppearance.Scale(this, 600), MainMenuAppearance.Scale(this, 420));
            ClientSize = new Size(MainMenuAppearance.Scale(this, 900), MainMenuAppearance.Scale(this, 560));
            MainMenuAppearance.Apply(this);
            Shown += (_, _) => registrationTab.ScrollControlIntoView(RegistrationRow(section));
        }
    }
}
