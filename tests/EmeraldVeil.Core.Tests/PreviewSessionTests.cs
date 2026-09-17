using EmeraldVeil.Core;
namespace EmeraldVeil.Core.Tests;
public sealed class PreviewSessionTests
{
    [Fact] public void AnyRealInputDismissesImmediately()
    {
        var p = new PreviewSession(); p.Request(10, 1000, TimeSpan.FromSeconds(15));
        Assert.True(p.Observe(true, 10, 1001, true));
        Assert.False(p.Observe(true, 11, 1002, true));
        Assert.False(p.Observe(true, 10, 1003, true));
    }
    [Theory] [InlineData(false, true)] [InlineData(true, false)]
    public void UnreliableInputOrDisallowedDisplayCancels(bool reliable, bool allowed)
    {
        var p = new PreviewSession(); p.Request(10, 1000, null);
        Assert.False(p.Observe(reliable, 10, 1001, allowed));
        Assert.False(p.Observe(true, 10, 1002, true));
    }
    [Fact] public void AbsoluteDeadlineEvenIfNativeLaunchNeverFinishes()
    {
        var p = new PreviewSession(); p.Request(10, 1000, TimeSpan.FromSeconds(15));
        Assert.True(p.Observe(true, 10, 15999, true));
        Assert.False(p.Observe(true, 10, 16000, true));
    }
    [Fact] public void ImmediateDisplayStillExitsOnInput()
    {
        var p = new PreviewSession(); p.Request(10, 1000, null);
        Assert.True(p.Observe(true, 10, 100000, true));
        Assert.False(p.Observe(true, 11, 100001, true));
    }
    [Fact] public void CancelCannotReappearWithoutANewRequest()
    {
        var p = new PreviewSession(); p.Request(10, 1000, null); p.Cancel();
        Assert.False(p.Observe(true, 10, 1001, true));
    }
}
