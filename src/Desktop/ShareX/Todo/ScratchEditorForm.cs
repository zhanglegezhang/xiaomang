using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace ShareX
{
    internal sealed class ScratchEditorForm : Form
    {
        internal static readonly Color EditorBackgroundColor = Color.FromArgb(243, 248, 253);
        internal static readonly Color EditorTextColor = Color.FromArgb(32, 48, 64);
        internal static readonly Color CaptionBackgroundColor = Color.FromArgb(175, 202, 227);
        private static readonly Color ScrollThumbColor = Color.FromArgb(111, 141, 168);
        private const long MaximumOpenBytes = ScratchLargeTextEditor.MaximumBytes;
        private const string AssetHost = "scratch-assets.invalid";
        private const string PreviewHost = "scratch-preview.invalid";
        private readonly bool markdownMode;
        private readonly TextBox editor;
        private readonly ScratchLargeTextEditor markdownEditor;
        private readonly Label fileLabel;
        private readonly ScratchCaptionBar captionBar;
        private readonly Label previewMessage;
        private readonly Panel previewPanel;
        private readonly Timer previewTimer;
        private readonly Timer autosaveTimer;
        private readonly Timer scrollSyncTimer;
        private readonly MarkdownPipeline markdownPipeline;
        private readonly SplitContainer splitContainer;
        private WebView2 preview;
        private string filePath;
        private bool dirty;
        private bool previewReady;
        private bool applyingSplitRatio;
        private bool previewDocumentReady;
        private bool scrollSyncBusy;
        private bool changingDocument;
        private int lastEditorOffset;
        private int lastPreviewY;
        private string fileFingerprint;
        private Encoding documentEncoding = new UTF8Encoding(false, true);
        private string saveNotice;
        private bool saving;
        private string saveFailure;
        private long documentRevision;
        private readonly SemaphoreSlim saveGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim documentGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource saveLifetime = new CancellationTokenSource();
        private bool ownerPrepared;
        private bool closePending;
        private bool closeApproved;
        private readonly ToolTip fileTip = new ToolTip();
        private CancellationTokenSource previewCancellation;
        private readonly SemaphoreSlim previewGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim previewApplyGate = new SemaphoreSlim(1, 1);
        private int previewVersion;
        private ulong activeNavigationId;
        private string activeNavigationUri;
        private PreviewSnapshot displayedPreview;
        private readonly Dictionary<string, PreviewSnapshot> pendingPreviews = new Dictionary<string, PreviewSnapshot>();

        private sealed record PreviewBlock(int Start, int End, string Html);
        private sealed record PreviewSnapshot(PreviewBlock[] Blocks, string BaseUrl, byte[] HtmlBytes);

        private const int EmGetFirstVisibleLine = 0x00CE;
        private const int EmLineFromChar = 0x00C9;
        private const int EmLineScroll = 0x00B6;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
        internal ScratchEditorForm(bool markdownMode)
        {
            this.markdownMode = markdownMode;
            Text = markdownMode ? "临时 Markdown 编辑器" : "临时文本编辑器";
            Icon = ShareXResources.Icon;
            MinimumSize = new Size(650, 400);
            BackColor = EditorBackgroundColor;
            ForeColor = EditorTextColor;
            Font = new Font("Microsoft YaHei", 9f);
            markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            captionBar = new ScratchCaptionBar(this);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, captionBar.PreferredCaptionHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            captionBar.Dock = DockStyle.Fill;
            layout.Controls.Add(captionBar, 0, 0);
            fileLabel = captionBar.FileLabel;
            Button saveAs = captionBar.SaveAsButton;
            saveAs.Click += async (sender, args) => await SaveAsUtf8Async();
            DpiChanged += (sender, args) => layout.RowStyles[0].Height = captionBar.PreferredCaptionHeight;
            SizeChanged += (sender, args) => captionBar.RefreshWindowState();

            if (markdownMode)
            {
                markdownEditor = new ScratchLargeTextEditor
                {
                    Dock = DockStyle.Fill,
                    InputRejected = reason => ShowFileNotice(reason)
                };
                markdownEditor.ContentChanged += (sender, args) => EditorTextChanged();
                markdownEditor.SaveRequested += async (sender, args) => await SaveDocumentAsync();
            }
            else
            {
                editor = new ScratchTextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    AcceptsTab = true,
                    AcceptsReturn = true,
                    BorderStyle = BorderStyle.None,
                    BackColor = EditorBackgroundColor,
                    ForeColor = ForeColor,
                    Font = new Font("Microsoft YaHei", 10f),
                    WordWrap = true,
                    ScrollBars = ScrollBars.None,
                    MaxLength = ScratchTextBox.MaximumCharacters,
                    InputRejected = reason => ShowFileNotice(reason)
                };
                editor.TextChanged += (sender, args) => EditorTextChanged();
                editor.KeyDown += async (sender, args) =>
                {
                    if (args.Control && args.KeyCode == Keys.S)
                    {
                        args.SuppressKeyPress = true;
                        await SaveDocumentAsync();
                    }
                };
            }

            autosaveTimer = new Timer { Interval = 5000 };
            autosaveTimer.Tick += async (sender, args) =>
            {
                if (dirty && !ownerPrepared) await SaveDocumentAsync();
            };
            previewTimer = new Timer { Interval = 180 };
            previewTimer.Tick += (sender, args) =>
            {
                previewTimer.Stop();
                RenderPreview();
            };
            scrollSyncTimer = new Timer { Interval = 180 };
            scrollSyncTimer.Tick += async (sender, args) => await SyncScrollAsync();

            TableLayoutPanel editorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = new Padding(12, 8, 4, 8),
                BackColor = EditorBackgroundColor
            };
            editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(8, (int)Math.Round(8 * DeviceDpi / 96f))));
            editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editorLayout.Controls.Add(markdownMode ? markdownEditor : editor, 0, 0);
            editorLayout.Controls.Add(markdownMode
                ? new SlimMarkdownScrollBar(markdownEditor, editorLayout.BackColor) { Dock = DockStyle.Fill }
                : new SlimTextScrollBar(editor, editorLayout.BackColor) { Dock = DockStyle.Fill }, 1, 0);

            if (markdownMode)
            {
                splitContainer = new SplitContainer
                {
                    Margin = Padding.Empty,
                    Dock = DockStyle.Fill,
                    Orientation = Orientation.Vertical,
                    SplitterWidth = 5,
                    BackColor = Color.FromArgb(200, 221, 240)
                };
                splitContainer.Panel1.Controls.Add(CreatePane("编辑", editorLayout));
                previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = EditorBackgroundColor };
                previewMessage = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = "正在加载预览…",
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = ForeColor
                };
                previewPanel.Controls.Add(previewMessage);
                splitContainer.Panel2.Controls.Add(CreatePane("预览", previewPanel));
                layout.Controls.Add(splitContainer, 0, 1);
                splitContainer.SizeChanged += (sender, args) => RestoreSplitRatio();
                splitContainer.SplitterMoved += (sender, args) => RememberSplitRatio();
                Shown += async (sender, args) => await InitializePreviewAsync();
                Shown += (sender, args) => BeginInvoke(new Action(RestoreSplitRatio));
            }
            else
            {
                layout.Controls.Add(editorLayout, 0, 1);
            }

            UpdateFileLabel();
            Shown += (sender, args) => BeginInvoke(new Action(FocusEditorIfActive));
        }

        private Control CreatePane(string title, Control content)
        {
            var pane = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = EditorBackgroundColor
            };
            pane.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pane.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)Math.Round(25 * DeviceDpi / 96f)));
            pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pane.Controls.Add(new Label
            {
                Text = title, Dock = DockStyle.Fill, Margin = Padding.Empty,
                Padding = new Padding(12, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(231, 240, 249), ForeColor = Color.FromArgb(80, 104, 126)
            }, 0, 0);
            pane.Controls.Add(content, 0, 1);
            return pane;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCaptionStyle();
            ScratchCaptionBar.RefreshFrame(Handle);
        }

        protected override void WndProc(ref Message message)
        {
            // Keep the native resize frame and window styles; replace only the caption area.
            if (message.Msg == 0x0083 && message.LParam != IntPtr.Zero)
            {
                int proposedTop = Marshal.ReadInt32(message.LParam, sizeof(int));
                base.WndProc(ref message);
                int border = ScratchCaptionBar.ResizeBorder(DeviceDpi);
                Marshal.WriteInt32(message.LParam, sizeof(int), proposedTop + border);
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == 0x0084 && captionBar != null)
            {
                long packed = message.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
                if (ClientRectangle.Contains(point))
                {
                    message.Result = (IntPtr)captionBar.HitTest(PointToScreen(point));
                    return;
                }
            }
            base.WndProc(ref message);
        }

        internal bool ApplyCaptionStyle()
        {
            NativeMethods.UseImmersiveDarkMode(Handle, false);
            int background = ColorTranslator.ToWin32(CaptionBackgroundColor);
            int foreground = ColorTranslator.ToWin32(EditorTextColor);
            int backgroundResult = NativeMethods.DwmSetWindowAttribute(Handle,
                (int)DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, ref background, sizeof(int));
            int foregroundResult = NativeMethods.DwmSetWindowAttribute(Handle,
                (int)DWMWINDOWATTRIBUTE.DWMWA_TEXT_COLOR, ref foreground, sizeof(int));
            if (backgroundResult != 0 || foregroundResult != 0)
                DebugHelper.WriteLine($"Scratch editor caption customization unavailable: {backgroundResult}, {foregroundResult}.");
            return backgroundResult == 0 && foregroundResult == 0;
        }

        internal void ShowForEditing()
        {
            DebugHelper.WriteLine($"Scratch editor activation: mode={(markdownMode ? "MD" : "TXT")}, managedVisible={Visible}, nativeVisible={IsHandleCreated && NativeMethods.IsWindowVisible(Handle)}, state={WindowState}.");
            this.ForceActivate();
            // SW_HIDE at process startup can leave Visible true while the HWND is hidden.
            if (!NativeMethods.IsWindowVisible(Handle))
                NativeMethods.ShowWindow(Handle, (int)WindowShowStyle.Show);
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            NativeMethods.SetForegroundWindow(Handle);
            bool visible = NativeMethods.IsWindowVisible(Handle);
            DebugHelper.WriteLine($"Scratch editor activation completed: mode={(markdownMode ? "MD" : "TXT")}, nativeVisible={visible}, state={WindowState}.");
            if (!visible) throw new InvalidOperationException("Scratch editor window could not be shown.");
            FocusEditorIfActive();
        }

        private void FocusEditorIfActive()
        {
            if (!IsDisposed && IsHandleCreated && NativeMethods.GetForegroundWindow() == Handle)
                FocusEditor();
        }

        private string DocumentText
        {
            get => markdownMode ? markdownEditor.Text : editor.Text;
            set
            {
                if (markdownMode) markdownEditor.Text = value;
                else editor.Text = value;
            }
        }

        private void EditorTextChanged()
        {
            if (changingDocument) return;
            documentRevision++;
            dirty = true;
            CancelPreview();
            UpdateFileLabel();
            if (!autosaveTimer.Enabled) autosaveTimer.Start();
            if (markdownMode)
            {
                previewTimer.Interval = markdownEditor.TextLength > 128 * 1024 ? 650 : 180;
                previewTimer.Stop();
                previewTimer.Start();
            }
        }

        private void SetDocumentReadOnly(bool readOnly)
        {
            if (IsDisposed) return;
            if (markdownMode) markdownEditor.Editor.IsReadOnly = readOnly;
            else editor.ReadOnly = readOnly;
            captionBar.SaveAsButton.Enabled = !readOnly;
        }

        private async Task<bool> ConfirmLeaveCoreAsync()
        {
            while (await SaveDocumentAsync()) { if (!dirty) return true; }
            autosaveTimer.Stop();
            try
            {
                while (true)
                {
                    TaskDialogButton retry = new TaskDialogButton("重试保存");
                    TaskDialogButton saveAs = new TaskDialogButton("另存为…");
                    TaskDialogButton back = new TaskDialogButton("返回编辑");
                    TaskDialogButton choice = TaskDialog.ShowDialog(this, new TaskDialogPage
                    {
                        Caption = Text,
                        Heading = "保存失败，文档尚未关闭",
                        Text = "修改内容仍保留在编辑器中。可以重试保存，或另存到其他位置。",
                        Icon = TaskDialogIcon.Warning,
                        AllowCancel = true,
                        DefaultButton = back,
                        Buttons = { retry, saveAs, back }
                    });
                    if (choice == retry)
                    {
                        while (await SaveDocumentAsync()) { if (!dirty) return true; }
                    }
                    else if (choice == saveAs) return await SaveAsUtf8CoreAsync();
                    else return false;
                }
            }
            finally { if (dirty && !IsDisposed) autosaveTimer.Start(); }
        }

        internal async Task<bool> PrepareCloseFromOwnerAsync()
        {
            await documentGate.WaitAsync();
            try
            {
                if (IsDisposed || ownerPrepared) return true;
                SetDocumentReadOnly(true);
                ownerPrepared = await ConfirmLeaveCoreAsync();
                if (ownerPrepared) autosaveTimer.Stop();
                else SetDocumentReadOnly(false);
                return ownerPrepared;
            }
            finally { documentGate.Release(); }
        }

        internal void CancelPreparedClose()
        {
            ownerPrepared = false;
            closeApproved = false;
            SetDocumentReadOnly(false);
            if (dirty && !IsDisposed) autosaveTimer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (closeApproved || (ownerPrepared && !dirty && !saving))
            {
                base.OnFormClosing(e);
                return;
            }
            base.OnFormClosing(e);
            e.Cancel = true;
            if (!closePending) _ = CompleteCloseAsync();
        }

        private async Task CompleteCloseAsync()
        {
            closePending = true;
            try
            {
                if (await PrepareCloseFromOwnerAsync())
                {
                    closeApproved = true;
                    Close();
                    if (!IsDisposed) CancelPreparedClose();
                }
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Temporary document close preparation failed.");
                saveFailure = "关闭前保存未完成，编辑内容已保留：" + error.Message;
                CancelPreparedClose();
                if (!IsDisposed) UpdateFileLabel();
            }
            finally { closePending = false; }
        }

        internal async Task<bool> LoadDocumentAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            await documentGate.WaitAsync();
            try
            {
                if (IsDisposed || ownerPrepared) return false;
                SetDocumentReadOnly(true);
                if (!await ConfirmLeaveCoreAsync()) return false;
                var loaded = await Task.Run(() =>
                {
                    if (new FileInfo(path).Length > MaximumOpenBytes)
                        return (TooLarge: true, Content: (string)null, Encoding: (Encoding)null, Fingerprint: (string)null);
                    byte[] bytes = File.ReadAllBytes(path);
                    string text = TemporaryFileStorage.DecodeDocument(bytes, out Encoding encoding);
                    return (TooLarge: false, Content: text, Encoding: encoding, Fingerprint: Convert.ToHexString(SHA256.HashData(bytes)));
                });
                if (IsDisposed) return false;
                if (loaded.TooLarge)
                {
                    OfferExternalEditor(path, "文件超过内置编辑器的 5 MB 上限。");
                    return false;
                }
                if (!(markdownMode
                    ? ScratchLargeTextEditor.IsSupported(loaded.Content, out string reason)
                    : ScratchTextBox.IsSupported(loaded.Content, out reason)))
                {
                    OfferExternalEditor(path, reason);
                    return false;
                }
                filePath = path;
                documentEncoding = loaded.Encoding;
                fileFingerprint = loaded.Fingerprint;
                documentRevision++;
                saveNotice = null;
                saveFailure = null;
                changingDocument = true;
                try { DocumentText = loaded.Content; }
                finally { changingDocument = false; }
                dirty = false;
                autosaveTimer.Stop();
                UpdateFileLabel();
                FocusEditor();
                RenderPreview();
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "打开失败：" + error.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            finally
            {
                if (!ownerPrepared) SetDocumentReadOnly(false);
                documentGate.Release();
            }
        }

        private sealed record SaveResult(string Path, string Fingerprint, bool Conflict);

        private async Task<bool> SaveDocumentAsync()
        {
            await saveGate.WaitAsync();
            try
            {
                if (IsDisposed) return false;
                if (!dirty) return true;
                saving = true;
                saveFailure = null;
                autosaveTimer.Stop();
                UpdateFileLabel();
                string destination = filePath;
                string content = DocumentText;
                long revision = documentRevision;
                string expected = fileFingerprint;
                Encoding encoding = documentEncoding;
                SaveResult result = await Task.Run(() =>
                {
                    string target = destination ?? (markdownMode ? TemporaryFileStorage.NewMarkdownPath() : TemporaryFileStorage.NewTextPath());
                    string saved = TemporaryFileStorage.WriteDocumentChecked(target, content, expected, encoding,
                        TimeSpan.FromSeconds(3), saveLifetime.Token, out bool conflict);
                    return new SaveResult(saved, Convert.ToHexString(SHA256.HashData(TemporaryFileStorage.EncodeDocument(content, encoding))), conflict);
                });
                if (IsDisposed) return false;
                bool pathChanged = filePath != result.Path;
                filePath = result.Path;
                fileFingerprint = result.Fingerprint;
                if (result.Conflict) saveNotice = "检测到外部修改，双方内容已分别保留；当前文件：" + result.Path;
                dirty = documentRevision != revision;
                if (pathChanged) RenderPreview();
                return true;
            }
            catch (EncoderFallbackException)
            {
                saveFailure = "当前编码无法保存部分字符，原文件未修改。请另存为 UTF-8 文件。";
                return false;
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Temporary document autosave failed.");
                saveFailure = "内容仍在编辑器中：" + error.Message;
                return false;
            }
            finally
            {
                saving = false;
                if (!IsDisposed)
                {
                    UpdateFileLabel();
                    if (dirty && !ownerPrepared) autosaveTimer.Start();
                }
                saveGate.Release();
            }
        }

        private async Task<bool> SaveAsUtf8Async()
        {
            await documentGate.WaitAsync();
            try
            {
                if (IsDisposed || ownerPrepared) return false;
                SetDocumentReadOnly(true);
                return await SaveAsUtf8CoreAsync();
            }
            finally
            {
                if (!ownerPrepared) SetDocumentReadOnly(false);
                documentGate.Release();
            }
        }

        private async Task<bool> SaveAsUtf8CoreAsync()
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = markdownMode ? "Markdown 文件|*.md" : "文本文件|*.txt",
                DefaultExt = markdownMode ? "md" : "txt",
                FileName = string.IsNullOrEmpty(filePath) ? (markdownMode ? "文档-UTF8.md" : "文档-UTF8.txt") : Path.GetFileName(filePath),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            return await SaveDocumentAsUtf8Async(dialog.FileName);
        }

        private async Task<bool> SaveDocumentAsUtf8Async(string destination)
        {
            await saveGate.WaitAsync();
            try
            {
                if (IsDisposed) return false;
                saving = true;
                saveFailure = null;
                autosaveTimer.Stop();
                UpdateFileLabel();
                Encoding utf8 = new UTF8Encoding(false, true);
                string content = DocumentText;
                long revision = documentRevision;
                SaveResult result = await Task.Run(() =>
                {
                    string path = TemporaryFileStorage.WriteDocumentChecked(destination, content,
                        TemporaryFileStorage.GetFileFingerprint(destination), utf8,
                        TimeSpan.FromSeconds(3), saveLifetime.Token, out bool conflict);
                    return new SaveResult(path, Convert.ToHexString(SHA256.HashData(TemporaryFileStorage.EncodeDocument(content, utf8))), conflict);
                });
                if (IsDisposed) return false;
                filePath = result.Path;
                documentEncoding = utf8;
                fileFingerprint = result.Fingerprint;
                dirty = documentRevision != revision;
                saveNotice = result.Conflict ? "检测到外部修改，双方内容已分别保留。" : null;
                RenderPreview();
                return true;
            }
            catch (Exception error)
            {
                saveFailure = "另存失败，内容仍在编辑器中：" + error.Message;
                return false;
            }
            finally
            {
                saving = false;
                if (!IsDisposed)
                {
                    UpdateFileLabel();
                    if (dirty && !ownerPrepared) autosaveTimer.Start();
                }
                saveGate.Release();
            }
        }

        private void UpdateFileLabel()
        {
            string name = string.IsNullOrEmpty(filePath) ? (markdownMode ? "临时 Markdown" : "临时文本") : Path.GetFileName(filePath);
            string status = saving ? "保存中…" : saveFailure != null ? "保存失败" : dirty ? "未保存" :
                string.IsNullOrEmpty(filePath) ? "输入后自动保存" : "已保存";
            string notice = saveFailure ?? saveNotice;
            ShowFileNotice(name + (string.IsNullOrEmpty(notice) ? "" : " · " + notice));
            captionBar.SetStatus(status == "已保存" ? "✓ 已保存" : status, saveFailure != null);
            fileTip.SetToolTip(fileLabel, name + " · " + status + (string.IsNullOrEmpty(notice) ? "" : " · " + notice) +
                (string.IsNullOrEmpty(filePath) ? "" : "\n保存位置：" + filePath));
        }

        private void ShowFileNotice(string message)
        {
            captionBar.SetStatus("", false);
            fileLabel.Text = message;
            fileTip.SetToolTip(fileLabel, message + (string.IsNullOrEmpty(filePath) ? "" : "\n保存位置：" + filePath));
        }

        private void OfferExternalEditor(string path, string reason)
        {
            if (MessageBox.Show(this, reason + "\n是否使用 Windows 记事本打开？", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true, ArgumentList = { path } });
        }

        internal async Task<bool> NewDocumentAsync()
        {
            await documentGate.WaitAsync();
            try
            {
                if (IsDisposed || ownerPrepared) return false;
                SetDocumentReadOnly(true);
                // A failed save must never clear the current document.
                if (!await ConfirmLeaveCoreAsync()) return false;
                CancelPreview();
                filePath = null;
                fileFingerprint = null;
                documentEncoding = new UTF8Encoding(false, true);
                documentRevision++;
                saveNotice = null;
                saveFailure = null;
                changingDocument = true;
                try
                {
                    if (markdownMode) markdownEditor.Clear();
                    else editor.Clear();
                }
                finally { changingDocument = false; }
                dirty = false;
                autosaveTimer.Stop();
                UpdateFileLabel();
                RenderPreview();
                FocusEditor();
                return true;
            }
            finally
            {
                if (!ownerPrepared) SetDocumentReadOnly(false);
                documentGate.Release();
            }
        }

        private void CancelPreview()
        {
            previewVersion++;
            previewCancellation?.Cancel();
            previewCancellation?.Dispose();
            previewCancellation = null;
        }

        private void RestoreSplitRatio()
        {
            if (splitContainer == null || splitContainer.IsDisposed || splitContainer.ClientSize.Width < 410) return;
            applyingSplitRatio = true;
            try
            {
                if (splitContainer.Panel1MinSize < 200) splitContainer.Panel1MinSize = 200;
                if (splitContainer.Panel2MinSize < 200) splitContainer.Panel2MinSize = 200;
            }
            finally { applyingSplitRatio = false; }
            double ratio = Program.Settings?.ScratchMarkdownSplitRatio ?? 0.5;
            if (double.IsNaN(ratio) || double.IsInfinity(ratio)) ratio = 0.5;
            ratio = Math.Clamp(ratio, 0.2, 0.8);
            int available = splitContainer.ClientSize.Width - splitContainer.SplitterWidth;
            int distance = Math.Clamp((int)Math.Round(available * ratio),
                splitContainer.Panel1MinSize, available - splitContainer.Panel2MinSize);
            if (distance == splitContainer.SplitterDistance) return;
            applyingSplitRatio = true;
            try { splitContainer.SplitterDistance = distance; }
            finally { applyingSplitRatio = false; }
        }

        private void RememberSplitRatio()
        {
            if (applyingSplitRatio || !IsHandleCreated || Program.Settings == null) return;
            int available = splitContainer.ClientSize.Width - splitContainer.SplitterWidth;
            if (available <= 0) return;
            double ratio = (double)splitContainer.SplitterDistance / available;
            if (Math.Abs(ratio - Program.Settings.ScratchMarkdownSplitRatio) < 0.001) return;
            Program.Settings.ScratchMarkdownSplitRatio = ratio;
            SettingManager.SaveApplicationConfigAsync();
        }

        private async Task InitializePreviewAsync()
        {
            try
            {
                preview = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = EditorBackgroundColor, Visible = false };
                previewPanel.Controls.Add(preview);
                string userDataFolder = Path.Combine(Program.PersonalFolder, "ScratchMarkdownWebView");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                if (IsDisposed) return;
                await preview.EnsureCoreWebView2Async(environment);
                if (IsDisposed) return;
                preview.CoreWebView2.Settings.IsScriptEnabled = false;
                preview.CoreWebView2.Settings.AreDevToolsEnabled = false;
                preview.CoreWebView2.Settings.IsStatusBarEnabled = false;
                preview.CoreWebView2.AddWebResourceRequestedFilter(
                    "https://" + PreviewHost + "/*", CoreWebView2WebResourceContext.Document);
                preview.CoreWebView2.WebResourceRequested += (sender, args) =>
                {
                    if (!pendingPreviews.TryGetValue(args.Request.Uri, out PreviewSnapshot requested))
                    {
                        args.Response = preview.CoreWebView2.Environment.CreateWebResourceResponse(
                            new MemoryStream(Array.Empty<byte>()), 404, "Not Found", "");
                        return;
                    }
                    args.Response = preview.CoreWebView2.Environment.CreateWebResourceResponse(
                        new MemoryStream(requested.HtmlBytes, false), 200, "OK",
                        "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
                };
                preview.CoreWebView2.NavigationStarting += (sender, args) =>
                {
                    bool internalPreview = args.Uri == activeNavigationUri &&
                        pendingPreviews.ContainsKey(args.Uri);
                    if (!internalPreview && !args.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
                    if (internalPreview)
                    {
                        activeNavigationId = args.NavigationId;
                        previewDocumentReady = false;
                    }
                };
                preview.CoreWebView2.NavigationCompleted += async (sender, args) =>
                {
                    string uri = preview.CoreWebView2.Source;
                    if (!args.IsSuccess || args.NavigationId != activeNavigationId ||
                        uri != activeNavigationUri ||
                        !pendingPreviews.TryGetValue(uri, out PreviewSnapshot loaded))
                        return;
                    displayedPreview = loaded;
                    pendingPreviews.Clear();
                    previewDocumentReady = true;
                    if (previewDocumentReady)
                    {
                        try
                        {
                            await preview.ExecuteScriptAsync(ImageFailureScript);
                            await AlignPreviewToEditorAsync();
                        }
                        catch (Exception error) { DebugHelper.WriteException(error, "Markdown image preview failed."); }
                    }
                };
                previewReady = true;
                previewMessage.Visible = false;
                preview.Visible = true;
                preview.BringToFront();
                RenderPreview();
                scrollSyncTimer.Start();
                FocusEditorIfActive();
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Markdown preview initialization failed.");
                preview?.Dispose();
                preview = null;
                previewMessage.Text = "预览不可用；文本仍可编辑和保存。请检查 WebView2 运行环境。";
                previewMessage.Visible = true;
                previewMessage.BringToFront();
            }
        }

        private void RenderPreview()
        {
            _ = RenderPreviewAsync();
        }

        private async Task RenderPreviewAsync()
        {
            if (IsDisposed || !previewReady || preview?.CoreWebView2 == null) return;
            CancelPreview();
            int version = previewVersion;
            previewCancellation = new CancellationTokenSource();
            CancellationToken token = previewCancellation.Token;
            string text = DocumentText;
            try
            {
                string folder = filePath == null ? null : Path.GetDirectoryName(Path.GetFullPath(filePath));
                string baseUrl = "";
                preview.CoreWebView2.ClearVirtualHostNameToFolderMapping(AssetHost);
                if (folder != null && Directory.Exists(folder))
                {
                    preview.CoreWebView2.SetVirtualHostNameToFolderMapping(AssetHost, folder, CoreWebView2HostResourceAccessKind.DenyCors);
                    baseUrl = "https://" + AssetHost + "/";
                }
                await previewGate.WaitAsync(token);
                PreviewSnapshot snapshot;
                try { snapshot = await Task.Run(() => BuildPreviewSnapshot(text, baseUrl, token), token); }
                finally { previewGate.Release(); }
                if (IsDisposed || token.IsCancellationRequested || version != previewVersion) return;
                await previewApplyGate.WaitAsync(token);
                try
                {
                    if (IsDisposed || token.IsCancellationRequested || version != previewVersion) return;
                    if (previewDocumentReady && displayedPreview != null &&
                        displayedPreview.BaseUrl == baseUrl &&
                        await TryPatchPreviewAsync(displayedPreview, snapshot))
                    {
                        if (token.IsCancellationRequested || version != previewVersion) return;
                        await AlignPreviewToEditorAsync();
                        return;
                    }
                    if (IsDisposed || token.IsCancellationRequested || version != previewVersion) return;
                    previewDocumentReady = false;
                    displayedPreview = null;
                    pendingPreviews.Clear();
                    activeNavigationId = 0;
                    activeNavigationUri = "https://" + PreviewHost + "/document-" +
                        version.ToString(CultureInfo.InvariantCulture) + ".html";
                    pendingPreviews[activeNavigationUri] = snapshot;
                    preview.CoreWebView2.Navigate(activeNavigationUri);
                    previewMessage.Visible = false;
                    preview.Visible = true;
                    preview.BringToFront();
                    scrollSyncTimer.Start();
                }
                finally { previewApplyGate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!IsDisposed && version == previewVersion)
                {
                    DebugHelper.WriteException(error, "Markdown preview rendering failed.");
                    ShowPreviewMessage("预览暂时不可用；内容仍可编辑和保存。");
                }
            }
        }

        private void ShowPreviewMessage(string message)
        {
            previewDocumentReady = false;
            displayedPreview = null;
            pendingPreviews.Clear();
            preview.Visible = false;
            previewMessage.Text = message;
            previewMessage.Visible = true;
            previewMessage.BringToFront();
            scrollSyncTimer.Stop();
        }

        private const string ImageFailureScript =
            "document.querySelectorAll('img').forEach(img=>{const fail=()=>{const note=document.createElement('span');note.textContent='[图片无法读取：'+(img.getAttribute('src')||'')+']';img.replaceWith(note);};if(img.complete&&!img.naturalWidth)fail();else img.addEventListener('error',fail,{once:true});});";

        private PreviewSnapshot BuildPreviewSnapshot(string text, string baseUrl, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            MarkdownDocument document = Markdown.Parse(text, markdownPipeline);
            var blocks = new List<PreviewBlock>(document.Count);
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            var renderer = new HtmlRenderer(writer);
            markdownPipeline.Setup(renderer);
            foreach (Block block in document)
            {
                token.ThrowIfCancellationRequested();
                writer.GetStringBuilder().Clear();
                renderer.Render(block);
                blocks.Add(new PreviewBlock(block.Span.Start, block.Span.End, writer.ToString()));
            }
            token.ThrowIfCancellationRequested();
            var html = new StringBuilder(Math.Max(1024, text.Length + blocks.Count * 90));
            html.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            html.Append(
                (baseUrl.Length == 0 ? "" : "<base href=\"" + baseUrl + "\">") + "<style>" +
                "body{font:14px/1.35 'Microsoft YaHei',sans-serif;background:#f3f8fd;color:#203040;" +
                "margin:0;padding:8px;overflow-wrap:anywhere}*{scrollbar-width:thin;scrollbar-color:#6f8da8 #f3f8fd}" +
                "::-webkit-scrollbar{width:8px;height:8px;background:#f3f8fd}" +
                "::-webkit-scrollbar-thumb{background:#6f8da8;border-radius:4px}" +
                "a{color:#1767ad}pre,code{background:#d8e8f6;font-family:'Microsoft YaHei',sans-serif}" +
                "p{margin:0 0 6px}h1,h2,h3,h4,h5,h6{line-height:1.25;margin:6px 0 4px}" +
                "h1{font-size:20px}h2{font-size:18px}h3{font-size:16px}h4,h5,h6{font-size:14px}" +
                "ul,ol{margin:6px 0;padding-left:24px}li p{margin-bottom:2px}" +
                "pre{margin:10px 0;padding:8px;overflow:auto}" +
                "blockquote{margin:10px 0;padding:4px 12px;border-left:3px solid #6f8da8}" +
                "blockquote p:last-child{margin-bottom:0}hr{margin:10px 0}" +
                "img{max-width:100%}table{margin:10px 0;border-collapse:collapse}" +
                "th,td{border:1px solid #a9c2d8;padding:4px 8px}th p,td p{margin:0}" +
                ".preview-block{display:flow-root}</style></head><body><div id=\"content\">");
            foreach (PreviewBlock block in blocks) AppendPreviewBlock(html, block);
            html.Append("</div></body></html>");
            token.ThrowIfCancellationRequested();
            return new PreviewSnapshot(blocks.ToArray(), baseUrl, Encoding.UTF8.GetBytes(html.ToString()));
        }

        private static void AppendPreviewBlock(StringBuilder builder, PreviewBlock block)
        {
            builder.Append("<div class=\"preview-block\" data-source-start=\"")
                .Append(block.Start).Append("\" data-source-end=\"").Append(block.End).Append("\">")
                .Append(block.Html).Append("</div>");
        }

        private async Task<bool> TryPatchPreviewAsync(
            PreviewSnapshot previous, PreviewSnapshot current)
        {
            PreviewBlock[] before = previous.Blocks;
            PreviewBlock[] after = current.Blocks;
            int prefix = 0;
            while (prefix < before.Length && prefix < after.Length &&
                before[prefix].Html == after[prefix].Html &&
                before[prefix].Start == after[prefix].Start &&
                before[prefix].End == after[prefix].End) prefix++;

            int suffix = 0;
            int shift = 0;
            while (suffix < before.Length - prefix && suffix < after.Length - prefix)
            {
                PreviewBlock oldBlock = before[before.Length - suffix - 1];
                PreviewBlock newBlock = after[after.Length - suffix - 1];
                int startShift = newBlock.Start - oldBlock.Start;
                int endShift = newBlock.End - oldBlock.End;
                if (oldBlock.Html != newBlock.Html || startShift != endShift ||
                    (suffix > 0 && shift != startShift)) break;
                shift = startShift;
                suffix++;
            }

            int oldRemoved = before.Length - prefix - suffix;
            int newAdded = after.Length - prefix - suffix;
            if (oldRemoved == 0 && newAdded == 0 && shift == 0) return true;

            var replacement = new StringBuilder();
            for (int i = prefix; i < prefix + newAdded; i++)
                AppendPreviewBlock(replacement, after[i]);
            // A structural change uses a full in-memory navigation; ordinary edits patch the affected blocks.
            if (replacement.Length > 256 * 1024 || oldRemoved > 1000 || newAdded > 1000) return false;

            string script = "(() => { const root=document.getElementById('content');" +
                "if(!root||root.children.length!==" + before.Length.ToString(CultureInfo.InvariantCulture) + ")return false;" +
                "for(let i=0;i<" + oldRemoved.ToString(CultureInfo.InvariantCulture) + ";i++)root.children[" +
                prefix.ToString(CultureInfo.InvariantCulture) + "].remove();" +
                "const template=document.createElement('template');template.innerHTML=" +
                JsonSerializer.Serialize(replacement.ToString()) + ";" +
                "root.insertBefore(template.content,root.children[" + prefix.ToString(CultureInfo.InvariantCulture) + "]||null);" +
                "for(let i=" + prefix.ToString(CultureInfo.InvariantCulture) + ";i<" +
                (prefix + newAdded).ToString(CultureInfo.InvariantCulture) + ";i++){" +
                ImageFailureScript.Replace("document.querySelectorAll('img')", "root.children[i].querySelectorAll('img')") +
                "}" +
                "for(let i=" + (prefix + newAdded).ToString(CultureInfo.InvariantCulture) +
                ";i<root.children.length;i++){" +
                "const node=root.children[i];node.dataset.sourceStart=Number(node.dataset.sourceStart)+" +
                shift.ToString(CultureInfo.InvariantCulture) +
                ";node.dataset.sourceEnd=Number(node.dataset.sourceEnd)+" +
                shift.ToString(CultureInfo.InvariantCulture) + ";}" +
                "return true;})()";

            previewDocumentReady = false;
            try
            {
                string result = await preview.ExecuteScriptAsync(script);
                if (IsDisposed) return false;
                bool patched = result == "true";
                previewDocumentReady = patched;
                displayedPreview = patched ? current : null;
                return patched;
            }
            catch
            {
                displayedPreview = null;
                previewDocumentReady = false;
                return false;
            }
        }

        private int GetEditorTopOffset()
        {
            if (markdownMode)
            {
                var view = markdownEditor.Editor.TextArea.TextView;
                if (!view.VisualLinesValid || view.VisualLines.Count == 0) return lastEditorOffset;
                return view.VisualLines[0].FirstDocumentLine.Offset;
            }
            if (!editor.IsHandleCreated) return 0;
            return Math.Clamp(editor.GetCharIndexFromPosition(new Point(2, 2)), 0, editor.TextLength);
        }

        private void ScrollEditorToOffset(int offset)
        {
            if (markdownMode)
            {
                var textEditor = markdownEditor.Editor;
                int markdownCharacter = Math.Clamp(offset, 0, textEditor.Document.TextLength);
                int markdownLine = textEditor.Document.GetLineByOffset(markdownCharacter).LineNumber;
                textEditor.ScrollToVerticalOffset(textEditor.TextArea.TextView.GetVisualTopByDocumentLine(markdownLine));
                return;
            }
            if (!editor.IsHandleCreated) return;
            int character = Math.Clamp(offset, 0, editor.TextLength);
            int targetLine = SendMessage(editor.Handle, EmLineFromChar, new IntPtr(character), IntPtr.Zero).ToInt32();
            int currentLine = SendMessage(editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
            int delta = targetLine - currentLine;
            if (delta != 0) SendMessage(editor.Handle, EmLineScroll, IntPtr.Zero, new IntPtr(delta));
        }

        private async Task AlignPreviewToEditorAsync()
        {
            if (IsDisposed || !previewDocumentReady || preview?.CoreWebView2 == null) return;
            int offset = GetEditorTopOffset();
            string script = "(() => { const offset=" + offset.ToString(CultureInfo.InvariantCulture) + ";" +
                "const nodes=document.getElementById('content')?.children||[];" +
                "if(!nodes.length||offset<=0){window.scrollTo(0,0);return Math.round(window.scrollY);}" +
                "let lo=0,hi=nodes.length-1,index=0;while(lo<=hi){" +
                "const mid=(lo+hi)>>1;if(Number(nodes[mid].dataset.sourceStart)<=offset){index=mid;lo=mid+1;}else hi=mid-1;}" +
                "const node=nodes[index];" +
                "const start=Number(node.dataset.sourceStart),end=Number(node.dataset.sourceEnd);" +
                "const fraction=Math.max(0,Math.min(1,(offset-start)/Math.max(1,end-start)));" +
                "const rect=node.getBoundingClientRect();" +
                "window.scrollTo(0,window.scrollY+rect.top+rect.height*fraction);" +
                "return Math.round(window.scrollY);})()";
            try
            {
                string result = await preview.ExecuteScriptAsync(script);
                if (IsDisposed || !previewDocumentReady) return;
                if (int.TryParse(result, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                {
                    lastEditorOffset = offset;
                    lastPreviewY = y;
                }
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Markdown preview scroll alignment failed.");
            }
        }

        private async Task SyncScrollAsync()
        {
            if (scrollSyncBusy || IsDisposed || !previewDocumentReady || preview?.CoreWebView2 == null) return;
            scrollSyncBusy = true;
            try
            {
                int editorOffset = GetEditorTopOffset();
                if (editorOffset != lastEditorOffset)
                {
                    await AlignPreviewToEditorAsync();
                }
                else
                {
                    string yResult = await preview.ExecuteScriptAsync("Math.round(window.scrollY)");
                    if (!int.TryParse(yResult, NumberStyles.Integer, CultureInfo.InvariantCulture, out int previewY) ||
                        IsDisposed || !previewDocumentReady || Math.Abs(previewY - lastPreviewY) <= 2) return;
                    const string sourceScript = "(() => {const y=window.scrollY;" +
                        "const nodes=document.getElementById('content')?.children||[];" +
                        "if(!nodes.length)return 0;" +
                        "let lo=0,hi=nodes.length-1,index=0;while(lo<=hi){" +
                        "const mid=(lo+hi)>>1;const top=nodes[mid].getBoundingClientRect().top+window.scrollY;" +
                        "if(top<=y+2){index=mid;lo=mid+1;}else hi=mid-1;}" +
                        "const node=nodes[index];" +
                        "const rect=node.getBoundingClientRect(),top=rect.top+window.scrollY;" +
                        "const fraction=Math.max(0,Math.min(1,(y-top)/Math.max(1,rect.height)));" +
                        "const start=Number(node.dataset.sourceStart),end=Number(node.dataset.sourceEnd);" +
                        "return Math.round(start+(end-start)*fraction);})()";
                    string offsetResult = await preview.ExecuteScriptAsync(sourceScript);
                    if (!int.TryParse(offsetResult, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sourceOffset) ||
                        IsDisposed || !previewDocumentReady) return;
                    ScrollEditorToOffset(sourceOffset);
                    lastEditorOffset = GetEditorTopOffset();
                    lastPreviewY = previewY;
                }
            }
            catch (Exception error)
            {
                scrollSyncTimer.Stop();
                DebugHelper.WriteException(error, "Markdown two-way scroll synchronization failed.");
            }
            finally
            {
                scrollSyncBusy = false;
            }
        }

        private void FocusEditor()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (markdownMode)
            {
                markdownEditor.Editor.CaretOffset = markdownEditor.TextLength;
                markdownEditor.FocusText();
            }
            else
            {
                editor.Select();
                editor.SelectionStart = editor.TextLength;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                saveLifetime.Cancel();
                CancelPreview();
                fileTip.Dispose();
                previewTimer?.Dispose();
                autosaveTimer?.Dispose();
                scrollSyncTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class SlimMarkdownScrollBar : Control
        {
            private readonly ScratchLargeTextEditor host;
            private bool dragging;
            private int dragOffset;

            internal SlimMarkdownScrollBar(ScratchLargeTextEditor host, Color trackColor)
            {
                this.host = host;
                BackColor = trackColor;
                TabStop = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                host.Editor.TextArea.TextView.ScrollOffsetChanged += (sender, args) => Invalidate();
                host.Editor.TextChanged += (sender, args) => Invalidate();
                host.Resize += (sender, args) => Invalidate();
            }

            private double Maximum => Math.Max(0, host.Editor.ExtentHeight - host.Editor.ViewportHeight);
            private double Position => Math.Clamp(host.Editor.TextArea.TextView.ScrollOffset.Y, 0, Maximum);

            private Rectangle ThumbBounds
            {
                get
                {
                    double extent = host.Editor.ExtentHeight;
                    double viewport = host.Editor.ViewportHeight;
                    if (extent <= viewport || extent <= 0) return Rectangle.Empty;
                    int height = Math.Min(Height, Math.Max(20, (int)Math.Round(Height * viewport / extent)));
                    int top = (int)Math.Round((Height - height) * Position / Math.Max(1, Maximum));
                    return new Rectangle(0, top, Math.Max(1, Width), height);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                Rectangle thumb = ThumbBounds;
                if (thumb.IsEmpty) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int radius = Math.Min(4, Math.Min(thumb.Width, thumb.Height) / 2);
                using GraphicsPath path = new GraphicsPath();
                if (radius > 0)
                {
                    int diameter = radius * 2;
                    path.AddArc(thumb.Left, thumb.Top, diameter, diameter, 180, 90);
                    path.AddArc(thumb.Right - diameter, thumb.Top, diameter, diameter, 270, 90);
                    path.AddArc(thumb.Right - diameter, thumb.Bottom - diameter, diameter, diameter, 0, 90);
                    path.AddArc(thumb.Left, thumb.Bottom - diameter, diameter, diameter, 90, 90);
                    path.CloseFigure();
                }
                else path.AddRectangle(thumb);
                using Brush brush = new SolidBrush(ScrollThumbColor);
                e.Graphics.FillPath(brush, path);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left || Maximum <= 0) return;
                Rectangle thumb = ThumbBounds;
                dragging = true;
                Capture = true;
                dragOffset = thumb.Contains(e.Location) ? e.Y - thumb.Top : thumb.Height / 2;
                ScrollFromPointer(e.Y);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (dragging) ScrollFromPointer(e.Y);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                dragging = false;
                Capture = false;
            }

            private void ScrollFromPointer(int y)
            {
                Rectangle thumb = ThumbBounds;
                int travel = Math.Max(1, Height - thumb.Height);
                int top = Math.Clamp(y - dragOffset, 0, travel);
                host.Editor.ScrollToVerticalOffset(top * Maximum / travel);
                Invalidate();
            }
        }

        private sealed class SlimTextScrollBar : Control
        {
            private const int EmGetFirstVisibleLine = 0x00CE;
            private const int EmGetLineCount = 0x00BA;
            private const int EmLineScroll = 0x00B6;
            private readonly TextBox textBox;
            private readonly Timer positionTimer;
            private bool dragging;
            private int dragOffset;
            private int lastVisibleLine;

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

            internal SlimTextScrollBar(TextBox textBox, Color trackColor)
            {
                this.textBox = textBox;
                BackColor = trackColor;
                TabStop = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                textBox.TextChanged += (sender, args) => Invalidate();
                textBox.Resize += (sender, args) => Invalidate();
                textBox.KeyUp += (sender, args) => BeginInvoke(Invalidate);
                textBox.MouseWheel += (sender, args) =>
                {
                    int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
                    ScrollTo(FirstVisibleLine - Math.Sign(args.Delta) * lines);
                    if (args is HandledMouseEventArgs handled) handled.Handled = true;
                };
                positionTimer = new Timer { Interval = 250 };
                positionTimer.Tick += (sender, args) =>
                {
                    int position = FirstVisibleLine;
                    if (position != lastVisibleLine)
                    {
                        lastVisibleLine = position;
                        Invalidate();
                    }
                };
                positionTimer.Start();
            }

            private int FirstVisibleLine => textBox.IsHandleCreated
                ? SendMessage(textBox.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;
            private int TotalLines => textBox.IsHandleCreated
                ? Math.Max(1, SendMessage(textBox.Handle, EmGetLineCount, IntPtr.Zero, IntPtr.Zero).ToInt32()) : 1;
            private int VisibleLines => Math.Max(1, textBox.ClientSize.Height / Math.Max(1, textBox.Font.Height));
            private int Maximum => Math.Max(0, TotalLines - VisibleLines);

            private Rectangle ThumbBounds
            {
                get
                {
                    if (Maximum == 0) return Rectangle.Empty;
                    int height = Math.Min(Height, Math.Max(20, Height * VisibleLines / TotalLines));
                    int top = (Height - height) * Math.Min(Maximum, FirstVisibleLine) / Maximum;
                    return new Rectangle(0, top, Math.Max(1, Width), height);
                }
            }

            private void ScrollTo(int line)
            {
                if (!textBox.IsHandleCreated) return;
                int delta = Math.Clamp(line, 0, Maximum) - FirstVisibleLine;
                if (delta != 0) SendMessage(textBox.Handle, EmLineScroll, IntPtr.Zero, new IntPtr(delta));
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                Rectangle thumb = ThumbBounds;
                if (!thumb.IsEmpty)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    int radius = Math.Min(4, Math.Min(thumb.Width, thumb.Height) / 2);
                    using GraphicsPath path = new GraphicsPath();
                    if (radius > 0)
                    {
                        int diameter = radius * 2;
                        path.AddArc(thumb.Left, thumb.Top, diameter, diameter, 180, 90);
                        path.AddArc(thumb.Right - diameter, thumb.Top, diameter, diameter, 270, 90);
                        path.AddArc(thumb.Right - diameter, thumb.Bottom - diameter, diameter, diameter, 0, 90);
                        path.AddArc(thumb.Left, thumb.Bottom - diameter, diameter, diameter, 90, 90);
                        path.CloseFigure();
                    }
                    else path.AddRectangle(thumb);
                    using Brush brush = new SolidBrush(ScrollThumbColor);
                    e.Graphics.FillPath(brush, path);
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left || Maximum == 0) return;
                Rectangle thumb = ThumbBounds;
                dragging = true;
                Capture = true;
                dragOffset = thumb.Contains(e.Location) ? e.Y - thumb.Top : thumb.Height / 2;
                ScrollFromPointer(e.Y);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (dragging) ScrollFromPointer(e.Y);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                dragging = false;
                Capture = false;
            }

            private void ScrollFromPointer(int y)
            {
                Rectangle thumb = ThumbBounds;
                int travel = Math.Max(1, Height - thumb.Height);
                int top = Math.Clamp(y - dragOffset, 0, travel);
                ScrollTo((int)Math.Round((double)top * Maximum / travel));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) positionTimer?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
