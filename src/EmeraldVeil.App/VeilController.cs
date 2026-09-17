using System.Diagnostics;
using System.Windows.Threading;
using EmeraldVeil.Core;

namespace EmeraldVeil.App;

internal sealed class VeilController : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PreviewDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RendererRecoveryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RuntimePolicyMaintenanceInterval = TimeSpan.FromSeconds(30);
    private readonly VeilWindow _window;
    private readonly IIdleInputSource _inputSource;
    private readonly IdleTimeline _timeline = new();
    private readonly VeilActivationPolicy _policy;
    private readonly VeilModeReconciler _reconciler = new(RendererRecoveryDelay, Stopwatch.Frequency);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _stateLock = new();
    private Task? _monitorTask;
    private bool _hotKeyAvailable;
    private string? _hotKeyError;
    private readonly PreviewSession _preview = new();
    private ulong _suppressIdleUntilTick64;
    private ulong _nextRuntimePolicyCheckTick64;
    private IdleObservation _lastObservation = new(false, TimeSpan.Zero);
    private string? _lastMonitorError;
    private VeilMode _desiredMode;
    private bool _applyQueued;

    internal VeilController(VeilWindow window, IIdleInputSource inputSource, TimeSpan activationDelay)
    {
        _window = window;
        _inputSource = inputSource;
        _policy = new VeilActivationPolicy(activationDelay);
        _nextRuntimePolicyCheckTick64 = NativeMethods.GetTickCount64() +
            (ulong)RuntimePolicyMaintenanceInterval.TotalMilliseconds;
    }

    internal TimeSpan ActivationDelay => _policy.ActivationDelay;
    internal void SetHotKeyAvailability(bool available, string? error)
    {
        lock (_stateLock)
        {
            _hotKeyAvailable = available;
            _hotKeyError = error;
        }
    }
    internal void Start() => _monitorTask ??= Task.Run(MonitorLoopAsync);

    internal object ReadStatus()
    {
        lock (_stateLock)
        {
            return new
            {
                schema = "emerald-veil.status.v1",
                version = typeof(VeilController).Assembly.GetName().Version?.ToString(),
                processId = Environment.ProcessId,
                enabled = NativeBubblesSettings.IsEnabled(),
                activationSeconds = ActivationDelay.TotalSeconds,
                idleSeconds = _lastObservation.IdleDuration.TotalSeconds,
                inputReliable = _lastObservation.IsReliable,
                desiredMode = _desiredMode.ToString(),
                lastMonitorError = _lastMonitorError,
                immediateHotKey = "Ctrl+Win+E",
                hotKeyAvailable = _hotKeyAvailable,
                hotKeyError = _hotKeyError,
                presentation = _window.ReadStatus(),
            };
        }
    }

    internal void RequestPreview() => RequestExplicitDisplay(PreviewDuration);
    internal void RequestImmediateDisplay() => RequestExplicitDisplay(timeout: null);

    internal async Task RequestImmediateDisplayFromHotKeyAsync()
    {
        try
        {
            ulong deadline = NativeMethods.GetTickCount64() + 3_000;
            while (NativeMethods.AreImmediateDisplayChordKeysHeld() && NativeMethods.GetTickCount64() < deadline)
                await Task.Delay(20, _cancellation.Token).ConfigureAwait(true);

            // Low-level input and GetLastInputInfo can settle on adjacent scheduler
            // turns, and an older owner of this chord may also receive key-up. Do
            // not baseline the screen saver until input has stayed unchanged for
            // a short bounded quiet period after every chord key is released.
            uint stableTick = 0;
            ulong stableSince = 0;
            while (NativeMethods.GetTickCount64() < deadline)
            {
                var sample = _inputSource.Read();
                if (!sample.Succeeded) return;
                if (sample.LastInputTick32 != stableTick)
                {
                    stableTick = sample.LastInputTick32;
                    stableSince = sample.CurrentTick64;
                }
                if (sample.CurrentTick64 - stableSince >= 250) break;
                await Task.Delay(20, _cancellation.Token).ConfigureAwait(true);
            }
            RequestImmediateDisplay();
        }
        catch (OperationCanceledException) { }
    }

    private void RequestExplicitDisplay(TimeSpan? timeout)
    {
        var sample = _inputSource.Read();
        lock (_stateLock)
        {
            if (!sample.Succeeded || !NativeBubblesSettings.IsEnabled()) return;
            _preview.Request(sample.LastInputTick32, sample.CurrentTick64, timeout);
            // A command without local input must not turn an expired preview into another idle launch.
            _suppressIdleUntilTick64 = sample.CurrentTick64 + (ulong)ActivationDelay.TotalMilliseconds;
        }
        _window.ResetRecovery();
        QueueMode(VeilMode.Preview);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_monitorTask is not null)
        {
            try { await _monitorTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cancellation.Dispose();
    }

    private async Task MonitorLoopAsync()
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(_cancellation.Token).ConfigureAwait(false))
        {
            try
            {
                var sample = _inputSource.Read();
                var observation = _timeline.Observe(sample.Succeeded, sample.CurrentTick32,
                    sample.CurrentTick64, sample.LastInputTick32);
                MaintainRuntimePolicy(sample.CurrentTick64);
                bool suppressed;
                bool preview;
                lock (_stateLock)
                {
                    _lastObservation = observation;
                    suppressed = !NativeBubblesSettings.IsEnabled();
                    preview = _preview.Observe(sample.Succeeded, sample.LastInputTick32,
                        sample.CurrentTick64, allowed: !suppressed);
                    suppressed |= !preview && sample.CurrentTick64 < _suppressIdleUntilTick64;
                }
                // Display-specific blackout exclusion belongs to the display resolver;
                // it must not freeze the shared user-session idle clock.
                QueueMode(_policy.Evaluate(observation, suppressed, preview));
            }
            catch (Exception exception)
            {
                lock (_stateLock) { _lastMonitorError = exception.Message; }
                QueueMode(VeilMode.Hidden);
            }
        }
    }

    private void MaintainRuntimePolicy(ulong currentTick64)
    {
        if (currentTick64 < _nextRuntimePolicyCheckTick64) return;
        _nextRuntimePolicyCheckTick64 = currentTick64 + (ulong)RuntimePolicyMaintenanceInterval.TotalMilliseconds;
        try { _ = NativeBubblesSettings.EnsureRuntimePolicy(); }
        catch (Exception exception)
        {
            lock (_stateLock) { _lastMonitorError = exception.Message; }
        }
    }

    private void QueueMode(VeilMode mode)
    {
        lock (_stateLock)
        {
            _desiredMode = mode;
            if (!_reconciler.ShouldApply(mode, _window.IsPresentationActive, Stopwatch.GetTimestamp()) || _applyQueued)
                return;
            _applyQueued = true;
        }
        if (_window.Dispatcher.CheckAccess()) ApplyLatestMode();
        else _ = _window.Dispatcher.InvokeAsync(ApplyLatestMode, DispatcherPriority.Send);
    }

    private void ApplyLatestMode()
    {
        VeilMode mode;
        lock (_stateLock)
        {
            _applyQueued = false;
            mode = _desiredMode;
        }
        try
        {
            if (mode == VeilMode.Hidden) _window.HideVeil();
            else _window.ShowVeil();
        }
        catch (Exception exception)
        {
            lock (_stateLock) { _lastMonitorError = exception.Message; }
            _window.HideVeil();
        }
    }
}
