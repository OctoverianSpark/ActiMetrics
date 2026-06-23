using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ActiMetrics.Data;
using Microsoft.Extensions.Logging;

namespace ActiMetrics.Service.Services
{
    public class AppTrackerService
    {
        private readonly AppUsageRepository _repository;
        private readonly ILogger<AppTrackerService> _logger;
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

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);


        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int wtsInfoClass, out IntPtr ppBuffer, out uint pBytesReturned);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        private static string GetActualUserName()
        {
            try
            {
                var sessionId = (int)WTSGetActiveConsoleSessionId();
                if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, 5 /* WTSUserName */, out var buffer, out _))
                {
                    try { return Marshal.PtrToStringUni(buffer) ?? Environment.UserName; }
                    finally { WTSFreeMemory(buffer); }
                }
            }
            catch { }
            return Environment.UserName;
        }

        public AppTrackerService(AppUsageRepository repository, ILogger<AppTrackerService> logger)
        {
            _repository   = repository;
            _logger       = logger;
            _workerId     = Environment.MachineName;
            _workerUserName = GetActualUserName();
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

                _logger.LogDebug("[App] Foco → {App}", appName);
                _lastApp      = appName;
                _lastAppStart = now;
            }
        }

        public async Task FlushIntervalAsync(int activeSeconds, int idleSeconds, int mouseClicks, int keystrokes)
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
                await _repository.LogIntervalAsync(
                    _workerId, _workerUserName, _intervalStart, now, appsJson,
                    activeSeconds, idleSeconds, mouseClicks, keystrokes);

                _logger.LogInformation(
                    "[AppUsage] {Start:HH:mm:ss}→{End:HH:mm:ss} {N} apps | Activo:{A}s Clicks:{C} Teclas:{K}",
                    _intervalStart, now, apps.Length, activeSeconds, mouseClicks, keystrokes);

                _currentInterval.Clear();
            }

            _intervalStart = now;
        }

        private static string? TryGetMainModulePath(Process process)
        {
            try { return process.MainModule?.FileName; }
            catch { return null; }
        }

        private static string? QueryProcessPath(uint pid)
        {
            const uint ProcessQueryLimitedInformation = 0x1000;
            var hProcess = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (hProcess == IntPtr.Zero) return null;
            try
            {
                var sb   = new StringBuilder(1024);
                var size = sb.Capacity;
                return QueryFullProcessImageName(hProcess, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        private string GetForegroundAppName()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return string.Empty;

                GetWindowThreadProcessId(hwnd, out uint pid);
                var process     = Process.GetProcessById((int)pid);
                var processName = process.ProcessName;

                // Apps UWP/Store corren dentro de ApplicationFrameHost;
                // el título de la ventana ya refleja el nombre del app real.
                if (processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                {
                    var sb = new StringBuilder(512);
                    GetWindowText(hwnd, sb, sb.Capacity);
                    var title = sb.ToString().Trim();
                    return string.IsNullOrEmpty(title) ? "Windows App" : title;
                }

                // Para todos los demás procesos: usar ProductName del ejecutable.
                // QueryFullProcessImageName funciona cross-bitness (32/64-bit);
                // process.MainModule se usa como fallback si falla.
                var exePath = QueryProcessPath(pid) ?? TryGetMainModulePath(process);

                if (!string.IsNullOrEmpty(exePath))
                    return Path.GetFileName(exePath);

                return processName;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[AppTracker] Error al obtener app activo");
                return string.Empty;
            }
        }
    }
}
