using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Velopack;
using Velopack.Sources;

namespace ActiMetrics.Service.Workers
{
  public class UpdateWorker : BackgroundService
  {
    private readonly ILogger<UpdateWorker> _logger;
    private readonly IConfiguration _configuration;

    public UpdateWorker(ILogger<UpdateWorker> logger, IConfiguration configuration)
    {
      _logger = logger;
      _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

      while (!stoppingToken.IsCancellationRequested)
      {
        await VerificarActualizacion();
        await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
      }
    }

    private async Task VerificarActualizacion()
    {
      try
      {
        // Leer configuración de GitHub desde appsettings
        var repoUrl = _configuration["GitHub:RepoUrl"];
        var accessToken = _configuration["GitHub:AccessToken"];

        if (string.IsNullOrEmpty(repoUrl))
        {
          _logger.LogWarning("[UPDATE]: RepoUrl no configurado en appsettings.json");
          return;
        }

        _logger.LogInformation("[UPDATE]: Verificando actualizaciones en {RepoUrl}", repoUrl);

        var source = new GithubSource(
            repoUrl: repoUrl,
            accessToken: accessToken,
            prerelease: false
        );

        var mgr = new UpdateManager(source);
        var update = await mgr.CheckForUpdatesAsync();

        if (update is null)
        {
          _logger.LogInformation("[UPDATE]: No hay nuevas versiones.");
          return;
        }

        _logger.LogInformation("[UPDATE]: Nueva versión disponible: {Version}",
            update.TargetFullRelease.Version);

        await mgr.DownloadUpdatesAsync(update, progress =>
        {
          _logger.LogInformation("[UPDATE]: Descargando {Progress}%", progress);
        });

        _logger.LogInformation("[UPDATE]: Descarga completa, reiniciando...");

        mgr.ApplyUpdatesAndRestart(update);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "[UPDATE]: Error verificando actualizaciones.");
      }
    }
  }
}