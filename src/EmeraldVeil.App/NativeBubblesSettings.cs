using Microsoft.Win32;

namespace EmeraldVeil.App;

internal static class NativeBubblesSettings
{
    private const string OwnerKeyPath = @"Software\EmeraldVeil";
    private const string EnabledValueName = "NativeBubblesEnabled";
    private const uint RequiredTimeoutSeconds = 360;

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(OwnerKeyPath, writable: false);
        return key?.GetValue(EnabledValueName) is int value && value == 1;
    }

    internal static bool EnsureRuntimePolicy()
    {
        if (!IsEnabled())
        {
            return false;
        }

        bool drifted =
            NativeMethods.GetScreenSaverTimeout() != RequiredTimeoutSeconds ||
            NativeMethods.GetScreenSaverSecure() ||
            NativeMethods.GetScreenSaverActive();
        if (drifted)
        {
            NativeMethods.SetScreenSaverRuntimePolicy(
                RequiredTimeoutSeconds,
                secure: false,
                active: false);
        }

        return drifted;
    }
}
