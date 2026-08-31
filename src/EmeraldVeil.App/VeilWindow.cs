using System.ComponentModel;
using System.Runtime.InteropServices;
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
        // The background must contribute real pixels above any wallpaper
        // compositor. A transparent WPF window can remain present in the
        // desktop z-order while contributing no visible surface at all.
        // Keep input pass-through in the native hook below, but use an opaque
        // window so the embedded image is the actual visual background.
        AllowsTransparency = false;
        Background = System.Windows.Media.Brushes.Black;
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

    internal bool IsVeilVisible => _nativeBubbles.IsRunning;

    internal void ShowVeil(bool force = false)
    {
        ThrowIfDisposed();

        // Tray preview is explicit. Automatic activation uses the project-owned
        // setting because Windows' own screen-saver trigger stays off.
        if (!force && !NativeBubblesSettings.IsEnabled())
        {
            return;
        }

        var targetBounds = GetTargetBounds();
        ShowBackground(targetBounds);
        try
        {
            var started = _nativeBubbles.Start(targetBounds);
            if (!started && !_nativeBubbles.IsRunning)
            {
                HideBackground();
            }
        }
        catch
        {
            HideBackground();
            throw;
        }
    }

    internal void HideVeil()
    {
        if (_disposed)
        {
            return;
        }

        _nativeBubbles.Stop();
        HideBackground();
    }

    internal NativeMethods.Rect ReadPhysicalBounds()
    {
        if (_nativeBubbles.TryGetBounds(out var hostBounds))
        {
            return new NativeMethods.Rect
            {
                Left = hostBounds.Left,
                Top = hostBounds.Top,
                Right = hostBounds.Right,
                Bottom = hostBounds.Bottom,
            };
        }

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
            | NativeMethods.WsExTransparent
            | NativeMethods.WsExNoActivate
            | NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, new nint(updated));
    }

    private static System.Drawing.Rectangle GetTargetBounds()
    {
        var target = Forms.Screen.PrimaryScreen
            ?? throw new InvalidOperationException("No primary display is available.");
        return target.Bounds;
    }

    private void ShowBackground(System.Drawing.Rectangle physicalBounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = physicalBounds.Width / dpi.DpiScaleX;
        Height = physicalBounds.Height / dpi.DpiScaleY;
        Left = physicalBounds.Left / dpi.DpiScaleX;
        Top = physicalBounds.Top / dpi.DpiScaleY;
        Opacity = 1;

        if (!IsVisible)
        {
            Show();
        }

        if (!NativeMethods.SetWindowPos(
                _windowHandle,
                NativeMethods.HwndTopmost,
                physicalBounds.Left,
                physicalBounds.Top,
                physicalBounds.Width,
                physicalBounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Unable to place the Emerald Veil background layer.");
        }

        _surface.StartAnimation();
    }

    private void HideBackground()
    {
        Opacity = 0;
        _surface.StopAnimation();
        if (IsVisible)
        {
            Hide();
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
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        var targetBounds = GetTargetBounds();
                        ShowBackground(targetBounds);
                        _nativeBubbles.Restart(targetBounds);
                    });
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
