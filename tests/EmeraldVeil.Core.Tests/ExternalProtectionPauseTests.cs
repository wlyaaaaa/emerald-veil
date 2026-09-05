namespace EmeraldVeil.Core.Tests;

public sealed class ExternalProtectionPauseTests
{
    [Fact]
    public void MarkerLastsOnlyWhileAProtectingClientHoldsAHandle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var name = @"Local\EmeraldVeil.Test." + Guid.NewGuid().ToString("N");
        Assert.False(ExternalProtectionPause.IsActive(name));
        using (var first = new Mutex(false, name))
        {
            Assert.True(ExternalProtectionPause.IsActive(name));
            using (var second = new Mutex(false, name))
            {
                first.Dispose();
                Assert.True(ExternalProtectionPause.IsActive(name));
            }
            Assert.False(ExternalProtectionPause.IsActive(name));
        }
        Assert.False(ExternalProtectionPause.IsActive(name));
    }
}
