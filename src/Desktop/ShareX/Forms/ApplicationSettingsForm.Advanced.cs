using ShareX.HelpersLib;
using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private void InitializeAdvancedSettings()
        {
            // A presentation-only adapter keeps legacy JSON keys and hidden values intact.
            pgSettings.SelectedObject = new AdvancedApplicationSettings(Program.Settings);
            pgSettings.PropertyValueChanged -= AdvancedSettings_PropertyValueChanged;
            pgSettings.PropertyValueChanged += AdvancedSettings_PropertyValueChanged;
        }

        private void AdvancedSettings_PropertyValueChanged(object sender, PropertyValueChangedEventArgs e)
            => ApplyAdvancedSettingsChange(e.ChangedItem.PropertyDescriptor?.Name);

        private void ApplyAdvancedSettingsChange(string propertyName)
        {
            ApplicationConfig settings = Program.Settings;
            switch (propertyName)
            {
                case nameof(ApplicationConfig.DisableHotkeys):
                    Program.HotkeyManager?.ToggleHotkeys(settings.DisableHotkeys);
                    Program.MainForm?.UpdateToggleHotkeyButton();
                    break;
                case nameof(ApplicationConfig.HotkeyRepeatLimit):
                    if (Program.MainForm != null) Program.MainForm.HotkeyRepeatLimit = settings.HotkeyRepeatLimit;
                    break;
            }

            HelpersOptions.BrowserPath = settings.BrowserPath;
            HelpersOptions.DefaultCopyImageFillBackground = settings.DefaultClipboardCopyImageFillBackground;
            HelpersOptions.UseAlternativeClipboardCopyImage = settings.UseAlternativeClipboardCopyImage;
            HelpersOptions.UseAlternativeClipboardGetImage = settings.UseAlternativeClipboardGetImage;
            HelpersOptions.RotateImageByExifOrientationData = settings.RotateImageByExifOrientationData;
            HelpersOptions.DevMode = settings.DevMode;
            // Refresh clamped numeric values rather than displaying an unapplied input.
            pgSettings.Refresh();
        }
    }

    internal sealed class AdvancedApplicationSettings
    {
        private readonly ApplicationConfig settings;

        public AdvancedApplicationSettings(ApplicationConfig settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        [Category("01 程序"), DisplayName("使用 1024 进制显示大小"), DefaultValue(false), TypeConverter(typeof(AdvancedSwitchConverter))]
        [Description("开启：按 1024 进制显示 KiB、MiB；关闭：按 1000 进制显示 KB、MB。仅影响支持此选项的任务大小提示，不改变文件内容，也不统一更改各文件夹列表。")]
        public bool BinaryUnits { get => settings.BinaryUnits; set => settings.BinaryUnits = value; }

        [Category("01 程序"), DisplayName("打开网页的浏览器"), DefaultValue("")]
        [Editor(typeof(ExeFileNameEditor), typeof(UITypeEditor))]
        [Description("留空时使用 Windows 默认浏览器。也可选择浏览器的 .exe 文件，用于打开天气、资讯等网页链接。")]
        public string BrowserPath { get => settings.BrowserPath; set => settings.BrowserPath = value; }

        [Category("01 程序"), DisplayName("任务完成后额外保存设置"), DefaultValue(false), TypeConverter(typeof(AdvancedSwitchConverter))]
        [Description("开启后，截图等任务完成且没有其他任务运行时，额外保存一次程序设置。关闭不影响原有设置保存机制；这不是 Markdown 自动保存开关。")]
        public bool SaveSettingsAfterTaskCompleted { get => settings.SaveSettingsAfterTaskCompleted; set => settings.SaveSettingsAfterTaskCompleted = value; }

        [Category("02 快捷键"), DisplayName("禁用快捷键"), DefaultValue(false), TypeConverter(typeof(AdvancedSwitchConverter))]
        [Description("开启后禁用程序快捷键，与托盘菜单的“禁用快捷键”同步。关闭后恢复；修改立即生效。")]
        public bool DisableHotkeys { get => settings.DisableHotkeys; set => settings.DisableHotkeys = value; }

        [Category("02 快捷键"), DisplayName("全屏应用中停用快捷键"), DefaultValue(false), TypeConverter(typeof(AdvancedSwitchConverter))]
        [Description("开启后，当前前台窗口处于全屏状态时不执行程序快捷键，用于避免游戏、演示中误触截图。修改立即生效。")]
        public bool DisableHotkeysOnFullscreen { get => settings.DisableHotkeysOnFullscreen; set => settings.DisableHotkeysOnFullscreen = value; }

        [Category("02 快捷键"), DisplayName("重复触发间隔（毫秒）"), DefaultValue(500)]
        [Description("持续按住快捷键时，两次执行之间的最短间隔。500 毫秒等于 0.5 秒；最小值为 200 毫秒，小于 200 的输入会自动调整为 200。修改立即生效。")]
        public int HotkeyRepeatLimit { get => settings.HotkeyRepeatLimit; set => settings.HotkeyRepeatLimit = value; }

        [Category("03 路径"), DisplayName("快捷键配置存放位置")]
        [Editor(typeof(DirectoryNameEditor), typeof(UITypeEditor))]
        [Description("留空时使用程序默认配置位置。修改后，后续保存及下次启动读取快捷键配置都会使用此目录。请先备份；使用已有配置目录时，应在退出程序后恢复备份，再启动程序，避免旧配置被当前配置覆盖。")]
        public string CustomHotkeysConfigPath { get => settings.CustomHotkeysConfigPath; set => settings.CustomHotkeysConfigPath = value; }

        [Category("03 路径"), DisplayName("截图备用保存位置")]
        [Editor(typeof(DirectoryNameEditor), typeof(UITypeEditor))]
        [Description("自定义截图目录暂时不可用时（例如网络目录断开），使用此目录作为备用保存位置。建议选择本机目录；不需要备用位置时可留空。")]
        public string CustomScreenshotsPath2 { get => settings.CustomScreenshotsPath2; set => settings.CustomScreenshotsPath2 = value; }

        [Category("04 专业：图片兼容"), DisplayName("复制图片时填充白色背景"), DefaultValue(true), TypeConverter(typeof(AdvancedSwitchConverter))]
        [Description("使用普通图片复制方式时，将透明部分填成白色，避免粘贴后出现黑底。开启“复制图片保留透明背景”时改用透明复制方式，本项不再参与该复制过程。")]
        public bool DefaultClipboardCopyImageFillBackground { get => settings.DefaultClipboardCopyImageFillBackground; set => settings.DefaultClipboardCopyImageFillBackground = value; }

        [Category("04 专业：图片兼容"), DisplayName("复制图片保留透明背景"), DefaultValue(false), TypeConverter(typeof(AdvancedSwitchConverter))]
        [Description("使用 PNG、32 位 DIB 等剪贴板格式保留透明背景。能否正确粘贴取决于接收软件；普通无透明背景的截图通常无需开启。")]
        public bool UseAlternativeClipboardCopyImage { get => settings.UseAlternativeClipboardCopyImage; set => settings.UseAlternativeClipboardCopyImage = value; }

        [Category("04 专业：图片兼容"), DisplayName("读取图片保留透明背景"), DefaultValue(false), TypeConverter(typeof(AdvancedSwitchConverter))]
        [Description("读取剪贴板图片时优先检查 PNG、32 位 DIB 格式，以保留透明背景。遇到图片读取兼容问题时可尝试此选项。")]
        public bool UseAlternativeClipboardGetImage { get => settings.UseAlternativeClipboardGetImage; set => settings.UseAlternativeClipboardGetImage = value; }

        [Category("04 专业：图片兼容"), DisplayName("按照片方向信息自动旋转"), DefaultValue(true), TypeConverter(typeof(AdvancedSwitchConverter))]
        [Description("读取带 EXIF 方向信息的 JPEG 照片时，自动调整到正确方向。主要用于相机、手机照片，通常建议开启；不影响长截图的拼接方向。")]
        public bool RotateImageByExifOrientationData { get => settings.RotateImageByExifOrientationData; set => settings.RotateImageByExifOrientationData = value; }

        [Category("04 专业：图片兼容"), DisplayName("移除 PNG 色彩空间信息"), DefaultValue(false), TypeConverter(typeof(AdvancedSwitchConverter))]
        [Description("保存 PNG 时移除色彩空间信息，用于排查部分软件的颜色显示兼容问题。通常保持关闭；此项不是图片清晰度或长图长度设置。")]
        public bool PNGStripColorSpaceInformation { get => settings.PNGStripColorSpaceInformation; set => settings.PNGStripColorSpaceInformation = value; }

        [Category("05 专业：诊断"), DisplayName("显示开发诊断信息"), DefaultValue(false), TypeConverter(typeof(AdvancedSwitchConverter))]
        [Description("显示额外版本、运行权限等诊断信息。普通使用保持关闭；不会解锁新功能。标题信息在离开全局设置页面后刷新。")]
        public bool DevMode { get => settings.DevMode; set => settings.DevMode = value; }
    }

    public sealed class AdvancedSwitchConverter : BooleanConverter
    {
        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is bool enabled) return enabled ? "开启" : "关闭";
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string text)
            {
                if (text.Trim() == "开启") return true;
                if (text.Trim() == "关闭") return false;
            }
            return base.ConvertFrom(context, culture, value);
        }
    }
}
