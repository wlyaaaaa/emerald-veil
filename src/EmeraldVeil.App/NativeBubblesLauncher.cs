using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EmeraldVeil.App;

/// <summary>
/// Starts the Windows-provided full-size Bubbles renderer inside the current
/// interactive desktop, then turns its top-level window into a transparent,
/// click-through overlay. Windows' own screen-saver trigger remains disabled.
/// </summary>
internal sealed class NativeBubblesLauncher : IDisposable
{
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan InitializationPollInterval = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan MaintenancePollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly int CurrentSessionId = Process.GetCurrentProcess().SessionId;
    private static readonly string SessionLeasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EmeraldVeil",
        $"native-bubbles-session-{CurrentSessionId}.lock");
    private const long RequiredExtendedStyles =
        NativeMethods.WsExTransparent |
        NativeMethods.WsExToolWindow |
        NativeMethods.WsExLayered |
        NativeMethods.WsExNoActivate;

    private readonly string _screenSaverPath = Path.Combine(
        Environment.SystemDirectory,
        "Bubbles.scr");
    private readonly object _stateLock = new();

    private Process? _process;
    private SafeFileHandle? _jobHandle;
    private FileStream? _sessionLease;
    private CancellationTokenSource? _maintenanceCancellation;
    private Task? _maintenanceTask;
    private Rectangle _physicalBounds;
    private nint _windowHandle;
    private bool _disposed;

    internal bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _process is not null && !HasExited(_process);
            }
        }
    }

    internal bool TryGetBounds(out Rectangle bounds)
    {
        lock (_stateLock)
        {
            if (_process is null || HasExited(_process))
            {
                bounds = Rectangle.Empty;
                return false;
            }

            bounds = _physicalBounds;
            return true;
        }
    }

    internal bool Start(Rectangle physicalBounds)
    {
        ThrowIfDisposed();

        if (!File.Exists(_screenSaverPath))
        {
            throw new FileNotFoundException(
                "The Windows Bubbles screen saver is not installed.",
                _screenSaverPath);
        }

        if (IsRunning)
        {
            return false;
        }

        Stop();

        Process? process = null;
        SafeFileHandle? jobHandle = null;
        FileStream? sessionLease = null;
        CancellationTokenSource? cancellation = null;
        try
        {
            sessionLease = TryAcquireSessionLease();
            if (sessionLease is null || IsNativeBubblesRunningInCurrentSession())
            {
                sessionLease?.Dispose();
                return false;
            }

            process = Process.Start(new ProcessStartInfo
            {
                FileName = _screenSaverPath,
                Arguments = "/s",
                WorkingDirectory = Path.GetDirectoryName(_screenSaverPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            }) ?? throw new InvalidOperationException("Windows Bubbles did not start.");
            jobHandle = CreateKillOnCloseJob(process);

            cancellation = new CancellationTokenSource();
            lock (_stateLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(NativeBubblesLauncher));
                }

                _process = process;
                _jobHandle = jobHandle;
                _sessionLease = sessionLease;
                _physicalBounds = physicalBounds;
                _windowHandle = nint.Zero;
                _maintenanceCancellation = cancellation;
                _maintenanceTask = Task.Run(
                    () => MaintainOverlayWindow(process, physicalBounds, cancellation.Token),
                    CancellationToken.None);
                _ = _maintenanceTask.ContinueWith(
                    _ => FinishSession(process, jobHandle, sessionLease, cancellation),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            return true;
        }
        catch
        {
            cancellation?.Cancel();
            jobHandle?.Dispose();
            TerminateProcess(process);
            sessionLease?.Dispose();
            cancellation?.Dispose();
            process?.Dispose();
            throw;
        }
    }

    internal void Restart(Rectangle physicalBounds)
    {
        ThrowIfDisposed();
        Stop();
        _ = Start(physicalBounds);
    }

    internal void Stop()
    {
        Process? process;
        SafeFileHandle? jobHandle;
        FileStream? sessionLease;
        CancellationTokenSource? cancellation;
        nint windowHandle;
        lock (_stateLock)
        {
            process = _process;
            jobHandle = _jobHandle;
            sessionLease = _sessionLease;
            cancellation = _maintenanceCancellation;
            windowHandle = _windowHandle;
            _process = null;
            _jobHandle = null;
            _sessionLease = null;
            _maintenanceCancellation = null;
            _maintenanceTask = null;
            _physicalBounds = Rectangle.Empty;
            _windowHandle = nint.Zero;
        }

        // Hiding the owned native window is synchronous and is intentionally
        // first: input must remove visible pixels without waiting for process
        // teardown or a render-thread/driver response.
        if (windowHandle != nint.Zero)
        {
            _ = NativeMethods.ShowWindow(windowHandle, NativeMethods.SwHide);
        }

        if (cancellation is not null)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE also covers an unexpected child
        // process or a launcher bookkeeping failure. Explicit termination is
        // retained as a best-effort fallback for the exact process handle.
        jobHandle?.Dispose();
        TerminateProcess(process);
        sessionLease?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void MaintainOverlayWindow(
        Process process,
        Rectangle physicalBounds,
        CancellationToken cancellationToken)
    {
        var initialization = Stopwatch.StartNew();
        bool initialized = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested && !HasExited(process))
            {
                if (initialized)
                {
                    Task.Delay(MaintenancePollInterval, cancellationToken)
                        .GetAwaiter()
                        .GetResult();
                    continue;
                }

                var candidates = EnumerateWindows(process.Id);
                var selected = SelectTargetWindow(candidates, physicalBounds);
                if (selected is not null)
                {
                    HideOtherVisibleWindows(candidates, selected.Value.Handle);
                    if (!ApplyOverlayContract(selected.Value.Handle, physicalBounds))
                    {
                        throw new InvalidOperationException(
                            "The native Bubbles window could not satisfy the overlay contract.");
                    }

                    initialized = true;
                    lock (_stateLock)
                    {
                        if (ReferenceEquals(_process, process))
                        {
                            _windowHandle = selected.Value.Handle;
                        }
                    }
                }

                if (!initialized && initialization.Elapsed >= InitializationTimeout)
                {
                    throw new TimeoutException(
                        "Windows Bubbles did not expose a usable overlay window within two seconds.");
                }

                Task.Delay(InitializationPollInterval, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Native Bubbles overlay stopped: {exception}");
            TerminateProcess(process);
        }
    }

    private void FinishSession(
        Process process,
        SafeFileHandle jobHandle,
        FileStream sessionLease,
        CancellationTokenSource cancellation)
    {
        lock (_stateLock)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
                _jobHandle = null;
                _sessionLease = null;
                _maintenanceCancellation = null;
                _maintenanceTask = null;
                _physicalBounds = Rectangle.Empty;
                _windowHandle = nint.Zero;
            }
        }

        jobHandle.Dispose();
        TerminateProcess(process);
        sessionLease.Dispose();
        cancellation.Dispose();
        process.Dispose();
    }

    private static FileStream? TryAcquireSessionLease()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SessionLeasePath)!);
        try
        {
            return new FileStream(
                SessionLeasePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            // Another process already owns the native renderer. Refuse a
            // second layer; never terminate or take over an unknown owner.
            return null;
        }
    }

    private static bool IsNativeBubblesRunningInCurrentSession()
    {
        // Windows 11 reports the native renderer as "Bubbles.scr". Keep the
        // extensionless name as a compatibility fallback for older builds.
        foreach (string processName in new[] { "Bubbles.scr", "Bubbles" })
        {
            foreach (var candidate in Process.GetProcessesByName(processName))
            {
                using (candidate)
                {
                    try
                    {
                        if (!candidate.HasExited && candidate.SessionId == CurrentSessionId)
                        {
                            // This is deliberately a refusal, not a cleanup. It
                            // covers a Windows/manual instance and an older build
                            // that predates the session lease without killing it.
                            return true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // The candidate exited while it was inspected.
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // If the current-session process cannot be inspected,
                        // fail closed and avoid creating a potentially duplicate
                        // native overlay.
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static SafeFileHandle CreateKillOnCloseJob(Process process)
    {
        var jobHandle = NativeMethods.CreateJobObject(nint.Zero, name: null);
        if (jobHandle.IsInvalid)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Unable to create the native Bubbles lifetime job.");
        }

        try
        {
            var information = new NativeMethods.JobObjectExtendedLimitInformation
            {
                BasicLimitInformation =
                {
                    LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose,
                },
            };
            if (!NativeMethods.SetInformationJobObject(
                    jobHandle,
                    NativeMethods.JobObjectExtendedLimitInformationClass,
                    ref information,
                    (uint)Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>()))
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Unable to configure the native Bubbles lifetime job.");
            }

            if (!NativeMethods.AssignProcessToJobObject(jobHandle, process.Handle))
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Unable to assign Windows Bubbles to its lifetime job.");
            }

            return jobHandle;
        }
        catch
        {
            jobHandle.Dispose();
            throw;
        }
    }

    private static IReadOnlyList<WindowCandidate> EnumerateWindows(int processId)
    {
        var candidates = new List<WindowCandidate>();
        _ = NativeMethods.EnumWindows((windowHandle, unused) =>
        {
            _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out uint ownerProcessId);
            if (ownerProcessId != processId ||
                !NativeMethods.GetWindowRect(windowHandle, out var rectangle))
            {
                return true;
            }

            var bounds = Rectangle.FromLTRB(
                rectangle.Left,
                rectangle.Top,
                rectangle.Right,
                rectangle.Bottom);
            candidates.Add(new WindowCandidate(
                windowHandle,
                bounds,
                NativeMethods.IsWindowVisible(windowHandle)));
            return true;
        }, nint.Zero);
        return candidates;
    }

    private static WindowCandidate? SelectTargetWindow(
        IReadOnlyList<WindowCandidate> candidates,
        Rectangle physicalBounds)
    {
        return candidates
            .Where(candidate => candidate.Bounds.Width > 0 && candidate.Bounds.Height > 0)
            .Select(candidate => new
            {
                Candidate = candidate,
                IntersectionArea = GetIntersectionArea(candidate.Bounds, physicalBounds),
            })
            .Where(candidate => candidate.IntersectionArea > 0)
            .OrderByDescending(candidate => candidate.IntersectionArea)
            .ThenByDescending(candidate =>
                candidate.Candidate.Bounds.Width * (long)candidate.Candidate.Bounds.Height)
            .Select(candidate => (WindowCandidate?)candidate.Candidate)
            .FirstOrDefault();
    }

    private static void HideOtherVisibleWindows(
        IReadOnlyList<WindowCandidate> candidates,
        nint selectedHandle)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Handle != selectedHandle && candidate.Visible)
            {
                _ = NativeMethods.ShowWindow(candidate.Handle, NativeMethods.SwHide);
            }
        }
    }

    private static bool ApplyOverlayContract(nint windowHandle, Rectangle physicalBounds)
    {
        long existingStyles = NativeMethods
            .GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle)
            .ToInt64();
        long updatedStyles = existingStyles | RequiredExtendedStyles;
        if (updatedStyles != existingStyles)
        {
            NativeMethods.SetWindowLongPtr(
                windowHandle,
                NativeMethods.GwlExStyle,
                new nint(updatedStyles));
        }

        if (!NativeMethods.SetLayeredWindowAttributes(
                windowHandle,
                colorKey: 0,
                alpha: byte.MaxValue,
                NativeMethods.LwaColorKey))
        {
            return false;
        }

        if (!NativeMethods.SetWindowPos(
                windowHandle,
                NativeMethods.HwndTopmost,
                physicalBounds.Left,
                physicalBounds.Top,
                physicalBounds.Width,
                physicalBounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow))
        {
            return false;
        }

        long readBackStyles = NativeMethods
            .GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle)
            .ToInt64();
        if ((readBackStyles & RequiredExtendedStyles) != RequiredExtendedStyles ||
            (readBackStyles & NativeMethods.WsExTopmost) == 0 ||
            !NativeMethods.GetWindowRect(windowHandle, out var readBackBounds))
        {
            return false;
        }

        return readBackBounds.Left == physicalBounds.Left &&
            readBackBounds.Top == physicalBounds.Top &&
            readBackBounds.Right == physicalBounds.Right &&
            readBackBounds.Bottom == physicalBounds.Bottom;
    }

    private static long GetIntersectionArea(Rectangle first, Rectangle second)
    {
        var intersection = Rectangle.Intersect(first, second);
        return intersection.Width * (long)intersection.Height;
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void TerminateProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: false);
                _ = process.WaitForExit(milliseconds: 2_000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private readonly record struct WindowCandidate(
        nint Handle,
        Rectangle Bounds,
        bool Visible);
}
