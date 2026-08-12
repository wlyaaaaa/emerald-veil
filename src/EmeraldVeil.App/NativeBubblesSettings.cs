using Microsoft.Win32;

namespace EmeraldVeil.App;

internal static class NativeBubblesSettings
{
    private const string OwnerKeyPath = @"Software\EmeraldVeil";
    private const string EnabledValueName = "NativeBubblesEnabled";

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(OwnerKeyPath, writable: false);
        return key?.GetValue(EnabledValueName) is int value && value == 1;
    }
}
