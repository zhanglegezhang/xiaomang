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
using ShareX.Properties;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ShareX
{
    public partial class HotkeyManager
    {
        public List<HotkeySettings> Hotkeys { get; private set; }
        public bool IgnoreHotkeys { get; set; }

        public delegate void HotkeyTriggerEventHandler(HotkeySettings hotkeySetting);
        public delegate void HotkeysToggledEventHandler(bool hotkeysEnabled);

        public HotkeyTriggerEventHandler HotkeyTrigger;
        public HotkeysToggledEventHandler HotkeysToggledTrigger;

        private HotkeyForm hotkeyForm;

        public HotkeyManager(HotkeyForm form)
        {
            hotkeyForm = form;
            hotkeyForm.HotkeyPress += HotkeyForm_HotkeyPress;
            hotkeyForm.FormClosed += HotkeyForm_FormClosed;
            InitializeFullscreenMonitoring();
        }

        private void HotkeyForm_HotkeyPress(ushort id, Keys key, Modifiers modifier)
        {
            RefreshFullscreenPolicy();
            if (!IgnoreHotkeys)
            {
                HotkeySettings hotkeySetting = Hotkeys.Find(x => x.HotkeyInfo.ID == id);

                if (hotkeySetting != null && (!FullscreenSuspended || IsRecordingControl(hotkeySetting.TaskSettings.Job)))
                {
                    OnHotkeyTrigger(hotkeySetting);
                }
            }
        }

        private void HotkeyForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            fullscreenMonitor?.Dispose();
            if (hotkeyForm != null && !hotkeyForm.IsDisposed)
            {
                UnregisterAllHotkeys(false);
            }
        }

        public void UpdateHotkeys(List<HotkeySettings> hotkeys, bool showFailedHotkeys)
        {
            if (Hotkeys != null)
            {
                UnregisterAllHotkeys();
            }

            Hotkeys = hotkeys;

            RegisterAllHotkeys();

            if (showFailedHotkeys)
            {
                ShowFailedHotkeys();
            }
        }

        protected void OnHotkeyTrigger(HotkeySettings hotkeySetting)
        {
            HotkeyTrigger?.Invoke(hotkeySetting);
        }

        public void RegisterHotkey(HotkeySettings hotkeySetting)
        {
            if (FullscreenSuspended && !IsRecordingControl(hotkeySetting.TaskSettings.Job))
            {
                UnregisterHotkey(hotkeySetting, false);
                fullscreenReleased.Add(hotkeySetting);
                if (!Hotkeys.Contains(hotkeySetting)) Hotkeys.Add(hotkeySetting);
                return;
            }
            if (!Program.Settings.DisableHotkeys || hotkeySetting.TaskSettings.Job == HotkeyType.DisableHotkeys)
            {
                UnregisterHotkey(hotkeySetting, false);

                if (hotkeySetting.HotkeyInfo.Status != HotkeyStatus.Registered && hotkeySetting.HotkeyInfo.IsValidHotkey)
                {
                    hotkeyForm.RegisterHotkey(hotkeySetting.HotkeyInfo);

                    if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Registered)
                    {
                        DebugHelper.WriteLine("Hotkey registered: " + hotkeySetting);
                    }
                    else if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Failed)
                    {
                        DebugHelper.WriteLine("Hotkey register failed: " + hotkeySetting);
                    }
                }
                else
                {
                    hotkeySetting.HotkeyInfo.Status = HotkeyStatus.NotConfigured;
                }
            }

            if (!Hotkeys.Contains(hotkeySetting))
            {
                Hotkeys.Add(hotkeySetting);
            }
        }

        public void RegisterAllHotkeys()
        {
            foreach (HotkeySettings hotkeySetting in Hotkeys.ToArray())
            {
                RegisterHotkey(hotkeySetting);
            }
        }

        public void RegisterFailedHotkeys()
        {
            foreach (HotkeySettings hotkeySetting in Hotkeys.Where(x => x.HotkeyInfo.Status == HotkeyStatus.Failed))
            {
                RegisterHotkey(hotkeySetting);
            }
        }

        public void UnregisterHotkey(HotkeySettings hotkeySetting, bool removeFromList = true)
        {
            if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Registered)
            {
                hotkeyForm.UnregisterHotkey(hotkeySetting.HotkeyInfo);

                if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.NotConfigured)
                {
                    DebugHelper.WriteLine("Hotkey unregistered: " + hotkeySetting);
                }
                else if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Failed)
                {
                    DebugHelper.WriteLine("Hotkey unregister failed: " + hotkeySetting);
                }
            }

            if (removeFromList)
            {
                fullscreenReleased.Remove(hotkeySetting);
                Hotkeys.Remove(hotkeySetting);
            }
        }

        public void UnregisterAllHotkeys(bool removeFromList = true, bool temporary = false)
        {
            if (Hotkeys != null)
            {
                foreach (HotkeySettings hotkeySetting in Hotkeys.ToArray())
                {
                    if (!temporary || hotkeySetting.TaskSettings.Job != HotkeyType.DisableHotkeys)
                    {
                        UnregisterHotkey(hotkeySetting, removeFromList);
                    }
                }
            }
        }

        public void ToggleHotkeys(bool hotkeysDisabled)
        {
            if (!hotkeysDisabled)
            {
                RegisterAllHotkeys();
            }
            else
            {
                UnregisterAllHotkeys(false, true);
            }

            HotkeysToggledTrigger?.Invoke(hotkeysDisabled);
        }

        public void ShowFailedHotkeys()
        {
            List<HotkeySettings> failedHotkeysList = Hotkeys.Where(x => x.HotkeyInfo.Status == HotkeyStatus.Failed).ToList();

            if (failedHotkeysList.Count > 0)
            {
                string failedHotkeys = string.Join("\r\n", failedHotkeysList.Select(x => $"[{x.HotkeyInfo}] {x.TaskSettings}"));
                string hotkeyText = failedHotkeysList.Count > 1 ? Resources.HotkeyManager_ShowFailedHotkeys_hotkeys : Resources.HotkeyManager_ShowFailedHotkeys_hotkey;
                string text = string.Format(Resources.HotkeyManager_ShowFailedHotkeys_Unable_to_register_hotkey, hotkeyText, failedHotkeys);

                MessageBox.Show(text, Program.DisplayName + " - " + Resources.HotkeyManager_ShowFailedHotkeys_Hotkey_registration_failed, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void ResetHotkeys()
        {
            UnregisterAllHotkeys();
            Hotkeys.AddRange(GetDefaultHotkeyList());
            RegisterAllHotkeys();

            if (Program.Settings.DisableHotkeys)
            {
                TaskHelpers.ToggleHotkeys();
            }
        }

        public static List<HotkeySettings> GetDefaultHotkeyList()
        {
            return new List<HotkeySettings>
            {
                new HotkeySettings(HotkeyType.RectangleRegion, Keys.Control | Keys.D1),
                new HotkeySettings(HotkeyType.ScreenRecorder, Keys.Control | Keys.D2),

                new HotkeySettings(HotkeyType.ActiveWindowTopMost),
                new HotkeySettings(HotkeyType.SwitchToXiaomangInputMethod, Keys.Control | Keys.Shift | Keys.J)
            };
        }

        internal static bool EnsureDeskPinsHotkey(List<HotkeySettings> hotkeys)
        {
            if (hotkeys == null || hotkeys.Any(x => x?.TaskSettings?.Job == HotkeyType.ActiveWindowTopMost))
            {
                return false;
            }

            hotkeys.Add(new HotkeySettings(HotkeyType.ActiveWindowTopMost));
            return true;
        }

        internal static void EnsureInputMethodSwitchHotkey(HotkeysConfig config)
        {
            if (config.InputMethodSwitchHotkeyMigrated) return;
            config.Hotkeys ??= new List<HotkeySettings>();
            if (!config.Hotkeys.Any(x => x?.TaskSettings?.Job == HotkeyType.SwitchToXiaomangInputMethod))
            {
                Keys shortcut = Keys.Control | Keys.Shift | Keys.J;
                if (config.Hotkeys.Any(x => x?.HotkeyInfo != null && !x.HotkeyInfo.Win && x.HotkeyInfo.Hotkey == shortcut))
                {
                    shortcut = Keys.None;
                    config.InputMethodSwitchDefaultConflict = true;
                    DebugHelper.WriteLine("Input method switch added without a binding: Ctrl+Shift+J already belongs to another configured action.");
                }
                config.Hotkeys.Add(new HotkeySettings(HotkeyType.SwitchToXiaomangInputMethod, shortcut));
            }
            config.InputMethodSwitchHotkeyMigrated = true;
        }
    }
}
