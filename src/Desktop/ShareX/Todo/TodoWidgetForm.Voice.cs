using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;
using ShareX.Properties;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private Task noteVoiceTask = Task.CompletedTask;
        private bool recordingClosePending;
        private Func<string, string, VoiceTranscriptionResult> transcribeVoiceNote = VoiceNoteTranscriber.TranscribeChinese;
        private readonly Timer compactLongPressTimer;
        private readonly Timer voiceRecordingTimer;
        private readonly Timer voicePlaybackTimer;
        private bool compactRecording, compactRecordingHandledPress, stoppingVoiceRecording;
        private DateTime voiceRecordingStartedAt;
        private bool voiceTypingActive, voiceTypingRecording;
        private VoiceNoteRecorder voiceRecorder;
        private string pendingVoicePath;
        private readonly VoiceNotePlaybackService voicePlayback = new VoiceNotePlaybackService();
        private Guid? playingVoiceItemId;
        private DateTime voicePlaybackEndsAt;

        private async Task FinishRecordingAndCloseAsync()
        {
            recordingClosePending = true;
            try
            {
                await StopVoiceRecordingAsync();
                await FlushNoteLogsAsync();
                await SettingManager.FlushApplicationConfigAsync();
                if (!IsDisposed && !Disposing) Close();
            }
            catch (Exception error)
            {
                closingWidget = false;
                DebugHelper.WriteException(error, "Voice-note window close preparation failed.");
                TaskHelpers.ShowNotificationTip("保存尚未完成，窗口已保留，请重试关闭。", "闪念胶囊", 5000);
            }
            finally { recordingClosePending = false; }
        }

        private void CompactLabel_MouseDown(object sender, MouseEventArgs e)
        {
            HideCompactHint();
            if (e.Button != MouseButtons.Left) return;
            Control surface = (Control)sender;
            collapseTimer.Stop();
            compactDragging = true;
            compactMoved = false;
            compactRecordingHandledPress = false;
            compactDragStartMouse = surface.PointToScreen(e.Location);
            compactDragStartY = Top;
            surface.Capture = true;
            compactLongPressTimer.Stop();
            compactLongPressTimer.Start();
        }

        private void CompactLabel_MouseMove(object sender, MouseEventArgs e)
        {
            if (!compactDragging) return;
            Control surface = (Control)sender;
            Point mouse = surface.PointToScreen(e.Location);
            int deltaY = mouse.Y - compactDragStartMouse.Y;
            // This float only moves vertically. Tolerate click jitter and ignore horizontal
            // movement; once a vertical drag starts, keep it a drag until release.
            if (!compactRecording && Math.Abs(deltaY) >= D(6))
            {
                compactMoved = true;
                compactLongPressTimer.Stop();
            }
            if (compactRecording) return;
            if (!compactMoved) return;
            Rectangle area = dockScreen.WorkingArea;
            int dragHeight = surface is CompactOverlayForm ? Math.Max(Height, surface.Height) : Height;
            Top = Math.Clamp(compactDragStartY + deltaY, area.Top, Math.Max(area.Top, area.Bottom - dragHeight));
        }

        private void CompactLabel_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !compactDragging) return;
            compactLongPressTimer.Stop();
            compactDragging = false;
            ((Control)sender).Capture = false;
            SaveCompactDragPosition();
            if (compactRecording)
            {
                _ = StopVoiceRecordingAsync();
                return;
            }
            if (!compactMoved && !compactRecordingHandledPress) OpenPanel();
        }

        private void CompactSurface_MouseCaptureChanged(Control surface)
        {
            if (!compactDragging || surface.Capture) return;
            compactDragging = false;
            compactLongPressTimer?.Stop();
            if (compactRecording) _ = StopVoiceRecordingAsync();
            SaveCompactDragPosition();
        }

        private void SaveCompactDragPosition()
        {
            if (!compactMoved || Settings.WidgetY == Top) return;
            Settings.WidgetY = Top;
            SettingManager.SaveApplicationConfigAsync();
        }

        // Reserve the channel through final transcription. Global typing never calls
        // the voice-note persistence path. Only active recording changes the logos.
        internal bool TryBeginVoiceTyping()
        {
            if (pendingWritesPaused) return false;
            if (IsDisposed || Disposing || closingWidget || compactRecording ||
                stoppingVoiceRecording || voiceTypingActive) return false;
            voiceTypingActive = true;
            return true;
        }

        internal void StartVoiceTypingFeedback()
        {
            if (IsDisposed || Disposing || !voiceTypingActive || voiceTypingRecording) return;
            voiceTypingRecording = true;
            voiceRecordingStartedAt = DateTime.UtcNow;
            UpdateRecordingFeedback(1f);
            voiceRecordingTimer.Start();
        }

        internal void StopVoiceTypingFeedback()
        {
            if (IsDisposed || Disposing || !voiceTypingRecording) return;
            voiceTypingRecording = false;
            if (!compactRecording) voiceRecordingTimer.Stop();
            UpdateRecordingFeedback(1f);
        }

        internal void EndVoiceTyping()
        {
            StopVoiceTypingFeedback();
            voiceTypingActive = false;
        }

        private void UpdateRecordingFeedback(float opacity)
        {
            bool recording = compactRecording || voiceTypingRecording;
            compactLabel.IsRecording = recording;
            compactLabel.PulseOpacity = opacity;
            compactLabel.Invalidate();
            foreach (RecordingTitleLogo logo in new[] { todoPage.todoTitleLogo, clipboardPage.clipboardTitleLogo, favoritesPage.favoriteTitleLogo, toolsPage.newsTitleLogo, diaryPage.diaryTitleLogo })
            {
                logo.IsRecording = recording;
                logo.PulseOpacity = opacity;
                logo.Invalidate();
            }
            RefreshCompactOverlay();
        }

        private void StartVoiceRecording()
        {
            if (pendingWritesPaused) return;
            if (PersonalDataManager.Busy) return;
            if (compactRecording || stoppingVoiceRecording) return;
            compactRecordingHandledPress = true;
            if (voiceTypingActive) return;
            try
            {
                pendingVoicePath = VoiceNoteStorage.CreateRecordingPath(Program.PersonalFolder, DateTime.Now);
                voiceRecorder = new VoiceNoteRecorder();
                voiceRecorder.Start(pendingVoicePath);
                compactRecording = true;
                voiceRecordingStartedAt = DateTime.UtcNow;
                UpdateRecordingFeedback(1f);
                voiceRecordingTimer.Start();
            }
            catch (Exception e)
            {
                voiceRecorder?.Dispose();
                voiceRecorder = null;
                VoiceNoteStorage.TryDelete(pendingVoicePath);
                pendingVoicePath = null;
                TaskHelpers.ShowNotificationTip("无法开始录音，请检查麦克风和保存目录：" + e.Message, "闪念胶囊", 5000);
            }
        }

        private async void VoiceRecordingTimer_Tick(object sender, EventArgs e)
        {
            if (!compactRecording && !voiceTypingRecording) return;
            double elapsed = (DateTime.UtcNow - voiceRecordingStartedAt).TotalMilliseconds;
            UpdateRecordingFeedback((float)(0.5d + 0.5d * Math.Sin(elapsed / 260d)));
            if (compactRecording && elapsed >= 60000d) await StopVoiceRecordingAsync();
        }

        private Task StopVoiceRecordingAsync()
        {
            if (!compactRecording || stoppingVoiceRecording) return noteVoiceTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            noteVoiceTask = completion.Task;
            _ = CompleteStopAsync(completion);
            return noteVoiceTask;
        }

        private async Task CompleteStopAsync(TaskCompletionSource<bool> completion)
        {
            try { await StopVoiceRecordingCoreAsync(); completion.TrySetResult(true); }
            catch (Exception error) { completion.TrySetException(error); }
        }

        private async Task StopVoiceRecordingCoreAsync()
        {
            if (!compactRecording || stoppingVoiceRecording) return;
            stoppingVoiceRecording = true;
            compactRecording = false;
            voiceRecordingTimer.Stop();
            try
            {
                UpdateRecordingFeedback(1f);
                UpdateCompactText();
                if (Visible && !expanded && !closingWidget && !pendingWritesPaused)
                {
                    compactHintSurface = compactOverlay != null && compactOverlay.Visible ? compactOverlay : compactLabel;
                    UpdateCompactHintWindow();
                }
            }
            catch (Exception error) { DebugHelper.WriteException(error, "Voice processing feedback failed; recording will still be saved."); }

            string wavePath = pendingVoicePath;
            pendingVoicePath = null;
            string personalFolder = Program.PersonalFolder;
            string moduleAtStop = Settings.ActiveModule;
            VoiceNoteRecorder recorder = voiceRecorder;
            voiceRecorder = null;
            TimeSpan elapsed = DateTime.UtcNow - voiceRecordingStartedAt;
            TodoItem savedItem = null;
            string pendingText = "**闪念胶囊**" + Environment.NewLine + "转写未完成，可播放录音";
            try
            {
                elapsed = await VoiceNoteRecordingService.StopAsync(recorder, elapsed);
                if (IsDisposed || Disposing) return;

                if (elapsed.TotalSeconds < 1d)
                {
                    VoiceNoteStorage.TryDelete(wavePath);
                    TaskHelpers.ShowNotificationTip("录音时间太短", "闪念胶囊", 5000);
                    return;
                }

                int durationSeconds = VoiceNoteStorage.GetDurationSeconds(wavePath, elapsed);
                savedItem = new TodoItem
                {
                    Text = pendingText,
                    VoiceAudioRelativePath = VoiceNoteStorage.ToRelativePath(Program.PersonalFolder, wavePath),
                    VoiceDurationSeconds = durationSeconds
                };
                SaveVoiceTodo(savedItem, "语音新增（录音已保存）");
                if (Settings.ActiveModule == moduleAtStop) SelectModule("Todo", false);
                try { await SettingManager.FlushApplicationConfigAsync(); }
                catch (IOException)
                {
                    TaskHelpers.ShowNotificationTip("录音文件已保留，但待办保存失败，请检查保存目录后重试。", "闪念胶囊", 5000);
                    return;
                }
                // Closing/transfer preparation only needs durable audio and its pending
                // item; do not start fresh model work while the application is exiting.
                if (closingWidget || pendingWritesPaused) return;
                VoiceTranscriptionResult result = await Task.Run(() => transcribeVoiceNote(wavePath, personalFolder));
                if (IsDisposed || Disposing || !Settings.Items.Contains(savedItem)) return;
                // Do not overwrite an edit made while transcription was running.
                if (savedItem.Text != pendingText) return;
                savedItem.Text = "**闪念胶囊**" + Environment.NewLine + (result.Succeeded ? result.Text : "转写失败，可播放录音");
                WriteNoteLog(NoteLogKind.Todo, result.Succeeded ? "语音转写完成" : "语音转写失败", savedItem.Text);
                SaveAndRender();
                string notice = result.Succeeded ?
                    (string.IsNullOrWhiteSpace(result.WarningMessage) ? "已写入待办" : "已写入待办；" + result.WarningMessage) :
                    result.ErrorMessage + "；录音已写入待办，可直接播放";
                TaskHelpers.ShowNotificationTip(notice, "闪念胶囊", 5000);
            }
            catch (Exception e)
            {
                if (IsDisposed || Disposing) return;
                if (savedItem != null)
                {
                    if (Settings.Items.Contains(savedItem))
                        TaskHelpers.ShowNotificationTip("录音已保留，但转写失败：" + e.Message, "闪念胶囊", 5000);
                    return;
                }
                if (File.Exists(wavePath))
                {
                    int durationSeconds = VoiceNoteStorage.GetDurationSeconds(wavePath, elapsed);
                    TodoItem item = new TodoItem
                    {
                        Text = "**闪念胶囊**" + Environment.NewLine + "转写失败，可播放录音",
                        VoiceAudioRelativePath = VoiceNoteStorage.ToRelativePath(Program.PersonalFolder, wavePath),
                        VoiceDurationSeconds = durationSeconds
                    };
                    SaveVoiceTodo(item, "语音新增（转写失败）");
                    if (Settings.ActiveModule == moduleAtStop) SelectModule("Todo", false);
                    TaskHelpers.ShowNotificationTip("录音已保留，但转写失败：" + e.Message, "闪念胶囊", 5000);
                }
                else
                {
                    TaskHelpers.ShowNotificationTip("录音保存失败：" + e.Message, "闪念胶囊", 5000);
                }
            }
            finally
            {
                stoppingVoiceRecording = false;
                if (!IsDisposed && !Disposing)
                {
                    HideCompactHint();
                    UpdateCompactText();
                }
            }
        }

        private void SaveVoiceTodo(TodoItem item, string logAction)
        {
            Settings.Items.Add(item);
            WriteNoteLog(NoteLogKind.Todo, logAction, item.Text, item.CreatedAt);
            SaveAndRender();
            // The caller may return to Todo only if the user has not selected another
            // module while background recording finalization was running.
        }

        private void ToggleVoicePlayback(Guid itemId)
        {
            if (playingVoiceItemId == itemId)
            {
                StopVoicePlayback();
                return;
            }

            TodoItem item = Settings.Items.FirstOrDefault(x => x.Id == itemId);
            string path = item == null ? "" : VoiceNoteStorage.ResolvePath(Program.PersonalFolder, item.VoiceAudioRelativePath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                TaskHelpers.ShowNotificationTip("录音文件不存在，可能已被移动或删除。", "闪念胶囊", 5000);
                return;
            }

            try
            {
                StopVoicePlayback();
                voicePlayback.Play(path);
                playingVoiceItemId = itemId;
                voicePlaybackEndsAt = DateTime.UtcNow.AddSeconds(Math.Max(1, item.VoiceDurationSeconds));
                voicePlaybackTimer.Start();
                RefreshVisibleVoiceButtons();
            }
            catch (Exception e)
            {
                StopVoicePlayback();
                TaskHelpers.ShowNotificationTip("无法播放录音：" + e.Message, "闪念胶囊", 5000);
            }
        }

        private void StopVoicePlayback()
        {
            voicePlaybackTimer.Stop();
            voicePlayback.Stop();
            playingVoiceItemId = null;
            RefreshVisibleVoiceButtons();
        }

        private void RefreshVisibleVoiceButtons()
        {
            foreach (Panel row in todoPage.todoRowPool.Where(x => x.Visible))
            {
                Button button = row.Controls.OfType<Button>().FirstOrDefault(x => x.Name == "VoicePlaybackButton");
                if (button == null || row.Tag is not Guid id) continue;
                bool playing = playingVoiceItemId == id;
                button.Image = playing ? sharedStopIcon : sharedSpeakerIcon;
                button.AccessibleName = playing ? "停止播放录音" : "播放录音";
                button.Invalidate();
            }
        }

    }
}
