namespace EmeraldVeil.Core;

/// <summary>
/// Converts Win32's 32-bit last-input tick into a conservative 64-bit idle timeline.
/// </summary>
public sealed class IdleTimeline
{
    private bool _initialized;
    private bool _reliable;
    private uint _lastInputTick32;
    private ulong _lastActivityTick64;
    private ulong _lastSampleTick64;

    public IdleObservation Observe(
        bool inputReadSucceeded,
        uint currentTick32,
        ulong currentTick64,
        uint lastInputTick32)
    {
        if (!inputReadSucceeded)
        {
            _reliable = false;
            return new IdleObservation(false, TimeSpan.Zero);
        }

        if (!_initialized)
        {
            _initialized = true;
            _lastInputTick32 = lastInputTick32;
            _lastSampleTick64 = currentTick64;

            var signedDelta = unchecked((int)(currentTick32 - lastInputTick32));
            if (signedDelta < 0)
            {
                _lastActivityTick64 = currentTick64;
                _reliable = false;
                return new IdleObservation(false, TimeSpan.Zero);
            }

            _lastActivityTick64 = currentTick64 - (uint)signedDelta;
            _reliable = true;
            return ReliableObservation(currentTick64);
        }

        if (currentTick64 < _lastSampleTick64)
        {
            _lastActivityTick64 = currentTick64;
            _reliable = false;
        }

        _lastSampleTick64 = currentTick64;

        if (lastInputTick32 != _lastInputTick32)
        {
            _lastInputTick32 = lastInputTick32;
            _lastActivityTick64 = currentTick64;
            _reliable = true;
        }
        else if (!_reliable)
        {
            var signedDelta = unchecked((int)(currentTick32 - lastInputTick32));
            if (signedDelta >= 0)
            {
                _lastActivityTick64 = currentTick64 - (uint)signedDelta;
                _reliable = true;
            }
        }

        return _reliable
            ? ReliableObservation(currentTick64)
            : new IdleObservation(false, TimeSpan.Zero);
    }

    private IdleObservation ReliableObservation(ulong currentTick64)
    {
        var elapsedMilliseconds = currentTick64 >= _lastActivityTick64
            ? currentTick64 - _lastActivityTick64
            : 0;

        return new IdleObservation(
            true,
            TimeSpan.FromMilliseconds(Math.Min(elapsedMilliseconds, (ulong)long.MaxValue)));
    }
}
