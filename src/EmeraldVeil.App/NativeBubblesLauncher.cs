using System.Diagnostics;
using System.IO;

namespace EmeraldVeil.App;

/// <summary>
/// Starts and stops only the Windows-provided Bubbles screen saver.
/// The path check is intentional: a same-named third-party process is never
/// treated as owned by Emerald Veil.
/// </summary>
internal sealed class NativeBubblesLauncher : IDisposable
{
    private readonly string _screenSaverPath = Path.Combine(
        Environment.SystemDirectory,
        "Bubbles.scr");

    private bool _disposed;

    internal string ScreenSaverPath => _screenSaverPath;

    internal bool Start()
    {
        ThrowIfDisposed();

        if (!File.Exists(_screenSaverPath))
        {
            throw new FileNotFoundException(
                "The Windows Bubbles screen saver is not installed.",
                _screenSaverPath);
        }

        if (FindOwnedProcesses().Count > 0)
        {
            return false;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = _screenSaverPath,
            Arguments = "/s",
            WorkingDirectory = Path.GetDirectoryName(_screenSaverPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

        if (process is null)
        {
            throw new InvalidOperationException("Windows Bubbles did not start.");
        }

        return true;
    }

    internal void Stop()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var process in FindOwnedProcesses())
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: false);
                        process.WaitForExit(1000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The saver exited between enumeration and the stop call.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // A process that is no longer accessible is not ours to force.
                }
            }
        }
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

    private List<Process> FindOwnedProcesses()
    {
        var owned = new List<Process>();
        foreach (var process in EnumerateBubblesProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null &&
                    string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(_screenSaverPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    owned.Add(process);
                    continue;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access-denied processes are deliberately ignored.
            }
            catch (InvalidOperationException)
            {
                // The process exited during inspection.
            }

            process.Dispose();
        }

        return owned;
    }

    private static IEnumerable<Process> EnumerateBubblesProcesses()
    {
        foreach (var name in new[] { "Bubbles.scr", "Bubbles" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                yield return process;
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
