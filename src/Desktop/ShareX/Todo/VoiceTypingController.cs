using Microsoft.Win32;
using ShareX.HelpersLib;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace ShareX
{
    internal sealed class VoiceTypingController : IDisposable
    {
        internal const int RecognitionIntervalMilliseconds = 350;
        internal const int ActivationHoldMilliseconds = 180;
        private static int activeSessions;
        internal static bool IsTyping => Volatile.Read(ref activeSessions) != 0;
        private readonly Control owner;
        private readonly Func<bool> enabled;
        private readonly VoiceTypingHooks hooks;
        private readonly Timer timer;
        private readonly Func<TodoWidgetForm> getWidget;
        private readonly Func<VoiceTypingAudio> createAudio;
        private readonly VoiceInputSetupCoordinator setup;
        private long armedAt;
        private bool disposed;
        private bool warmupRequested;
        private bool awaitingRegistration, registrationPending, registrationNoticeShown;
        private Session session;

        private sealed class Session
        {
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal readonly TaskCompletionSource<bool> Stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> Activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly Stopwatch Clock = Stopwatch.StartNew();
            internal readonly VoiceRecognitionDiagnostics Diagnostics = new VoiceRecognitionDiagnostics();
            internal VoiceTypingTarget Target;
            internal VoiceInputOutput Output;
            internal TodoWidgetForm Widget;
            internal string Sent = "", Latest = "", UnsafeReason = "";
            internal bool Finishing, InputUncertain;
            internal bool Confirmed;
            internal int Counted;
        }

        internal VoiceTypingController(Control owner, Func<bool> enabled, Func<TodoWidgetForm> getWidget)
            : this(owner, enabled, getWidget, () => new VoiceTypingAudio()) { }

        internal VoiceTypingController(Control owner, Func<bool> enabled, Func<TodoWidgetForm> getWidget,
            Func<VoiceTypingAudio> createAudio)
            : this(owner, enabled, getWidget, createAudio, VoiceInputSetupCoordinator.Default) { }

        internal VoiceTypingController(Control owner, Func<bool> enabled, Func<TodoWidgetForm> getWidget,
            Func<VoiceTypingAudio> createAudio, VoiceInputSetupCoordinator setup)
        {
            this.owner = owner;
            this.enabled = enabled;
            this.getWidget = getWidget;
            this.createAudio = createAudio;
            this.setup = setup;
            hooks = new VoiceTypingHooks();
            hooks.Active = () => armedAt != 0 || session != null;
            hooks.Changed = OnKey;
            timer = new Timer { Interval = 60 };
            timer.Tick += Tick;
            timer.Start();
            SystemEvents.SessionSwitch += SessionSwitch;
            SystemEvents.PowerModeChanged += PowerChanged;
        }

        private void OnKey(VoiceTypingKeyAction action)
        {
            // Called by hooks: mutate state or post work only, never perform capture, ASR,
            // UI Automation, disk I/O or a modal dialog on the hook callback.
            if (disposed) return;
            switch (action)
            {
                case VoiceTypingKeyAction.Arm:
                    if (session == null && !registrationPending && !setup.IsBusy && enabled())
                    {
                        armedAt = Environment.TickCount64;
                        long request = armedAt;
                        // Post immediately: the 180ms gesture check must not delay capture.
                        // Never initialize the microphone or widget in the keyboard hook.
                        try
                        {
                            owner.BeginInvoke(new Action(() =>
                            {
                                if (!disposed && armedAt == request && hooks.Chord.Held && session == null && !setup.IsBusy && enabled())
                                {
                                    // Disk/registry inspection runs on the UI thread after the
                                    // hook returns. Missing registration must not start recording.
                                    awaitingRegistration = setup.State != VoiceInputRegistrationState.Registered;
                                    if (!awaitingRegistration) StartSession();
                                }
                            }));
                        }
                        catch (InvalidOperationException) { armedAt = 0; }
                    }
                    break;
                case VoiceTypingKeyAction.Release:
                    ReleaseSession();
                    break;
                case VoiceTypingKeyAction.Cancel:
                    armedAt = 0;
                    session?.Cancellation.Cancel();
                    break;
                case VoiceTypingKeyAction.Interference:
                    armedAt = 0;
                    if (session != null)
                    {
                        if (!session.Confirmed) session.Cancellation.Cancel();
                        else
                        {
                            session.UnsafeReason = "输入位置或按键发生变化，已暂停自动输入。";
                            session.Output?.PreserveUserInputChoice();
                        }
                    }
                    break;
            }
        }

        private void ConfirmSession()
        {
            if (awaitingRegistration && !registrationPending && armedAt != 0 &&
                Environment.TickCount64 - armedAt >= ActivationHoldMilliseconds && enabled())
            {
                awaitingRegistration = false; armedAt = 0; registrationPending = true;
                _ = RegisterForTypingAsync();
                return;
            }
            if (session == null || session.Confirmed || session.Cancellation.IsCancellationRequested ||
                armedAt == 0 || Environment.TickCount64 - armedAt < ActivationHoldMilliseconds || !enabled()) return;
            session.Confirmed = true;
            session.Activation.TrySetResult(true);
            armedAt = 0;
        }

        private async Task RegisterForTypingAsync()
        {
            try
            {
                var result = await setup.EnsureRegisteredAsync(true);
                if (!disposed && (result.Attempted || !registrationNoticeShown))
                {
                    registrationNoticeShown = true;
                    TaskHelpers.ShowNotificationTip(result.Message, "小蟒语音输入", 5000);
                }
            }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "Voice input first-use registration failed.");
                if (!disposed && !registrationNoticeShown)
                {
                    registrationNoticeShown = true;
                    TaskHelpers.ShowNotificationTip("请到输入法 → 小蟒语音输入注册组件。", "小蟒语音输入", 5000);
                }
            }
            finally { registrationPending = false; }
        }

        private void ReleaseSession()
        {
            // A release can precede the next UI timer tick. A genuine >=180ms hold
            // must still flush its buffered audio; shorter gestures are discarded.
            ConfirmSession();
            armedAt = 0;
            if (session == null) return;
            if (!session.Confirmed) session.Cancellation.Cancel();
            session.Stop.TrySetResult(true);
        }

        private void Tick(object sender, EventArgs e)
        {
            if (!enabled())
            {
                warmupRequested = false;
                armedAt = 0;
                session?.Cancellation.Cancel();
            }
            else if (!warmupRequested)
            {
                warmupRequested = true;
                string folder = Program.PersonalFolder;
                // Load the existing local model off the UI thread. This does not record
                // audio or download a model, and the normal idle release still applies.
                _ = Task.Run(() => VoiceNoteTranscriber.PreloadTypingModel(folder));
            }
            if (hooks.Chord.Held) ConfirmSession();
            if (session == null) return;
            // No foreground target is a clipboard fallback, not an explicit cancellation.
            // Lock, suspend and Escape retain their separate cancellation paths.
            if (NativeMethods.GetForegroundWindow() == IntPtr.Zero)
                session.UnsafeReason = "未检测到输入焦点，结束后复制到系统剪贴板。";
            if (session.Widget?.IsDisposed == true || session.Widget?.Disposing == true) session.Cancellation.Cancel();
            if (session.Cancellation.IsCancellationRequested) { session.Widget?.StopVoiceTypingFeedback(); return; }
            if (!VoiceTypingHooks.CtrlAltDown()) ReleaseSession();
            if (!session.Target.NativeMatches()) session.UnsafeReason = "输入窗口或输入框已切换，已暂停自动输入。";
            if (session.Clock.Elapsed >= TimeSpan.FromSeconds(60)) session.Stop.TrySetResult(true);
            if (session.Stop.Task.IsCompleted && !session.Finishing)
            {
                session.Finishing = true;
                session.Widget?.StopVoiceTypingFeedback();
            }
        }

        private void StartSession()
        {
            Session current = new Session();
            try
            {
                current.Target = VoiceTypingTarget.CaptureNative();
                current.Output = new VoiceInputOutput(current.Target.Window, current.Target.Focus);
                current.Output.CaptureInputState();
                current.Widget = getWidget();
                if (current.Widget == null || !current.Widget.TryBeginVoiceTyping())
                {
                    armedAt = 0;
                    current.Output.RestoreInputState();
                    current.Cancellation.Dispose();
                    current.Diagnostics.Dispose();
                    return;
                }
                session = current;
                current.Counted = 1; Interlocked.Increment(ref activeSessions);
                string folder = Program.PersonalFolder;
                _ = Task.Run(() => RunSession(current, folder));
            }
            catch (Exception error)
            {
                current.Diagnostics.Exception("session-start", error);
                current.Diagnostics.Dispose();
                if (Interlocked.Exchange(ref current.Counted, 0) != 0) Interlocked.Decrement(ref activeSessions);
                session = null; current.Cancellation.Dispose(); current.Widget?.EndVoiceTyping();
                current.Output?.RestoreInputState();
                armedAt = 0;
                TaskHelpers.ShowNotificationTip("无法开始语音输入：" + error.Message, "语音打字", 5000);
            }
        }

        private async Task RunSession(Session current, string folder)
        {
            CancellationToken token = current.Cancellation.Token;
            VoiceTypingTranscript transcript = new VoiceTypingTranscript(
                (samples, cancel) => VoiceNoteTranscriber.TranscribeTypingSegmentWithDiagnostics(samples, folder, cancel, current.Diagnostics));
            try
            {
                using VoiceTypingAudio audio = createAudio();
                audio.Diagnostics = current.Diagnostics;
                token.ThrowIfCancellationRequested();
                audio.Start();
                Stopwatch captureClock = Stopwatch.StartNew();
                using CancellationTokenRegistration cancelRecording = token.Register(() => current.Stop.TrySetResult(true));
                // Stop the microphone independently of inference. A native model decode may
                // still be running when the user releases Ctrl/Alt.
                Task stopCapture = current.Stop.Task.ContinueWith(_ => audio.StopAsync(),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
                try
                {
                  // Keep the beginning in the same memory buffer while the chord is
                  // being confirmed. No transcription, clipboard or typing before it.
                  await current.Activation.Task.WaitAsync(token).ConfigureAwait(false);
                  await OnUI(async () =>
                  {
                      if (disposed || session != current || token.IsCancellationRequested) return;
                      if (current.UnsafeReason.Length != 0 || !current.Target.NativeMatches())
                      { current.UnsafeReason = "输入位置已切换。"; return; }
                      try
                      {
                          VoiceInputResult result = await current.Output.BeginAsync(token);
                          if (!result.Success) current.UnsafeReason = "系统输入组件未能建立可编辑的输入范围。";
                      }
                      catch (OperationCanceledException) { throw; }
                      catch (Exception error) { current.UnsafeReason = error.Message; }
                  }).ConfigureAwait(false);
                  await Task.WhenAny(audio.Ready, audio.Ended, current.Stop.Task).WaitAsync(token).ConfigureAwait(false);
                  token.ThrowIfCancellationRequested();
                  await OnUI(() =>
                  {
                      if (!disposed && session == current && !token.IsCancellationRequested &&
                          !current.Stop.Task.IsCompleted && !audio.Ended.IsCompleted && audio.Ready.IsCompletedSuccessfully)
                          current.Widget?.StartVoiceTypingFeedback();
                      return Task.CompletedTask;
                  }).ConfigureAwait(false);
                  bool first = true;
                  while (true)
                  {
                    int delay = first ? Math.Max(0, RecognitionIntervalMilliseconds - (int)captureClock.ElapsedMilliseconds) : RecognitionIntervalMilliseconds;
                    first = false;
                    await Task.WhenAny(Task.Delay(delay, token), current.Stop.Task, audio.Ended).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    bool final = current.Stop.Task.IsCompleted;
                    if (audio.Ended.IsCompleted && !final) throw new InvalidOperationException("麦克风录音中断：设备已断开或录音意外停止。");
                    if (final) await stopCapture.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    int audioOffset = transcript.AudioOffset;
                    float[] samples = audio.SnapshotFrom(audioOffset);
                    if (final) current.Diagnostics.AudioSliceSummary(samples, audioOffset);
                    transcript.UpdateSlice(samples, audioOffset, final, token);
                    if (final) current.Diagnostics.Event("recognition-final", $"attempts={transcript.RecognitionCount}; characters={transcript.Text.Length}; error={transcript.Error.Length != 0}");
                    await OnUI(async () =>
                    {
                        if (disposed || session != current || token.IsCancellationRequested) return;
                        current.Latest = transcript.Text;
                        if (!string.IsNullOrEmpty(transcript.Error)) current.UnsafeReason = "部分语音未能识别，请检查结果后复制。";
                        await Deliver(current, transcript.Text);
                    }).ConfigureAwait(false);
                    if (final)
                    {
                        await OnUI(async () =>
                        {
                            if (session == current && !token.IsCancellationRequested)
                                await Finish(current, transcript.Text, transcript.Error);
                        }).ConfigureAwait(false);
                        break;
                    }
                  }
                }
                finally
                {
                    current.Stop.TrySetResult(true);
                    try { await stopCapture.ConfigureAwait(false); }
                    catch when (token.IsCancellationRequested) { }
                }
            }
            catch (OperationCanceledException) { current.Diagnostics.Event("session-cancelled", "cancelled"); }
            catch (Exception error)
            {
                current.Diagnostics.Exception("session-failed", error);
                // A device can fail during the speculative 180ms capture. Report only
                // if the gesture is confirmed; an accidental shortcut stays silent.
                try
                {
                    await current.Activation.Task.WaitAsync(token).ConfigureAwait(false);
                    await OnUI(async () =>
                    {
                        if (session == current && current.Confirmed && !token.IsCancellationRequested)
                            await Finish(current, transcript.Text, error is System.IO.IOException ? error.Message : "语音输入失败：" + error.Message);
                    }).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
            finally
            {
                try
                {
                    if (current.Output != null && current.Output.HasComposition)
                    {
                        try { await current.Output.CancelAsync().ConfigureAwait(false); }
                        catch (Exception error) { DebugHelper.WriteException(error, "Voice TSF composition cleanup failed."); }
                    }
                    await OnUI(() =>
                    {
                        current.Output?.RestoreInputState();
                        if (session == current) { session = null; armedAt = 0; current.Widget?.EndVoiceTyping(); }
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);
                }
                finally
                {
                    current.Diagnostics.Dispose();
                    current.Cancellation.Dispose();
                    if (Interlocked.Exchange(ref current.Counted, 0) != 0) Interlocked.Decrement(ref activeSessions);
                }
            }
        }

        private async Task Deliver(Session current, string text)
        {
            if (string.IsNullOrEmpty(text) || text == current.Sent || current.UnsafeReason.Length != 0) return;
            if (disposed || session != current || current.Cancellation.IsCancellationRequested) return;
            if (!current.Target.NativeMatches()) { current.UnsafeReason = "输入位置已切换。"; return; }
            VoiceInputResult result = await current.Output.UpdateAsync(text, false, current.Cancellation.Token);
            if (result.Success) current.Sent = text;
            else
            {
                current.InputUncertain = true;
                current.UnsafeReason = "系统输入组件未能确认文字更新。";
                DebugHelper.WriteLine($"Voice TSF update rejected: status={result.Status}.");
            }
        }

        private async Task Finish(Session current, string text, string error)
        {
            if (disposed || session != current || current.Cancellation.IsCancellationRequested) return;
            bool safe = current.UnsafeReason.Length == 0 && !current.InputUncertain && current.Target.NativeMatches();
            if (!string.IsNullOrWhiteSpace(text) && string.IsNullOrEmpty(error) && safe)
            {
                VoiceInputResult result = await current.Output.UpdateAsync(text, true, current.Cancellation.Token);
                if (result.Success) { current.Sent = text; Complete(current, text, error); return; }
                current.InputUncertain = true;
                DebugHelper.WriteLine($"Voice TSF commit rejected: status={result.Status}.");
            }
            if (current.Output.HasComposition) await current.Output.CancelAsync();
            current.Sent = "";
            Complete(current, text, error);
        }

        private void Complete(Session current, string text, string error)
        {
            if (!string.IsNullOrEmpty(error)) current.Diagnostics.Failure("completion", error);
            else if (string.IsNullOrWhiteSpace(text)) current.Diagnostics.Event("no-speech", "No clear speech; check microphone input/volume.");
            current.Widget?.StopVoiceTypingFeedback();
            if (disposed || current.Cancellation.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                TaskHelpers.ShowNotificationTip(string.IsNullOrEmpty(error) ? "没有识别到清晰语音，请检查麦克风后重试。" : error, "语音打字", 5000);
                return;
            }
            if (!text.Equals(current.Sent, StringComparison.Ordinal) || current.InputUncertain)
            {
                bool copied = VoiceTypingClipboard.CopyWithoutHistory(text);
                string message = !copied ? "系统剪贴板暂不可用，语音文字未复制成功，请重试。" :
                    !string.IsNullOrEmpty(error) ? error + " 已识别文字已复制，请检查后粘贴。" :
                    !VoiceInputComponent.Installed ? "系统语音输入组件未完整注册；文字已复制，请按 Ctrl + V 粘贴。可到输入法 → 小蟒语音输入注册组件。" :
                    "系统输入未完成，最终文字已复制；请检查输入框后按 Ctrl + V 粘贴。";
                TaskHelpers.ShowNotificationTip(message, "语音打字", 5000);
            }
            else if (!string.IsNullOrEmpty(error)) TaskHelpers.ShowNotificationTip(error, "语音打字", 5000);
        }
        private Task OnUI(Func<Task> action)
        {
            if (disposed || owner.IsDisposed || !owner.IsHandleCreated) return Task.CompletedTask;
            TaskCompletionSource<bool> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                owner.BeginInvoke(new Action(async () =>
                {
                    try { if (!disposed) await action(); done.TrySetResult(true); }
                    catch (Exception e) { done.TrySetException(e); }
                }));
            }
            catch (InvalidOperationException) { done.TrySetResult(true); }
            return done.Task;
        }

        private void SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff)
                _ = OnUI(() => { armedAt = 0; hooks.Chord.Reset(); session?.Cancellation.Cancel(); return Task.CompletedTask; });
        }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
                _ = OnUI(() => { armedAt = 0; hooks.Chord.Reset(); session?.Cancellation.Cancel(); return Task.CompletedTask; });
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SystemEvents.SessionSwitch -= SessionSwitch;
            SystemEvents.PowerModeChanged -= PowerChanged;
            timer.Stop(); timer.Dispose(); hooks.Dispose();
            session?.Cancellation.Cancel();
            session?.Output?.RestoreInputState();
            session?.Widget?.EndVoiceTyping();
        }
    }
}
