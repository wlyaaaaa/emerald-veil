using EmeraldVeil.Core;

namespace EmeraldVeil.Core.Tests;

public sealed class LaunchRecoveryPolicyTests
{
    [Fact] public void FailuresBackOffAndEventuallyStopAutomaticFlashing()
    {
        var policy = new LaunchRecoveryPolicy();
        Assert.True(policy.CanAttempt(TimeSpan.Zero));
        policy.RecordFailure(TimeSpan.Zero);
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(4.999)));
        Assert.True(policy.CanAttempt(TimeSpan.FromSeconds(5)));
        policy.RecordFailure(TimeSpan.FromSeconds(5));
        Assert.False(policy.CanAttempt(TimeSpan.FromSeconds(34.999)));
        Assert.True(policy.CanAttempt(TimeSpan.FromSeconds(35)));
        policy.RecordFailure(TimeSpan.FromSeconds(35));
        Assert.True(policy.IsSuspended);
        Assert.False(policy.CanAttempt(TimeSpan.FromDays(1)));
    }
    [Fact] public void InputManualRetryOrChangedTargetCanStartANewEpisode()
    {
        var policy = new LaunchRecoveryPolicy();
        for (int i = 0; i < 100; i++) policy.RecordFailure(TimeSpan.Zero);
        Assert.Equal(LaunchRecoveryPolicy.MaximumFailures, policy.FailureCount);
        policy.Reset();
        Assert.True(policy.CanAttempt(TimeSpan.Zero));
        Assert.Equal(0, policy.FailureCount);
    }
}
