namespace EmeraldVeil.Core;

/// <summary>Bounds failed starts within one idle episode, including short-lived renderers.</summary>
public sealed class LaunchRecoveryPolicy
{
    public const int MaximumFailures = 3;
    private static readonly TimeSpan[] Delays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];
    public int FailureCount { get; private set; }
    public TimeSpan NextAttempt { get; private set; }
    public bool IsSuspended => FailureCount >= MaximumFailures;
    public bool CanAttempt(TimeSpan now) => !IsSuspended && now >= NextAttempt;

    public void RecordFailure(TimeSpan now)
    {
        FailureCount = Math.Min(MaximumFailures, FailureCount + 1);
        NextAttempt = IsSuspended ? TimeSpan.MaxValue : now + Delays[FailureCount - 1];
    }

    public void Reset()
    {
        FailureCount = 0;
        NextAttempt = TimeSpan.Zero;
    }
}
