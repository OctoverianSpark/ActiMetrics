using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;
using Tracer.Data;
using Tracer.Service.Services;
using Tracer.Service.Workers;
using Tracer.UI.Tray;

namespace Tracer.UI
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [STAThread] 
        static async Task Main(string[] args)
        {

            AllocConsole();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {

                    //Data
                    services.AddSingleton<Database>();
                    services.AddSingleton<StateRepository>();
                    services.AddSingleton<AppUsageRepository>();
                    services.AddSingleton<ScreenshotRepository>();


                    //Services
                    services.AddSingleton<TokenService>(new TokenService("MCBO9LzhFDMm72jcLQhSgdnPVznSxZj8/2fqQ5G3mzg="));
                    services.AddSingleton<ActivityService>();
                    services.AddSingleton<TimerService>();
                    services.AddSingleton<ScreenshotService>();
                    services.AddSingleton<AppTrackerService>();
                    services.AddSingleton<WebSocketService>();
                    services.AddSingleton<SyncService>(); 


                    //Workers
                    services.AddHostedService<TimeWorker>();
                    services.AddHostedService<ScreenWorker>();
                    services.AddHostedService<WebSocketWorker>();
                    services.AddHostedService<SyncWorker>();
                }) 
                .Build();

            var timerService = host.Services.GetRequiredService<TimerService>();
            var trayService = new TrayService(timerService);

            var cts = new CancellationTokenSource();
            _ = host.RunAsync(cts.Token);

            Application.Run();

            cts.Cancel();
            trayService.Dispose();
            await host.StopAsync();
        }
    }
}