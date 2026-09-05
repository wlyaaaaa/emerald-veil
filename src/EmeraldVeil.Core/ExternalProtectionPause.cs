using System.Runtime.Versioning;

namespace EmeraldVeil.Core;

/// <summary>A session-local lifetime marker held by an active external blackout.</summary>
[SupportedOSPlatform("windows")]
public static class ExternalProtectionPause
{
    public const string MutexName = @"Local\EmeraldVeil.ExternalProtectionPause";

    public static bool IsActive(string mutexName = MutexName)
    {
        if (!Mutex.TryOpenExisting(mutexName, out var marker))
        {
            return false;
        }

        // Do not retain this observer handle: only the protecting client owns
        // the lifetime. Its normal exit or crash closes its kernel handle.
        marker.Dispose();
        return true;
    }
}
