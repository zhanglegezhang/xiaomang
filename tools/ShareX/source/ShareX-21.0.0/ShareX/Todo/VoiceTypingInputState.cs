using ShareX.HelpersLib;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO.Pipes;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Windows.Forms.Timer;

namespace ShareX
{
    // Owned by one dictation session. Capture/restore and COM callbacks run on the UI STA.
    internal sealed class VoiceTypingInputState : IDisposable
    {
        private static readonly Guid VoiceClass = new("7FD8708D-A55C-492D-918F-1802DC259B3A");
        private static readonly Guid VoiceProfile = new("96A307E7-E071-429B-A384-D9485884515F");
        private readonly InputProfile original;
        private readonly Func<InputProfile?> active;
        private readonly Func<InputProfile, int> activate;
        private readonly Func<bool> targetMatches;
        private readonly Action restoreMode;
        private readonly Action closeSpeech;
        private bool voiceStarted, userChanged, finished;
        private TsfInputProfiles profiles;
        private Timer timer;
        private VoiceProfileObserver observer;
        private VoiceTypingWorkerMode workerMode;

        internal VoiceTypingInputState(InputProfile original, Func<InputProfile?> active,
            Func<InputProfile, int> activate, Func<bool> targetMatches, Action restoreMode, Action closeSpeech = null)
        {
            this.original = original; this.active = active; this.activate = activate;
            this.targetMatches = targetMatches; this.restoreMode = restoreMode;
            this.closeSpeech = closeSpeech;
        }

        internal static VoiceTypingInputState Capture(IntPtr window, IntPtr focus)
        {
            var profiles = new TsfInputProfiles();
            VoiceTypingInputState state = null;
            VoiceTypingWorkerMode workerMode = null;
            try
            {
                var original = profiles.Active;
                uint thread = GetWindowThreadProcessId(window, out uint pid);
                IntPtr layout = GetKeyboardLayout(thread);
                // Do not guess another application's profile or leave it in speech mode.
                if (original == null || thread == 0 || pid == 0 || window != GetForegroundWindow() ||
                    original.Value.Language != unchecked((ushort)layout.ToInt64()) ||
                    (original.Value.Type == 2 && original.Value.Layout != layout))
                    throw new InvalidOperationException("无法确认原输入法，语音结果将保留到系统剪贴板。");
                IntPtr modeWindow = focus != IntPtr.Zero ? focus : window;
                workerMode = original.Value.IsXiaomang ? new VoiceTypingWorkerMode() : null;
                bool? english = workerMode?.English;
                var mode = ImeMode.Capture(modeWindow, english);
                bool TargetMatches() => window == GetForegroundWindow() &&
                    GetWindowThreadProcessId(window, out uint currentPid) == thread && currentPid == pid &&
                    (focus == IntPtr.Zero || GetGUIThreadInfo(thread, new GUIThreadInfo { Size = Marshal.SizeOf<GUIThreadInfo>() }, out IntPtr currentFocus) && currentFocus == focus);
                state = new VoiceTypingInputState(original.Value, () => profiles.Active, profiles.Activate,
                    TargetMatches, () =>
                    {
                        workerMode?.Restore();
                        mode.Restore(modeWindow);
                    }, () =>
                    {
                        var speech = profiles.Get(VoiceClass, VoiceProfile, unchecked((ushort)layout.ToInt64()));
                        Marshal.ThrowExceptionForHR(profiles.Deactivate(speech));
                    });
                state.profiles = profiles;
                state.workerMode = workerMode;
                // Sink catches a manual switch away and back between timer ticks.
                state.observer = new VoiceProfileObserver(state.Observe);
                state.timer = new Timer { Interval = 50 };
                state.timer.Tick += (_, _) => state.ObserveCurrent();
                state.timer.Start();
                return state;
            }
            catch
            {
                if (state != null) state.Dispose(); else { workerMode?.Dispose(); profiles.Dispose(); }
                throw;
            }
        }

        private static bool IsVoice(InputProfile p) => p.Type == 1 && p.ClassId == VoiceClass && p.ProfileId == VoiceProfile;
        internal void Activated() { voiceStarted = true; ObserveCurrent(); }
        internal void PreserveUserChoice() => userChanged = true;
        internal void Observe(InputProfile profile)
        {
            if (finished) return;
            // Speech is a separate TSF category; the keyboard profile may remain active.
            if (!IsVoice(profile) && !profile.SameIdentity(original)) userChanged = true;
        }
        internal void ObserveCurrent()
        {
            if (finished) return;
            try
            {
                if (!targetMatches()) userChanged = true;
                if (active() is InputProfile current) Observe(current);
            }
            catch (Exception error)
            {
                userChanged = true; // Uncertain observations never force a profile change.
                DebugHelper.WriteException(error, "Voice input profile observation");
            }
        }
        internal bool Restore()
        {
            if (finished) return false;
            try
            {
                ObserveCurrent();
                if (!voiceStarted) return false;
                InputProfile? observed = active();
                bool restore = !userChanged && targetMatches() && !IsVoice(original) &&
                    observed is InputProfile current && (IsVoice(current) || current.SameIdentity(original));
                // End only our speech service, including failed/cancelled or moved-focus sessions.
                // Do not change the selected keyboard profile when the user selected another one.
                finished = true;
                closeSpeech?.Invoke();
                if (!restore)
                    return false;
                if (observed is InputProfile selected && !selected.SameIdentity(original))
                {
                    int result = activate(original);
                    if (result != 0) throw new InvalidOperationException("Windows 未能恢复原输入法（0x" + result.ToString("X8") + "）。");
                }
                if (active() is not InputProfile restored || !restored.SameIdentity(original))
                    throw new InvalidOperationException("原输入法恢复状态未得到确认。");
                if (targetMatches()) restoreMode();
                return true;
            }
            finally { Dispose(); }
        }
        public void Dispose()
        {
            finished = true;
            timer?.Dispose(); timer = null;
            observer?.Dispose(); observer = null;
            profiles?.Dispose(); profiles = null;
            workerMode?.Dispose(); workerMode = null;
        }

        private readonly record struct ImeMode(bool Valid, bool Open, uint Conversion, uint Sentence)
        {
            internal static ImeMode Capture(IntPtr window, bool? english)
            {
                IntPtr context = ImmGetContext(window);
                if (context == IntPtr.Zero) return default;
                try { return ImmGetConversionStatus(context, out uint conversion, out uint sentence)
                    ? new(true, english.HasValue ? !english.Value : ImmGetOpenStatus(context),
                        english.HasValue ? (english.Value ? conversion & ~1u : conversion | 1u) : conversion, sentence) : default; }
                finally { ImmReleaseContext(window, context); }
            }
            internal void Restore(IntPtr window)
            {
                if (!Valid) return;
                IntPtr context = ImmGetContext(window);
                if (context == IntPtr.Zero) return;
                try
                {
                    if (!ImmSetConversionStatus(context, Conversion, Sentence) || !ImmSetOpenStatus(context, Open))
                        DebugHelper.WriteLine("Input method does not support restoring its IMM mode; retained its own saved mode.");
                }
                finally { ImmReleaseContext(window, context); }
            }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
        [StructLayout(LayoutKind.Sequential)]
        private struct GUIThreadInfo
        {
            internal int Size, Flags;
            internal IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            internal System.Drawing.Rectangle CaretRect;
        }
        private static bool GetGUIThreadInfo(uint thread, GUIThreadInfo info, out IntPtr focus)
        {
            bool result = GetGUIThreadInfo(thread, ref info);
            focus = info.Focus; return result;
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetGUIThreadInfo(uint thread, ref GUIThreadInfo info);
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr window);
        [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmReleaseContext(IntPtr window, IntPtr context);
        [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmGetOpenStatus(IntPtr context);
        [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmGetConversionStatus(IntPtr context, out uint conversion, out uint sentence);
        [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmSetOpenStatus(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool open);
        [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmSetConversionStatus(IntPtr context, uint conversion, uint sentence);
    }

    // Reuse the worker's bounded, session-based protocol. No config/learning data is written.
    internal sealed class VoiceTypingWorkerMode : IDisposable
    {
        private readonly NamedPipeClientStream pipe = new(".", "xiaomang-input-v1-" + Process.GetCurrentProcess().SessionId,
            PipeDirection.InOut, PipeOptions.Asynchronous);
        private readonly ulong id = BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        internal bool English { get; }
        internal VoiceTypingWorkerMode()
        {
            try { English = Exchange(null, true); }
            catch { Dispose(); throw; }
        }
        internal void Restore() => Exchange(English, false);
        private bool Exchange(bool? english, bool open)
        {
            using var timeout = new CancellationTokenSource(250);
            return ExchangeAsync(english, open, timeout.Token).GetAwaiter().GetResult();
        }
        private async Task<bool> ExchangeAsync(bool? english, bool open, CancellationToken token)
        {
            if (open)
            {
                await pipe.ConnectAsync(token).ConfigureAwait(false);
                await Send(new { OpenSession = new { session = id, app = "xiaomang-voice-mode", protocol = 7 } }, token).ConfigureAwait(false);
                using var opened = await Read(token).ConfigureAwait(false);
                if (!opened.RootElement.TryGetProperty("SessionOpened", out _)) throw new InvalidOperationException("输入法模式会话未确认。");
            }
            if (english.HasValue)
                await Send(new { ModeChanged = new { session = id, english = english.Value } }, token).ConfigureAwait(false);
            await Send(new { SyncMode = new { session = id } }, token).ConfigureAwait(false);
            using var result = await Read(token).ConfigureAwait(false);
            bool value = result.RootElement.GetProperty("ModeSync").GetProperty("english").GetBoolean();
            if (english.HasValue && value != english.Value) throw new InvalidOperationException("输入法未确认原中英文模式。");
            return value;
        }
        private async Task Send(object value, CancellationToken token)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), token).ConfigureAwait(false);
            await pipe.WriteAsync(bytes, token).ConfigureAwait(false);
            await pipe.FlushAsync(token).ConfigureAwait(false);
        }
        private async Task<JsonDocument> Read(CancellationToken token)
        {
            byte[] prefix = new byte[4];
            await pipe.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
            int length = BitConverter.ToInt32(prefix);
            if (length <= 0 || length > 1048576) throw new InvalidOperationException("输入法模式响应无效。");
            byte[] bytes = new byte[length];
            await pipe.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            return JsonDocument.Parse(bytes);
        }
        public void Dispose()
        {
            try
            {
                if (pipe.IsConnected)
                {
                    using var timeout = new CancellationTokenSource(100);
                    Send(new { CloseSession = new { session = id } }, timeout.Token).GetAwaiter().GetResult();
                }
            }
            catch (Exception error) { DebugHelper.WriteException(error, "Voice input mode session cleanup"); }
            finally { pipe.Dispose(); }
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class VoiceProfileObserver : IVoiceProfileActivationSink, IDisposable
    {
        private readonly Action<InputProfile> changed;
        private IVoiceThreadMgr manager;
        private IVoiceTsfSource source;
        private uint cookie = uint.MaxValue;
        private bool activated;
        internal VoiceProfileObserver(Action<InputProfile> changed)
        {
            this.changed = changed;
            try
            {
                manager = (IVoiceThreadMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("529A9E6B-6587-4F23-AB9E-9C7D683E3C50"), true));
                Marshal.ThrowExceptionForHR(manager.Activate(out _)); activated = true;
                source = (IVoiceTsfSource)manager;
                Guid sink = typeof(IVoiceProfileActivationSink).GUID;
                Marshal.ThrowExceptionForHR(source.AdviseSink(ref sink, this, out cookie));
            }
            catch { Dispose(); throw; }
        }
        public int OnActivated(uint type, ushort language, ref Guid clsid, ref Guid category, ref Guid profile, IntPtr layout, uint flags)
        {
            if ((flags & 1) != 0 && (type == 2 || category == InputProfile.KeyboardCategory))
            {
                changed(new InputProfile { Type = type, Language = language, ClassId = clsid, Category = category, ProfileId = profile, Layout = layout });
            }
            return 0;
        }
        public void Dispose()
        {
            if (cookie != uint.MaxValue && source != null) { source.UnadviseSink(cookie); cookie = uint.MaxValue; }
            source = null;
            if (manager != null)
            {
                if (activated) { manager.Deactivate(); activated = false; }
                Marshal.ReleaseComObject(manager); manager = null;
            }
        }
    }
    [ComVisible(true), Guid("71C6E74E-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IVoiceProfileActivationSink
    {
        [PreserveSig] int OnActivated(uint type, ushort language, ref Guid clsid, ref Guid category, ref Guid profile, IntPtr layout, uint flags);
    }
    [ComImport, Guid("4EA48A35-60AE-446F-8FD6-E6A8D82459F7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IVoiceTsfSource
    {
        [PreserveSig] int AdviseSink(ref Guid id, [MarshalAs(UnmanagedType.IUnknown)] object sink, out uint cookie);
        [PreserveSig] int UnadviseSink(uint cookie);
    }
    [ComImport, Guid("AA80E801-2021-11D2-93E0-0060B067B86E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IVoiceThreadMgr
    {
        [PreserveSig] int Activate(out uint client);
        [PreserveSig] int Deactivate();
    }
}
