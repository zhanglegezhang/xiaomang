using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private readonly HashSet<TabPage> designedSettingsPages=new();
        private SettingsDesignSurface voiceDesignSurface;
        private Control voiceProgressLayoutRow;
        private SettingsDesignStatus voiceState;
        private void DesignSettingsPage(TabPage page)
        {
            if(page==registrationTab||designedSettingsPages.Contains(page))return;
            var originals=SettingsDesignSurface.All(page).ToArray();
            foreach(var control in originals)control.SuspendLayout();
            Control Find(string name)=>originals.First(x=>x.Name==name);
            var design=new SettingsDesignSurface(page.Name=="tpToolsGeneral"?"拾色器":page.Text.Replace("&",""));
            design.RightAlignedToggles=page.Name is "tpGeneral" or "tpCaptureGeneral" or "tpNotifications";
            design.RightAlignedActions=page.Name=="tpGeneral";
            // Stop legacy layout callbacks before moving their child controls.
            designedSettingsPages.Add(page);
            void Toggle(string name,string title=null){var check=(CheckBox)Find(name);design.Toggle(title??check.Text.Replace("&",""),check);}
            void Editor(string name,string title,int width=300)=>design.Editor(title,Find(name),width:width);
            void Number(string name,string title,string unit)=>design.Editor(title,design.Inline(Find(name),design.Unit(unit)));
            try {
            switch(page.Name)
            {
                case "tpGeneral":
                    design.Section("启动与托盘");design.Toggle("开机自动启动",cbStartWithWindows);design.Toggle("显示托盘图标",cbShowTray);design.Hint("关闭窗口仍在后台运行，点击「退出小蟒」结束。");design.Toggle("启动时隐藏主窗口",cbSilentRun);
                    design.Section("界面语言");design.Editor("显示语言",btnLanguages);
                    design.Section("个人数据");
                    design.Action("导出个人数据",(Button)Find("personalData_export"),"导出…");
                    design.Action("导入个人数据",(Button)Find("personalData_import"),"导入…");
                    design.Action("待处理输入法数据",(Button)Find("personalData_restore"),"恢复…");
                    design.Hint("导入导出时可选择类别并预览数据。");break;
                case "tpCaptureGeneral":
                    design.Section("截图行为");Toggle("cbShowCursor","在截图中显示鼠标指针");Toggle("cbCaptureAutoHideDesktopIcons","自动隐藏桌面图标");
                    Toggle("cbCaptureTransparent","截图窗口保留透明度");Toggle("cbCaptureShadow","截图窗口添加阴影");
                    Number("nudCaptureShadowOffset","阴影偏移","px");Number("nudScreenshotDelay","截图延迟","秒");
                    design.Section("区域截图");Editor("cbRegionCaptureMouseRightClickAction","鼠标右键动作");
                    Number("nudRegionCaptureBackgroundDimStrength","背景调暗强度","%");Toggle("cbRegionCaptureShowInfo","显示位置与尺寸");
                    design.Hint("截图窗口阴影依赖透明度。");break;
                case "tpScreenRecorder":
                    design.Section("帧率");Number("nudScreenRecordFPS","视频帧率","FPS");Number("nudGIFFPS","GIF 帧率","FPS");
                    design.Section("录制行为");design.Related("自动开始录制",(CheckBox)Find("cbScreenRecordAutoStart"),Find("nudScreenRecorderStartDelay"),design.Unit("秒"));
                    design.Related("固定录制时长",(CheckBox)Find("cbScreenRecorderFixedDuration"),Find("nudScreenRecorderDuration"),design.Unit("秒"));
                    Toggle("cbScreenRecorderShowCursor","在录像中显示鼠标指针");Toggle("cbScreenRecordTransparentRegion","使用透明区域选择");
                    Toggle("cbScreenRecordConfirmAbort","取消录制前确认");Toggle("cbScreenRecordTwoPassEncoding","使用无损编码并应用编码选项");
                    design.Section("编码与声音");design.Action("编码与声音来源",(Button)Find("btnScreenRecorderFFmpegOptions"),"录屏选项…");break;
                case "tpOCR":
                    design.Section("识别偏好");design.Editor("默认语言",Find("cbCaptureOCRDefaultLanguage"),description:"此语言用于 Windows OCR 识别。");Toggle("cbCaptureOCRSilent","静默识别");
                    design.Section("结果处理");Toggle("cbCaptureOCRAutoCopy","自动复制识别结果");Toggle("cbCloseWindowAfterOpenServiceLink","打开翻译后关闭识别窗口");
                    design.Hint("中英混合组件在“注册”中管理。");break;
                case "tpToolsGeneral":
                    void Picker(string name,string title,bool multiline=false){
                        var field=(TextBox)Find(name);if(multiline){field.Multiline=true;field.WordWrap=true;field.ScrollBars=ScrollBars.None;}
                        var variables=new Button{Text="变量 ▾",Name="variables_"+name};
                        variables.Click+=(_,_)=>{field.Focus();GetDefaultTaskSettingsForm().GetPickerFormatMenu(field)?.Show(field,new Point(0,field.Height));};
                        design.Editor(title,design.Inline(field,variables));
                    }
                    design.Section("复制格式");Picker("txtToolsScreenColorPickerFormat","默认复制格式");Picker("txtToolsScreenColorPickerFormatCtrl","Ctrl + 单击复制格式");
                    design.Section("浮层信息");Picker("txtToolsScreenColorPickerInfoText","显示文本",true);design.Hint("点击“变量”可插入格式变量，$n 表示换行。");break;
                case "tpNotifications":
                    DesignNotifications(design,notificationSettingsControl);break;
                case "tpDailyNews":
                    design.Toggle("启用资讯功能",dailyNewsEnabled);design.Section("AI 服务");
                    design.Editor("API Key",dailyNewsApiKey,description:"密钥本机加密保存；手动更新会消耗 API 额度。");design.Editor("API 地址",dailyNewsApiBase);design.Editor("模型",dailyNewsModel);
                    design.Section("更新与来源");
                    design.Pair("每日最多资讯数",design.Inline(dailyNewsMaxTotal,design.Unit("条")),"每个来源最多条数",design.Inline(dailyNewsMaxPerSource,design.Unit("条")));
                    design.Pair("并发处理数",dailyNewsConcurrency,"网络超时",design.Inline(dailyNewsTimeout,design.Unit("秒")));
                    design.Related("自动更新",dailyNewsAutoRun,design.Unit("每天"),dailyNewsAutoRunHour,design.Unit("时（错过后启动时补更）"));
                    design.Related("国际资讯使用代理",dailyNewsProxyEnabled,dailyNewsProxyUrl);design.Editor("国内热榜接口",dailyNewsHotApi);
                    design.Pair("生成今日导读",new SettingsDesignSwitch(dailyNewsDigest),"合并同一事件",new SettingsDesignSwitch(dailyNewsTopicGroup));
                    design.Section("存储管理");design.Editor("保存位置",new TextBox{ReadOnly=true,Text=Path.Combine(Program.PersonalFolder,"daily_news")});
                    design.Pair("资讯文件",dailyNewsStorageUsage,"网页缓存",dailyNewsCacheUsage);
                    var buttons=originals.OfType<Button>().Where(b=>b.Text is "刷新占用" or "清理历史资讯" or "清理网页缓存").ToArray();
                    design.Editor("存储管理",design.Inline(buttons));break;
                case "tpPaths":
                    foreach(Control control in pathLocationsPanel.Controls.Cast<Control>().ToArray()) {
                        if(control is Label heading) {if(heading.Tag as string=="storage-section-heading" && heading.Text!="各功能的实际存储位置")design.Section(heading.Text);continue;}
                        var row=pathLocationRows.FirstOrDefault(r=>r.Title.Parent==control);
                        if(row!=null){design.Editor(row.Name,design.Inline(row.Path,row.Open),row.Description.Text);design.MirrorCaption(row.Title);}
                    }
                    design.Hint("路径为只读。鼠标停在项目名称上可查看用途。");break;
                case "tpVoiceTranscription":
                    design.Hint("本机离线识别，与 Ctrl + Alt 语音输入共用模型。");design.Section("本地识别模型");
                    voiceDesignSurface=design;voiceState=new SettingsDesignStatus(voiceModelStatus);
                    design.Editor("模型",new Label{Text="SenseVoice-Small INT8"});design.Editor("状态",voiceState);
                    var modelPath=new TextBox{ReadOnly=true,Text=voiceModelLocation.Text,Name="voiceModelPathDisplay"};
                    voiceModelLocation.TextChanged+=(_,_)=>{if(!modelPath.IsDisposed)modelPath.Text=voiceModelLocation.Text;};
                    design.Editor("保存位置",modelPath);
                    var open=originals.OfType<Button>().First(b=>b.Text=="打开模型文件夹");
                    voiceModelRemoveButton.Tag="settings-danger";
                    design.Editor("模型管理",design.Inline(voiceModelInstallButton,open,voiceModelRemoveButton,voiceModelCancelButton));
                    voiceProgressLayoutRow=design.Editor("下载进度",voiceModelProgress);
                    design.SetRowVisible(voiceProgressLayoutRow,voiceModelDownloadCancellation!=null);
                    design.Hint("自动识别语言 · 标点与数字格式化\n模型不可用时回退 Windows 语音识别。");
                    design.Hint("模型来源：SenseVoice / sherpa-onnx。首次安装需联网，识别全程离线。");break;
                case "tpAdvanced":DesignAdvanced(design);break;
                default:designedSettingsPages.Remove(page);design.Dispose();return;
            }
            designedSettingsPages.Add(page);
            page.AutoScroll=false;page.AutoScrollMinSize=Size.Empty;page.Padding=Padding.Empty;
            // Keep the old control containers alive so handlers and enabled scopes
            // continue working. Only the presentation surface is visible.
            foreach(Control old in page.Controls){old.Visible=false;}
            var host=new SettingsDesignHost(design);page.Controls.Add(host);host.BringToFront();
            } catch {designedSettingsPages.Remove(page);throw;}
            finally {foreach(var control in originals.Reverse())control.ResumeLayout(false);}
            design.Finish();
        }

        private static void DesignNotifications(SettingsDesignSurface design,NotificationSettingsControl owner)
        {
            var all=SettingsDesignSurface.All(owner).ToArray();Control Find(string n)=>all.First(c=>c.Name==n);
            void Toggle(string n,string title=null){var check=(CheckBox)Find(n);design.Toggle(title??check.Text.Replace("&",""),check);}
            Control popup=Find("gbToastWindow");
            void Popup(string title,Control field){var row=design.Editor(title,field);design.LinkEnabled(row,popup);}
            design.Section("提示音");Toggle("cbPlaySoundAfterCapture","截图完成时播放提示音");
            if(!SystemOptions.DisableUpload)Toggle("cbPlaySoundAfterUpload","上传完成时播放提示音");
            Toggle("cbPlaySoundAfterAction","操作完成时播放提示音");design.Section("弹窗显示");Toggle("cbShowToastNotificationAfterTaskCompleted","任务完成后显示通知");
            Popup("显示时长",design.Inline(Find("nudToastWindowDuration"),design.Unit("秒")));
            Popup("淡出时长",design.Inline(Find("nudToastWindowFadeDuration"),design.Unit("秒")));
            Popup("显示位置",Find("cbToastWindowPlacement"));
            Popup("弹窗尺寸",design.Inline(Find("nudToastWindowSizeWidth"),design.Unit("×"),Find("nudToastWindowSizeHeight"),design.Unit("px")));
            Toggle("cbToastWindowAutoHide","截图时自动隐藏通知");design.LinkEnabled(design.Controls[0].Controls.Cast<Control>().Last(),popup);
            Toggle("cbDisableNotificationsOnFullscreen","全屏应用中不显示");design.LinkEnabled(design.Controls[0].Controls.Cast<Control>().Last(),popup);
            design.Section("点击动作");Popup("左键",Find("cbToastWindowLeftClickAction"));Popup("右键",Find("cbToastWindowRightClickAction"));Popup("中键",Find("cbToastWindowMiddleClickAction"));
            // Custom sound settings existed below the fold; retain them rather than
            // losing them merely because the concept image shows the first viewport.
            design.Section("自定义声音");
            foreach(var sound in new[]{("Capture","截图"),("TaskCompleted","任务完成"),("ActionCompleted","操作完成"),("Error","错误")}){
                Toggle("cbUseCustom"+sound.Item1+"Sound","自定义"+sound.Item2+"提示音");
                design.Editor("声音文件",design.Inline(Find("txtCustom"+sound.Item1+"SoundPath"),Find("btnCustom"+sound.Item1+"SoundPath")));
            }
        }
        private void DesignAdvanced(SettingsDesignSurface design)
        {
            var adapter=new AdvancedApplicationSettings(Program.Settings);string category=null;
            foreach(PropertyDescriptor property in TypeDescriptor.GetProperties(adapter).Cast<PropertyDescriptor>().OrderBy(p=>p.Category)){
                if(property.Category!=category){category=property.Category;design.Section(category.Length>3?category[3..]:category);}
                Control field;
                void Apply(object value){property.SetValue(adapter,value);ApplyAdvancedSettingsChange(property.Name);MarkSettingsChanged();}
                if(property.PropertyType==typeof(bool)){
                    var original=new CheckBox{Text=property.DisplayName,Checked=(bool)property.GetValue(adapter),Name="advanced_"+property.Name};
                    // Keep backing checkbox owned and hidden, with the same immediate-save semantics.
                    pgSettings.Controls.Add(original);original.Visible=false;
                    original.CheckedChanged+=(_,_)=>Apply(original.Checked);design.Toggle(property.DisplayName,original,property.Description);continue;
                }
                if(property.PropertyType==typeof(int)){
                    var number=new NumericUpDown{Minimum=200,Maximum=int.MaxValue,Increment=100,Value=(int)property.GetValue(adapter),Name="advanced_"+property.Name};
                    number.ValueChanged+=(_,_)=>{Apply((int)number.Value);number.Value=(int)property.GetValue(adapter);};field=design.Inline(number,design.Unit("毫秒"));
                } else {
                    var text=new TextBox{Text=(string)property.GetValue(adapter),Name="advanced_"+property.Name};
                    text.TextChanged+=(_,_)=>Apply(text.Text);
                    var browse=new Button{Text="…",Name="browse_"+property.Name};
                    browse.Click+=(_,_)=>{
                        if(property.Name==nameof(ApplicationConfig.BrowserPath)) { using var dialog=new OpenFileDialog{Filter="程序 (*.exe)|*.exe",CheckFileExists=true};if(dialog.ShowDialog(this)==DialogResult.OK)text.Text=dialog.FileName; }
                        else { using var dialog=new FolderBrowserDialog();if(dialog.ShowDialog(this)==DialogResult.OK)text.Text=dialog.SelectedPath; }
                    };
                    field=design.Inline(text,browse);text.Width=design.S(260);
                }
                design.Editor(property.DisplayName,field,property.Description);
            }
            design.Hint("更改配置存放位置前请先备份。专业兼容选项通常保持原设置即可。");
        }
    }
}

