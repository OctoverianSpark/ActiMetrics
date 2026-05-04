// Tracer.Service/Services/AppTrackerService.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ActiMetrics.Data;

namespace ActiMetrics.Service.Services
{
    public class AppTrackerService
    {
        private readonly AppUsageRepository _repository;
        private readonly string _workerId;
        private readonly string _workerUserName;

        private readonly Dictionary<string, double> _currentInterval = new();
        private DateTime _intervalStart = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.Local);
        private string _lastApp = string.Empty;
        private DateTime _lastAppStart = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.Local);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private static readonly Dictionary<string, string> BrowserNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["chrome"]   = "Chrome",
            ["msedge"]   = "Edge",
            ["firefox"]  = "Firefox",
            ["brave"]    = "Brave",
            ["opera"]    = "Opera",
            ["vivaldi"]  = "Vivaldi",
            ["iexplore"] = "Internet Explorer",
        };

        // Sufijos que cada navegador añade al final del título de la ventana
        private static readonly Dictionary<string, string[]> BrowserSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["chrome"]   = [" - Google Chrome"],
            ["msedge"]   = [" - Microsoft Edge"],
            ["firefox"]  = [" - Mozilla Firefox", " — Mozilla Firefox"],
            ["brave"]    = [" - Brave"],
            ["opera"]    = [" - Opera"],
            ["vivaldi"]  = [" - Vivaldi"],
            ["iexplore"] = [" - Windows Internet Explorer", " - Internet Explorer"],
        };

        public AppTrackerService(AppUsageRepository repository)
        {
            _repository = repository;
            _workerId = Environment.MachineName;
            _workerUserName = Environment.UserName;
        }

        public void Tick()
        {
            var appName = GetForegroundAppName();
            if (string.IsNullOrEmpty(appName)) return;

            var now = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.Local);

            if (_lastApp != appName)
            {
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

        public async Task FlushIntervalAsync()
        {
            var now = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.Local);

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

                await _repository.LogIntervalAsync(_workerId, _workerUserName, _intervalStart, now, appsJson);

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
                var processName = process.ProcessName;

                if (BrowserNames.TryGetValue(processName, out var browserLabel))
                {
                    var sb = new StringBuilder(512);
                    GetWindowText(hwnd, sb, sb.Capacity);
                    var title = sb.ToString().Trim();

                    if (!string.IsNullOrEmpty(title))
                    {
                        if (BrowserSuffixes.TryGetValue(processName, out var suffixes))
                        {
                            foreach (var suffix in suffixes)
                            {
                                if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                                {
                                    title = title[..^suffix.Length].Trim();
                                    break;
                                }
                            }
                        }
                        return $"{browserLabel}: {title}";
                    }
                }

                return process.MainModule?.FileVersionInfo.ProductName
                    ?? processName;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}