using Microsoft.Extensions.Hosting;
using Tracer.Service.Services;

namespace Tracer.Service.Workers
{
    public class SyncWorker : BackgroundService
    {
        private readonly SyncService _syncService;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);

        public SyncWorker(SyncService syncService)
        {
            _syncService = syncService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _syncService.SyncAsync();
                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}