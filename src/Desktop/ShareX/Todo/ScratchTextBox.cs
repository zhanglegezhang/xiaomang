using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class ScratchTextBox : TextBox
    {
        internal const int MaximumCharacters = 256 * 1024;
        internal const int MaximumBytes = 512 * 1024;
        internal const int MaximumLineLength = 4096;
        internal Action<string> InputRejected;

        internal static bool IsSupported(string text, out string reason)
        {
            text ??= "";
            reason = null;
            if (text.Length > MaximumCharacters || Encoding.UTF8.GetByteCount(text) > MaximumBytes)
                reason = "内容过大：内置编辑器最多支持 26 万字符、512 KB，请用外部编辑器打开。";
            else
            {
                int line = 0;
                foreach (char character in text)
                {
                    line = character == '\r' || character == '\n' ? 0 : line + 1;
                    if (line > MaximumLineLength)
                    {
                        reason = "单行超过 4096 个字符，请分行后再输入，或用外部编辑器打开。";
                        break;
                    }
                }
            }
            return reason == null;
        }

        public override string Text
        {
            get => base.Text;
            set
            {
                if (Accept(value)) base.Text = value;
            }
        }

        private bool Accept(string value)
        {
            if (IsSupported(value, out string reason)) return true;
            InputRejected?.Invoke(reason);
            return false;
        }

        private bool AcceptInsertion(string value, int start = -1, int length = -1)
        {
            start = start < 0 ? SelectionStart : start;
            length = length < 0 ? SelectionLength : length;
            if (value != null && value.Length > MaximumCharacters) return Accept(value);
            return Accept(base.Text.Remove(start, length).Insert(start, value ?? ""));
        }

        protected override void WndProc(ref Message message)
        {
            const int WmPaste = 0x302, EmReplaceSel = 0xC2, WmSetText = 0xC, WmChar = 0x102, WmKeyDown = 0x100;
            if (message.Msg == WmPaste)
            {
                try
                {
                    if (!AcceptInsertion(Clipboard.GetText())) return;
                }
                catch (ExternalException) { InputRejected?.Invoke("剪贴板暂时不可用，请重试。"); return; }
            }
            else if (message.Msg == EmReplaceSel)
            {
                if (!AcceptInsertion(Marshal.PtrToStringUni(message.LParam))) return;
            }
            else if (message.Msg == WmSetText)
            {
                if (!Accept(Marshal.PtrToStringUni(message.LParam))) return;
            }
            else if (message.Msg == WmChar)
            {
                char character = (char)message.WParam.ToInt32();
                if (character == '\b' && SelectionLength == 0 && SelectionStart > 0)
                {
                    if (!AcceptInsertion("", SelectionStart - 1, 1)) return;
                }
                else if ((character >= ' ' || character == '\t' || character == '\r') &&
                    !AcceptInsertion(character == '\r' ? "\r\n" : character.ToString())) return;
            }
            else if (message.Msg == WmKeyDown && (Keys)message.WParam.ToInt32() == Keys.Delete && SelectionLength == 0 && SelectionStart < TextLength)
            {
                if (!AcceptInsertion("", SelectionStart, 1)) return;
            }
            base.WndProc(ref message);
        }
    }
}
