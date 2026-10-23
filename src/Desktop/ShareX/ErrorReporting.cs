using System;
using System.Diagnostics;
using System.Threading;

namespace ShareX;

// Error reporting must not replace the original failure with a formatting/UI failure.
internal static class ErrorReporting
{
    private static int reporting;
    private const string Unavailable = "发生错误，无法生成完整错误详情。请先保存可保存的工作；如反复出现，请关闭小蟒后重新启动。";
    private const string LowMemory = "可用内存不足，无法显示完整错误详情。请先保存可保存的工作，关闭暂时不用的程序后重新启动小蟒。";

    internal static void Handle(Exception error, string title, Action<string> log,
        Action<string, string> showDetails, Action<string> showFallback)
    {
        // Covers reentrancy from a modal message loop and simultaneous thread failures.
        if (Interlocked.CompareExchange(ref reporting, 1, 0) != 0) return;
        try
        {
            string summary = Unavailable;
            try { summary = error.GetType().FullName; } catch { }
            try { summary += ": " + error.Message; } catch { }
            TryLog(log, summary); // Preserve original identity before requesting its stack trace.
            try
            {
                using var process = Process.GetCurrentProcess();
                TryLog(log, $"Error resources: privateBytes={process.PrivateMemorySize64}; workingSetBytes={process.WorkingSet64}; handles={process.HandleCount}; managedBytes={GC.GetTotalMemory(false)}");
            }
            catch { }

            if (error is OutOfMemoryException)
            {
                TryFallback(showFallback, LowMemory);
                return;
            }

            string details;
            try { details = error.ToString() + "\r\n\r\n" + title; }
            catch (Exception formattingError)
            {
                TryLog(log, "Error detail formatting failed; original summary recorded above.");
                TryFallback(showFallback, formattingError is OutOfMemoryException ? LowMemory : Unavailable);
                return;
            }
            TryLog(log, details);
            try { showDetails(summary, details); }
            catch (Exception displayError)
            {
                TryLog(log, "Error dialog failed; using minimal notification.");
                TryFallback(showFallback, displayError is OutOfMemoryException ? LowMemory : Unavailable);
            }
        }
        catch { TryFallback(showFallback, Unavailable); }
        finally { Volatile.Write(ref reporting, 0); }
    }

    private static void TryLog(Action<string> log, string text) { try { log(text); } catch { } }
    private static void TryFallback(Action<string> show, string text) { try { show(text); } catch { } }
}
