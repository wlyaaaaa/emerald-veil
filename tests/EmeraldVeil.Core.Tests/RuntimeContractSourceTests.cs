namespace EmeraldVeil.Core.Tests;

/// <summary>Architectural guards complement behavioral policy and live Windows acceptance.</summary>
public sealed class RuntimeContractSourceTests
{
    private static string Source(string file) => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "src", "EmeraldVeil.App", file));

    [Fact]
    public void BackgroundReusesVddSelectionWithoutCapturingADesktopOrEmbeddingAnotherImage()
    {
        string source = Source("VeilWindow.cs");
        int background = source.IndexOf("await UpdateBackgroundAsync(target, token)", StringComparison.Ordinal);
        int native = source.IndexOf("_nativeBubbles.Start(target.Bounds)", StringComparison.Ordinal);
        Assert.True(background >= 0 && native > background);
        Assert.DoesNotContain("WaitForBackgroundCompositionAsync", source);
        Assert.DoesNotContain("pack://", Source("VeilSurface.cs"));
        Assert.Contains("DwmRegisterThumbnail", Source("WallpaperEngineBackground.cs"));
        Assert.DoesNotContain("SetWindowLongPtr(_handle", Source("WallpaperEngineBackground.cs"));
        Assert.Contains("\"-32000\"", Source("WallpaperEngineBackground.cs"));
        Assert.DoesNotContain("CopyFromScreen", Source("VeilSurface.cs"));
        Assert.DoesNotContain("DrawEllipse", Source("VeilSurface.cs"));
        Assert.Contains("closeWallpaper", Source("WallpaperEngineBackground.cs"));
        Assert.Contains("openWallpaper", Source("WallpaperEngineBackground.cs"));
        Assert.DoesNotContain("\"-activate\"", Source("WallpaperEngineBackground.cs"));
        Assert.Contains("_launchCancellation?.Cancel()", source);
    }

    [Fact]
    public void DisplayChangesRevalidateRatherThanUnconditionallyRestarting()
    {
        string source = Source("VeilWindow.cs");
        int hook = source.IndexOf("private nint WindowMessageHook", StringComparison.Ordinal);
        string handler = source[hook..];
        Assert.Contains("_nextTargetCheck = TimeSpan.Zero", handler);
        Assert.DoesNotContain("HideVeil()", handler);
        Assert.Contains("DisplayTargetResolver.Resolve() != _activeTarget", source);
        Assert.Contains("IsPresentationActive", Source("VeilController.cs"));
        Assert.DoesNotContain("ExternalProtectionPause.IsActive()", Source("VeilController.cs"));
    }

    [Fact]
    public void RuntimeSettingsAreMaintainedWithoutAddingAnAutomaticWindowsTrigger()
    {
        string controller = Source("VeilController.cs");
        string settings = Source("NativeBubblesSettings.cs");
        Assert.Contains("RuntimePolicyMaintenanceInterval", controller);
        Assert.Contains("NativeBubblesSettings.EnsureRuntimePolicy()", controller);
        Assert.Contains("RequiredTimeoutSeconds = 360", settings);
        Assert.Contains("NativeMethods.GetScreenSaverTimeout()", settings);
        Assert.Contains("NativeMethods.GetScreenSaverSecure()", settings);
        Assert.Contains("NativeMethods.GetScreenSaverActive()", settings);
        Assert.Contains("active: false", settings);
    }

    [Fact]
    public void OwnedNativeRendererRetainsItsLifetimeAndMaintenanceBoundaries()
    {
        string launcher = Source("NativeBubblesLauncher.cs");
        Assert.Contains("maintenanceFailures >= 8", launcher);
        Assert.Contains("maintenanceFailures = 0", launcher);
        Assert.Contains("JobObjectLimitKillOnJobClose", launcher);
        Assert.Contains("IsNativeBubblesRunningInCurrentSession", launcher);
        Assert.Contains("FileShare.None", launcher);
        Assert.Contains("LastFailure", launcher);
    }

    [Fact]
    public void LocalControlIsBoundedAndUsesOnlyTheCurrentUsersSession()
    {
        string control = Source("SessionCommandChannel.cs");
        Assert.Contains("PipeOptions.CurrentUserOnly", control);
        Assert.Contains("SessionId", control);
        Assert.Contains("byte[32]", control);
        Assert.Contains("CancelAfter", control);
        Assert.Contains("65536", control);
        Assert.DoesNotContain("TcpListener", control);
        Assert.DoesNotContain("File.Write", control);
    }

    [Fact]
    public void StartupAndPreviewCannotForceAnUnrequestedOrDisabledOverlay()
    {
        Assert.Contains("command ?? \"status\"", Source("App.xaml.cs"));
        Assert.Contains("interactive-session-required", Source("App.xaml.cs"));
        Assert.DoesNotContain("dismissOnInput: false", Source("VeilController.cs"));
        Assert.DoesNotContain("if (!force", Source("VeilWindow.cs"));
        Assert.Contains("Arguments = \"/s\"", Source("NativeBubblesLauncher.cs"));
        Assert.DoesNotContain("Arguments = \"/t\"", Source("NativeBubblesLauncher.cs"));
    }

    [Fact]
    public void VisibleBackgroundIsHiddenBeforeNativeTeardownCanBlock()
    {
        string source = Source("VeilWindow.cs");
        int cleanup = source.IndexOf("private void CleanupLayers()", StringComparison.Ordinal);
        int hidden = source.IndexOf("if (IsVisible) Hide();", cleanup, StringComparison.Ordinal);
        int stop = source.IndexOf("_nativeBubbles.Stop();", cleanup, StringComparison.Ordinal);
        Assert.True(cleanup >= 0 && hidden > cleanup && stop > hidden);
    }

    [Fact]
    public void NativePlacementRechecksCancellationAndExactOwnership()
    {
        Assert.Contains("!ReferenceEquals(_process, process) || cancellationToken.IsCancellationRequested", Source("NativeBubblesLauncher.cs"));
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "src", "EmeraldVeil.App", "WallpaperEngineQuiescence.cs")));
    }

    [Fact]
    public void EmergencyDisablePreventsRelaunchBeforeCleanupAndNeverRollsBackToEnabled()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "Set-NativeBubbles.ps1"));
        int start = script.IndexOf("    'Disable' {", script.IndexOf("switch ($Action)", StringComparison.Ordinal), StringComparison.Ordinal);
        int end = script.IndexOf("    'Verify' {", start, StringComparison.Ordinal);
        string disable = script[start..end];
        Assert.True(disable.IndexOf("$watchdogEnabledName", StringComparison.Ordinal) < disable.IndexOf("Stop-NativeBubblesProcess", StringComparison.Ordinal));
        Assert.DoesNotContain("Restore-StateSnapshot", disable);
    }

    [Fact]
    public void InstallerDoesNotRollbackUsingAnUnrelatedPreviousBuild()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "Install-EmeraldVeil.ps1"));
        Assert.Contains("$replacementPerformed -and $hadTarget", script);
        Assert.Contains("SessionId -eq 0", script);
        Assert.Contains("owner == process.Id", Source("NativeBubblesLauncher.cs"));
    }

    [Fact]
    public void ForeignWallpaperNeverMovesOntoTheUsersDesktopAndOrphansHaveStartupRecovery()
    {
        string source=Source("WallpaperEngineBackground.cs");
        Assert.Contains("DwmUnregisterThumbnail", source);
        Assert.Contains("RecoverStaleAsync", Source("App.xaml.cs"));
        Assert.Contains("if (!process.HasExited) return true;", source);
        Assert.DoesNotContain("SetWindowPos(_handle", source);
        Assert.DoesNotContain("SetWindowLongPtr(_handle", source);
        Assert.DoesNotContain("Kill(entireProcessTree", source);
    }

    [Fact]
    public void NativeGlassRetainsFullCompositeInsteadOfBlackColorKey()
    {
        string launcher = Source("NativeBubblesLauncher.cs");
        Assert.Contains("NativeMethods.LwaAlpha", launcher);
        Assert.DoesNotContain("LwaColorKey", launcher);
        Assert.Contains("alpha: byte.MaxValue", launcher);
        Assert.Contains("EnsureVisualProfile", Source("VeilWindow.cs"));
        Assert.Contains("\"ShowBubbles\", \"MaterialGlass\"", Source("NativeBubblesSettings.cs"));
        Assert.Contains("key.SetValue(name, 1, RegistryValueKind.DWord)", Source("NativeBubblesSettings.cs"));
    }

    [Fact]
    public void SelectedBackgroundIsComposedBeforeNativeSnapshotAndPlaybackIsExplicit()
    {
        string source = Source("VeilWindow.cs");
        int compose = source.IndexOf("NativeMethods.DwmFlush()", StringComparison.Ordinal);
        int launch = source.IndexOf("_nativeBubbles.Start(target.Bounds)", StringComparison.Ordinal);
        Assert.True(compose >= 0 && launch > compose);
        Assert.Contains("native-entry-snapshot", source);
        Assert.Contains("CloseBackground(_engineBackground)", source);
        Assert.DoesNotContain("CopyFromScreen", Source("VeilSurface.cs"));
        Assert.DoesNotContain("DrawEllipse", Source("VeilSurface.cs"));
    }
    [Fact]
    public void IncomingWindowsCompatibilityFixesArePreserved()
    {
        string settings = Source("NativeBubblesSettings.cs");
        Assert.Contains("active || runtimeTimeout != 0", settings);
        Assert.Contains("RegistryValueKind.String", settings);
        string wallpaper = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "Set-WindowsBackground.ps1"));
        Assert.Contains("GetFolderPath('MyPictures')", wallpaper);
        Assert.Contains("return desktop.GetWallpaper(null)", wallpaper);
        Assert.DoesNotContain("0x0014", wallpaper);
    }

    [Fact]
    public void DefaultStartupIsEnabledAndGlobalHotkeyStartsTheRealScreenSaver()
    {
        string app = Source("App.xaml.cs");
        string controller = Source("VeilController.cs");
        string window = Source("VeilWindow.cs");
        string tray = Source("TrayIconHost.cs");
        Assert.Contains("RequestImmediateDisplayFromHotKeyAsync", app);
        Assert.Contains("ImmediateDisplayHotKeyPressed", app);
        Assert.Contains("ImmediateDisplayHotKeyPressed", Source("LowLevelInputObserver.cs"));
        Assert.Contains("return new nint(1)", Source("LowLevelInputObserver.cs"));
        Assert.DoesNotContain("RegisterHotKey", window);
        Assert.Contains("AreImmediateDisplayChordKeysHeld", controller);
        Assert.Contains("Ctrl+Win+E", tray);
        Assert.Contains("RequestImmediateDisplay()", tray);
        Assert.DoesNotContain("_isPaused", controller);
        Assert.DoesNotContain("SetPaused", controller);
        Assert.Contains("if (NativeBubblesSettings.IsEnabled()) _ = NativeBubblesSettings.EnsureRuntimePolicy();", app);
        Assert.DoesNotContain("\"pause\"", app);
        Assert.DoesNotContain("\"resume\"", app);
        Assert.DoesNotContain("暂停本次会话", tray);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EmeraldVeil.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Emerald Veil repository root not found.");
    }
}
