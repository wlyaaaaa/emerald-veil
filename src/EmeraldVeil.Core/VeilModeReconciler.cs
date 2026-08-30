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
    private bool _rendererWasRunning;
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
            _rendererWasRunning = desiredMode != VeilMode.Hidden && rendererRunning;
            ScheduleNextRetry(desiredMode, timestamp);
            return true;
        }

        if (desiredMode == VeilMode.Hidden)
        {
            _rendererWasRunning = false;
            return false;
        }

        if (rendererRunning)
        {
            _rendererWasRunning = true;
            return false;
        }

        if (_rendererWasRunning)
        {
            _rendererWasRunning = false;
            _nextRetryTimestamp = checked(timestamp + _retryDelayTicks);
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
