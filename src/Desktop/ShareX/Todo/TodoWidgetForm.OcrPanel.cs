using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using ShareX.HelpersLib;

namespace ShareX
{
    public sealed partial class TodoWidgetForm
    {
        private bool ocrPanelHeldOpen;
        private HookProc ocrPanelMouseHook;
        private IntPtr ocrPanelMouseHookHandle;
        private int ocrPanelHoldGeneration;

        private void HoldOcrPanelOpen()
        {
            ocrPanelHeldOpen = true;
            ocrPanelHoldGeneration++;
            collapseTimer.Stop();
            if (ocrPanelMouseHookHandle != IntPtr.Zero) return;
            ocrPanelMouseHook ??= OcrPanelMouseHook;
            using Process process = Process.GetCurrentProcess();
            using ProcessModule module = process.MainModule;
            ocrPanelMouseHookHandle = NativeMethods.SetWindowsHookEx(NativeConstants.WH_MOUSE_LL,
                ocrPanelMouseHook, NativeMethods.GetModuleHandle(module.ModuleName), 0);
            if (ocrPanelMouseHookHandle == IntPtr.Zero)
                DebugHelper.WriteLine("OCR panel outside-click hook unavailable; panel can still be collapsed manually.");
        }

        private IntPtr OcrPanelMouseHook(int code, IntPtr message, IntPtr data)
        {
            int value = message.ToInt32();
            if (code >= 0 && ocrPanelHeldOpen &&
                (value == (int)WindowsMessages.LBUTTONDOWN || value == (int)WindowsMessages.RBUTTONDOWN ||
                 value == (int)WindowsMessages.MBUTTONDOWN || value == (int)WindowsMessages.XBUTTONDOWN))
            {
                POINT point = Marshal.PtrToStructure<POINT>(data);
                if (!ContainsWidgetPoint(new Point(point.X, point.Y)) && !showingWidgetOverlay && !todoPage.choosingSchedule)
                {
                    int generation = ocrPanelHoldGeneration;
                    // Pass the actual click through; collapse outside the native callback.
                    if (IsHandleCreated && !IsDisposed && !Disposing)
                        BeginInvoke(new Action(() =>
                        {
                            if (!IsDisposed && !Disposing && ocrPanelHeldOpen && generation == ocrPanelHoldGeneration)
                                SetExpanded(false);
                        }));
                }
            }
            return NativeMethods.CallNextHookEx(ocrPanelMouseHookHandle, code, message, data);
        }

        private void ReleaseOcrPanelHold()
        {
            ocrPanelHeldOpen = false;
            ocrPanelHoldGeneration++;
            if (ocrPanelMouseHookHandle == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(ocrPanelMouseHookHandle);
            ocrPanelMouseHookHandle = IntPtr.Zero;
        }
    }
}
