using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.IO;
using System.Text.Json;
using ShareX.ScreenCaptureLib;

internal static class Program
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, All, null, args.Select(a => a.GetType()).ToArray(), null)!.Invoke(target, args)!;
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(args.Contains("--dpi96") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        if (args.Contains("--golden")) return DamageChecks.Golden(args[0]);
        if (args.Contains("--verify")) return DamageChecks.Run(args.Contains("--dpi96"));
        var records = new List<object>();
        foreach (var size in new[] { new Size(2880, 1800), new Size(6720, 2160), new Size(10560, 2160) })
        {
            using var source = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(source)) { g.Clear(Color.Navy); g.FillRectangle(Brushes.Orange, 100, 100, 200, 150); }
            var options = new RegionCaptureOptions { DetectWindows = false, ShowMagnifier = false, ShowInfo = false, EnableAnimations = false, BackgroundDimStrength = 20 };
            using var form = new RegionCaptureForm(RegionCaptureMode.Annotation, options, (Bitmap)source.Clone(), true);
            typeof(RegionCaptureForm).GetProperty("CanvasRectangle")!.SetValue(form, (RectangleF)new Rectangle(Point.Empty, size));
            using var output = new Bitmap(size.Width, size.Height);
            using var graphics = Graphics.FromImage(output);
            Call(form, "DrawBackground", graphics);
            var samples = new List<double>();
            for (int i = 0; i < 12; i++)
            {
                // Same stationary snapshot, explicit full vs small damaged area; no real desktop capture.
                graphics.ResetClip();
                if (args.Contains("--partial")) graphics.SetClip(new Rectangle(900 + i * 8, 400, 280, 180));
                var watch = Stopwatch.StartNew(); Call(form, "DrawBackground", graphics); watch.Stop();
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            records.Add(new { width=size.Width, height=size.Height, partial=args.Contains("--partial"), backgroundMs=samples, medianMs=samples.Order().ElementAt(samples.Count/2) });
        }
        string result = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented=true });
        Console.WriteLine(result);
        if (args.Length > 0) File.WriteAllText(args[0], result);
        return 0;
    }
}
