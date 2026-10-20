using Microsoft.Win32;

namespace Tangerine.App.Settings;

internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Tangerine";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\" --background");
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName);
    }
}
