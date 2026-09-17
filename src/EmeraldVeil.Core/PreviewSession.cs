namespace EmeraldVeil.Core;

/// <summary>Explicit display never outranks input, pause, disable, or its deadline.</summary>
public sealed class PreviewSession
{
    private bool _requested;
    private uint _baselineInputTick;
    private ulong _deadline;

    public void Request(uint inputTick, ulong now, TimeSpan? maximumDuration)
    {
        if (maximumDuration is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        _baselineInputTick = inputTick;
        _deadline = maximumDuration is null ? ulong.MaxValue :
            checked(now + (ulong)maximumDuration.Value.TotalMilliseconds);
        _requested = true;
    }

    public bool Observe(bool reliable, uint inputTick, ulong now, bool allowed)
    {
        if (!allowed || !reliable || inputTick != _baselineInputTick || now >= _deadline)
            _requested = false;
        return _requested;
    }

    public void Cancel() => _requested = false;
}
