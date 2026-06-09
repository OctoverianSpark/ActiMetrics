#pragma warning disable CA1848
using Velopack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActiMetrics.Service.Workers
{
    public class UpdateWorker(
        ILogger<UpdateWorker> logger,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime) : BackgroundService
    {
        // Program.cs suscribe a este evento para aplicar la actualización
        // después del cierre limpio del host.
        public static event Action<(UpdateManager Mgr, UpdateInfo Update)>? UpdateReady;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await VerificarActualizacion(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
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

                var mgr = new UpdateManager(updateUrl);

                logger.LogInformation("[UPDATE] Instalado via Velopack: {Installed} | Versión actual: {Version}",
                    mgr.IsInstalled, mgr.CurrentVersion);

                if (!mgr.IsInstalled)
                {
                    logger.LogWarning("[UPDATE] La aplicación no está instalada via Velopack. Actualización omitida.");
                    return;
                }

                logger.LogInformation("[UPDATE] Verificando actualizaciones en {Url}", updateUrl);
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

                logger.LogInformation("[UPDATE] Descarga completa. Cerrando aplicación para aplicar actualización...");

                // Notificar a Program.cs para que aplique la actualización
                // DESPUÉS del cierre limpio (todos los handles liberados).
                UpdateReady?.Invoke((mgr, update));
                lifetime.StopApplication();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[UPDATE] Error al verificar actualizaciones.");
            }
        }
    }
}
