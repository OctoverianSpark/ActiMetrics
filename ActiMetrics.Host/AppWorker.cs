using System.Diagnostics;

namespace ActiMetrics.Host;

public class AppWorker : BackgroundService
{

    private readonly ILogger<AppWorker> _logger;
    private Process? _appProcess;


    public AppWorker(ILogger<AppWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LaunchApp();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_appProcess is null || _appProcess.HasExited)
            {
                _logger.LogWarning("[SERVICE]: App cerrada, reiniciando...");
                LaunchApp();
            }

            await Task.Delay(TimeSpan.FromSeconds(30));

        }
    }
    private void LaunchApp()
    {
        var exePath = Path.Combine(
            AppContext.BaseDirectory,
            "ActiMetrics.exe"
        );

        _appProcess = Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true
        });


        _logger.LogInformation("[SERVICE]: App launched with PID {Pid}", _appProcess?.Id);


    }
    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        if (_appProcess is not null && !_appProcess.HasExited)
        {
            _appProcess.Kill();
            _logger.LogInformation("[SERVICE]: App detenida.");
        }

        await base.StopAsync(stoppingToken);
    }
}

