using Microsoft.Win32;
using System.Diagnostics;

public static class StartupManager
{
  private const string AppName = "ActiMetrics";
  private const string ExeName = "ActiMetrics.exe";
  private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

  private static string GetExecutablePath()
  {
    // WiX instala el exe en el mismo directorio del proceso
    string installDir = AppContext.BaseDirectory;
    string exePath = Path.Combine(installDir, ExeName);

    // Fallback por si acaso
    if (!File.Exists(exePath))
      exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;

    return exePath;
  }

  public static void HabilitarInicio(bool habilitar)
  {
    using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);

    if (habilitar)
    {
      string exePath = GetExecutablePath();
      key?.SetValue(AppName, $"\"{exePath}\"");
    }
    else
    {
      key?.DeleteValue(AppName, throwOnMissingValue: false);
    }
  }

  public static bool EstaHabilitado()
  {
    using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
    return key?.GetValue(AppName) != null;
  }
}