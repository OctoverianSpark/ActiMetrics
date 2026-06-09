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
            // --checkInstall lo pasa el instalador como hook de verificación; salir OK antes que Velopack lo vea
            if (args.Contains("--checkInstall"))
            {
                StartupManager.HabilitarInicio(false);

                var self = Process.GetCurrentProcess();
                foreach (var p in Process.GetProcessesByName("ActiMetrics"))
                {
                    if (p.Id == self.Id) continue;
                    p.CloseMainWindow();
                    if (!p.WaitForExit(3000))
                        p.Kill();
                }

                return;
            }

            // Velopack PRIMERO: intercepta hooks de install/uninstall y sale antes de hacer nada más
            VelopackApp.Build()
                .OnAfterInstallFastCallback(v =>
                {
                    StartupManager.HabilitarInicio(true);
                })
                .OnAfterUpdateFastCallback(v =>
                {
                    StartupManager.HabilitarInicio(true);
                })
                .OnBeforeUninstallFastCallback(v =>
                {
                    StartupManager.HabilitarInicio(false);
                })
                .Run();

            // Garantiza una única instancia; Task Scheduler puede intentar reiniciar
            // mientras la app todavía está corriendo → salir silenciosamente en ese caso
            using var mutex = new Mutex(true, "Global\\ActiMetrics_SingleInstance", out bool isNewInstance);
            if (!isNewInstance)
                return;

            // Configurar Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
                .WriteTo.File(
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "app", "actimetrics-.log"),
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
                    .UseSerilog()
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

                // Capturar la actualización pendiente antes del cierre limpio
                (UpdateManager Mgr, UpdateInfo Update)? pendingUpdate = null;
                ActiMetrics.Service.Workers.UpdateWorker.UpdateReady += u => pendingUpdate = u;

                // Cierre iniciado por UpdateWorker (instalador de actualización)
                host.Services.GetRequiredService<IHostApplicationLifetime>()
                    .ApplicationStopping.Register(() => uiContext.Post(_ => Application.Exit(), null));

                // Application.Run() bloquea aquí hasta que se llame Application.Exit()
                Application.Run();

                // Limpieza al salir — todos los servicios y handles se liberan
                await cts.CancelAsync();
                await host.StopAsync();

                // Aplicar actualización DESPUÉS del cierre limpio para evitar
                // "Access Denied" cuando Update.exe intenta reemplazar los archivos.
                if (pendingUpdate.HasValue)
                {
                    Log.Information("[UPDATE] Aplicando actualización tras cierre limpio...");
                    Log.CloseAndFlush();
                    pendingUpdate.Value.Mgr.ApplyUpdatesAndRestart(pendingUpdate.Value.Update);
                    return;
                }
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
