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
            suppressActivation: false,
            previewRequested: false);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void UnreliableInputObservationKeepsVeilHidden()
    {
        var result = Policy.Evaluate(
            new IdleObservation(false, TimeSpan.FromHours(1)),
            suppressActivation: false,
            previewRequested: false);

        Assert.Equal(VeilMode.Hidden, result);
    }

    [Fact]
    public void SuppressionBlocksIdleActivation()
    {
        var result = Policy.Evaluate(
            new IdleObservation(true, TimeSpan.FromMinutes(6)),
            suppressActivation: true,
            previewRequested: false);

        Assert.Equal(VeilMode.Hidden, result);
    }

    [Fact]
    public void SuppressionAlsoBlocksExplicitPreview()
    {
        var result = Policy.Evaluate(
            new IdleObservation(true, TimeSpan.Zero),
            suppressActivation: true,
            previewRequested: true);

        Assert.Equal(VeilMode.Hidden, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalBlackoutSuppressesIdleAndExplicitPreview(bool preview)
    {
        var observation = new IdleObservation(true, TimeSpan.FromHours(8));

        Assert.Equal(VeilMode.Hidden, Policy.Evaluate(
            observation, suppressActivation: false, previewRequested: preview,
            externalProtectionActive: true));
        Assert.Equal(preview ? VeilMode.Preview : VeilMode.Idle, Policy.Evaluate(
            observation, suppressActivation: false, previewRequested: preview,
            externalProtectionActive: false));
    }
}
