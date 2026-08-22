using Microsoft.Win32;

namespace RoImportBridge;

internal static class StartupManager
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RoImportBridge";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true) ?? throw new InvalidOperationException("Windows startup settings could not be opened.");

        if (enabled)
        {
            key.SetValue(ValueName, BuildStartupCommand());
            return;
        }

        key.DeleteValue(ValueName, false);
    }

    private static string BuildStartupCommand()
    {
        return $"\"{Environment.ProcessPath}\" --background";
    }
}
