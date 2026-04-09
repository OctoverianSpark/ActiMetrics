using Microsoft.Win32;

public static class StartupManager
{
  private const string AppName = "ActiMetrics";
  private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

  public static void HabilitarInicio(bool habilitar)
  {
    using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);

    if (habilitar)
    {
      string exePath = Application.ExecutablePath;
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