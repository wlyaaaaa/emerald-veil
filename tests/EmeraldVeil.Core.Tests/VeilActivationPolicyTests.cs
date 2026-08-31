using EmeraldVeil.Core;

namespace EmeraldVeil.Core.Tests;

public sealed class VeilActivationPolicyTests
{
    private static readonly VeilActivationPolicy Policy = new(TimeSpan.FromMinutes(6));

    [Theory]
    [InlineData(359_999, VeilMode.Hidden)]
    [InlineData(360_000, VeilMode.Idle)]
    public void ActivatesAtConfiguredThreshold(int milliseconds, VeilMode expected)
    {
        var result = Policy.Evaluate(
            new IdleObservation(true, TimeSpan.FromMilliseconds(milliseconds)),
            isPaused: false,
            previewRequested: false);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void UnreliableInputObservationKeepsVeilHidden()
    {
        var result = Policy.Evaluate(
            new IdleObservation(false, TimeSpan.FromHours(1)),
            isPaused: false,
            previewRequested: false);

        Assert.Equal(VeilMode.Hidden, result);
    }

    [Fact]
    public void PauseSuppressesIdleActivation()
    {
        var result = Policy.Evaluate(
            new IdleObservation(true, TimeSpan.FromMinutes(6)),
            isPaused: true,
            previewRequested: false);

        Assert.Equal(VeilMode.Hidden, result);
    }

    [Fact]
    public void ExplicitPreviewWorksWhileIdleActivationIsPaused()
    {
        var result = Policy.Evaluate(
            new IdleObservation(true, TimeSpan.Zero),
            isPaused: true,
            previewRequested: true);

        Assert.Equal(VeilMode.Preview, result);
    }
}
