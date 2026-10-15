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

namespace ShareX
{
    public static class SystemOptions
    {
        // 小蟒同学桌面版只保留本地工作流，不提供任何云端上传能力。
        private const bool LocalOnlyBuild = true;
        private const string RegistryPath = @"SOFTWARE\ShareX";

        public static bool DisableUpdateCheck { get; private set; }
        public static bool DisableUpload { get; private set; }
        public static bool DisableLogging { get; private set; }
        public static string PersonalPath { get; private set; }

        public static bool IsHotkeyActionAvailable(HotkeyType action)
        {
            // Keep legacy enum values for settings compatibility, but do not expose
            // tools that are not part of the streamlined desktop workflow.
            if (action == HotkeyType.AnalyzeImage ||
                action == HotkeyType.Metadata ||
                action == HotkeyType.IndexFolder ||
                action == HotkeyType.ClipboardViewer ||
                action == HotkeyType.BorderlessWindow ||
                action == HotkeyType.InspectWindow ||
                action == HotkeyType.MonitorTest ||
                action == HotkeyType.ColorPicker ||
                action == HotkeyType.Ruler ||
                action == HotkeyType.ImageEditor ||
                action == HotkeyType.ImageBeautifier ||
                action == HotkeyType.ImageEffects ||
                action == HotkeyType.ImageViewer ||
                action == HotkeyType.BackgroundRemover ||
                action == HotkeyType.ImageComparer ||
                action == HotkeyType.ImageCombiner ||
                action == HotkeyType.ImageSplitter ||
                action == HotkeyType.ImageThumbnailer ||
                action == HotkeyType.VideoConverter ||
                action == HotkeyType.VideoThumbnailer ||
                action == HotkeyType.QRCode ||
                action == HotkeyType.HashCheck)
            {
                return false;
            }

            if (!DisableUpload)
            {
                return true;
            }

            return action != HotkeyType.FileUpload &&
                action != HotkeyType.FolderUpload &&
                action != HotkeyType.ClipboardUpload &&
                action != HotkeyType.ClipboardUploadWithContentViewer &&
                action != HotkeyType.UploadText &&
                action != HotkeyType.UploadURL &&
                action != HotkeyType.DragDropUpload &&
                action != HotkeyType.ShortenURL &&
                action != HotkeyType.StopUploads &&
                action != HotkeyType.OpenHistory &&
                action != HotkeyType.OpenImageHistory;
        }

        public static void UpdateSystemOptions()
        {
            // This local desktop build does not use ShareX's upstream update channel.
            // Keeping this hard-disabled also avoids background GitHub requests at startup.
            DisableUpdateCheck = LocalOnlyBuild || GetSystemOptionBoolean("DisableUpdateCheck");
            DisableUpload = LocalOnlyBuild || GetSystemOptionBoolean("DisableUpload");
            DisableLogging = GetSystemOptionBoolean("DisableLogging");
            PersonalPath = GetSystemOptionString("PersonalPath");
        }

        private static bool GetSystemOptionBoolean(string name)
        {
            object value = RegistryHelpers.GetValue(RegistryPath, name, RegistryHive.LocalMachine);

            if (value != null)
            {
                try
                {
                    return Convert.ToBoolean(value);
                }
                catch
                {
                }
            }

            value = RegistryHelpers.GetValue(RegistryPath, name, RegistryHive.CurrentUser);

            if (value != null)
            {
                try
                {
                    return Convert.ToBoolean(value);
                }
                catch
                {
                }
            }

            return false;
        }

        private static string GetSystemOptionString(string name)
        {
            string value = RegistryHelpers.GetValueString(RegistryPath, name, RegistryHive.LocalMachine);

            if (value == null)
            {
                value = RegistryHelpers.GetValueString(RegistryPath, name, RegistryHive.CurrentUser);
            }

            return value;
        }
    }
}
