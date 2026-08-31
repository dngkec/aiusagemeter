using Microsoft.Win32;

namespace AIUsageMeter.Windows.Services;

internal static class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AIUsageMeter";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The application executable path is unavailable.");
            key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, false);
    }
}
