using Velopack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using ActiMetrics.Data;
using ActiMetrics.Service.Services;
using ActiMetrics.Service.Workers;
using ActiMetrics.UI.Tray;
using Serilog;



namespace ActiMetrics.UI
{
    internal static class Program
    {
        private static bool _shouldRestart = false;

        private static void Relaunch()
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }

        [STAThread]
        static async Task Main(string[] args)
        {
            // Configurar Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
                .WriteTo.File(
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "actimetrics-.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // Reinicio inmediato si la app muere en un hilo secundario
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    Log.Fatal(ex, "Excepción crítica no controlada");
                Log.CloseAndFlush();
                Relaunch();
            };

            try
            {
                Log.Information("Iniciando ActiMetrics...");

                VelopackApp.Build()
                    .OnBeforeUninstallFastCallback(v => StartupManager.HabilitarInicio(false))
                    .Run();

                // Registrar arranque con Windows si aún no está habilitado
                if (!StartupManager.EstaHabilitado())
                    StartupManager.HabilitarInicio(true);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Reinicio controlado si muere el hilo UI
                Application.ThreadException += (_, e) =>
                {
                    Log.Error(e.Exception, "Excepción no controlada en el hilo UI");
                    _shouldRestart = true;
                    Application.Exit();
                };

                var uiContext = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(uiContext);
                var host = Host.CreateDefaultBuilder(args)
                    .UseSerilog() // Usar Serilog en lugar del logging por defecto
                    .ConfigureAppConfiguration((context, config) =>
                    {
                        config.SetBasePath(AppContext.BaseDirectory)
                              .AddJsonFile("appsettings.json", optional: true)
                              .AddJsonFile($"appsettings.Development.json", optional: true)
                              .AddEnvironmentVariables();
                    })
                    .ConfigureServices(services =>
                    {
                        // Data
                        services.AddSingleton<Database>();
                        services.AddSingleton<Session>();
                        services.AddSingleton<StateRepository>();
                        services.AddSingleton<AppUsageRepository>();
                        services.AddSingleton<ScreenshotRepository>();

                        // Services
                        services.AddSingleton<TokenService>();
                        services.AddSingleton<ActivityService>();
                        services.AddSingleton<TimerService>();
                        services.AddSingleton<ScreenshotService>();
                        services.AddSingleton<AppTrackerService>();
                        services.AddSingleton<WebSocketService>();
                        services.AddSingleton<SyncService>();

                        // Workers
                        services.AddHostedService<TimeWorker>();
                        services.AddHostedService<ScreenWorker>();
                        services.AddHostedService<WebSocketWorker>();
                        services.AddHostedService<SyncWorker>();
                        services.AddHostedService<UpdateWorker>();
                    })
                    .Build();

                Session session = host.Services.GetRequiredService<Session>();

                var timerService = host.Services.GetRequiredService<TimerService>();
                var socketService = host.Services.GetRequiredService<WebSocketService>();
                var syncService = host.Services.GetRequiredService<SyncService>();
                if (!session.IsAuthenticated())
                {
                    LoginCheck login = new();

                    if (login.ShowDialog() != DialogResult.OK)
                    {
                        Application.Exit();
                        return;
                    }
                    session.SaveEmail(login.Email!);

                }
                Console.WriteLine($"[SESSION]: EMAIL LOGGED_IN LIKE :{session.GetEmail()}");


                socketService.OnRestart += () => uiContext.Post(_ =>
                {
                    Log.Information("Reinicio solicitado por el servidor");
                    _shouldRestart = true;
                    Application.Exit();
                }, null);

                using var trayService = new TrayService(timerService, socketService, syncService, uiContext!);

                var cts = new CancellationTokenSource();

                _ = host.RunAsync(cts.Token);

                // Application.Run() bloquea aquí hasta que se llame Application.Exit()
                Application.Run();

                // Limpieza al salir
                await cts.CancelAsync();
                await host.StopAsync();

            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "La aplicación terminó inesperadamente");
                _shouldRestart = true;
            }
            finally
            {
                Log.CloseAndFlush();
            }

            if (_shouldRestart)
            {
                await Task.Delay(1500);
                Relaunch();
            }
        }
    }
}