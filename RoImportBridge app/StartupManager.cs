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

    public static bool RunsInBackground()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
        var value = key?.GetValue(ValueName) as string;
        return value?.Contains("--background", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static void SetEnabled(bool enabled, bool runInBackground)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true) ?? throw new InvalidOperationException("Windows startup settings could not be opened.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        key.SetValue(ValueName, BuildStartupCommand(runInBackground));
    }

    private static string BuildStartupCommand(bool runInBackground)
    {
        var backgroundArgument = runInBackground ? " --background" : string.Empty;
        return $"\"{Environment.ProcessPath}\"{backgroundArgument}";
    }
}
