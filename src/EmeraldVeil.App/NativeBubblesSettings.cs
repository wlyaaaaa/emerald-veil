using Microsoft.Win32;

namespace EmeraldVeil.App;

internal static class NativeBubblesSettings
{
    private const string OwnerKeyPath = @"Software\EmeraldVeil";
    private const string DesktopKeyPath = @"Control Panel\Desktop";
    private const string TimeoutValueName = "ScreenSaveTimeOut";
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

        // On Windows 11 builds that keep the automatic screen-saver trigger
        // disabled, SPI_GETSCREENSAVETIMEOUT can report 0 even while the
        // persisted REG_SZ timeout remains 360. The project's own watchdog
        // owns idle activation, so 0 is an expected non-effective runtime
        // observation while active=false; keep the persisted setting exact.
        bool persistedTimeoutDrifted = !HasRequiredPersistedTimeout();
        uint runtimeTimeout = NativeMethods.GetScreenSaverTimeout();
        bool active = NativeMethods.GetScreenSaverActive();
        bool secure = NativeMethods.GetScreenSaverSecure();
        bool drifted =
            persistedTimeoutDrifted ||
            (runtimeTimeout != RequiredTimeoutSeconds &&
                (active || runtimeTimeout != 0)) ||
            secure ||
            active;
        if (drifted)
        {
            PersistRequiredTimeout();
            NativeMethods.SetScreenSaverRuntimePolicy(
                RequiredTimeoutSeconds,
                secure: false,
                active: false);
        }

        return drifted;
    }

    private static bool HasRequiredPersistedTimeout()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopKeyPath, writable: false);
        return key?.GetValue(TimeoutValueName) is string value &&
            uint.TryParse(value, out uint timeout) &&
            timeout == RequiredTimeoutSeconds;
    }

    private static void PersistRequiredTimeout()
    {
        using var key = Registry.CurrentUser.CreateSubKey(DesktopKeyPath);
        key?.SetValue(
            TimeoutValueName,
            RequiredTimeoutSeconds.ToString(),
            RegistryValueKind.String);
    }
}
