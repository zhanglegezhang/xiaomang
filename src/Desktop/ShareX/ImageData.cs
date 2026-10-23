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
using System;
using System.IO;
using System.Windows.Forms;

namespace ShareX
{
    public class ImageData : IDisposable
    {
        public MemoryStream ImageStream { get; set; }
        public EImageFormat ImageFormat { get; set; }

        public bool Write(string filePath)
        {
            try
            {
                if (ImageStream != null && !string.IsNullOrEmpty(filePath))
                {
                    return ImageStream.WriteToFile(filePath);
                }
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);

                ErrorForm.ShowHandled(GetWriteErrorSummary(e, filePath), e, filePath);
            }

            return false;
        }

        public void Dispose()
        {
            ImageStream?.Dispose();
        }

        internal static string GetWriteErrorSummary(Exception error, string filePath)
        {
            if (Directory.Exists(filePath)) return "保存失败：目标是文件夹，请选择包含文件名的保存路径。";
            if (error is UnauthorizedAccessException) return "保存失败：没有写入权限。请选择其他文件夹或检查访问权限。";
            if (error is DirectoryNotFoundException || error is FileNotFoundException) return "保存失败：目标路径不存在，请重新选择保存位置。";
            if (error is IOException && ((error.HResult & 0xffff) == 32 || (error.HResult & 0xffff) == 33))
                return "保存失败：文件正被占用，请关闭占用程序或更换文件名。";
            return "图片保存失败。请检查保存路径和磁盘空间，或选择其他位置。";
        }
    }
}
