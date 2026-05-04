// Tracer.Service/Workers/ScreenWorker.cs
using Microsoft.Extensions.Hosting;
using ActiMetrics.Service.Services;

namespace ActiMetrics.Service.Workers
{
    public class ScreenWorker : BackgroundService
    {
        private readonly ScreenshotService _screenshotService;
        private readonly AppTrackerService _appTrackerService;
        private readonly SyncService _syncService;

        public ScreenWorker(ScreenshotService screenshotService, AppTrackerService appTrackerService, SyncService syncService)
        {
            _screenshotService = screenshotService;
            _appTrackerService = appTrackerService;
            _syncService = syncService;
        }

        private static readonly TimeSpan ScreenshotInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan AppUsageInterval  = TimeSpan.FromMinutes(3);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var lastScreenshot = DateTime.MinValue;
            var lastFlush      = DateTime.Now;

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;

                _appTrackerService.Tick();

                if (now - lastFlush >= AppUsageInterval)
                {
                    await _appTrackerService.FlushIntervalAsync();
                    lastFlush = now;
                }

                if (now - lastScreenshot >= ScreenshotInterval)
                {
                    await _screenshotService.TickAsync();
                    await _syncService.SyncScreenshotAsync();
                    lastScreenshot = now;
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}