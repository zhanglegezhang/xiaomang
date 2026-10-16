using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShareX.HelpersLib;

namespace ShareX
{
    internal static class PersonalDataManager
    {
        internal static bool Busy { get; private set; }
        internal static bool RestartReady => pendingScope != null || updateReady;
        private static bool updateReady;
        private static TransferScope pendingScope;
        private static string pendingStage;
        private static PersonalDataKind[] pendingKinds;
        private static PersonalDataPaths pendingPaths;
        private static Size pendingDisplay;
        private static bool pendingDeferredInput;
        private static string pendingConsumedArchive;
        internal static PersonalDataPaths Paths() => new() { Personal = Path.GetFullPath(Program.PersonalFolder), Input = XiaomangInputMethodManager.UserDirectory, Media = Path.GetFullPath(Program.ScreenshotsParentFolder), Hotkeys = Path.GetFullPath(SettingManager.HotkeysConfigFilePath), ConfigCodec = ConfigCodec };
        internal static async Task ConfigCodec(string mode, string input, string current, string output)
        {
            PersonalDataInputPreflight.RequireComponent(XiaomangInputMethodManager.BundleDirectory);
            var start = new ProcessStartInfo(Path.Combine(XiaomangInputMethodManager.BundleDirectory, "XiaomangInputWorker.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, WorkingDirectory = XiaomangInputMethodManager.BundleDirectory };
            foreach (var arg in new[] { "--personal-data-config", mode, input }) start.ArgumentList.Add(arg);
            if (mode == "merge") start.ArgumentList.Add(current);
            start.ArgumentList.Add(output);
            using var process = Process.Start(start) ?? throw new IOException("输入法配置转换未启动。");
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(); throw new IOException("输入法配置转换超时，未修改个人数据。"); }
            if (process.ExitCode != 0 || !File.Exists(output)) throw new IOException("输入法配置转换失败：" + await error);
        }
        private static async Task CloseOwnedWindows(string executable)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (process) {
                    string path; try { path = process.MainModule?.FileName; } catch { continue; }
                    if (!string.Equals(path, executable, StringComparison.OrdinalIgnoreCase) || process.HasExited) continue;
                    if (!process.CloseMainWindow()) throw new IOException("相关编辑窗口无法正常关闭，请先手动保存并关闭后重试。");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) { throw new IOException("编辑窗口尚未保存并关闭，操作已取消，原数据未修改。"); }
                }
            }
        }
        private sealed class TransferScope : IDisposable
        {
            internal readonly List<FileStream> Locks = new();
            internal readonly List<(Form form, bool enabled)> Forms = new();
            internal bool InputPaused, OldIgnore;
            internal IDisposable FileFavoritesLease;
            internal void Lock(string root) {
                PersonalDataArchive.SafePath(root); Directory.CreateDirectory(root);
                Locks.Add(new FileStream(Path.Combine(root, ".personal-transfer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            public void Dispose() {
                FileFavoritesLease?.Dispose(); FileFavoritesLease = null;
                foreach (var file in Locks) file.Dispose(); Locks.Clear();
                foreach (var (form, enabled) in Forms) if (!form.IsDisposed) form.Enabled = enabled;
                if (Program.HotkeyManager != null) Program.HotkeyManager.IgnoreHotkeys = OldIgnore;
                Busy = false;
                if (InputPaused && Program.MainForm != null && !Program.MainForm.IsDisposed) XiaomangInputMethodManager.Start();
            }
        }
        private static async Task<TransferScope> Pause(PersonalDataPaths paths, PersonalDataKind[] selected, Form transfer, PersonalDataTransferControl control = null)
        {
            control?.Token.ThrowIfCancellationRequested();
            if (Busy || VoiceTypingController.IsTyping || Application.OpenForms.OfType<TodoWidgetForm>().Any(form => form.PersonalDataRecordingActive) || TaskManager.Tasks.Any(task => task.IsWorking)) throw new IOException("请先结束录音、截图或录屏任务，再备份或导入。");
            var scope = new TransferScope(); Busy = true;
            try {
                if (selected.Contains(PersonalDataKind.Favorites) && FileFavoritesService.Store != null)
                    scope.FileFavoritesLease = await FileFavoritesService.Store.Hold();
                if (Program.HotkeyManager != null) { scope.OldIgnore = Program.HotkeyManager.IgnoreHotkeys; Program.HotkeyManager.IgnoreHotkeys = true; }
                if (!await Program.MainForm.PreparePersonalDataEditorsAsync()) throw new IOException("临时文档尚未保存，操作已取消。");
                control?.Token.ThrowIfCancellationRequested();
                var widgets = Application.OpenForms.OfType<TodoWidgetForm>().ToArray();
                foreach (var widget in widgets) widget.PreparePersonalDataSnapshot();
                foreach (Form form in Application.OpenForms.Cast<Form>().ToArray()) if (form != transfer) { scope.Forms.Add((form, form.Enabled)); form.Enabled = false; }
                if (selected.Contains(PersonalDataKind.Diary)) {
                    await CloseOwnedWindows(Path.Combine(AppContext.BaseDirectory, "ThirdParty", "LocalDiary", "xiaomang-diary.exe"));
                    scope.Lock(paths.Diary);
                }
                if (selected.Contains(PersonalDataKind.InputMethod)) {
                    await CloseOwnedWindows(Path.Combine(XiaomangInputMethodManager.BundleDirectory, "qingjian-settings.exe"));
                    scope.InputPaused = true;
                    await Task.Run(XiaomangInputMethodManager.PauseForPersonalData);
                    scope.Lock(paths.Input);
                }
                await Task.Run(() => SettingManager.WaitHotkeysConfig());
                SettingManager.SaveHotkeysConfigAsync();
                await SettingManager.FlushHotkeysConfigAsync();
                await SettingManager.FlushApplicationConfigAsync();
                foreach (var widget in widgets) await widget.FlushNoteLogsAsync();
                if (!SettingManager.SaveApplicationConfig()) throw new IOException("当前设置保存失败，操作已取消。");
                control?.Token.ThrowIfCancellationRequested();
                return scope;
            } catch { scope.Dispose(); throw; }
        }
        internal static async Task Export(Form transfer, PersonalDataKind[] selected, string destination, IProgress<string> progress, PersonalDataTransferControl control = null)
        {
            var paths = Paths();
            selected = await Task.Run(() => PersonalDataInputPreflight.ForExport(paths.Input, selected, XiaomangInputMethodManager.BundleDirectory, control?.Token ?? default));
            using var scope = await Pause(paths, selected, transfer, control);
            var app = PersonalDataPreferences.Json(Program.Settings);
            await Task.Run(() => SettingManager.WaitHotkeysConfig());
            var hotkeys = PersonalDataPreferences.Json(Program.HotkeysConfig);
            await Task.Run(() => PersonalDataArchive.Export(paths, app, hotkeys, selected, destination, Program.ProductVersion, progress, control: control));
        }
        internal static async Task BackupForUpdate(Form transfer, string destination, IProgress<string> progress, Func<Task> install)
        {
            var paths = Paths();
            var selected = await Task.Run(() => PersonalDataInputPreflight.ForExport(paths.Input, Enum.GetValues<PersonalDataKind>(), XiaomangInputMethodManager.BundleDirectory, default));
            using var scope = await Pause(paths, selected, transfer);
            try
            {
                var app = PersonalDataPreferences.Json(Program.Settings);
                var hotkeys = PersonalDataPreferences.Json(Program.HotkeysConfig);
                await Task.Run(() => PersonalDataArchive.Export(paths, app, hotkeys, selected, destination, Program.ProductVersion, progress));
                updateReady = true;
                await install();
            }
            finally { updateReady = false; }
        }

        internal static async Task QueueImport(Form transfer, PersonalDataKind[] selected, string source, IProgress<string> progress, PersonalDataTransferControl control = null)
        {
            bool deferInput = selected.Contains(PersonalDataKind.InputMethod) && !PersonalDataInputPreflight.ConverterPresent(XiaomangInputMethodManager.BundleDirectory);
            var effective = deferInput ? selected.Where(k => k != PersonalDataKind.InputMethod).ToArray() : selected;
            var paths = Paths();
            var scope = await Pause(paths, effective, transfer, control);
            string stage = Path.Combine(paths.Personal, "PersonalDataRestore", Guid.NewGuid().ToString("N"));
            try {
                var app = PersonalDataPreferences.Json(Program.Settings);
                await Task.Run(() => {
                    progress?.Report("检查导入空间…");
                    var manifest = PersonalDataArchive.Inspect(source);
                    PersonalDataSpace.CheckImport(paths, app, manifest, effective, false, control);
                    if (deferInput) PersonalDataDeferredInput.CheckSpace(paths.Personal, manifest, control);
                    PersonalDataArchive.Extract(source, stage, selected, progress, control);
                });
                control?.EnterCommit();
                // Capture target paths now; they are never read from the backup.
                pendingDisplay = Screen.FromControl(transfer).WorkingArea.Size;
                pendingPaths = paths; pendingStage = stage; pendingKinds = selected; pendingScope = scope;
                pendingDeferredInput = deferInput;
                pendingConsumedArchive = !deferInput && PersonalDataDeferredInput.List(paths.Personal).Contains(Path.GetFullPath(source), StringComparer.OrdinalIgnoreCase) ? Path.GetFullPath(source) : null;
            } catch { scope.Dispose(); PersonalDataArchive.Remove(stage); throw; }
        }
        internal static async void RestartForImport()
        {
            if (pendingScope == null) return;
            if (!await Program.RestartAsync()) {
                Program.CancelRestart(); pendingScope.Dispose(); pendingScope = null;
                PersonalDataArchive.Remove(pendingStage); pendingStage = null;
                PersonalDataMessage.Show("有窗口阻止退出，导入已取消，原数据未修改。", "个人数据");
            }
        }
        internal static void ApplyAfterExit()
        {
            if (pendingScope == null) return;
            // Application.Exit has stopped the original WinForms thread context.
            // A new STA owns the recovery message loop and all its dialogs.
            var recovery = new Thread(ApplyAfterExitCore);
            recovery.SetApartmentState(ApartmentState.STA);
            recovery.Start();
            recovery.Join();
        }
        private static void ApplyAfterExitCore()
        {
            try {
                var incoming = PersonalDataArchive.ReadJson(Path.Combine(pendingStage, PersonalDataArchive.ManifestName)).ToObject<PersonalDataManifest>(PersonalDataPreferences.Serializer());
                using var restore = new PersonalDataRestoreProgressForm((control, progress) => PersonalDataRestore.Apply(pendingPaths, pendingStage, pendingKinds, Program.ProductVersion, pendingDisplay, control: control, progress: progress, deferInput: pendingDeferredInput));
                restore.ShowDialog();
                if (!restore.Completed) throw new IOException("恢复窗口未能执行，请保留备份并重试导入。");
                if (restore.Failure != null) throw restore.Failure;
                string backup = restore.Backup;
                int missing = 0;
                var previousInvalid = new List<string>();
                try { var previous = backup == null ? new PersonalDataManifest() : PersonalDataArchive.Inspect(backup); missing = previous.MissingVoiceAttachments.Count; previousInvalid.AddRange(previous.InvalidDiaryFiles); }
                catch (Exception error) { DebugHelper.WriteException(error, "Reading completed import backup notice"); }
                string warning = missing > 0 ? $"\n\n导入前有 {missing} 个录音附件已缺失，自动备份已注明，并保留了待办文字及现有附件。" : "";
                if (incoming.InvalidDiaryFiles.Count > 0) warning += "\n\n" + PersonalDataArchive.DiaryWarning(incoming.InvalidDiaryFiles);
                if (previousInvalid.Count > 0) warning += "\n\n导入前的自动备份保留了异常日记辅助原件（" + string.Join("、", previousInvalid) + "），供核对。";
                if (pendingConsumedArchive != null) File.Move(pendingConsumedArchive, pendingConsumedArchive + ".restored-" + Guid.NewGuid().ToString("N"));
                if (pendingDeferredInput) warning += "\n\n输入法个人数据已保留待恢复。组件准备好后，可在全局设置 → 常规 → 恢复待处理输入法数据中恢复。";
                PersonalDataMessage.Show("个人数据导入完成，即将重新打开小蟒。" + (backup == null ? "" : "\n\n导入前的备份：\n" + backup) + warning, "个人数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (OperationCanceledException) {
                PersonalDataMessage.Show("导入已取消，原数据未替换，即将重新打开小蟒。已生成的导入前备份保留在 PersonalDataBackups 文件夹。", "个人数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (Exception error) {
                DebugHelper.WriteException(error, "Personal data import");
                PersonalDataMessage.Show("导入未完成：" + error.Message + "\n\n已尝试恢复原数据；自动备份位于个人数据目录的 PersonalDataBackups 文件夹。", "个人数据", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } finally {
                pendingScope.Dispose(); pendingScope = null;
                // Retain the verified payload if a journal remains and manual recovery is needed.
                if (!File.Exists(PersonalDataRestore.JournalPath(pendingPaths.Personal))) PersonalDataArchive.Remove(pendingStage);
            }
        }
        internal static async Task OpenDeferredInput(Control owner)
        {
            try {
                var pending = PersonalDataDeferredInput.List(Program.PersonalFolder);
                if (pending.Length == 0) { PersonalDataMessage.Show(owner, "没有待恢复的输入法数据。", "个人数据"); return; }
                if (!PersonalDataInputPreflight.ConverterPresent(XiaomangInputMethodManager.BundleDirectory)) {
                    PersonalDataMessage.Show(owner, "输入法数据已安全保留。请先到全局设置 → 注册下载文本输入组件，再点击此处恢复；无需先完成系统注册。", "个人数据"); return;
                }
                if (PersonalDataMessage.Show(owner, "发现 " + pending.Length + " 份待恢复的输入法数据，将先恢复最早的一份。\n恢复将替换当前词库及个人输入记录，恢复前会自动备份当前数据。是否继续？", "恢复输入法数据", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                await PersonalDataTransferForm.Open(owner, true, pending[0]);
            } catch (Exception error) { PersonalDataMessage.Show(owner, "待恢复数据仍保留：" + error.Message, "个人数据"); }
        }
        internal static void NotifyDeferredInput()
        {
            try {
                if (PersonalDataInputPreflight.ConverterPresent(XiaomangInputMethodManager.BundleDirectory) && PersonalDataDeferredInput.List(Program.PersonalFolder).Length > 0)
                    TaskHelpers.ShowNotificationTip("有待恢复的输入法数据。请到全局设置 → 常规 → 恢复待处理输入法数据，确认后恢复。", "个人数据", 8000);
            } catch (Exception error) { DebugHelper.WriteException(error, "Checking deferred input data"); }
        }
        internal static bool RecoverBeforeStartup()
        {
            if (!File.Exists(PersonalDataRestore.JournalPath(Program.PersonalFolder))) return true;
            try {
                var journal = PersonalDataArchive.ReadJson(PersonalDataRestore.JournalPath(Program.PersonalFolder), maxBytes: PersonalDataRestore.MaxJournalBytes);
                using var scope = new TransferScope();
                CloseOwnedWindows(Path.Combine(AppContext.BaseDirectory, "ThirdParty", "LocalDiary", "xiaomang-diary.exe")).GetAwaiter().GetResult();
                XiaomangInputMethodManager.PauseForPersonalData();
                scope.Lock(Path.Combine(Program.PersonalFolder, "Diary"));
                scope.Lock((string)journal["Input"]);
                PersonalDataRestore.Recover(Program.PersonalFolder); return true;
            } catch (Exception error) {
                DebugHelper.WriteException(error, "Personal data recovery");
                PersonalDataMessage.Show("上次导入尚未完成，自动恢复遇到问题：" + error.Message + "\n\n为保护原数据，小蟒暂不启动。请关闭相关编辑窗口并重新启动，勿删除个人数据目录。", "个人数据恢复", MessageBoxButtons.OK, MessageBoxIcon.Error); return false;
            }
        }
    }
}
