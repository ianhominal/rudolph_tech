using System.Runtime.Versioning;
using Microsoft.Win32;

namespace RudolphTech.Services;

/// <summary>
/// "Iniciar con Windows", as the Run key of the current user. Per user on purpose: the survey needs
/// a real desktop session with a visible Chrome window, so starting it for the machine (or as a
/// service) would not work at all.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AutostartRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RudolphTech";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary> Returns false when Windows refused the change, so the checkbox can be put back. </summary>
    public static bool Set(bool enabled, string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (enabled) key.SetValue(ValueName, $"\"{executablePath}\" --startup");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
