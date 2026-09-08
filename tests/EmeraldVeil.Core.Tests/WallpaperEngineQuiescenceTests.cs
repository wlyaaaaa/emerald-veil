using System.Reflection;
using EmeraldVeil.App;

namespace EmeraldVeil.Core.Tests;

public sealed class WallpaperEngineQuiescenceTests
{
    [Fact]
    public void Failed_control_command_is_not_reported_as_success()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // where.exe rejects the control client's arguments and exits nonzero.
        // This exercises the real process-result path without starting or
        // controlling Wallpaper Engine or the display.
        var harmlessCommand = Path.Combine(Environment.SystemDirectory, "where.exe");
        Assert.True(File.Exists(harmlessCommand));
        var invokeControl = typeof(WallpaperEngineQuiescence).GetMethod(
            "InvokeControl", BindingFlags.NonPublic | BindingFlags.Static)!;

        var error = Assert.Throws<TargetInvocationException>(() =>
            invokeControl.Invoke(null, [harmlessCommand, "stop"]));
        var failure = Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Contains("stop", failure.Message, StringComparison.Ordinal);
        Assert.Contains("exit code", failure.Message, StringComparison.Ordinal);
    }
}
