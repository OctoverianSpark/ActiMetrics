#pragma warning disable CA1848
using Velopack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActiMetrics.Service.Workers
{
    public class UpdateWorker(
        ILogger<UpdateWorker> logger,
        IConfiguration configuration) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await VerificarActualizacion(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(2), stoppingToken);
            }
        }

        private async Task VerificarActualizacion(CancellationToken stoppingToken)
        {
            try
            {
                var updateUrl = configuration["UpdateUrl"];
                if (string.IsNullOrEmpty(updateUrl))
                {
                    logger.LogWarning("[UPDATE] UpdateUrl no configurado.");
                    return;
                }

                logger.LogInformation("[UPDATE] Verificando actualizaciones en {Url}", updateUrl);

                var mgr = new UpdateManager(updateUrl);
                var update = await mgr.CheckForUpdatesAsync();

                if (update is null)
                {
                    logger.LogInformation("[UPDATE] Sin actualizaciones disponibles.");
                    return;
                }

                logger.LogInformation("[UPDATE] Nueva versión disponible: {Version}. Descargando...",
                    update.TargetFullRelease.Version);

                await mgr.DownloadUpdatesAsync(update,
                    p => logger.LogInformation("[UPDATE] Descargando: {Percent}%", p),
                    stoppingToken);

                logger.LogInformation("[UPDATE] Descarga completa. Aplicando actualización y reiniciando...");
                mgr.ApplyUpdatesAndRestart(update);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[UPDATE] Error al verificar actualizaciones.");
            }
        }
    }
}
