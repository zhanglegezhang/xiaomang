using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShareX
{
    /// <summary>Hosts AvalonEdit in the existing WinForms scratch window.</summary>
    internal sealed class ScratchLargeTextEditor : ElementHost
    {
        internal const int MaximumBytes = 5 * 1024 * 1024;
        internal const int MaximumCharacters = 5 * 1024 * 1024;
        internal const int MaximumLineLength = 4096;

        internal readonly TextEditor Editor;
        internal Action<string> InputRejected;
        internal event EventHandler ContentChanged;
        internal event EventHandler SaveRequested;

        private int byteCount;
        private bool settingText;
        private bool rejectingChange;

        internal ScratchLargeTextEditor()
        {
            Editor = new TextEditor
            {
                FontFamily = new FontFamily("Microsoft YaHei"),
                FontSize = 13.333,
                Background = new SolidColorBrush(Color.FromRgb(ScratchEditorForm.EditorBackgroundColor.R,
                    ScratchEditorForm.EditorBackgroundColor.G, ScratchEditorForm.EditorBackgroundColor.B)),
                Foreground = new SolidColorBrush(Color.FromRgb(ScratchEditorForm.EditorTextColor.R,
                    ScratchEditorForm.EditorTextColor.G, ScratchEditorForm.EditorTextColor.B)),
                WordWrap = true,
                ShowLineNumbers = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
            };
            Editor.Options.EnableHyperlinks = false;
            Editor.Options.EnableEmailHyperlinks = false;
            Editor.Options.ConvertTabsToSpaces = false;
            Editor.Document.Changed += DocumentChanged;
            Editor.TextArea.TextEntering += TextEntering;
            Editor.PreviewKeyDown += EditorPreviewKeyDown;
            DataObject.AddPastingHandler(Editor.TextArea, Pasting);
            Child = Editor;
            BackColor = ScratchEditorForm.EditorBackgroundColor;
        }

        internal static bool IsSupported(string value, out string reason)
        {
            value ??= "";
            reason = null;
            if (value.Length > MaximumCharacters || Encoding.UTF8.GetByteCount(value) > MaximumBytes)
                reason = "内容超过内置编辑器的 5 MB 上限，请使用外部编辑器打开。";
            else
            {
                int lineLength = 0;
                foreach (char character in value)
                {
                    lineLength = character == '\r' || character == '\n' ? 0 : lineLength + 1;
                    if (lineLength > MaximumLineLength)
                    {
                        reason = "单行超过 4096 个字符，请分行后再输入，或使用外部编辑器打开。";
                        break;
                    }
                }
            }
            return reason == null;
        }

        public override string Text
        {
            get => Editor.Text;
            set
            {
                if (!IsSupported(value, out string reason))
                {
                    InputRejected?.Invoke(reason);
                    return;
                }
                settingText = true;
                try
                {
                    Editor.Text = value ?? "";
                    byteCount = Encoding.UTF8.GetByteCount(Editor.Text);
                }
                finally { settingText = false; }
                ContentChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal int TextLength => Editor.Document.TextLength;
        internal void Clear() => Text = "";
        internal void FocusText()
        {
            Editor.Focus();
            Keyboard.Focus(Editor.TextArea);
        }

        private bool AcceptInsertion(string inserted)
        {
            inserted ??= "";
            int start = Editor.SelectionStart;
            int removedLength = Editor.SelectionLength;
            if (TextLength - removedLength + inserted.Length > MaximumCharacters ||
                byteCount - Encoding.UTF8.GetByteCount(Editor.Document.GetText(start, removedLength)) +
                Encoding.UTF8.GetByteCount(inserted) > MaximumBytes)
            {
                InputRejected?.Invoke("内容超过内置编辑器的 5 MB 上限，请使用外部编辑器打开。");
                return false;
            }
            // Inspect only the affected line span. Copying and rescanning the entire 5 MB
            // document on every paste or IME composition would stall the UI again.
            if (inserted.Length > 1 || removedLength > 0 || inserted == "\n" || inserted == "\r")
            {
                DocumentLine firstLine = Editor.Document.GetLineByOffset(start);
                DocumentLine lastLine = Editor.Document.GetLineByOffset(start + removedLength);
                int suffixStart = Math.Min(lastLine.EndOffset, start + removedLength);
                string affected = Editor.Document.GetText(firstLine.Offset, start - firstLine.Offset) +
                    inserted + Editor.Document.GetText(suffixStart,
                        lastLine.EndOffset - suffixStart);
                int lineLength = 0;
                foreach (char character in affected)
                {
                    lineLength = character == '\r' || character == '\n' ? 0 : lineLength + 1;
                    if (lineLength <= MaximumLineLength) continue;
                    InputRejected?.Invoke("单行超过 4096 个字符，请先换行。");
                    return false;
                }
            }
            else if (inserted.Length == 1 && inserted != "\r")
            {
                DocumentLine line = Editor.Document.GetLineByOffset(start);
                if (line.Length >= MaximumLineLength)
                {
                    InputRejected?.Invoke("单行超过 4096 个字符，请先换行。");
                    return false;
                }
            }
            return true;
        }

        private void TextEntering(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (!AcceptInsertion(e.Text)) e.Handled = true;
        }

        private void Pasting(object sender, DataObjectPastingEventArgs e)
        {
            string text = e.DataObject.GetData(DataFormats.UnicodeText) as string ??
                e.DataObject.GetData(DataFormats.Text) as string;
            if (text != null && !AcceptInsertion(text)) e.CancelCommand();
        }

        private void DocumentChanged(object sender, DocumentChangeEventArgs e)
        {
            byteCount += Encoding.UTF8.GetByteCount(e.InsertedText.Text) -
                Encoding.UTF8.GetByteCount(e.RemovedText.Text);
            if (settingText || rejectingChange) return;
            if (Editor.Document.TextLength > MaximumCharacters || byteCount > MaximumBytes ||
                Editor.Document.GetLineByOffset(Math.Min(e.Offset, Editor.Document.TextLength)).Length > MaximumLineLength)
            {
                // AvalonEdit is still closing the current undo group in Changed; undo on the
                // dispatcher after that group has closed, before the next visible render.
                rejectingChange = true;
                Editor.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { Editor.Document.UndoStack.Undo(); }
                    finally { rejectingChange = false; }
                    InputRejected?.Invoke("内容超出内置编辑器的容量或单行限制，已撤销本次输入。");
                }), DispatcherPriority.Send);
                return;
            }
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EditorPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.S && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                SaveRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }
    }
}
