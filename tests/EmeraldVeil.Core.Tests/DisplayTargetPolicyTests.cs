using System.Drawing;
using EmeraldVeil.Core;
namespace EmeraldVeil.Core.Tests;
public sealed class DisplayTargetPolicyTests
{
    private static VeilDisplay Display(string id, bool primary = false, bool virtualDisplay = false,
        bool instrument = false, bool blackout = false) => new(id, new Rectangle(-2880, 0, 2880, 1800),
            primary, virtualDisplay, instrument, blackout);
    [Fact] public void VddPrimaryWithMainDisconnectedIsNeverCovered() => Assert.Null(
        DisplayTargetPolicy.Select([Display("virtual", primary: true, virtualDisplay: true), Display("panel", instrument: true)]));
    [Fact] public void PhysicalBlackoutMustNotMoveProtectionToVdd() => Assert.Null(
        DisplayTargetPolicy.Select([Display("main", primary: true, blackout: true), Display("virtual", virtualDisplay: true)]));
    [Fact] public void InstrumentPrimaryIsNeverUsedAsDesktopVeil() => Assert.Null(
        DisplayTargetPolicy.Select([Display("panel", primary: true, instrument: true), Display("virtual", virtualDisplay: true)]));
    [Fact] public void NoUsableDisplayDoesNotCoverAnInstrument() => Assert.Null(
        DisplayTargetPolicy.Select([Display("panel", primary: true, instrument: true)]));
    [Fact] public void PhysicalDesktopWinsEvenWhenVddIsPrimary() => Assert.Equal("main",
        DisplayTargetPolicy.Select([Display("main"), Display("virtual", primary: true, virtualDisplay: true)])?.DeviceName);
    [Fact] public void NormalPhysicalPrimaryRemainsPreferred() => Assert.Equal("main",
        DisplayTargetPolicy.Select([Display("main", primary: true), Display("virtual", virtualDisplay: true)])?.DeviceName);
    [Fact] public void LegacyUnscopedBlackoutRemainsFailClosed() => Assert.Null(
        DisplayTargetPolicy.Select([Display("main", primary: true)], unscopedProtection: true));
    [Fact] public void NegativePhysicalOriginsArePreserved()
    {
        var display = Display("main", primary: true);
        Assert.Equal(display.Bounds, DisplayTargetPolicy.Select([display])!.Bounds);
    }
    [Fact] public void EmptyTopologyIsSafe() => Assert.Null(DisplayTargetPolicy.Select([]));
    [Fact]
    public void UnidentifiedScreenNeverBecomesAnImplicitPhysicalTarget()
    {
        var unknown = new VeilDisplay("UNKNOWN", new System.Drawing.Rectangle(0, 0, 1920, 1080),
            true, false, false, false, IsIdentityKnown: false);
        Assert.Null(DisplayTargetPolicy.Select([unknown]));
    }

}
