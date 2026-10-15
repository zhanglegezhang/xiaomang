using System;
using System.IO;
using System.Security.Cryptography;

namespace ShareX
{
    internal enum VoiceInputPayloadState { Matching, Different, Missing }

    internal static class VoiceInputPayload
    {
        // Compare content, never the portable folder name. A moved/new package can
        // reuse the protected installation when its component files are identical.
        internal static VoiceInputPayloadState Inspect(string bundle, string installed)
        {
            string[] files = {
                Path.Combine("x86_64", "XiaomangVoiceInput.dll"),
                Path.Combine("x86_64", "RegisterVoiceInput.exe"),
                Path.Combine("i686", "XiaomangVoiceInput.dll"),
                Path.Combine("i686", "RegisterVoiceInput.exe")
            };
            try
            {
                if (!File.Exists(Path.Combine(bundle, "VoiceInputSetup.exe")) ||
                    !Directory.Exists(Path.Combine(bundle, "Licenses")) ||
                    Directory.GetFiles(Path.Combine(bundle, "Licenses")).Length == 0)
                    return VoiceInputPayloadState.Missing;
                foreach (string relative in files)
                    if (!File.Exists(Path.Combine(bundle, relative))) return VoiceInputPayloadState.Missing;
                bool different = false;
                foreach (string relative in files)
                {
                    string source = Path.Combine(bundle, relative), target = Path.Combine(installed, relative);
                    if (!File.Exists(target) || new FileInfo(source).Length != new FileInfo(target).Length)
                    { different = true; continue; }
                    using FileStream left = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using FileStream right = File.Open(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(left), SHA256.HashData(right))) different = true;
                }
                return different ? VoiceInputPayloadState.Different : VoiceInputPayloadState.Matching;
            }
            catch (IOException) { return VoiceInputPayloadState.Missing; }
            catch (UnauthorizedAccessException) { return VoiceInputPayloadState.Missing; }
        }
    }
}
