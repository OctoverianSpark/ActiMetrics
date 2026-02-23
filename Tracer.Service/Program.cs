using Tracer.Data;
using Tracer.Service.Services;
using Tracer.Service.Workers;

var host = Host.CreateDefaultBuilder(args).ConfigureServices(services =>
{
    services.AddSingleton<Database>();
    services.AddSingleton<StateRepository>();
    services.AddSingleton<TimerService>();
    services.AddHostedService<TimeWorker>();

}).Build();

await host.RunAsync();