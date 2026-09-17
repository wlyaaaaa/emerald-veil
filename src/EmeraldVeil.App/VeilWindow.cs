using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EmeraldVeil.Core;

namespace EmeraldVeil.App;

internal sealed class VeilWindow : Window, IDisposable
{
    private static readonly TimeSpan LayerMaintenanceInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan NativeWindowReadyTimeout = TimeSpan.FromSeconds(6);
    private readonly VeilSurface _surface = new();
    private readonly NativeBubblesLauncher _nativeBubbles = new();
    private readonly LaunchRecoveryPolicy _recovery = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _layerMaintenanceTimer;
    private readonly nint _windowHandle;
    private readonly HwndSource _windowSource;
    private bool _allowClose;
    private bool _disposed;
    private volatile bool _launchInProgress;
    private volatile bool _veilVisible;
    private CancellationTokenSource? _launchCancellation;
    private VeilDisplay? _activeTarget;
    private VeilDisplay? _lastTarget;
    private TimeSpan _shownAt;
    private TimeSpan _nextTargetCheck;
    private string _state = "waiting-for-idle";
    private string? _lastFailure;
    private int _launchCount;
    private int _failureCount;
    private VddBackground? _backgroundSource;
    private VddBackground? _lastBackgroundSource;
    private string? _lastBackgroundKind;
    private WallpaperEngineBackground? _engineBackground;
    private string? _backgroundError;
    private bool _backgroundRefreshInProgress;
    private TimeSpan _nextBackgroundCheck;
    private int _backgroundGeneration;
    private readonly List<Task> _backgroundCloses = new();

    internal VeilWindow()
    {
        Title = "Emerald Veil";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Background = System.Windows.Media.Brushes.Black;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32_000;
        Top = -32_000;
        Width = Height = 1;
        Opacity = 0;
        Content = _surface;
        _windowHandle = new WindowInteropHelper(this).EnsureHandle();
        _windowSource = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException("Unable to attach the background window.");
        _windowSource.AddHook(WindowMessageHook);
        ApplyExtendedWindowStyles();
        _layerMaintenanceTimer = new DispatcherTimer(LayerMaintenanceInterval, DispatcherPriority.Normal,
            (_, _) => MaintainBackgroundPlacement(), Dispatcher);
        _layerMaintenanceTimer.Stop();
    }

    // Snapshots are safe for the idle thread, without cross-thread WPF property access.
    internal bool IsVeilVisible => _veilVisible;
    internal bool IsPresentationActive => _veilVisible || _launchInProgress;

    internal object ReadStatus() => new
    {
        state = _state,
        visualMode = "windows-native-glass-composite",
        backgroundPlayback = "native-entry-snapshot",
        displayPolicy = "physical-desktop-only",
        eligibleTarget = DisplayTargetResolver.Resolve()?.DeviceName,
        visible = _veilVisible && (_engineBackground?.IsAlive == true || IsVisible) && _nativeBubbles.TryGetWindowHandle(out _),
        initializing = _launchInProgress,
        target = _activeTarget?.DeviceName ?? _lastTarget?.DeviceName,
        bounds = _activeTarget?.Bounds,
        nativeProcessId = _nativeBubbles.ProcessId,
        launchCount = _launchCount,
        failureCount = _failureCount,
        consecutiveFailures = _recovery.FailureCount,
        automaticRetrySuspended = _recovery.IsSuspended,
        retryAfterSeconds = _recovery.IsSuspended ? (double?)null :
            Math.Max(0, (_recovery.NextAttempt - _clock.Elapsed).TotalSeconds),
        lastFailure = _lastFailure,
        background = new {
            active = _veilVisible,
            kind = _lastBackgroundKind,
            sourceMonitor = (_backgroundSource ?? _lastBackgroundSource)?.MonitorId,
            sourceFile = (_backgroundSource ?? _lastBackgroundSource)?.Selection?.File ?? (_backgroundSource ?? _lastBackgroundSource)?.Image,
            window = _engineBackground?.WindowName,
            fallbackReason = _backgroundError,
        },
    };

    internal void ResetRecovery() => _recovery.Reset();

    internal void ShowVeil()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!NativeBubblesSettings.IsEnabled())
        {
            _state = "disabled";
            return;
        }
        if (IsPresentationActive || _backgroundRefreshInProgress) return;
        var target = DisplayTargetResolver.Resolve();
        if (target != _lastTarget)
        {
            _lastTarget = target;
            _recovery.Reset();
        }
        if (target is null)
        {
            _state = "no-eligible-physical-display";
            return;
        }
        if (!_recovery.CanAttempt(_clock.Elapsed))
        {
            _state = _recovery.IsSuspended ? "waiting-for-input-or-manual-retry" : "retry-cooldown";
            return;
        }
        _activeTarget = target;
        _launchInProgress = true;
        _state = "initializing";
        _launchCount++;
        _launchCancellation = new CancellationTokenSource();
        _ = ShowVeilAsync(target, _launchCancellation);
    }

    private async Task ShowVeilAsync(VeilDisplay target, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            NativeBubblesSettings.EnsureVisualProfile();
            await UpdateBackgroundAsync(target, token);
            // Bubbles takes its own desktop snapshot. Finish the selected VDD
            // material on the physical target before starting the native child.
            await Dispatcher.InvokeAsync(() => _surface.InvalidateVisual(), DispatcherPriority.Render, token);
            await Task.Delay(50, token);
            int compositionResult = NativeMethods.DwmFlush();
            if (compositionResult < 0) Marshal.ThrowExceptionForHR(compositionResult);
            token.ThrowIfCancellationRequested();
            if (!_nativeBubbles.Start(target.Bounds))
                throw new InvalidOperationException("Another native Bubbles instance owns this session.");
            bool ready = await Task.Run(() => _nativeBubbles.WaitForWindowReady(NativeWindowReadyTimeout, token), token);
            token.ThrowIfCancellationRequested();
            if (!ready) throw new InvalidOperationException(_nativeBubbles.LastFailure ?? "Native Bubbles did not establish a usable window.");
            if (DisplayTargetResolver.Resolve() != target) throw new OperationCanceledException("The target changed during initialization.");
            _veilVisible = true;
            _shownAt = _clock.Elapsed;
            _state = "visible";
            _layerMaintenanceTimer.Start();
            MaintainBackgroundPlacement();
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_launchCancellation, cancellation)) { CleanupLayers(); _state = "waiting-for-idle"; }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_launchCancellation, cancellation)) RecordFailure(exception.Message);
        }
        finally { _launchInProgress = false; }
    }

    private async Task UpdateBackgroundAsync(VeilDisplay target, CancellationToken token)
    {
        if (_backgroundRefreshInProgress) return;
        _backgroundRefreshInProgress = true;
        int generation = _backgroundGeneration;
        WallpaperEngineBackground? next = null;
        try
        {
            var source = VddBackgroundSource.Read();
            if (source == _backgroundSource && (_engineBackground is null || _engineBackground.IsAlive)) return;
            _surface.Load(source);
            string? error = null;
            if (source.Selection is not null)
            {
                next = new WallpaperEngineBackground(source);
                try { await next.OpenAsync(_windowHandle, target.Bounds, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { error = exception.Message; CloseBackground(next); next = null; }
            }
            token.ThrowIfCancellationRequested();
            if (generation != _backgroundGeneration) throw new OperationCanceledException(token);
            ShowBackground(target.Bounds);
            if (next is not null)
            {
                next.UpdateSize(target.Bounds);
            }
            var previous = _engineBackground;
            _engineBackground = next;
            next = null;
            _backgroundSource = source;
            _lastBackgroundSource = source;
            _lastBackgroundKind = _engineBackground is null ? "windows-vdd" : "wallpaper-engine-vdd";
            _backgroundError = error;
            CloseBackground(previous);
        }
        finally { CloseBackground(next); _backgroundRefreshInProgress = false; }
    }

    private void CloseBackground(WallpaperEngineBackground? background)
    {
        if (background is null) return;
        background.Dispose();
        _backgroundCloses.RemoveAll(task => task.IsCompleted);
        _backgroundCloses.Add(background.Closed);
    }

    private async Task RefreshBackgroundAsync(VeilDisplay target, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            // Only a real selected material/engine-state change recaptures the
            // native background. A polling tick never restarts the presentation.
            var source = VddBackgroundSource.Read();
            if (source != _backgroundSource && !token.IsCancellationRequested)
            {
                HideVeil();
                _state = "background-source-changed";
            }
            await Task.CompletedTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!token.IsCancellationRequested) _backgroundError = exception.Message; }
    }

    private void RecordFailure(string reason)
    {
        CleanupLayers();
        _lastFailure = reason;
        _failureCount++;
        _recovery.RecordFailure(_clock.Elapsed);
        _state = _recovery.IsSuspended ? "waiting-for-input-or-manual-retry" : "retry-cooldown";
    }

    internal void HideVeil()
    {
        if (_disposed) return;
        CleanupLayers();
        _recovery.Reset();
        _state = "waiting-for-idle";
    }

    private void CleanupLayers()
    {
        _backgroundGeneration++;
        _launchCancellation?.Cancel();
        _launchCancellation?.Dispose();
        _launchCancellation = null;
        _veilVisible = false;
        _layerMaintenanceTimer.Stop();
        Opacity = 0;
        _surface.StopAnimation();
        if (IsVisible) Hide();
        _activeTarget = null;
        CloseBackground(_engineBackground);
        _engineBackground = null;
        _backgroundSource = null;
        // Input removes the owned background before any native process teardown.
        _nativeBubbles.Stop();
    }

    internal NativeMethods.Rect ReadPhysicalBounds()
    {
        _ = NativeMethods.GetWindowRect(_windowHandle, out var bounds);
        return bounds;
    }

    public void Dispose()
    {
        if (_disposed) return;
        HideVeil();
        _disposed = true;
        _nativeBubbles.Dispose();
        try { Task.WhenAll(_backgroundCloses).Wait(TimeSpan.FromSeconds(4)); } catch { }
        _windowSource.RemoveHook(WindowMessageHook);
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; HideVeil(); return; }
        base.OnClosing(e);
    }

    private void ApplyExtendedWindowStyles()
    {
        long current = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle,
            new nint(current | NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
    }

    private void ShowBackground(System.Drawing.Rectangle physicalBounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = physicalBounds.Width / dpi.DpiScaleX;
        Height = physicalBounds.Height / dpi.DpiScaleY;
        Left = physicalBounds.Left / dpi.DpiScaleX;
        Top = physicalBounds.Top / dpi.DpiScaleY;
        Opacity = 1;
        if (!IsVisible) Show();
        PlaceBackground(NativeMethods.HwndTopmost, physicalBounds);
        _surface.StartAnimation();
        _nextTargetCheck = _clock.Elapsed;
        _layerMaintenanceTimer.Start();
    }

    private void PlaceBackground(nint insertAfter, System.Drawing.Rectangle bounds)
    {
        if (!NativeMethods.SetWindowPos(_windowHandle, insertAfter, bounds.Left, bounds.Top,
                bounds.Width, bounds.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to place the background layer.");
    }

    private void MaintainBackgroundPlacement()
    {
        if (_disposed || _launchInProgress || !_veilVisible || _activeTarget is null) return;
        try
        {
            if (_clock.Elapsed >= _nextTargetCheck)
            {
                _nextTargetCheck = _clock.Elapsed + TimeSpan.FromMilliseconds(500);
                if (DisplayTargetResolver.Resolve() != _activeTarget)
                {
                    HideVeil();
                    _state = "display-target-changed";
                    return;
                }
            }
            if (!_launchInProgress && !_nativeBubbles.TryGetWindowHandle(out _))
            {
                RecordFailure(_nativeBubbles.LastFailure ?? "Native Bubbles exited unexpectedly.");
                return;
            }
            nint insertAfter = _nativeBubbles.TryGetWindowHandle(out var handle) ? handle : NativeMethods.HwndTopmost;
            if (_engineBackground?.IsAlive == true)
            {
                _engineBackground.UpdateSize(_activeTarget.Bounds);
                PlaceBackground(insertAfter, _activeTarget.Bounds);
            }
            else if (IsVisible) PlaceBackground(insertAfter, _activeTarget.Bounds);
            if (_clock.Elapsed >= _nextBackgroundCheck && _launchCancellation is not null)
            {
                _nextBackgroundCheck = _clock.Elapsed + TimeSpan.FromSeconds(1);
                _ = RefreshBackgroundAsync(_activeTarget, _launchCancellation.Token);
            }
            if (_veilVisible && _clock.Elapsed - _shownAt >= TimeSpan.FromSeconds(30)) _recovery.Reset();
        }
        catch (Exception exception) { RecordFailure(exception.Message); }
    }

    private nint WindowMessageHook(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmNchittest) { handled = true; return new nint(NativeMethods.HtTransparent); }
        if (message == NativeMethods.WmMouseActivate) { handled = true; return new nint(NativeMethods.MaNoActivate); }
        if (message is NativeMethods.WmDisplayChange or NativeMethods.WmDpiChanged)
        {
            // WPF receives DPI notifications during its own placement. Revalidate
            // the target instead of repeatedly tearing down that valid launch.
            _nextTargetCheck = TimeSpan.Zero;
        }
        return nint.Zero;
    }
}
