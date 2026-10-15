using System;
using System.Threading.Tasks;
using ShareX.HelpersLib;

namespace ShareX
{
    // Ownership transfers to this operation. The UI must not dispose the recorder while
    // its audio callback or conversion is running. The original raw file survives failure.
    internal static class VoiceNoteRecordingService
    {
        internal static Task<TimeSpan> StopAsync(VoiceNoteRecorder recorder, TimeSpan fallback)
        {
            if (recorder == null) return Task.FromResult(fallback);
            return Task.Run(() =>
            {
                try { return recorder.Stop(); }
                finally
                {
                    try { recorder.Dispose(); }
                    catch (Exception error) { DebugHelper.WriteException(error, "Voice recorder cleanup failed."); }
                }
            });
        }
    }
}
