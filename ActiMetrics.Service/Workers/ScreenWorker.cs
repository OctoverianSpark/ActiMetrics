using Microsoft.Extensions.Hosting;
using ActiMetrics.Service.Services;

namespace ActiMetrics.Service.Workers
{
    public class ScreenWorker : BackgroundService
    {
        private readonly ScreenshotService _screenshotService;
        private readonly AppTrackerService _appTrackerService;
        private readonly InputTrackerService _inputTrackerService;
        private readonly SyncService _syncService;

        private static readonly TimeSpan ScreenshotInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FlushInterval      = TimeSpan.FromMinutes(1);

        public ScreenWorker(
            ScreenshotService screenshotService,
            AppTrackerService appTrackerService,
            InputTrackerService inputTrackerService,
            SyncService syncService)
        {
            _screenshotService   = screenshotService;
            _appTrackerService   = appTrackerService;
            _inputTrackerService = inputTrackerService;
            _syncService         = syncService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _inputTrackerService.Start();

            try
            {
                await Task.WhenAll(
                    RunTrackingLoopAsync(stoppingToken),
                    RunScreenshotLoopAsync(stoppingToken));
            }
            finally
            {
                _inputTrackerService.Dispose();
            }
        }

        private async Task RunTrackingLoopAsync(CancellationToken stoppingToken)
        {
            var lastFlush = DateTime.Now;

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;

                _appTrackerService.Tick();
                _inputTrackerService.Tick();

                if (now - lastFlush >= FlushInterval)
                {
                    var (active, idle, clicks, keys) = _inputTrackerService.GetAndResetCounts();
                    await _appTrackerService.FlushIntervalAsync(active, idle, clicks, keys);
                    lastFlush = now;
                }

                await Task.Delay(1000, stoppingToken);
            }
        }

        private async Task RunScreenshotLoopAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(ScreenshotInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (await _syncService.CheckTakeScreenshotsAsync())
                {
                    await _screenshotService.TickAsync();
                    await _syncService.SyncScreenshotAsync();
                }
            }
        }
    }
}