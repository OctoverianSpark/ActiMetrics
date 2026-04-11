using Velopack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;
using ActiMetrics.Data;
using ActiMetrics.Service.Services;
using ActiMetrics.Service.Workers;
using ActiMetrics.UI.Tray;



namespace ActiMetrics.UI
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [STAThread]
        static async Task Main(string[] args)
        {
            VelopackApp.Build()
            .OnFirstRun(v => StartupManager.HabilitarInicio(true))
            .OnBeforeUninstallFastCallback(v => StartupManager.HabilitarInicio(false))
            .Run();

//#if DEBUG
            AllocConsole();
//#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var uiContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(uiContext);
            var host = Host.CreateDefaultBuilder(args).ConfigureAppConfiguration((context, config) =>
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


            using var trayService = new TrayService(timerService, socketService, syncService, uiContext!);

            var cts = new CancellationTokenSource();

            _ = host.RunAsync(cts.Token);

            // Application.Run() bloquea aquí hasta que se llame Application.Exit()
            Application.Run();

            // Limpieza al salir
            await cts.CancelAsync(); // ← CancelAsync es preferible en .NET 6+
            await host.StopAsync();
        }
    }
}