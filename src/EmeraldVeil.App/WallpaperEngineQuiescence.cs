using System.Diagnostics;
using System.IO;

namespace EmeraldVeil.App;

internal sealed class WallpaperEngineQuiescence : IDisposable
{
    private static readonly TimeSpan ControlClientTimeout = TimeSpan.FromSeconds(3);
    private readonly string _controlPath;
    private bool _resumed;

    private WallpaperEngineQuiescence(string controlPath)
    {
        _controlPath = controlPath;
    }

    internal static WallpaperEngineQuiescence? PauseIfRunning()
    {
        int sessionId = Process.GetCurrentProcess().SessionId;
        using var candidates = new ProcessCollection(Process.GetProcessesByName("wallpaper64"));
        var currentSession = candidates.Processes
            .Where(process => TryGetSessionId(process) == sessionId)
            .ToArray();
        if (currentSession.Length == 0)
        {
            return null;
        }

        if (currentSession.Length != 1)
        {
            throw new InvalidOperationException(
                "Wallpaper Engine process identity is ambiguous in the current session.");
        }

        string? enginePath;
        try
        {
            enginePath = currentSession[0].MainModule?.FileName;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Unable to resolve the current Wallpaper Engine executable.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(enginePath))
        {
            throw new InvalidOperationException(
                "The current Wallpaper Engine executable path is unavailable.");
        }

        string controlPath = Path.Combine(
            Path.GetDirectoryName(enginePath)!,
            "wallpaper32.exe");
        if (!File.Exists(controlPath))
        {
            throw new FileNotFoundException(
                "Wallpaper Engine control client is missing.",
                controlPath);
        }

        InvokeControl(controlPath, "stop");
        return new WallpaperEngineQuiescence(controlPath);
    }

    internal void Resume()
    {
        if (_resumed)
        {
            return;
        }

        InvokeControl(_controlPath, "play");
        _resumed = true;
    }

    public void Dispose()
    {
        Resume();
    }

    private static void InvokeControl(string controlPath, string command)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = controlPath,
            Arguments = $"-control {command}",
            WorkingDirectory = Path.GetDirectoryName(controlPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        }) ?? throw new InvalidOperationException(
            $"Wallpaper Engine control command did not start: {command}.");
        if (!process.WaitForExit((int)ControlClientTimeout.TotalMilliseconds))
        {
            throw new TimeoutException(
                $"Wallpaper Engine control command timed out: {command}.");
        }
    }

    private static int? TryGetSessionId(Process process)
    {
        try
        {
            return process.SessionId;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed class ProcessCollection : IDisposable
    {
        internal ProcessCollection(Process[] processes)
        {
            Processes = processes;
        }

        internal Process[] Processes { get; }

        public void Dispose()
        {
            foreach (var process in Processes)
            {
                process.Dispose();
            }
        }
    }
}
