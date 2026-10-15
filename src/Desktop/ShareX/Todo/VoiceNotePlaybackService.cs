using System;
using System.Media;

namespace ShareX
{
    internal sealed class VoiceNotePlaybackService : IDisposable
    {
        private SoundPlayer player;
        internal void Play(string path)
        {
            Stop();
            player = new SoundPlayer(path);
            player.Play();
        }
        internal void Stop()
        {
            try { player?.Stop(); } catch { }
            player?.Dispose(); player = null;
        }
        public void Dispose() => Stop();
    }
}
