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
using ShareX.ScreenCaptureLib;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ShareX
{
    public class CaptureRegion : CaptureBase
    {
        protected static RegionCaptureType lastRegionCaptureType = RegionCaptureType.Default;
        private static int captureWorkflowActive;

        public RegionCaptureType RegionCaptureType { get; protected set; }

        public CaptureRegion()
        {
        }

        public CaptureRegion(RegionCaptureType regionCaptureType)
        {
            RegionCaptureType = regionCaptureType;
        }

        protected override bool TryBeginCaptureWorkflow()
        {
            if (Interlocked.CompareExchange(ref captureWorkflowActive, 1, 0) == 0)
            {
                return true;
            }

            DebugHelper.WriteLine("Region capture request ignored because another region capture is active.");
            return false;
        }

        protected override void EndCaptureWorkflow()
        {
            Interlocked.Exchange(ref captureWorkflowActive, 0);
        }

        protected override TaskMetadata Execute(TaskSettings taskSettings)
        {
            switch (RegionCaptureType)
            {
                default:
                case RegionCaptureType.Default:
                    return ExecuteRegionCapture(taskSettings);
                case RegionCaptureType.Light:
                    return ExecuteRegionCaptureLight(taskSettings);
                case RegionCaptureType.Transparent:
                    return ExecuteRegionCaptureTransparent(taskSettings);
            }
        }

        protected TaskMetadata ExecuteRegionCapture(TaskSettings taskSettings)
        {
            RegionCaptureMode mode;

            if (taskSettings.AdvancedSettings.RegionCaptureDisableAnnotation)
            {
                mode = RegionCaptureMode.Default;
            }
            else
            {
                mode = RegionCaptureMode.Annotation;
            }

            Bitmap canvas;
            Screenshot screenshot = TaskHelpers.GetScreenshot(taskSettings);
            screenshot.CaptureCursor = false;

            if (taskSettings.CaptureSettings.SurfaceOptions.ActiveMonitorMode)
            {
                canvas = screenshot.CaptureActiveMonitor();
            }
            else
            {
                canvas = screenshot.CaptureFullscreen();
            }

            CursorData cursorData = null;

            if (taskSettings.CaptureSettings.ShowCursor)
            {
                cursorData = new CursorData();
            }

            // Ordinary screenshots remain editable until the user completes the capture.
            // Preserve the shared legacy option for recording selection and other capture workflows.
            using (RegionCaptureForm form = new RegionCaptureForm(mode, taskSettings.CaptureSettingsReference.SurfaceOptions, canvas, requireCaptureConfirmation: true))
            {
                if (cursorData != null && cursorData.IsVisible)
                {
                    form.AddCursor(cursorData.ToBitmap(), form.PointToClient(cursorData.DrawPosition));
                }

                form.ShowDialog();

                if (form.LongCaptureRequested)
                {
                    Rectangle selectedRegion = form.GetSelectedRectangle();
                    using (LongCaptureSessionForm session = new LongCaptureSessionForm(selectedRegion))
                    {
                        if (session.ShowDialog() == DialogResult.OK)
                        {
                            Bitmap longImage = session.TakeResult();
                            if (longImage != null)
                            {
                                taskSettings.AfterCaptureJob = AfterCaptureTasks.CopyImageToClipboard |
                                    AfterCaptureTasks.SaveImageToFile;
                                lastRegionCaptureType = RegionCaptureType.Default;
                                return new TaskMetadata(longImage) { CaptureLocation = selectedRegion.Location };
                            }
                        }
                    }

                    return null;
                }

                Bitmap result = form.GetResultImage();

                if (result != null)
                {
                    if (form.TodoCaptureRequested)
                    {
                        // Transfer the rendered selection (including annotations) to the UI workflow.
                        var options = taskSettings.CaptureSettings.OCROptions;
                        // Quick todo capture is Chinese-first; the OCR window keeps its
                        // explicit language selection for English-only documents.
                        string language = options.Language;
                        foreach (var candidate in OCRHelper.AvailableLanguages)
                        {
                            if (candidate.LanguageTag.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase))
                            {
                                language = candidate.LanguageTag;
                                if (language.StartsWith("zh-Hans", System.StringComparison.OrdinalIgnoreCase) ||
                                    language.Equals("zh-CN", System.StringComparison.OrdinalIgnoreCase)) break;
                            }
                        }
                        float scale = options.ScaleFactor;
                        try
                        {
                            Program.MainForm.BeginInvoke(new System.Action(() => Program.MainForm.AddCaptureToTodo(result, language, scale)));
                        }
                        catch
                        {
                            result.Dispose();
                            throw;
                        }
                        return null;
                    }
                    TaskMetadata metadata = new TaskMetadata(result)
                    {
                        CaptureLocation = form.ResultScreenLocation
                    };

                    if (form.CopyCaptureRequested)
                    {
                        taskSettings.AfterCaptureJob = AfterCaptureTasks.CopyImageToClipboard |
                            AfterCaptureTasks.SaveImageToFile;
                    }
                    else if (form.PinCaptureRequested)
                    {
                        taskSettings.AfterCaptureJob = AfterCaptureTasks.CopyImageToClipboard |
                            AfterCaptureTasks.SaveImageToFile |
                            AfterCaptureTasks.PinToScreen;
                    }
                    else if (form.OCRCaptureRequested)
                    {
                        taskSettings.AfterCaptureJob = AfterCaptureTasks.SaveImageToFile |
                            AfterCaptureTasks.DoOCR;
                    }

                    // A completed region capture is always part of the local screenshot archive.
                    // This also repairs older hotkey profiles that predate the local-only workflow
                    // and only contain quick-task or pin actions.
                    taskSettings.AfterCaptureJob |= AfterCaptureTasks.SaveImageToFile;

                    if (form.IsImageModified)
                    {
                        AllowAnnotation = false;
                    }

                    if (form.Result == RegionResult.Region)
                    {
                        WindowInfo windowInfo = form.GetWindowInfo();
                        metadata.UpdateInfo(windowInfo);
                    }

                    lastRegionCaptureType = RegionCaptureType.Default;

                    return metadata;
                }
            }

            return null;
        }

        protected TaskMetadata ExecuteRegionCaptureLight(TaskSettings taskSettings)
        {
            Bitmap canvas;
            Screenshot screenshot = TaskHelpers.GetScreenshot(taskSettings);

            if (taskSettings.CaptureSettings.SurfaceOptions.ActiveMonitorMode)
            {
                canvas = screenshot.CaptureActiveMonitor();
            }
            else
            {
                canvas = screenshot.CaptureFullscreen();
            }

            bool activeMonitorMode = taskSettings.CaptureSettings.SurfaceOptions.ActiveMonitorMode;

            using (RegionCaptureLightForm rectangleLight = new RegionCaptureLightForm(canvas, activeMonitorMode))
            {
                if (rectangleLight.ShowDialog() == DialogResult.OK)
                {
                    Bitmap result = rectangleLight.GetAreaImage();

                    if (result != null)
                    {
                        lastRegionCaptureType = RegionCaptureType.Light;

                        return new TaskMetadata(result)
                        {
                            CaptureLocation = rectangleLight.ScreenSelectionRectangle.Location
                        };
                    }
                }
            }

            return null;
        }

        protected TaskMetadata ExecuteRegionCaptureTransparent(TaskSettings taskSettings)
        {
            bool activeMonitorMode = taskSettings.CaptureSettings.SurfaceOptions.ActiveMonitorMode;

            using (RegionCaptureLightForm rectangleTransparent = new RegionCaptureLightForm(null, activeMonitorMode))
            {
                if (rectangleTransparent.ShowDialog() == DialogResult.OK)
                {
                    Screenshot screenshot = TaskHelpers.GetScreenshot(taskSettings);
                    Bitmap result = rectangleTransparent.GetAreaImage(screenshot);

                    if (result != null)
                    {
                        lastRegionCaptureType = RegionCaptureType.Transparent;

                        return new TaskMetadata(result)
                        {
                            CaptureLocation = rectangleTransparent.ScreenSelectionRectangle.Location
                        };
                    }
                }
            }

            return null;
        }
    }
}
