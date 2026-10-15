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
        private void WriteNoteLog(NoteLogKind kind, string action, string text, DateTime? timestamp = null)
        {
            try
            {
                noteLogStore.EnqueueAppend(kind, action, text, timestamp);
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e, NoteLogStore.GetDisplayName(kind) + "写入失败。");
            }
        }

        private void NoteLogWriteFailed(Exception error)
        {
            DebugHelper.WriteException(error, "便签日志写入失败，待保存内容已保留。");
            if (!IsDisposed && IsHandleCreated)
            {
                try
                {
                    BeginInvoke((Action)(() => {
                        if (!IsDisposed && !Disposing)
                            TaskHelpers.ShowNotificationTip("便签日志暂未保存，内容已保留；请检查目录后重试。", "小蟒", 5000);
                    }));
                }
                catch (InvalidOperationException) { } // The window may close between checking and posting.
            }
        }

        internal void PreparePendingNoteWrites()
        {
            favoritesPage.CommitPendingFavorites();
            if (favoritesPage.dirtyFavoriteIds.Count != 0) throw new IOException("收藏夹尚未保存成功，请重试。");
            // Active recording is finalized asynchronously by FlushNoteLogsAsync.
        }

        internal void SetPendingWritesPaused(bool paused) => pendingWritesPaused = paused;

        internal async Task FlushNoteLogsAsync()
        {
            if (compactRecording) await StopVoiceRecordingAsync();
            await noteVoiceTask;
            if (noteLogSeedTask.IsFaulted) noteLogSeedTask = SeedExistingNoteItemsAsync();
            await noteLogSeedTask;
            await noteLogStore.FlushAsync();
        }

        private async Task SeedExistingNoteItemsAsync()
        {
            if (Settings.NoteLogSeedVersion >= 1) return;
            var todos = Settings.Items.Select(x => (x.IsCompleted ? "现有已办" : "现有待办", x.Text)).ToArray();
            var clipboard = Settings.ClipboardItems.Select(x => ("现有记录", x.GetDisplayText())).ToArray();
            var favorites = Settings.FavoriteItems.Select(x => ("现有记录", x.Text)).ToArray();

            try
            {
                await noteLogStore.FlushAsync();
                await Task.Run(() => {
                    noteLogStore.CleanupExpired();
                    SeedMissingLogEntries(NoteLogKind.Todo, todos);
                    SeedMissingLogEntries(NoteLogKind.Clipboard, clipboard);
                    SeedMissingLogEntries(NoteLogKind.Favorites, favorites);
                });
                Settings.NoteLogSeedVersion = 1;
                SettingManager.SaveApplicationConfigAsync();
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e, "现有便签内容补录失败。");
                throw;
            }
        }

        private void SeedMissingLogEntries(NoteLogKind kind, IEnumerable<(string Action, string Text)> currentItems)
        {
            Dictionary<string, int> existingCounts = noteLogStore.Read(kind)
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .GroupBy(x => NormalizeLogKey(x.Text), StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

            foreach ((string action, string text) in currentItems.Where(x => !string.IsNullOrWhiteSpace(x.Text)))
            {
                string key = NormalizeLogKey(text);
                if (existingCounts.TryGetValue(key, out int count) && count > 0)
                {
                    existingCounts[key] = count - 1;
                }
                else
                {
                    // Seed today so every item that currently exists remains visible within
                    // the rolling 100-day log window, even if it was created before logging existed.
                    noteLogStore.Append(kind, action, text, DateTime.Now);
                }
            }
        }

        private static string NormalizeLogKey(string text) =>
            text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();

        private static string NormalizeClipboardText(string text)
        {
            return ClipboardTextReader.Normalize(text);
        }

    }
}
