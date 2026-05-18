using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace ActiMetrics.Host;

public sealed class WatchdogWorker : BackgroundService
{
    private const string ProcessName = "ActiMetrics";
    private const string ExeName = "ActiMetrics.exe";

    private readonly ILogger<WatchdogWorker> _logger;

    public WatchdogWorker(ILogger<WatchdogWorker> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Watchdog] Vigilancia activa.");

        // Espera breve al arranque para que la sesión de usuario esté disponible
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool corriendo = AppCorriendo();

            if (!corriendo)
            {
                _logger.LogWarning("[Watchdog] {Process} no está corriendo — intentando levantar.", ProcessName);
                LanzarUI();
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private static bool AppCorriendo()
    {
        try
        {
            using var m = Mutex.OpenExisting("Global\\ActiMetrics_SingleInstance");
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    private void LanzarUI() => LanzarDirecto();

    private static string? BuscarExeUI()
    {
        // Producción: Velopack instala la UI en %LOCALAPPDATA%\ActiMetrics\current\
        var localAppData = ObtenerLocalAppDataUsuarioReal();
        if (localAppData is not null)
        {
            var rutaVelopack = Path.Combine(localAppData, "ActiMetrics", "current", ExeName);
            if (File.Exists(rutaVelopack)) return rutaVelopack;
        }

        // Fallback para desarrollo
        var rutaDev = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (File.Exists(rutaDev)) return rutaDev;

        return null;
    }

    private static string? ObtenerLocalAppDataUsuarioReal()
    {
        try
        {
            uint sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF) return null;

            if (!NativeMethods.WTSQueryUserToken(sessionId, out IntPtr token)) return null;

            try
            {
                uint size = 512;
                var sb = new System.Text.StringBuilder((int)size);
                if (!NativeMethods.GetUserProfileDirectory(token, sb, ref size)) return null;

                return Path.Combine(sb.ToString(), "AppData", "Local");
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        catch
        {
            return null;
        }
    }

    private void LanzarDirecto()
    {
        try
        {
            var exePath = BuscarExeUI();
            if (exePath is null)
            {
                _logger.LogError("[Watchdog] No se encontró {Exe} en ninguna ubicación conocida.", ExeName);
                return;
            }

            uint sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                _logger.LogWarning("[Watchdog] No hay sesión de usuario activa.");
                return;
            }

            if (!NativeMethods.WTSQueryUserToken(sessionId, out IntPtr token))
            {
                _logger.LogError("[Watchdog] WTSQueryUserToken falló (error {Code}). El servicio requiere LocalSystem.", Marshal.GetLastWin32Error());
                return;
            }

            try
            {
                NativeMethods.CreateEnvironmentBlock(out IntPtr envBlock, token, false);
                try
                {
                    var si = new NativeMethods.STARTUPINFO
                    {
                        cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
                        lpDesktop = "winsta0\\default"
                    };

                    const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

                    bool ok = NativeMethods.CreateProcessAsUser(
                        token, exePath, $"\"{exePath}\"",
                        IntPtr.Zero, IntPtr.Zero, false, CREATE_UNICODE_ENVIRONMENT,
                        envBlock, null, ref si, out var pi);

                    if (ok)
                    {
                        NativeMethods.CloseHandle(pi.hProcess);
                        NativeMethods.CloseHandle(pi.hThread);
                        _logger.LogInformation("[Watchdog] {Exe} lanzado en sesión {Session}.", ExeName, sessionId);
                    }
                    else
                    {
                        _logger.LogError("[Watchdog] CreateProcessAsUser falló (error {Code}).", Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    if (envBlock != IntPtr.Zero)
                        NativeMethods.DestroyEnvironmentBlock(envBlock);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Watchdog] Excepción al lanzar directamente.");
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        internal static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        internal static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool CreateProcessAsUser(
            IntPtr hToken,
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool GetUserProfileDirectory(IntPtr hToken, System.Text.StringBuilder lpProfileDir, ref uint lpcchSize);

        [DllImport("userenv.dll", SetLastError = true)]
        internal static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

        [DllImport("userenv.dll", SetLastError = true)]
        internal static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved, lpDesktop, lpTitle;
            public uint dwX, dwY, dwXSize, dwYSize;
            public uint dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public uint dwProcessId, dwThreadId;
        }
    }
}
