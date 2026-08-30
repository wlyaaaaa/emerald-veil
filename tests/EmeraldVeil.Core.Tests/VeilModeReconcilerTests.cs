using EmeraldVeil.Core;

namespace EmeraldVeil.Core.Tests;

public sealed class VeilModeReconcilerTests
{
    private const long Frequency = 1_000;

    [Fact]
    public void AbsentRendererWhileIdleIsRetriedAfterBoundedDelay()
    {
        var reconciler = new VeilModeReconciler(TimeSpan.FromSeconds(1), Frequency);

        Assert.True(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: false, timestamp: 10_000));
        Assert.False(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: false, timestamp: 10_999));
        Assert.True(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: false, timestamp: 11_000));
        Assert.False(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: false, timestamp: 11_999));
        Assert.True(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: false, timestamp: 12_000));
    }

    [Fact]
    public void RendererExitAfterLongUptimeStartsAFreshBackoff()
    {
        var reconciler = new VeilModeReconciler(TimeSpan.FromSeconds(1), Frequency);

        Assert.True(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: false, timestamp: 10_000));
        Assert.False(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: true, timestamp: 10_100));
        Assert.False(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: true, timestamp: 20_000));
        Assert.False(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: false, timestamp: 20_001));
        Assert.False(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: false, timestamp: 21_000));
        Assert.True(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: false, timestamp: 21_001));
    }

    [Fact]
    public void InputHidesImmediatelyDuringRecoveryBackoff()
    {
        var reconciler = new VeilModeReconciler(TimeSpan.FromSeconds(1), Frequency);

        Assert.True(reconciler.ShouldApply(VeilMode.Idle, rendererRunning: false, timestamp: 10_000));
        Assert.True(reconciler.ShouldApply(VeilMode.Hidden, rendererRunning: false, timestamp: 10_001));
        Assert.False(reconciler.ShouldApply(VeilMode.Hidden, rendererRunning: false, timestamp: 10_002));
    }

    [Fact]
    public void RunningRendererDoesNotCreateDuplicateApplyRequests()
    {
        var reconciler = new VeilModeReconciler(TimeSpan.FromSeconds(1), Frequency);

        Assert.True(reconciler.ShouldApply(VeilMode.Preview, rendererRunning: false, timestamp: 10_000));
        Assert.False(reconciler.ShouldApply(VeilMode.Preview, rendererRunning: true, timestamp: 11_000));
        Assert.False(reconciler.ShouldApply(VeilMode.Preview, rendererRunning: true, timestamp: 20_000));
    }
}
