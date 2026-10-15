using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private sealed class PathLocationRow
        {
            public string Name;
            public Func<string> Resolve;
            public bool IsFile;
            public Label Title;
            public Label Description;
            public TextBox Path;
            public Button Open;
        }

        private readonly List<PathLocationRow> pathLocationRows = new List<PathLocationRow>();
        private Panel pathLocationsPanel;
        private bool arrangingPathLocations;

        private int PathPx(int logicalPixels) =>
            (int)Math.Round(logicalPixels * tpPaths.DeviceDpi / 96f);

        private void InitializePathLocations()
        {
            tpPaths.AutoScroll = true;
            pathLocationsPanel = new Panel
            {
                Location = new Point(PathPx(16), PathPx(16)),
                Size = new Size(Math.Max(PathPx(400), tpPaths.ClientSize.Width - PathPx(32)), 1),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tpPaths.Controls.Add(pathLocationsPanel);

            int top = 0;
            AddPathHeading("各功能的实际存储位置", ref top);
            AddPathHint("本页只显示当前使用的路径；点击“打开”可进入所在文件夹。", ref top);

            AddPathHeading("主要位置", ref top);
            AddPathLocation("个人文件夹", "存放程序配置和用户数据；下方多数路径都位于此文件夹内。",
                () => Program.PersonalFolder, false, ref top);
            AddPathLocation("截图文件夹", "截图的根目录；可由程序当前设置指向自定义位置。",
                () => Program.ScreenshotsParentFolder, false, ref top);
            AddPathLocation("当前截图输出", "当前日期等子文件夹规则计算出的默认保存位置；单个任务也可另设位置。",
                () => TaskHelpers.GetScreenshotsFolder(), false, ref top);

            AddPathHeading("配置与历史", ref top);
            AddPathLocation("应用配置", "保存待办、剪贴板、收藏夹及其他程序设置。",
                () => System.IO.Path.Combine(Program.PersonalFolder, "ApplicationConfig.json"), true, ref top);
            AddPathLocation("快捷键配置", "保存快捷键和对应的任务动作。",
                GetHotkeysConfigDisplayPath, true, ref top);
            AddPathLocation("任务历史", "保存 ShareX 截图、上传等已完成任务的历史记录。",
                () => Program.HistoryFilePath, true, ref top);

            AddPathHeading("用户文件", ref top);
            AddPathLocation("本地日记", "存放日记正文、图片附件、历史快照和回收站。",
                () => LocalDiaryManager.DataFolder, false, ref top);
            AddPathLocation("便签日志", "存放待办、剪贴板、收藏夹三类操作日志。",
                () => System.IO.Path.Combine(Program.PersonalFolder, "便签日志"), false, ref top);
            AddPathLocation("临时文件", "存放工具中的临时文件，按月份整理。",
                () => System.IO.Path.Combine(Program.PersonalFolder, "临时文件夹"), false, ref top);
            AddPathLocation("语音录音", "闪念胶囊的录音文件，按日期存放。",
                () => System.IO.Path.Combine(Program.PersonalFolder, "VoiceNotes"), false, ref top);
            AddPathLocation("每日资讯", "国内和国际资讯生成的 HTML、JSON 文件。",
                () => System.IO.Path.Combine(Program.PersonalFolder, "daily_news"), false, ref top);

            AddPathHeading("模型、缓存与运行文件", ref top);
            AddPathLocation("输入法后台与资源", "小蟒专属输入法后台、公开词库和设置程序，随小蟒一起移动。",
                () => XiaomangInputMethodManager.BundleDirectory, false, ref top);
            AddPathLocation("输入法系统组件", "注册后位于受保护目录的 x64 / x86 输入组件；移动小蟒不需要改写此处。",
                () => XiaomangInputMethodManager.ComponentDirectory, false, ref top);
            AddPathLocation("输入法用户数据", "小蟒输入法的设置、学习词库和统计；撤销注册时保留，与原青简独立。",
                () => XiaomangInputMethodManager.UserDirectory, false, ref top);
            AddPathLocation("输入法日志", "小蟒输入法后台、输入组件和设置程序的运行日志。",
                () => XiaomangInputMethodManager.LogDirectory, false, ref top);
            AddPathLocation("离线语音模型", "按需下载的 SenseVoice 模型文件；未安装时文件夹可能不存在。",
                () => System.IO.Path.Combine(Program.PersonalFolder, "SpeechModels", "SenseVoiceSmall"), false, ref top);
            AddPathLocation("资讯网页缓存", "资讯页面的 WebView 缓存，可由资讯设置中的清理功能管理。",
                () => System.IO.Path.Combine(Program.PersonalFolder, "DailyNewsWebView"), false, ref top);
            AddPathLocation("Markdown 预览缓存", "便签 Markdown 预览所用的 WebView 数据。",
                () => System.IO.Path.Combine(Program.PersonalFolder, "ScratchMarkdownWebView"), false, ref top);
            AddPathLocation("翻译网页缓存", "OCR 翻译页面所用的 WebView 数据。",
                () => System.IO.Path.Combine(Program.PersonalFolder, "TranslationWebView"), false, ref top);
            AddPathLocation("图片效果", "保存图片效果相关的预设和资源。",
                () => Program.ImageEffectsFolder, false, ref top);
            AddPathLocation("运行日志", "保存程序运行和错误日志。",
                () => Program.LogsFolder, false, ref top);
            AddPathLocation("配置备份", "保存程序自动生成的配置文件备份。",
                () => SettingManager.BackupFolder, false, ref top);

            pathLocationsPanel.Height = top;
            tpPaths.AutoScrollMinSize = new Size(0, pathLocationsPanel.Bottom + PathPx(16));
            tpPaths.SizeChanged += (sender, e) => LayoutPathLocations();
            pathLocationsPanel.FontChanged += (sender, e) => LayoutPathLocations();
            LayoutPathLocations();
        }

        private void LayoutPathLocations()
        {
            if (arrangingPathLocations || pathLocationsPanel == null || pathLocationsPanel.IsDisposed) return;
            arrangingPathLocations = true;
            try
            {
                int width = Math.Max(1, tpPaths.ClientSize.Width - PathPx(32));
                pathLocationsPanel.Width = width;
                int top = 0;
                foreach (Control control in pathLocationsPanel.Controls)
                {
                    control.Top = top;
                    control.Width = width;
                    if (control is Label label)
                    {
                        label.Height = Math.Max(PathPx(22), label.GetPreferredSize(new Size(width, 0)).Height);
                        top += label.Height + PathPx(8);
                        continue;
                    }
                    PathLocationRow row = pathLocationRows.Find(value => value.Title.Parent == control);
                    if (row == null) continue;
                    row.Title.Bounds = new Rectangle(0, 0, width, Math.Max(PathPx(18), row.Title.Font.Height));
                    int detailHeight = Math.Max(row.Description.Font.Height, row.Description.GetPreferredSize(new Size(width, 0)).Height);
                    row.Description.Bounds = new Rectangle(0, row.Title.Bottom + PathPx(3), width, detailHeight);
                    int inputTop = row.Description.Bottom + PathPx(10);
                    int buttonWidth = Math.Max(PathPx(82), TextRenderer.MeasureText(row.Open.Text, row.Open.Font).Width + PathPx(24));
                    int buttonHeight = Math.Max(PathPx(25), row.Open.Font.Height + PathPx(10));
                    row.Open.Bounds = new Rectangle(Math.Max(0, width - buttonWidth), inputTop, buttonWidth, buttonHeight);
                    int inputHeight = row.Path.PreferredHeight;
                    row.Path.Bounds = new Rectangle(0, inputTop + Math.Max(0, (buttonHeight - inputHeight) / 2),
                        Math.Max(1, row.Open.Left - PathPx(8)), inputHeight);
                    control.Height = Math.Max(row.Path.Bottom, row.Open.Bottom) + PathPx(12);
                    top += control.Height;
                }
                pathLocationsPanel.Height = top;
                tpPaths.AutoScrollMinSize = new Size(0, pathLocationsPanel.Bottom + PathPx(16));
            }
            finally { arrangingPathLocations = false; }
        }

        private void AddPathHeading(string text, ref int top)
        {
            Label heading = new Label
            {
                Text = text,
                Font = new Font(tpPaths.Font.FontFamily, tpPaths.Font.SizeInPoints + 2f, FontStyle.Bold),
                Tag = "storage-section-heading",
                ForeColor = MainMenuAppearance.StorageHeading,
                Location = new Point(0, top),
                Size = new Size(pathLocationsPanel.Width, PathPx(22)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            pathLocationsPanel.Controls.Add(heading);
            top += PathPx(29);
        }

        private void AddPathHint(string text, ref int top)
        {
            Label hint = new Label
            {
                Text = text,
                ForeColor = Color.Gray,
                Location = new Point(0, top),
                Size = new Size(pathLocationsPanel.Width, PathPx(24)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            pathLocationsPanel.Controls.Add(hint);
            top += PathPx(32);
        }

        private void AddPathLocation(string name, string description, Func<string> resolve, bool isFile, ref int top)
        {
            Panel container = new Panel
            {
                Location = new Point(0, top),
                Size = new Size(pathLocationsPanel.Width, PathPx(77)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Label title = new Label
            {
                Location = new Point(0, 0),
                Size = new Size(container.Width, PathPx(18)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = name
            };
            Label detail = new Label
            {
                Location = new Point(0, PathPx(19)),
                Size = new Size(container.Width, PathPx(18)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = false,
                ForeColor = Color.Gray,
                Text = description
            };
            TextBox path = new TextBox
            {
                Location = new Point(0, PathPx(42)),
                Size = new Size(Math.Max(PathPx(100), container.Width - PathPx(90)), PathPx(22)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true
            };
            Button open = new Button
            {
                Text = "打开",
                Location = new Point(container.Width - PathPx(82), PathPx(40)),
                Size = new Size(PathPx(82), PathPx(25)),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            PathLocationRow row = new PathLocationRow
            {
                Name = name,
                Resolve = resolve,
                IsFile = isFile,
                Title = title,
                Description = detail,
                Path = path,
                Open = open
            };
            open.Click += (sender, e) =>
            {
                string folder = isFile ? System.IO.Path.GetDirectoryName(path.Text) : path.Text;
                if (Directory.Exists(folder)) FileHelpers.OpenFolder(folder);
            };
            container.Controls.AddRange(new Control[] { title, detail, path, open });
            pathLocationsPanel.Controls.Add(container);
            pathLocationRows.Add(row);
            top += PathPx(81);
        }

        private static string GetHotkeysConfigDisplayPath()
        {
            string customFolder = Program.Settings?.CustomHotkeysConfigPath;
            string folder = string.IsNullOrWhiteSpace(customFolder)
                ? Program.PersonalFolder : FileHelpers.ExpandFolderVariables(customFolder);
            return System.IO.Path.Combine(folder, "HotkeysConfig.json");
        }

        private void RefreshPathLocations()
        {
            foreach (PathLocationRow row in pathLocationRows)
            {
                try
                {
                    string location = row.Resolve();
                    row.Path.Text = location ?? "不可用";
                    bool exists = row.IsFile ? File.Exists(location) : Directory.Exists(location);
                    row.Title.Text = exists ? row.Name : row.Name + "（尚未生成）";
                    string folder = row.IsFile ? System.IO.Path.GetDirectoryName(location) : location;
                    row.Open.Enabled = !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder);
                }
                catch (Exception error)
                {
                    row.Path.Text = "路径读取失败：" + error.Message;
                    row.Title.Text = row.Name;
                    row.Open.Enabled = false;
                }
            }
        }
    }
}
