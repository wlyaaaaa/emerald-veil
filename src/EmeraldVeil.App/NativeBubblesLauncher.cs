using System.Diagnostics;
using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace EmeraldVeil.App;

/// <summary>
/// Hosts the Windows-provided Bubbles renderer in preview mode. Preview mode
/// is a normal desktop window, not a Windows screen-saver session, so remote
/// desktop/capture products keep their normal session and display pipeline.
/// </summary>
internal sealed class NativeBubblesLauncher : IDisposable
{
    private readonly string _screenSaverPath = Path.Combine(
        Environment.SystemDirectory,
        "Bubbles.scr");

    private BubblesPreviewHost? _host;
    private Process? _process;
    private bool _disposed;

    internal bool IsRunning =>
        _host is not null &&
        _process is not null &&
        !_process.HasExited;

    internal bool TryGetBounds(out Rectangle bounds)
    {
        if (_host is null || _host.IsDisposed)
        {
            bounds = Rectangle.Empty;
            return false;
        }

        bounds = _host.Bounds;
        return true;
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

        var host = new BubblesPreviewHost(physicalBounds);
        try
        {
            host.Show();
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = _screenSaverPath,
                Arguments = $"/p {host.Handle.ToInt64()}",
                WorkingDirectory = Path.GetDirectoryName(_screenSaverPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            }) ?? throw new InvalidOperationException("Windows Bubbles did not start.");

            _host = host;
            _process = process;
            return true;
        }
        catch
        {
            host.Close();
            host.Dispose();
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
        var process = _process;
        _process = null;
        if (process is not null)
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
                    // The preview renderer exited between checks.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // A process that is no longer accessible is not retried.
                }
            }
        }

        var host = _host;
        _host = null;
        if (host is not null)
        {
            host.Close();
            host.Dispose();
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class BubblesPreviewHost : Forms.Form
    {
        internal BubblesPreviewHost(Rectangle physicalBounds)
        {
            Text = "Emerald Veil Native Bubbles Host";
            AutoScaleMode = Forms.AutoScaleMode.None;
            FormBorderStyle = Forms.FormBorderStyle.None;
            StartPosition = Forms.FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = physicalBounds;
            BackColor = Color.Black;
            TransparencyKey = Color.Black;
        }

        protected override bool ShowWithoutActivation => true;

        protected override Forms.CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= checked((int)(
                    NativeMethods.WsExTransparent |
                    NativeMethods.WsExToolWindow |
                    NativeMethods.WsExLayered |
                    NativeMethods.WsExNoActivate));
                return parameters;
            }
        }

        protected override void WndProc(ref Forms.Message message)
        {
            switch (message.Msg)
            {
                case NativeMethods.WmNchittest:
                    message.Result = new nint(NativeMethods.HtTransparent);
                    return;

                case NativeMethods.WmMouseActivate:
                    message.Result = new nint(NativeMethods.MaNoActivate);
                    return;
            }

            base.WndProc(ref message);
        }
    }
}
