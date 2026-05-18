using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Serilog;

namespace ActiMetrics.UI
{
    public static class StartupManager
    {
        private const string TaskName = "ActiMetrics";
        private const string ExeName = "ActiMetrics.exe";

        private static string GetExecutablePath()
        {
            string exePath = Path.Combine(AppContext.BaseDirectory, ExeName);
            if (!File.Exists(exePath))
                exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
            return exePath;
        }

        // ── Tarea programada ─────────────────────────────────────────────────

        public static void HabilitarInicio(bool habilitar)
        {
            if (habilitar)
            {
                CrearTarea();
                AgregarRegistroArranque();
            }
            else
            {
                EliminarTarea();
                QuitarRegistroArranque();
            }
        }

        private static void CrearTarea()
        {
            string exePath = GetExecutablePath();
            var (usuario, _) = ObtenerUsuarioReal();

            string xml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo>
                    <Description>Mantiene ActiMetrics activo y lo reinicia si se cierra.</Description>
                  </RegistrationInfo>
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                      <Delay>PT30S</Delay>
                      <Repetition>
                        <Interval>PT3M</Interval>
                        <Duration>PT23H59M</Duration>
                        <StopAtDurationEnd>false</StopAtDurationEnd>
                      </Repetition>
                    </LogonTrigger>
                  </Triggers>
                  <Principals>
                    <Principal id="Author">
                      <UserId>{usuario}</UserId>
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>LeastPrivilege</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>7</Priority>
                  </Settings>
                  <Actions Context="Author">
                    <Exec>
                      <Command>"{exePath}"</Command>
                    </Exec>
                  </Actions>
                </Task>
                """;

            string tempFile = Path.Combine(Path.GetTempPath(), $"actimetrics-task-{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(tempFile, xml, Encoding.Unicode);
                RunSchtasks($"/create /tn \"{TaskName}\" /xml \"{tempFile}\" /f");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private static void EliminarTarea() =>
            RunSchtasks($"/delete /tn \"{TaskName}\" /f");

        public static bool EstaHabilitado()
        {
            using var proc = Process.Start(new ProcessStartInfo("schtasks", $"/query /tn \"{TaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            proc?.WaitForExit();
            return proc?.ExitCode == 0;
        }

        private static void RunSchtasks(string args) => RunComando("schtasks", args);

        // ── Registro de arranque (Task Manager → Startup) ────────────────────

        private static void AgregarRegistroArranque()
        {
            string exePath = GetExecutablePath();
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.SetValue(TaskName, $"\"{exePath}\"");
        }

        private static void QuitarRegistroArranque()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue(TaskName, throwOnMissingValue: false);
        }

        // ── P/Invoke WTS ─────────────────────────────────────────────────────

        private static class NativeMethods
        {
            internal enum WTS_INFO_CLASS
            {
                WTSUserName = 5,
                WTSDomainName = 7,
            }

            [DllImport("kernel32.dll")]
            internal static extern uint WTSGetActiveConsoleSessionId();

            [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern bool WTSQuerySessionInformation(
                IntPtr hServer, uint sessionId, WTS_INFO_CLASS wtsInfoClass,
                out IntPtr ppBuffer, out uint pBytesReturned);

            [DllImport("wtsapi32.dll")]
            internal static extern void WTSFreeMemory(IntPtr pMemory);
        }

        // ── Usuario real de la sesión activa ─────────────────────────────────

        private static (string Usuario, string LocalAppData) ObtenerUsuarioReal()
        {
            try
            {
                uint sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
                if (sessionId == 0xFFFFFFFF) return Fallback();

                if (!NativeMethods.WTSQuerySessionInformation(IntPtr.Zero, sessionId,
                        NativeMethods.WTS_INFO_CLASS.WTSUserName, out IntPtr pUser, out _) || pUser == IntPtr.Zero)
                    return Fallback();

                string user = Marshal.PtrToStringUni(pUser) ?? string.Empty;
                NativeMethods.WTSFreeMemory(pUser);
                if (string.IsNullOrEmpty(user)) return Fallback();

                NativeMethods.WTSQuerySessionInformation(IntPtr.Zero, sessionId,
                    NativeMethods.WTS_INFO_CLASS.WTSDomainName, out IntPtr pDomain, out _);
                string domain = pDomain != IntPtr.Zero ? Marshal.PtrToStringUni(pDomain) ?? string.Empty : string.Empty;
                if (pDomain != IntPtr.Zero) NativeMethods.WTSFreeMemory(pDomain);

                string fullName = string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";
                string localAppData = BuscarLocalAppData(user);

                Log.Debug("[Startup] Usuario real de sesión: {User}, LocalAppData: {Path}", fullName, localAppData);
                return (fullName, localAppData);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Startup] ObtenerUsuarioReal falló, usando contexto actual.");
                return Fallback();
            }

            static (string, string) Fallback() =>
                (WindowsIdentity.GetCurrent().Name,
                 Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        private static string BuscarLocalAppData(string userName)
        {
            using var hive = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            if (hive != null)
            {
                foreach (var sid in hive.GetSubKeyNames())
                {
                    using var sub = hive.OpenSubKey(sid);
                    if (sub?.GetValue("ProfileImagePath") is string profilePath &&
                        Path.GetFileName(profilePath).Equals(userName, StringComparison.OrdinalIgnoreCase))
                        return Path.Combine(profilePath, "AppData", "Local");
                }
            }

            string usersDir = Path.GetDirectoryName(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? @"C:\Users";
            string candidate = Path.Combine(usersDir, userName, "AppData", "Local");
            return Directory.Exists(candidate)
                ? candidate
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        // ── Utilidad ─────────────────────────────────────────────────────────

        private static void RunComando(string exe, string args)
        {
            using var proc = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (proc is null) return;

            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit();
            Task.WaitAll(stdout, stderr);

            if (proc.ExitCode != 0)
                Log.Warning("[StartupManager] {Exe} {Args} → exit {Code} | {Err}",
                    exe, args, proc.ExitCode, stderr.Result.Trim());
        }
    }
}
