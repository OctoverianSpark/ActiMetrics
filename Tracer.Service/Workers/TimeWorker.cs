
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracer.Service.Services;

namespace Tracer.Service.Workers
{
    public class TimeWorker : BackgroundService
    {
        private readonly TimerService _timerService;
        private readonly ILogger<TimeWorker> _logger;
        private int _tickCount = 0;
        private const int SyncEverySeconds = 300; // 5 minutos

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
