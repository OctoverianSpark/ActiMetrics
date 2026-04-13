using ActiMetrics.Host;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

if (args.Contains("--install"))
{
  await InstallService();
  return;
}

if (args.Contains("--uninstall"))
{
  await UninstallService();
  return;
}

await Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
      options.ServiceName = "ActiMetrics";
    })
    .ConfigureServices(services =>
    {
      services.AddHostedService<AppWorker>();
    })
    .Build()
    .RunAsync();

static async Task InstallService()
{
  var exePath = Environment.ProcessPath!;

  await RunSc($"create ActiMetrics binPath= \"{exePath}\" start= auto DisplayName= \"ActiMetrics\"");
  await RunSc("description ActiMetrics \"Servicio de monitoreo ActiMetrics\"");
  await RunSc("sdset ActiMetrics \"D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)\"");
  await RunSc("start ActiMetrics");

  Console.WriteLine("[HOST]: Servicio instalado y corriendo.");
}

static async Task UninstallService()
{
  await RunSc("stop ActiMetrics");
  await Task.Delay(2000);
  await RunSc("delete ActiMetrics");
  Console.WriteLine("[HOST]: Servicio desinstalado.");
}

static async Task RunSc(string arguments)
{
  var process = Process.Start(new ProcessStartInfo
  {
    FileName = "sc.exe",
    Arguments = arguments,
    UseShellExecute = false,
    CreateNoWindow = true
  });
  await process!.WaitForExitAsync();
}