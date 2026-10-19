using ShareX.HelpersLib;
using System;
using System.Windows.Forms;

namespace ShareX
{
    internal static class VoiceTypingClipboard
    {
        internal const string HistoryExclusionFormat = "Xiaomang.VoiceTyping.ExcludeClipboardHistory";

        // Publish text and its history policy together. A separate flag or a timed
        // pause would race the clipboard listener and suppress unrelated user copies.
        internal static bool CopyWithoutHistory(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                DataObject data = new DataObject();
                data.SetData(DataFormats.UnicodeText, false, text);
                data.SetData(HistoryExclusionFormat, false, "1");
                ClipboardPrivacy.ExcludeFromHistoryAndCloud(data);
                Clipboard.SetDataObject(data, true, 4, 50);
                return true;
            }
            catch (Exception error)
            {
                // Dictation is private; do not log the text or a serialized payload.
                DebugHelper.WriteLine($"Voice typing clipboard write failed after bounded retries (0x{error.HResult:X8}).");
                return false;
            }
        }
    }
}
