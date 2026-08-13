namespace EmeraldVeil.Core;

/// <summary>
/// Reconciles the desired veil mode with the independently owned native
/// renderer. A visible mode is retried after a bounded delay when the native
/// process is no longer running; hiding always applies immediately.
/// </summary>
public sealed class VeilModeReconciler
{
    private readonly long _retryDelayTicks;

    private VeilMode _lastMode = VeilMode.Hidden;
    private long _nextRetryTimestamp;

    public VeilModeReconciler(TimeSpan retryDelay, long timestampFrequency)
    {
        if (retryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        _retryDelayTicks = checked((long)(retryDelay.TotalSeconds * timestampFrequency));
    }

    public bool ShouldApply(VeilMode desiredMode, bool rendererRunning, long timestamp)
    {
        if (desiredMode != _lastMode)
        {
            _lastMode = desiredMode;
            ScheduleNextRetry(desiredMode, timestamp);
            return true;
        }

        if (desiredMode == VeilMode.Hidden || rendererRunning)
        {
            return false;
        }

        if (timestamp < _nextRetryTimestamp)
        {
            return false;
        }

        _nextRetryTimestamp = checked(timestamp + _retryDelayTicks);
        return true;
    }

    private void ScheduleNextRetry(VeilMode mode, long timestamp)
    {
        _nextRetryTimestamp = mode == VeilMode.Hidden
            ? 0
            : checked(timestamp + _retryDelayTicks);
    }
}
