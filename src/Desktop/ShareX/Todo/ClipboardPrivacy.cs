using System;
using System.IO;
using System.Windows.Forms;

namespace ShareX
{
    internal static class ClipboardPrivacy
    {
        internal const string ExcludeMonitoring = "ExcludeClipboardContentFromMonitorProcessing";
        internal const string IncludeHistory = "CanIncludeInClipboardHistory";
        internal const string UploadToCloud = "CanUploadToCloudClipboard";

        internal static bool ExcludesHistory(IDataObject data)
        {
            if (data == null) return true;
            try
            {
                if (data.GetDataPresent(VoiceTypingClipboard.HistoryExclusionFormat, false) ||
                    data.GetDataPresent(ExcludeMonitoring, false)) return true;
                if (!data.GetDataPresent(IncludeHistory, false)) return false;
                return !ReadDword(data.GetData(IncludeHistory, false), out uint value) || value != 1;
            }
            catch
            {
                // Do not fall back to reading private text when its privacy metadata fails.
                return true;
            }
        }

        private static bool ReadDword(object data, out uint value)
        {
            value = 0;
            if (data is uint number) { value = number; return true; }
            if (data is int signed) { value = unchecked((uint)signed); return true; }
            if (data is byte[] bytes && bytes.Length == 4)
            {
                value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                return true;
            }
            if (data is not Stream stream) return false;
            long position = stream.CanSeek ? stream.Position : 0;
            try
            {
                if (stream.CanSeek) stream.Position = 0;
                Span<byte> buffer = stackalloc byte[4];
                int read = 0;
                while (read < buffer.Length)
                {
                    int count = stream.Read(buffer[read..]);
                    if (count == 0) return false;
                    read += count;
                }
                if (stream.ReadByte() != -1) return false;
                value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer);
                return true;
            }
            finally { if (stream.CanSeek) stream.Position = position; }
        }

        internal static void ExcludeFromHistoryAndCloud(DataObject data)
        {
            // These are serialized DWORDs, not strings or .NET-serialized integers.
            data.SetData(ExcludeMonitoring, false, new MemoryStream(new byte[4], false));
            data.SetData(IncludeHistory, false, new MemoryStream(new byte[4], false));
            data.SetData(UploadToCloud, false, new MemoryStream(new byte[4], false));
        }
    }
}
