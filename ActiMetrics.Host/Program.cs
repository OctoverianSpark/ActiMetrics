using ActiMetrics.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

// Inicializar el logger estático ANTES de cualquier otra cosa,
// para que el catch y el mutex-check escriban al archivo.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System",    LogEventLevel.Warning)
    .WriteTo.File(
        path: Path.Combine(AppContext.BaseDirectory, "logs", "host", "host-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    if (e.ExceptionObject is Exception ex)
        Log.Fatal(ex, "[HOST] Excepción no controlada.");
    Log.CloseAndFlush();
};

using var mutex = new Mutex(true, "ActiMetrics_Host_SingleInstance", out bool isNewInstance);
if (!isNewInstance)
{
    Log.Warning("[HOST] Ya hay una instancia en ejecución. Saliendo.");
    Log.CloseAndFlush();
    return;
}

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => { options.ServiceName = "ActiMetrics Host"; })
    .UseSerilog()   // reutiliza el Log.Logger ya configurado
    .ConfigureServices(services =>
    {
        services.AddHostedService<WatchdogWorker>();
    });

try
{
    Log.Information("[HOST] Iniciando ActiMetrics Host...");
    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "[HOST] Terminó inesperadamente.");
}
finally
{
    Log.CloseAndFlush();
}
