using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace EmeraldVeil.App;

internal sealed class VeilWindow : Window, IDisposable
{
    private readonly VeilSurface _surface = new();
    private readonly NativeBubblesLauncher _nativeBubbles = new();
    private readonly nint _windowHandle;
    private readonly HwndSource _windowSource;
    private bool _allowClose;
    private bool _disposed;

    internal VeilWindow()
    {
        Title = "Emerald Veil";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32_000;
        Top = -32_000;
        Width = 1;
        Height = 1;
        Opacity = 0;
        Content = _surface;

        _windowHandle = new WindowInteropHelper(this).EnsureHandle();
        _windowSource = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException("Unable to attach to the veil window handle.");
        _windowSource.AddHook(WindowMessageHook);
        ApplyExtendedWindowStyles();
    }

    internal bool IsVeilVisible => IsVisible && Opacity > 0;

    internal void ShowVeil(bool force = false)
    {
        ThrowIfDisposed();

        // Preview is explicit. Automatic activation also has a separate
        // watchdog, but this guard makes Disable effective immediately even
        // if one final controller tick is already queued on the dispatcher.
        if (!force && !NativeMethods.GetScreenSaverActive())
        {
            return;
        }

        _nativeBubbles.Start();
    }

    internal void HideVeil()
    {
        if (_disposed)
        {
            return;
        }

        _nativeBubbles.Stop();
        if (IsVisible)
        {
            Opacity = 0;
            _surface.StopAnimation();
            Hide();
        }
    }

    internal NativeMethods.Rect ReadPhysicalBounds()
    {
        _ = NativeMethods.GetWindowRect(_windowHandle, out var bounds);
        return bounds;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _nativeBubbles.Dispose();
        _surface.StopAnimation();
        _windowSource.RemoveHook(WindowMessageHook);
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideVeil();
            return;
        }

        base.OnClosing(e);
    }

    private void ApplyExtendedWindowStyles()
    {
        var current = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        var updated = current
            | NativeMethods.WsExLayered
            | NativeMethods.WsExTransparent
            | NativeMethods.WsExNoActivate
            | NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, new nint(updated));
    }

    private void PositionOnTargetDisplay()
    {
        var target = Forms.Screen.PrimaryScreen
            ?? throw new InvalidOperationException("No primary display is available.");
        var bounds = target.Bounds;

        if (!NativeMethods.SetWindowPos(
                _windowHandle,
                NativeMethods.HwndTopmost,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow))
        {
            throw new Win32Exception();
        }
    }

    private nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        switch (message)
        {
            case NativeMethods.WmNchittest:
                handled = true;
                return new nint(NativeMethods.HtTransparent);

            case NativeMethods.WmMouseActivate:
                handled = true;
                return new nint(NativeMethods.MaNoActivate);

            case NativeMethods.WmDisplayChange:
            case NativeMethods.WmDpiChanged:
                if (IsVeilVisible)
                {
                    _ = Dispatcher.BeginInvoke(PositionOnTargetDisplay);
                }

                break;
        }

        return nint.Zero;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
