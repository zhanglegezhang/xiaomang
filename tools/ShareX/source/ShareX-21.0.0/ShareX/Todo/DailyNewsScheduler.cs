using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX
{
    internal sealed class DailyNewsScheduler : IDisposable
    {
        private readonly DailyNewsManager manager;
        private readonly System.Windows.Forms.Timer timer;
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private bool checking;
        private bool firstTick = true;
        private bool disposed;
        private readonly HashSet<DailyNewsRegion> failedRegions = new HashSet<DailyNewsRegion>();
        public event Action<string, bool> StatusChanged;

        public DailyNewsScheduler(DailyNewsManager manager)
        {
            this.manager = manager;
            manager.UpdateSucceeded += OnUpdateSucceeded;
            timer = new System.Windows.Forms.Timer { Interval = 10_000 };
            timer.Tick += async (sender, e) =>
            {
                if (firstTick)
                {
                    firstTick = false;
                    timer.Stop();
                    timer.Interval = 60_000;
                    timer.Start();
                }
                await CheckNowAsync();
            };
        }

        public void Start()
        {
            if (disposed) return;
            timer.Start();
        }

        public async Task CheckNowAsync()
        {
            if (checking || disposed || lifetimeCancellation.IsCancellationRequested) return;
            checking = true;
            try
            {
                Program.Settings.DailyNewsSettings ??= new DailyNewsSettings();
                DailyNewsSettings settings = Program.Settings.DailyNewsSettings;
                int windowSizeVersion = settings.WindowSizeVersion;
                settings.Normalize();
                if (windowSizeVersion != settings.WindowSizeVersion) SettingManager.SaveApplicationConfigAsync();
                if (!settings.Enabled || !settings.AutoRun || !manager.HasApiKey || DateTime.Now.Hour < settings.AutoRunHour) return;
                bool recovered = failedRegions.RemoveWhere(region => manager.GetLastSuccess(region)?.Date == DateTime.Today) > 0;
                if (recovered) PublishStatus();

                if (manager.GetLastSuccess(DailyNewsRegion.Domestic)?.Date != DateTime.Today)
                {
                    await TryUpdateAsync(DailyNewsRegion.Domestic, lifetimeCancellation.Token);
                }
                if (manager.GetLastSuccess(DailyNewsRegion.International)?.Date != DateTime.Today)
                {
                    await TryUpdateAsync(DailyNewsRegion.International, lifetimeCancellation.Token);
                }
            }
            finally
            {
                checking = false;
            }
        }

        private async Task TryUpdateAsync(DailyNewsRegion region, CancellationToken cancellationToken)
        {
            try
            {
                await manager.UpdateAsync(region, null, cancellationToken);
                failedRegions.Remove(region);
                PublishStatus();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Application shutdown cancels background work without reporting a failed source.
            }
            catch (Exception error)
            {
                DebugHelper.WriteLine("Daily news scheduled update failed ({0}): {1}", region, error.Message);
                failedRegions.Add(region);
                PublishStatus();
            }
        }

        private void PublishStatus()
        {
            string message = failedRegions.Count == 0 ? "" :
                string.Join("、", failedRegions.OrderBy(x => x).Select(x => x == DailyNewsRegion.Domestic ? "国内" : "国际")) + "资讯今日更新失败";
            StatusChanged?.Invoke(message, failedRegions.Count > 0);
        }

        private void OnUpdateSucceeded(DailyNewsRegion region)
        {
            if (disposed) return;
            failedRegions.Remove(region);
            PublishStatus();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            manager.UpdateSucceeded -= OnUpdateSucceeded;
            timer.Stop();
            lifetimeCancellation.Cancel();
            timer.Dispose();
            lifetimeCancellation.Dispose();
        }
    }
}
