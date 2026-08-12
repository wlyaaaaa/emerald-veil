using System.Diagnostics;
using System.Windows.Threading;
using EmeraldVeil.Core;

namespace EmeraldVeil.App;

internal sealed class VeilController : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PreviewDuration = TimeSpan.FromSeconds(15);

    private readonly VeilWindow _window;
    private readonly IIdleInputSource _inputSource;
    private readonly IdleTimeline _timeline = new();
    private readonly VeilActivationPolicy _policy;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _stateLock = new();

    private Task? _monitorTask;
    private bool _isPaused;
    private bool _previewRequested;
    private uint _previewBaselineInputTick;
    private ulong _previewDeadlineTick64;
    private VeilMode _lastMode = VeilMode.Hidden;

    internal VeilController(
        VeilWindow window,
        IIdleInputSource inputSource,
        TimeSpan activationDelay)
    {
        _window = window;
        _inputSource = inputSource;
        _policy = new VeilActivationPolicy(activationDelay);
    }

    internal bool IsPaused
    {
        get
        {
            lock (_stateLock)
            {
                return _isPaused;
            }
        }
    }

    internal TimeSpan ActivationDelay => _policy.ActivationDelay;

    internal void Start()
    {
        _monitorTask ??= Task.Run(MonitorLoopAsync);
    }

    internal void SetPaused(bool paused)
    {
        lock (_stateLock)
        {
            _isPaused = paused;
        }

        if (paused)
        {
            QueueMode(VeilMode.Hidden);
        }
    }

    internal void RequestPreview()
    {
        var sample = _inputSource.Read();
        lock (_stateLock)
        {
            _previewRequested = true;
            _previewBaselineInputTick = sample.LastInputTick32;
            _previewDeadlineTick64 = sample.CurrentTick64 + (ulong)PreviewDuration.TotalMilliseconds;
        }

        QueueMode(VeilMode.Preview);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation.Dispose();
    }

    private async Task MonitorLoopAsync()
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(_cancellation.Token).ConfigureAwait(false))
        {
            var sample = _inputSource.Read();
            var observation = _timeline.Observe(
                sample.Succeeded,
                sample.CurrentTick32,
                sample.CurrentTick64,
                sample.LastInputTick32);

            bool paused;
            bool preview;
            lock (_stateLock)
            {
                if (_previewRequested &&
                    (sample.LastInputTick32 != _previewBaselineInputTick ||
                     sample.CurrentTick64 >= _previewDeadlineTick64))
                {
                    _previewRequested = false;
                }

                paused = _isPaused;
                preview = _previewRequested;
            }

            QueueMode(_policy.Evaluate(observation, paused, preview));
        }
    }

    private void QueueMode(VeilMode mode)
    {
        lock (_stateLock)
        {
            if (mode == _lastMode)
            {
                return;
            }

            _lastMode = mode;
        }

        if (_window.Dispatcher.CheckAccess())
        {
            ApplyMode(mode);
            return;
        }

        _ = _window.Dispatcher.InvokeAsync(
            () => ApplyMode(mode),
            DispatcherPriority.Send);
    }

    private void ApplyMode(VeilMode mode)
    {
        try
        {
            if (mode == VeilMode.Hidden)
            {
                _window.HideVeil();
            }
            else
            {
                _window.ShowVeil(force: mode == VeilMode.Preview);
            }
        }
        catch (Exception exception)
        {
            // Rendering failures are fail-closed: do not leave a native
            // screen-saver window behind or crash the silent watchdog.
            Debug.WriteLine($"Unable to apply veil mode {mode}: {exception}");
            _window.HideVeil();
        }
    }
}
