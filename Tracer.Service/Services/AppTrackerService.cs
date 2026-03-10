// Tracer.Service/Services/AppTrackerService.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tracer.Data;

namespace Tracer.Service.Services
{
    public class AppTrackerService
    {
        private readonly AppUsageRepository _repository;
        private readonly string _workerId;
        private readonly string _workerUserName;

        private readonly Dictionary<string, double> _currentInterval = new();
        private DateTime _intervalStart = DateTime.Now;
        private string _lastApp = string.Empty;
        private DateTime _lastAppStart = DateTime.Now;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public AppTrackerService(AppUsageRepository repository)
        {
            _repository = repository;
            _workerId = Environment.MachineName;
            _workerUserName = Environment.UserName;
        }

        // Llamado cada segundo desde ScreenWorker
        public void Tick()
        {
            var appName = GetForegroundAppName();
            if (string.IsNullOrEmpty(appName)) return;

            var now = DateTime.Now;

            if (_lastApp != appName)
            {
                // Acumula segundos de la app anterior
                if (!string.IsNullOrEmpty(_lastApp))
                {
                    var elapsed = (now - _lastAppStart).TotalSeconds;
                    _currentInterval.TryAdd(_lastApp, 0);
                    _currentInterval[_lastApp] += elapsed;
                }

                _lastApp = appName;
                _lastAppStart = now;
                Console.WriteLine($"[App] {appName}");
            }
        }

        // Llamado cuando ScreenshotService toma una captura
        public async Task FlushIntervalAsync()
        {
            var now = DateTime.Now;

            // Acumula la app actual antes de cerrar el intervalo
            if (!string.IsNullOrEmpty(_lastApp))
            {
                var elapsed = (now - _lastAppStart).TotalSeconds;
                _currentInterval.TryAdd(_lastApp, 0);
                _currentInterval[_lastApp] += elapsed;
                _lastAppStart = now;
            }

            if (_currentInterval.Count > 0)
            {
                var apps = _currentInterval
                    .Select(x => new { app = x.Key, seconds = Math.Round(x.Value, 1) })
                    .OrderByDescending(x => x.seconds)
                    .ToArray();

                var appsJson = JsonSerializer.Serialize(apps);

                await _repository.LogIntervalAsync(_workerId,_workerUserName, _intervalStart, now, appsJson);

                Console.WriteLine($"[AppUsage] Intervalo {_intervalStart:HH:mm:ss} → {now:HH:mm:ss}");
                foreach (var a in apps)
                    Console.WriteLine($"  {a.app} → {TimeSpan.FromSeconds(a.seconds):mm\\:ss}");

                _currentInterval.Clear();
            }

            _intervalStart = now;
        }

        private string GetForegroundAppName()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return string.Empty;

                GetWindowThreadProcessId(hwnd, out uint pid);
                var process = Process.GetProcessById((int)pid);

                return process.MainModule?.FileVersionInfo.ProductName
                    ?? process.ProcessName;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}