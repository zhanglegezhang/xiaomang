using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    internal sealed record ProductRelease(Version Version, string Tag, string Notes, string Download, string Hash, long Size);

    internal static class ProductUpdater
    {
        internal const string Repository = "https://github.com/zhanglegezhang/xiaomang";
        internal static readonly HttpClient Http = CreateClient();
        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Xiaomang/" + Program.ProductVersion);
            return client;
        }
        internal static async Task<ProductRelease> Latest(CancellationToken token)
        {
            using var response = await Http.GetAsync("https://api.github.com/repos/zhanglegezhang/xiaomang/releases/latest", token);
            if (response.IsSuccessStatusCode) return ParseRelease(await response.Content.ReadAsStringAsync(token));
            if ((int)response.StatusCode is not (403 or 429)) response.EnsureSuccessStatusCode();
            // Public GitHub API quotas can be exhausted on shared networks. Use the
            // release redirect and version-pinned repository metadata, never a guessed URL/hash.
            using var page = await Http.GetAsync(Repository + "/releases/latest", HttpCompletionOption.ResponseHeadersRead, token);
            page.EnsureSuccessStatusCode();
            string url = page.RequestMessage.RequestUri.AbsoluteUri;
            if (!url.StartsWith(Repository + "/releases/tag/", StringComparison.Ordinal)) throw new IOException("无法确认最新正式发布。");
            string tag = url[(Repository.Length + "/releases/tag/".Length)..];
            if (!Regex.IsMatch(tag, @"^v\d+\.\d+-\d{8}(?:-r\d+)?$")) throw new IOException("发布标签无法识别。");
            string metadata = await Http.GetStringAsync("https://raw.githubusercontent.com/zhanglegezhang/xiaomang/" + tag + "/docs/RELEASE-" + tag + ".json", token);
            using var document = JsonDocument.Parse(metadata); var root = document.RootElement;
            if (root.GetProperty("releaseTag").GetString() != tag) throw new IOException("更新清单标签不匹配。");
            var package = root.GetProperty("usagePackage");
            return ParseRelease(JsonSerializer.Serialize(new
            {
                draft = false, prerelease = false, tag_name = tag,
                body = "发现正式发布 " + tag + "。详细更新说明请点击“发布页面”；更新前会备份个人数据。",
                assets = new[] { new { name = package.GetProperty("asset").GetString(), browser_download_url = package.GetProperty("url").GetString(), digest = "sha256:" + package.GetProperty("sha256").GetString(), size = package.GetProperty("bytes").GetInt64() } }
            }));
        }
        internal static ProductRelease ParseRelease(string json)
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) throw new IOException("未找到正式版本。");
            string tag = root.GetProperty("tag_name").GetString();
            var match = Regex.Match(tag ?? "", @"^v(\d+\.\d+)-\d{8}(?:-r\d+)?$");
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version)) throw new IOException("发布版本格式无法识别。");
            var assets = root.GetProperty("assets").EnumerateArray().Where(x => Regex.IsMatch(x.GetProperty("name").GetString() ?? "", @"^xiaomang" + Regex.Escape(version.ToString()) + @"_\d{4}-\d{2}-\d{2}_usage\.zip$")).ToArray();
            if (assets.Length != 1) throw new IOException("该版本缺少唯一的使用包。");
            var asset = assets[0]; string name = asset.GetProperty("name").GetString();
            string url = asset.GetProperty("browser_download_url").GetString();
            if (url != Repository + "/releases/download/" + tag + "/" + name) throw new IOException("更新来源不匹配。");
            string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            if (!Regex.IsMatch(digest ?? "", "^sha256:[a-fA-F0-9]{64}$")) throw new IOException("该发布没有 SHA256 校验值，请前往发布页下载。");
            long size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > 1024L * 1024 * 1024) throw new IOException("更新包大小超出支持范围。");
            return new ProductRelease(version, tag, root.GetProperty("body").GetString() ?? "", url, digest[7..], size);
        }
        internal static async Task Download(ProductRelease release, string destination, IProgress<int> progress, CancellationToken token)
        {
            using var response = await Http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            using var input = await response.Content.ReadAsStreamAsync(token);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920]; long total = 0; int count, last = -1;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                total += count; if (total > release.Size) throw new IOException("下载长度超出发布清单。");
                hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token);
                int percent = (int)(total * 100 / release.Size); if (percent != last) { last = percent; progress.Report(percent); }
            }
            if (total != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("下载校验失败，旧程序未修改。");
        }
        internal static string Extract(string archive, string destination, string personal, string app, CancellationToken token)
        {
            using var zip = ZipFile.OpenRead(archive);
            if (zip.Entries.Count > 10000) throw new IOException("更新包文件数异常。");
            string prefix = null; long bytes = 0;
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                string path = entry.FullName;
                if (string.IsNullOrEmpty(entry.Name)) continue;
                PersonalDataArchive.Relative(path);
                int slash = path.IndexOf('/'); if (slash < 1) throw new IOException("更新包目录结构无效。");
                prefix ??= path[..slash]; if (prefix != path[..slash]) throw new IOException("更新包包含多个根目录。");
                string relative = path[(slash + 1)..]; if (!seen.Add(relative)) throw new IOException("更新包含重复路径。");
                string target = Path.GetFullPath(Path.Combine(app, relative));
                string personalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(personal));
                if (target.Equals(personalRoot, StringComparison.OrdinalIgnoreCase) || target.StartsWith(personalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(relative, @"(?i)(^|/)(ApplicationConfig\.json|HotkeysConfig\.json|UploadersConfig\.json|first-use\.bin|SpeechModels|XiaomangInputMethod|ffmpeg\.exe|ffprobe\.exe)(/|$)"))
                    throw new IOException("更新包包含个人数据或按需组件，已拒绝覆盖。");
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (bytes += entry.Length) > 2L * 1024 * 1024 * 1024) throw new IOException("更新包含链接或解压大小异常。");
                PersonalDataArchive.SafePath(target);
                string output = Path.Combine(destination, relative); Directory.CreateDirectory(Path.GetDirectoryName(output)); entry.ExtractToFile(output);
            }
            if (!seen.Contains("xiaomang.exe") || !seen.Contains("ShareX.dll") || !seen.Contains("ShareX.runtimeconfig.json")) throw new IOException("更新包不完整。");
            // Existing official releases use the GitHub asset digest for the entire ZIP.
            // When an internal manifest is present, validate it as an additional check.
            if (!seen.Contains("files.sha256")) return destination;
            var verified = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadLines(Path.Combine(destination, "files.sha256")))
            {
                token.ThrowIfCancellationRequested(); if (string.IsNullOrWhiteSpace(line)) continue;
                var match = Regex.Match(line, @"^([A-Fa-f0-9]{64})\s+\*?(.+)$");
                if (!match.Success) throw new IOException("文件校验清单格式无效。");
                string relative = PersonalDataArchive.Relative(match.Groups[2].Value.Replace('\\', '/'));
                if (!verified.Add(relative) || !seen.Contains(relative) || !PersonalDataArchive.Hash(Path.Combine(destination, relative)).Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) throw new IOException("文件校验失败。");
            }
            if (!seen.Where(x => x != "files.sha256").All(verified.Contains)) throw new IOException("存在未校验的更新文件。");
            return destination;
        }
    }

    internal sealed class ProductUpdateForm : Form
    {
        private static ProductUpdateForm current;
        private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label detail = new() { AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label notesHeading = new() { Text = "更新内容", AutoSize = true, Dock = DockStyle.Top };
        private readonly ProductUpdateStatusIcon stateIcon = new() { Dock = DockStyle.Fill };
        private readonly TextBox notes = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly ProductUpdateProgress progress = new() { Dock = DockStyle.Fill, Visible = false };
        private readonly ProductUpdateButton update = new() { Text = "下载并更新", Enabled = false, Primary = true };
        private readonly ProductUpdateButton check = new() { Text = "重新检查" };
        private readonly CancellationTokenSource cancellation = new();
        private ProductRelease release;
        private bool busy, committing;
        internal static void Open()
        {
            if (current == null || current.IsDisposed) current = new ProductUpdateForm();
            current.Show(); current.Activate();
        }
        internal ProductUpdateForm()
        {
            SuspendLayout();
            Text = "小蟒 · 检查更新"; Icon = ShareX.HelpersLib.ShareXResources.Icon;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 10); ClientSize = new Size(600, 390);
            MinimumSize = new Size(560, 390); StartPosition = FormStartPosition.CenterScreen;
            BackColor = ProductUpdateStyle.Background; ForeColor = ProductUpdateStyle.Text; Padding = new Padding(20, 16, 20, 16);
            status.Font = new Font(Font.FontFamily, 16, FontStyle.Bold); detail.ForeColor = ProductUpdateStyle.Muted;
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 14) };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var titles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(12, 0, 0, 0) };
            titles.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); titles.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            titles.Controls.Add(status, 0, 0); titles.Controls.Add(detail, 0, 1);
            header.Controls.Add(stateIcon, 0, 0); header.Controls.Add(titles, 1, 0);
            var card = new Panel { Dock = DockStyle.Fill, BackColor = ProductUpdateStyle.Surface, Padding = new Padding(14), Margin = Padding.Empty };
            card.Paint += (_, e) =>
            {
                if (card.Width < 2 || card.Height < 2) return;
                e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = ProductUpdateStyle.Rounded(new RectangleF(.5f, .5f, card.Width - 1, card.Height - 1), 5 * DeviceDpi / 96f);
                using var fill = new SolidBrush(ProductUpdateStyle.Surface); using var pen = new Pen(ProductUpdateStyle.Border);
                e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path);
            };
            notesHeading.Font = new Font(Font, FontStyle.Bold); notesHeading.Padding = new Padding(0, 0, 0, 8);
            notes.BackColor = ProductUpdateStyle.Surface; notes.ForeColor = ForeColor; notes.BorderStyle = BorderStyle.None;
            notes.HandleCreated += (_, _) => NativeMethods.SetWindowTheme(notes.Handle, "DarkMode_Explorer", null);
            card.Controls.Add(notes); card.Controls.Add(notesHeading);
            var hint = new Label { Text = "更新前会保存并备份个人数据，完成后重启小蟒。", Dock = DockStyle.Fill, ForeColor = ProductUpdateStyle.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var page = new LinkLabel { Text = "↗ 发布页面", AutoSize = true, Anchor = AnchorStyles.Left, LinkColor = Color.FromArgb(111,190,255), ActiveLinkColor = Color.FromArgb(165,216,255), LinkBehavior = LinkBehavior.HoverUnderline, Margin = Padding.Empty };
            page.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(ProductUpdater.Repository + "/releases") { UseShellExecute = true });
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            buttons.Controls.Add(check); buttons.Controls.Add(update); footer.Controls.Add(page, 0, 0); footer.Controls.Add(buttons, 1, 0);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(header,0,0); layout.Controls.Add(card,0,1); layout.Controls.Add(hint,0,2); layout.Controls.Add(progress,0,3); layout.Controls.Add(footer,0,4);
            Controls.Add(layout);
            SetState("checking", "正在检查正式版本…", "当前版本 " + Program.ProductVersion);
            Shown += async (_, _) => await Check(); check.Click += async (_, _) => await Check(); update.Click += async (_, _) => await Install();
            FormClosing += (_, e) => { if (committing) { e.Cancel = true; return; } cancellation.Cancel(); };
            AutoScaleDimensions = new SizeF(96, 96);
            ResumeLayout(true);
        }
        private void SetState(string state, string title, string subtitle)
        {
            status.Text = title; detail.Text = subtitle; stateIcon.State = state; stateIcon.Invalidate();
            check.Primary = state == "failed"; update.Primary = state != "failed";
            progress.Visible = state is "downloading" or "installing";
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int background = ColorTranslator.ToWin32(ProductUpdateStyle.Background);
            int foreground = ColorTranslator.ToWin32(ProductUpdateStyle.Text);
            NativeMethods.DwmSetWindowAttribute(Handle, (int)DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, ref background, sizeof(int));
            NativeMethods.DwmSetWindowAttribute(Handle, (int)DWMWINDOWATTRIBUTE.DWMWA_TEXT_COLOR, ref foreground, sizeof(int));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { status.Font.Dispose(); notesHeading.Font.Dispose(); }
            base.Dispose(disposing);
        }
        private async Task Check()
        {
            if (busy) return; busy = true; check.Enabled = update.Enabled = false;
            SetState("checking", "正在检查正式版本…", "当前版本 " + Program.ProductVersion);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var found = await Task.Run(() => ProductUpdater.Latest(timeout.Token), timeout.Token).WaitAsync(timeout.Token);
                if (IsDisposed) return;
                release = found; notes.Text = release.Notes;
                bool newer = release.Version > Version.Parse(Program.ProductVersion);
                notesHeading.Text = newer ? "更新内容" : "当前版本说明";
                SetState(newer ? "available" : "current", newer ? "发现新版本" : "已是最新正式版本",
                    newer ? $"当前 {Program.ProductVersion} → 新版本 {release.Version}" : $"当前版本 {Program.ProductVersion}");
                update.Enabled = newer;
            }
            catch (Exception error) { if (!IsDisposed) { SetState("failed", "检查未完成", "请重新检查，或打开发布页面查看。"); notesHeading.Text = "详细信息"; notes.Text = error.Message; } }
            finally { busy = false; if (!IsDisposed) check.Enabled = true; }
        }
        private async Task Install()
        {
            if (busy || release == null) return;
            if (MessageBox.Show(this, "更新会先下载并校验，然后保存并关闭编辑窗口、备份个人数据，最后重启小蟒。是否继续？", "更新小蟒", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            busy = true; check.Enabled = update.Enabled = false;
            string stage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XiaomangUpdates", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(stage); string zip = Path.Combine(stage, "update.zip");
                progress.Value = 0;
                SetState("downloading", "正在下载更新包", "下载完成后将进行校验。");
                var downloadProgress = new Progress<int>(n => { if (!IsDisposed) progress.Value = n; });
                await Task.Run(() => ProductUpdater.Download(release, zip, downloadProgress, cancellation.Token), cancellation.Token).WaitAsync(cancellation.Token);
                SetState("installing", "校验并解压程序文件…", "请稍候，完成后将保存并备份个人数据。");
                string payload = await Task.Run(() => ProductUpdater.Extract(zip, Path.Combine(stage, "payload"), Program.PersonalFolder, AppContext.BaseDirectory, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                committing = true; SetState("installing", "保存并备份个人数据…", "完成后重启小蟒。"); progress.Style = ProgressBarStyle.Marquee;
                string backup = Path.Combine(stage, "personal-backup.zip");
                await PersonalDataManager.BackupForUpdate(this, backup, new Progress<string>(text => { if (!IsDisposed) { detail.Text = "正在保存并备份个人数据…"; notes.Text = text; } }), async () =>
                {
                    if (!await Program.MainForm.PrepareApplicationExitAsync()) throw new IOException("保存或退出准备未完成，已取消更新。");
                    await ProductUpdateInstaller.Start(stage, payload);
                    committing = false; Program.MainForm.ForceClose();
                });
            }
            catch (Exception error)
            {
                Program.MainForm.CancelApplicationExitPreparation();
                if (!IsDisposed) { SetState("failed", "更新未完成", "原程序保留，可重试。"); notesHeading.Text = "详细信息"; notes.AppendText(Environment.NewLine + error.Message + Environment.NewLine + "更新资料及备份：" + stage); }
            }
            finally { committing = busy = false; if (!IsDisposed) { check.Enabled = true; update.Enabled = true; progress.Style = ProgressBarStyle.Blocks; } }
        }
    }
}
