using EmeraldVeil.Core;

namespace EmeraldVeil.Core.Tests;

public sealed class IdleTimelineTests
{
    [Fact]
    public void FirstReliableSampleReconstructsIdleDuration()
    {
        var timeline = new IdleTimeline();

        var observation = timeline.Observe(
            inputReadSucceeded: true,
            currentTick32: 500_000,
            currentTick64: 1_500_000,
            lastInputTick32: 200_000);

        Assert.True(observation.IsReliable);
        Assert.Equal(TimeSpan.FromMinutes(5), observation.IdleDuration);
    }

    [Fact]
    public void FirstSampleHandlesThirtyTwoBitTickRollover()
    {
        var timeline = new IdleTimeline();

        var observation = timeline.Observe(
            inputReadSucceeded: true,
            currentTick32: 0x0001_0000,
            currentTick64: 0x1_0001_0000,
            lastInputTick32: 0xFFFF_0000);

        Assert.True(observation.IsReliable);
        Assert.Equal(TimeSpan.FromMilliseconds(0x0002_0000), observation.IdleDuration);
    }

    [Fact]
    public void FutureOrAmbiguousFirstSampleStaysConservativelyHidden()
    {
        var timeline = new IdleTimeline();

        var observation = timeline.Observe(
            inputReadSucceeded: true,
            currentTick32: 1_000,
            currentTick64: 10_000,
            lastInputTick32: 2_000);

        Assert.False(observation.IsReliable);
        Assert.Equal(TimeSpan.Zero, observation.IdleDuration);
    }

    [Fact]
    public void AnyChangedInputTickResetsActivityEvenWhenTickMovesBackward()
    {
        var timeline = new IdleTimeline();
        _ = timeline.Observe(true, 10_000, 10_000, 9_000);

        var observation = timeline.Observe(true, 11_000, 11_000, 8_000);

        Assert.True(observation.IsReliable);
        Assert.Equal(TimeSpan.Zero, observation.IdleDuration);
    }

    [Fact]
    public void ContinuousRunUsesSixtyFourBitTimelineAcrossTickRollover()
    {
        var timeline = new IdleTimeline();
        _ = timeline.Observe(true, 0xFFFF_F000, 0x0000_0000_FFFF_F000, 0xFFFF_F000);

        var observation = timeline.Observe(
            true,
            0x0004_83E0,
            0x0000_0001_0004_83E0,
            0xFFFF_F000);

        Assert.True(observation.IsReliable);
        Assert.Equal(TimeSpan.FromMilliseconds(300_000), observation.IdleDuration);
    }

    [Fact]
    public void GamepadActivityRestartsAutomaticScreenSaverDelayWithoutKeyboardInput()
    {
        var timeline = new IdleTimeline();
        var policy = new VeilActivationPolicy(TimeSpan.FromMinutes(6));

        VeilMode ModeAt(uint tick, bool gamepadActive = false) =>
            policy.Evaluate(timeline.Observe(true, tick, tick, 1_000, gamepadActive),
                suppressActivation: false, previewRequested: false);

        Assert.Equal(VeilMode.Idle, ModeAt(361_000));
        Assert.Equal(VeilMode.Hidden, ModeAt(361_050, gamepadActive: true));
        Assert.Equal(VeilMode.Hidden, ModeAt(721_050, gamepadActive: true));
        Assert.Equal(VeilMode.Hidden, ModeAt(1_081_049));
        Assert.Equal(VeilMode.Idle, ModeAt(1_081_050));
    }

    [Fact]
    public void ReadFailureIsNeverReportedAsIdle()
    {
        var timeline = new IdleTimeline();

        var observation = timeline.Observe(false, 600_000, 600_000, 0);

        Assert.False(observation.IsReliable);
        Assert.Equal(TimeSpan.Zero, observation.IdleDuration);
    }

    [Fact]
    public void ReliableReadAfterTransientFailureReconstructsTimeline()
    {
        var timeline = new IdleTimeline();
        _ = timeline.Observe(true, 100_000, 100_000, 90_000);
        _ = timeline.Observe(false, 101_000, 101_000, 90_000);

        var observation = timeline.Observe(true, 102_000, 102_000, 90_000);

        Assert.True(observation.IsReliable);
        Assert.Equal(TimeSpan.FromSeconds(12), observation.IdleDuration);
    }
}
