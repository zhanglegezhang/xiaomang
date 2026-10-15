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

using Microsoft.Win32;
using ShareX.HelpersLib;
using System;
using System.Windows.Forms;

#if MicrosoftStore
using Windows.ApplicationModel;
#endif

namespace ShareX
{
    public static class StartupManager
    {
        internal static void ApplyFirstProfileDefault(bool newProfile)
        {
            if (Program.Sandbox || Program.Settings.StartupPreferenceInitialized) return;
            // Existing profiles retain their OS startup choice. The approved current
            // machine change is performed separately, through the existing setting.
            if (newProfile && Program.Settings.StartWithWindowsByDefault)
            {
                try
                {
                    if (State == StartupState.Disabled) State = StartupState.Enabled;
                }
                catch (Exception error) { DebugHelper.WriteException(error,"Unable to apply startup default."); return; }
            }
            Program.Settings.StartupPreferenceInitialized = true;
        }
#if MicrosoftStore
        private const int StartupTargetIndex = 0;
        private static readonly StartupTask packageTask = StartupTask.GetForCurrentPackageAsync().GetAwaiter().GetResult()[StartupTargetIndex];
#endif

        public static string StartupTargetPath
        {
            get
            {
#if STEAM
                return FileHelpers.GetAbsolutePath("../ShareX_Launcher.exe");
#else
                return Application.ExecutablePath;
#endif
            }
        }

        public static StartupState State
        {
            get
            {
#if MicrosoftStore
                return (StartupState)packageTask.State;
#else
                if (ShortcutHelpers.CheckShortcut(Environment.SpecialFolder.Startup, Program.AppName, StartupTargetPath))
                {
                    if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
                        Program.AppName + ".lnk", null) is byte[] status && status.Length > 0 && status[0] == 3)
                    {
                        return StartupState.DisabledByUser;
                    }
                    else
                    {
                        return StartupState.Enabled;
                    }
                }
                else
                {
                    return StartupState.Disabled;
                }
#endif
            }
            set
            {
#if MicrosoftStore
                if (value == StartupState.Enabled)
                {
                    packageTask.RequestEnableAsync().GetAwaiter().GetResult();
                }
                else if (value == StartupState.Disabled)
                {
                    packageTask.Disable();
                }
                else
                {
                    throw new NotSupportedException();
                }
#else
                if (value == StartupState.Enabled || value == StartupState.Disabled)
                {
                    ShortcutHelpers.SetShortcut(value == StartupState.Enabled, Environment.SpecialFolder.Startup, Program.AppName, StartupTargetPath, "-silent");
                }
                else
                {
                    throw new NotSupportedException();
                }
#endif
            }
        }
    }
}
