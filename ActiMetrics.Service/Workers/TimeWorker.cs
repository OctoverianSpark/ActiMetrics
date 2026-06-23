
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
            try
            {
                await _timerService.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TimeWorker] Error en InitializeAsync — iniciando en modo libre");
                await _timerService.FallbackInitializeAsync();
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _timerService.Tick();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TimeWorker] Error en Tick");
                }
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
