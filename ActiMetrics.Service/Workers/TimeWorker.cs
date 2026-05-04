
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ActiMetrics.Service.Services;

namespace ActiMetrics.Service.Workers
{
    public class TimeWorker : BackgroundService
    {
        private readonly TimerService _timerService;
        private readonly ILogger<TimeWorker> _logger;

        public TimeWorker(ILogger<TimeWorker> logger, TimerService timerService)
        {
            this._logger = logger;
            this._timerService = timerService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _timerService.InitializeAsync();

            while (!stoppingToken.IsCancellationRequested)
            {
                await _timerService.Tick();
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
