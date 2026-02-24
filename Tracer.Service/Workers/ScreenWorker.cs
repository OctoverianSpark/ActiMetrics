// Tracer.Service/Workers/ScreenWorker.cs
using Microsoft.Extensions.Hosting;
using Tracer.Service.Services;

namespace Tracer.Service.Workers
{
    public class ScreenWorker : BackgroundService
    {
        private readonly ScreenshotService _screenshotService;
        private readonly AppTrackerService _appTrackerService;

        public ScreenWorker(ScreenshotService screenshotService, AppTrackerService appTrackerService)
        {
            _screenshotService = screenshotService;
            _appTrackerService = appTrackerService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _appTrackerService.Tick();

                var files = await _screenshotService.TickAsync();
                if (files is not null)
                    await _appTrackerService.FlushIntervalAsync();

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}