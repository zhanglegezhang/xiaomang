#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using ShareX.HistoryLib;
using ShareX.Properties;
using ShareX.ScreenCaptureLib;
using ShareX.UploadersLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX
{
    internal static class SettingManager
    {
        private const string ApplicationConfigFileName = "ApplicationConfig.json";
        private static readonly ApplicationConfigPersistence applicationSaves = new(() => Settings);
        private static readonly object hotkeySaveGate = new object();
        private static (HotkeysConfig config, string path, byte[] bytes)? pendingHotkeySave;
        private static Task hotkeySaveWorker;
        private static string hotkeySaveFailure;

        private static string ApplicationConfigFilePath
        {
            get
            {
                if (Program.Sandbox) return null;

                return Path.Combine(Program.PersonalFolder, ApplicationConfigFileName);
            }
        }

        private const string UploadersConfigFileNamePrefix = "UploadersConfig";
        private const string UploadersConfigFileNameExtension = "json";
        private const string UploadersConfigFileName = UploadersConfigFileNamePrefix + "." + UploadersConfigFileNameExtension;

        private static string UploadersConfigFilePath
        {
            get
            {
                if (Program.Sandbox) return null;

                string uploadersConfigFolder;

                if (Settings != null && !string.IsNullOrEmpty(Settings.CustomUploadersConfigPath))
                {
                    uploadersConfigFolder = FileHelpers.ExpandFolderVariables(Settings.CustomUploadersConfigPath);
                }
                else
                {
                    uploadersConfigFolder = Program.PersonalFolder;
                }

                string uploadersConfigFileName = GetUploadersConfigFileName(uploadersConfigFolder);

                return Path.Combine(uploadersConfigFolder, uploadersConfigFileName);
            }
        }

        private const string HotkeysConfigFileName = "HotkeysConfig.json";

        internal static string HotkeysConfigFilePath
        {
            get
            {
                if (Program.Sandbox) return null;

                string hotkeysConfigFolder;

                if (Settings != null && !string.IsNullOrEmpty(Settings.CustomHotkeysConfigPath))
                {
                    hotkeysConfigFolder = FileHelpers.ExpandFolderVariables(Settings.CustomHotkeysConfigPath);
                }
                else
                {
                    hotkeysConfigFolder = Program.PersonalFolder;
                }

                return Path.Combine(hotkeysConfigFolder, HotkeysConfigFileName);
            }
        }

        public static string BackupFolder => Path.Combine(Program.PersonalFolder, "Backup");

        private static ApplicationConfig Settings { get => Program.Settings; set => Program.Settings = value; }
        private static TaskSettings DefaultTaskSettings { get => Program.DefaultTaskSettings; set => Program.DefaultTaskSettings = value; }
        private static UploadersConfig UploadersConfig { get => Program.UploadersConfig; set => Program.UploadersConfig = value; }
        private static HotkeysConfig HotkeysConfig { get => Program.HotkeysConfig; set => Program.HotkeysConfig = value; }

        private static ManualResetEvent uploadersConfigResetEvent = new ManualResetEvent(false);
        private static ManualResetEvent hotkeysConfigResetEvent = new ManualResetEvent(false);

        public static void LoadInitialSettings()
        {
            LoadApplicationConfig();

            Task.Run(() =>
            {
                LoadUploadersConfig();
                uploadersConfigResetEvent.Set();

                LoadHotkeysConfig();
                hotkeysConfigResetEvent.Set();
            });
        }

        public static void WaitUploadersConfig()
        {
            if (UploadersConfig == null)
            {
                uploadersConfigResetEvent.WaitOne();
            }
        }

        public static void WaitHotkeysConfig()
        {
            if (HotkeysConfig == null)
            {
                hotkeysConfigResetEvent.WaitOne();
            }
        }

        public static void LoadApplicationConfig(bool fallbackSupport = true)
        {
            Settings = ApplicationConfig.Load(ApplicationConfigFilePath, BackupFolder, fallbackSupport);
            Settings.CreateBackup = true;
            Settings.CreateWeeklyBackup = true;
            Settings.SettingsSaveFailed += Settings_SettingsSaveFailed;
            DefaultTaskSettings = Settings.DefaultTaskSettings;
            ApplicationConfigBackwardCompatibilityTasks();
            MigrateHistoryFile();
            HistoryConnect();
        }

        private static void Settings_SettingsSaveFailed(Exception e)
        {
            MainForm owner = Program.MainForm;
            if (owner == null || owner.IsDisposed || owner.Disposing) return;
            if (owner.IsHandleCreated && owner.InvokeRequired)
            {
                // Never synchronously wait for the UI while the ordered writer holds its lock.
                try { owner.BeginInvoke((Action)(() => Settings_SettingsSaveFailed(e))); }
                catch (InvalidOperationException) { }
                return;
            }
            string message;

            if (e is UnauthorizedAccessException || e is FileNotFoundException)
            {
                message = Resources.YourAntiVirusSoftwareOrTheControlledFolderAccessFeatureInWindowsCouldBeBlockingShareX;
            }
            else
            {
                message = e.Message;
            }

            TaskHelpers.ShowNotificationTip(message, Program.DisplayName + " - " + Resources.FailedToSaveSettings, 5000);
        }

        public static void LoadUploadersConfig(bool fallbackSupport = true)
        {
            if (SystemOptions.DisableUpload)
            {
                UploadersConfig = new UploadersConfig();
                return;
            }

            UploadersConfig = UploadersConfig.Load(UploadersConfigFilePath, BackupFolder, fallbackSupport);
            UploadersConfig.CreateBackup = true;
            UploadersConfig.CreateWeeklyBackup = true;
            UploadersConfig.SupportDPAPIEncryption = true;
            UploadersConfigBackwardCompatibilityTasks();
        }

        public static void LoadHotkeysConfig(bool fallbackSupport = true)
        {
            HotkeysConfig = HotkeysConfig.Load(HotkeysConfigFilePath, BackupFolder, fallbackSupport);
            HotkeysConfig.CreateBackup = true;
            HotkeysConfig.CreateWeeklyBackup = true;
            HotkeysConfigBackwardCompatibilityTasks();
        }

        public static void LoadAllSettings()
        {
            LoadApplicationConfig();
            LoadUploadersConfig();
            LoadHotkeysConfig();
        }

        private static string GetUploadersConfigFileName(string destinationFolder)
        {
            if (string.IsNullOrEmpty(destinationFolder))
            {
                return UploadersConfigFileName;
            }

            if (Settings != null && Settings.UseMachineSpecificUploadersConfig)
            {
                string sanitizedMachineName = FileHelpers.SanitizeFileName(Environment.MachineName.ToLowerInvariant());

                if (!string.IsNullOrEmpty(sanitizedMachineName))
                {
                    string machineSpecificFileName = $"{UploadersConfigFileNamePrefix}-{sanitizedMachineName}.{UploadersConfigFileNameExtension}";
                    string machineSpecificPath = Path.Combine(destinationFolder, machineSpecificFileName);

                    if (!File.Exists(machineSpecificPath))
                    {
                        string defaultFilePath = Path.Combine(destinationFolder, UploadersConfigFileName);

                        if (File.Exists(defaultFilePath))
                        {
                            try
                            {
                                File.Copy(defaultFilePath, machineSpecificPath, false);
                            }
                            catch (IOException)
                            {
                                // Ignore copy issues; file may have been created in the meantime.
                            }
                        }
                    }

                    return machineSpecificFileName;
                }
            }

            return UploadersConfigFileName;
        }

        private static void ApplicationConfigBackwardCompatibilityTasks()
        {
            if (SystemOptions.DisableUpload)
            {
                RemoveUploadTasks(DefaultTaskSettings);
                Settings.QuickTaskPresets?.RemoveAll(x => x.AfterCaptureTasks.HasFlag(AfterCaptureTasks.UploadImageToHost));
                Settings.ActionsToolbarList?.RemoveAll(x => !SystemOptions.IsHotkeyActionAvailable(x));
                SanitizeTrayAction(ref Settings.TrayLeftClickAction);
                SanitizeTrayAction(ref Settings.TrayLeftDoubleClickAction);
                SanitizeTrayAction(ref Settings.TrayMiddleClickAction);
            }

            if (Settings.IsUpgradeFrom("14.1.1"))
            {
                if (Helpers.IsDefaultSettings(Settings.Themes, ShareXTheme.GetDefaultThemes(), (x, y) => x.Name == y.Name))
                {
                    if (!Settings.Themes.IsValidIndex(Settings.SelectedTheme))
                    {
                        Settings.SelectedTheme = 0;
                    }

                    ShareXTheme selectedTheme = Settings.Themes[Settings.SelectedTheme];

                    Settings.Themes = ShareXTheme.GetDefaultThemes();

                    int index = Settings.Themes.FindIndex(x => x.Name.Equals(selectedTheme.Name, StringComparison.OrdinalIgnoreCase));

                    if (index >= 0)
                    {
                        Settings.SelectedTheme = index;
                    }
                    else
                    {
                        Settings.SelectedTheme = 0;
                    }
                }
            }

            if (Settings.IsUpgradeFrom("14.1.2"))
            {
                if (!Environment.Is64BitOperatingSystem && !string.IsNullOrEmpty(DefaultTaskSettings.CaptureSettings.FFmpegOptions.CLIPath))
                {
                    DefaultTaskSettings.CaptureSettings.FFmpegOptions.OverrideCLIPath = true;
                }
            }

            if (Settings.IsUpgradeFrom("15.0.1"))
            {
                DefaultTaskSettings.CaptureSettings.ScrollingCaptureOptions = new ScrollingCaptureOptions();
                DefaultTaskSettings.CaptureSettings.FFmpegOptions.FixSources();
            }

            if (Settings.IsUpgradeFrom("16.0.2"))
            {
                if (Settings.CheckPreReleaseUpdates)
                {
                    Settings.UpdateChannel = UpdateChannel.PreRelease;
                }

                if (!DefaultTaskSettings.CaptureSettings.SurfaceOptions.UseDimming)
                {
                    DefaultTaskSettings.CaptureSettings.SurfaceOptions.BackgroundDimStrength = 0;
                }
            }
        }

        public static void HistoryConnect()
        {
            HistoryClose();
            Program.HistoryManager = new HistoryManagerSQLite(Program.HistoryFilePath);
        }

        public static void HistoryClose()
        {
            if (Program.HistoryManager != null)
            {
                Program.HistoryManager.Dispose();
                Program.HistoryManager = null;
            }
        }

        private static void MigrateHistoryFile()
        {
            if (File.Exists(Program.HistoryFilePathOld))
            {
                try
                {
                    if (!File.Exists(Program.HistoryFilePath))
                    {
                        DebugHelper.WriteLine($"Migrating JSON history file \"{Program.HistoryFilePathOld}\" to SQLite history file \"{Program.HistoryFilePath}\"");

                        using (HistoryManagerSQLite historyManager = new HistoryManagerSQLite(Program.HistoryFilePath))
                        {
                            historyManager.MigrateFromJSON(Program.HistoryFilePathOld);
                        }
                    }

                    FileHelpers.MoveFile(Program.HistoryFilePathOld, BackupFolder);
                }
                catch (Exception e)
                {
                    DebugHelper.WriteException(e);
                    e.ShowError();
                }
            }
        }

        private static void UploadersConfigBackwardCompatibilityTasks()
        {
            if (UploadersConfig.CustomUploadersList != null)
            {
                foreach (CustomUploaderItem cui in UploadersConfig.CustomUploadersList)
                {
                    try
                    {
                        cui.CheckBackwardCompatibility();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void HotkeysConfigBackwardCompatibilityTasks()
        {
            HotkeysConfig.Hotkeys ??= new List<HotkeySettings>();
            HotkeyManager.EnsureDeskPinsHotkey(HotkeysConfig.Hotkeys);
            HotkeyManager.EnsureInputMethodSwitchHotkey(HotkeysConfig);

            if (SystemOptions.DisableUpload)
            {
                foreach (TaskSettings taskSettings in HotkeysConfig.Hotkeys.Select(x => x.TaskSettings))
                {
                    RemoveUploadTasks(taskSettings);

                    if (taskSettings != null && !SystemOptions.IsHotkeyActionAvailable(taskSettings.Job))
                    {
                        taskSettings.Job = HotkeyType.None;
                    }
                }
            }

            if (Settings.IsUpgradeFrom("15.0.1"))
            {
                foreach (TaskSettings taskSettings in HotkeysConfig.Hotkeys.Select(x => x.TaskSettings))
                {
                    if (taskSettings != null && taskSettings.CaptureSettings != null)
                    {
                        taskSettings.CaptureSettings.ScrollingCaptureOptions = new ScrollingCaptureOptions();
                        taskSettings.CaptureSettings.FFmpegOptions.FixSources();
                    }
                }
            }
        }

        private static void RemoveUploadTasks(TaskSettings taskSettings)
        {
            if (taskSettings == null)
            {
                return;
            }

            taskSettings.AfterCaptureJob = taskSettings.AfterCaptureJob
                .Remove(AfterCaptureTasks.ShowBeforeUploadWindow)
                .Remove(AfterCaptureTasks.UploadImageToHost);
            taskSettings.AfterUploadJob = AfterUploadTasks.None;
            taskSettings.UseDefaultAfterUploadJob = true;
            taskSettings.UseDefaultDestinations = true;
        }

        private static void SanitizeTrayAction(ref HotkeyType action)
        {
            if (!SystemOptions.IsHotkeyActionAvailable(action))
            {
                action = HotkeyType.None;
            }
        }

        public static void CleanupHotkeysConfig()
        {
            foreach (TaskSettings taskSettings in HotkeysConfig.Hotkeys.Select(x => x.TaskSettings))
            {
                taskSettings.Cleanup();
            }
        }

        public static void SaveAllSettings()
        {
            if (Settings != null)
            {
                if (!SaveApplicationConfig()) DebugHelper.WriteLine("Final ApplicationConfig save failed; last successful file remains available.");
            }

            if (!SystemOptions.DisableUpload && UploadersConfig != null)
            {
                UploadersConfig.Save(UploadersConfigFilePath);
            }

            if (HotkeysConfig != null)
            {
                SaveHotkeysConfigAsync();
                // Final shutdown runs after the UI is closed. This worker never needs the UI.
                FlushHotkeysConfigAsync().GetAwaiter().GetResult();
            }
        }

        public static bool SaveApplicationConfig()
        {
            if (Settings == null || Program.Sandbox) return false;
            if (Program.MainForm?.IsHandleCreated == true && Program.MainForm.InvokeRequired)
                return (bool)Program.MainForm.Invoke(new Func<bool>(SaveApplicationConfig));
            try { return applicationSaves.Save(Settings, ApplicationConfigFilePath); }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "ApplicationConfig confirmed snapshot capture failed.");
                Settings_SettingsSaveFailed(error);
                return false;
            }
        }

        public static void SaveApplicationConfigAsync()
        {
            if (Settings == null || Program.Sandbox) return;
            if (Program.MainForm?.IsHandleCreated == true && Program.MainForm.InvokeRequired)
            {
                Program.MainForm.BeginInvoke((Action)SaveApplicationConfigAsync);
                return;
            }
            if (Program.MainForm?.IsDisposed == true || Program.MainForm?.Disposing == true) return;
            try { _ = applicationSaves.Queue(Settings, ApplicationConfigFilePath); }
            catch (Exception error)
            {
                DebugHelper.WriteException(error, "ApplicationConfig snapshot capture failed.");
                Settings_SettingsSaveFailed(error);
            }
        }

        internal static Task FlushApplicationConfigAsync()
        {
            if (Settings == null || Program.Sandbox) return Task.CompletedTask;
            if (Program.MainForm?.IsHandleCreated == true && Program.MainForm.InvokeRequired)
                return (Task)Program.MainForm.Invoke(new Func<Task>(FlushApplicationConfigAsync));
            return applicationSaves.FlushAsync(Settings, ApplicationConfigFilePath);
        }

        public static void SaveUploadersConfigAsync()
        {
            if (!SystemOptions.DisableUpload && UploadersConfig != null)
            {
                UploadersConfig.SaveAsync(UploadersConfigFilePath);
            }
        }

        public static void SaveHotkeysConfigAsync()
        {
            if (Program.MainForm?.IsHandleCreated == true && Program.MainForm.InvokeRequired)
            {
                Program.MainForm.Invoke((Action)SaveHotkeysConfigAsync);
                return;
            }
            if (HotkeysConfig == null || Program.Sandbox) return;
            CleanupHotkeysConfig();
            var snapshot = (HotkeysConfig, HotkeysConfigFilePath, HotkeysConfig.CaptureSaveSnapshot());
            lock (hotkeySaveGate)
            {
                pendingHotkeySave = snapshot;
                hotkeySaveFailure = null;
                if (hotkeySaveWorker == null || hotkeySaveWorker.IsCompleted)
                    hotkeySaveWorker = Task.Run(SaveHotkeySnapshotsAsync);
            }
        }

        private static async Task SaveHotkeySnapshotsAsync()
        {
            while (true)
            {
                (HotkeysConfig config, string path, byte[] bytes) request;
                lock (hotkeySaveGate)
                {
                    if (!pendingHotkeySave.HasValue) { hotkeySaveWorker = null; return; }
                    request = pendingHotkeySave.Value;
                    pendingHotkeySave = null;
                }
                bool saved = false;
                for (int attempt = 0; attempt < 3 && !saved; attempt++)
                {
                    saved = request.config.SaveSnapshot(request.path, request.bytes);
                    if (!saved) await Task.Delay(150 * (attempt + 1)).ConfigureAwait(false);
                }
                if (!saved)
                {
                    lock (hotkeySaveGate)
                    {
                        // A newer snapshot supersedes this one, including after a failed write.
                        if (pendingHotkeySave.HasValue) continue;
                        pendingHotkeySave = request;
                        hotkeySaveFailure = "快捷键保存失败，修改仍保留，请检查保存目录后重试。";
                        hotkeySaveWorker = null;
                    }
                    DebugHelper.WriteLine(hotkeySaveFailure);
                    return;
                }
                lock (hotkeySaveGate) hotkeySaveFailure = null;
            }
        }

        internal static async Task FlushHotkeysConfigAsync()
        {
            Task worker;
            lock (hotkeySaveGate)
            {
                if (pendingHotkeySave.HasValue && (hotkeySaveWorker == null || hotkeySaveWorker.IsCompleted))
                    hotkeySaveWorker = Task.Run(SaveHotkeySnapshotsAsync);
                worker = hotkeySaveWorker;
            }
            while (worker != null)
            {
                await worker.ConfigureAwait(false);
                lock (hotkeySaveGate)
                {
                    if (hotkeySaveFailure != null) throw new IOException(hotkeySaveFailure);
                    worker = hotkeySaveWorker;
                }
            }
        }

        public static void SaveAllSettingsAsync()
        {
            SaveApplicationConfigAsync();
            SaveUploadersConfigAsync();
            SaveHotkeysConfigAsync();
        }

        public static void ResetSettings()
        {
            if (File.Exists(ApplicationConfigFilePath)) File.Delete(ApplicationConfigFilePath);
            LoadApplicationConfig(false);

            if (!SystemOptions.DisableUpload)
            {
                if (File.Exists(UploadersConfigFilePath)) File.Delete(UploadersConfigFilePath);
                LoadUploadersConfig(false);
            }

            if (File.Exists(HotkeysConfigFilePath)) File.Delete(HotkeysConfigFilePath);
            LoadHotkeysConfig(false);
        }
    }
}
