// Tracer.Service/Workers/ScreenWorker.cs
using Microsoft.Extensions.Hosting;
using Tracer.Service.Services;

namespace Tracer.Service.Workers
{
    public class ScreenWorker : BackgroundService
    {
        private readonly ScreenshotService _screenshotService;
        private readonly AppTrackerService _appTrackerService;
        private readonly SyncService _syncService;

        public ScreenWorker(ScreenshotService screenshotService, AppTrackerService appTrackerService,SyncService syncService)
        {
            _screenshotService = screenshotService;
            _appTrackerService = appTrackerService;
            _syncService = syncService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _appTrackerService.Tick();

                var files = await _screenshotService.TickAsync();
                Console.WriteLine(files);
                if (files is not null)
                    await _appTrackerService.FlushIntervalAsync();

                await _syncService.SyncScreenshotAsync();
                await Task.Delay(30000, stoppingToken);
            }
        }
    }
}