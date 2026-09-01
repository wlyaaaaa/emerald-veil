namespace EmeraldVeil.Core.Tests;

public sealed class RuntimeContractSourceTests
{
    [Fact]
    public void Project_background_is_composed_before_native_bubbles_start()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "EmeraldVeil.App",
            "VeilWindow.cs"));

        int showBackground = source.IndexOf(
            "ShowBackground(targetBounds);",
            StringComparison.Ordinal);
        int waitForComposition = source.IndexOf(
            "WaitForBackgroundComposition();",
            showBackground,
            StringComparison.Ordinal);
        int startBubbles = source.IndexOf(
            "_nativeBubbles.Start(targetBounds)",
            waitForComposition,
            StringComparison.Ordinal);

        int pauseWallpaper = source.IndexOf(
            "WallpaperEngineQuiescence.PauseIfRunning()",
            StringComparison.Ordinal);

        Assert.True(pauseWallpaper >= 0);
        Assert.True(showBackground > pauseWallpaper);
        Assert.True(waitForComposition > showBackground);
        Assert.True(startBubbles > waitForComposition);
        Assert.Contains("DispatcherPriority.Render", source, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.DwmFlush()", source, StringComparison.Ordinal);
        Assert.Contains("WaitForWindowReady", source, StringComparison.Ordinal);
        Assert.Contains("_launchInProgress", source, StringComparison.Ordinal);
        Assert.Contains("_nativeBubbles.IsRunning", source, StringComparison.Ordinal);
        Assert.Contains("WallpaperStopSettleDelay", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay", source, StringComparison.Ordinal);
        Assert.Contains("_launchCancellation?.Cancel()", source, StringComparison.Ordinal);

        string controller = File.ReadAllText(Path.Combine(
            root,
            "src",
            "EmeraldVeil.App",
            "VeilController.cs"));
        Assert.Contains(
            "RequestExplicitDisplay(PreviewDuration, dismissOnInput: false)",
            controller,
            StringComparison.Ordinal);
        Assert.Contains(
            "RequestExplicitDisplay(timeout: null, dismissOnInput: true)",
            controller,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Watchdog_continuously_repairs_runtime_screen_saver_policy()
    {
        string root = FindRepositoryRoot();
        string controller = File.ReadAllText(Path.Combine(
            root,
            "src",
            "EmeraldVeil.App",
            "VeilController.cs"));
        string settings = File.ReadAllText(Path.Combine(
            root,
            "src",
            "EmeraldVeil.App",
            "NativeBubblesSettings.cs"));

        Assert.Contains(
            "RuntimePolicyMaintenanceInterval",
            controller,
            StringComparison.Ordinal);
        Assert.Contains(
            "NativeBubblesSettings.EnsureRuntimePolicy()",
            controller,
            StringComparison.Ordinal);
        Assert.Contains("RequiredTimeoutSeconds = 360", settings, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.GetScreenSaverTimeout()", settings, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.GetScreenSaverSecure()", settings, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.GetScreenSaverActive()", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_bubbles_reasserts_the_overlay_contract_after_initialization()
    {
        string root = FindRepositoryRoot();
        string launcher = File.ReadAllText(Path.Combine(
            root,
            "src",
            "EmeraldVeil.App",
            "NativeBubblesLauncher.cs"));

        Assert.Contains("initializedWindowHandle", launcher, StringComparison.Ordinal);
        Assert.Contains(
            "could not recover its overlay contract within two seconds",
            launcher,
            StringComparison.Ordinal);
        Assert.Contains("maintenanceFailures >= 8", launcher, StringComparison.Ordinal);
        Assert.Contains("maintenanceFailures = 0", launcher, StringComparison.Ordinal);
        Assert.True(
            launcher.Split("ApplyOverlayContract", StringSplitOptions.None).Length - 1 >= 3);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EmeraldVeil.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Emerald Veil repository root not found.");
    }
}
