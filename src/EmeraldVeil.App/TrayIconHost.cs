using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using Forms = System.Windows.Forms;

namespace EmeraldVeil.App;

internal sealed class TrayIconHost : IDisposable
{
    private readonly VeilController _controller;
    private readonly StartAtLoginService _startAtLogin;
    private readonly Action _exitApplication;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _startAtLoginItem;
    private bool _disposed;

    internal TrayIconHost(
        VeilController controller,
        StartAtLoginService startAtLogin,
        Action exitApplication)
    {
        _controller = controller;
        _startAtLogin = startAtLogin;
        _exitApplication = exitApplication;

        _icon = CreateEmeraldIcon();
        _pauseItem = new Forms.ToolStripMenuItem("Pause protection")
        {
            Checked = controller.IsPaused,
        };
        _pauseItem.Click += (_, _) => TogglePause();

        _startAtLoginItem = new Forms.ToolStripMenuItem("Start with Windows")
        {
            Checked = startAtLogin.IsEnabled(),
        };
        _startAtLoginItem.Click += (_, _) => ToggleStartAtLogin();

        var previewItem = new Forms.ToolStripMenuItem("Preview for 15 seconds");
        previewItem.Click += (_, _) => _controller.RequestPreview();

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => _exitApplication();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(previewItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_startAtLoginItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = $"Emerald Veil — activates after {controller.ActivationDelay.TotalMinutes:0} minutes",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => _controller.RequestPreview();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private void TogglePause()
    {
        var paused = !_controller.IsPaused;
        _controller.SetPaused(paused);
        _pauseItem.Checked = paused;
    }

    private void ToggleStartAtLogin()
    {
        try
        {
            if (_startAtLogin.IsEnabled())
            {
                _startAtLogin.Disable();
            }
            else
            {
                _startAtLogin.Enable();
            }

            _startAtLoginItem.Checked = _startAtLogin.IsEnabled();
        }
        catch (Exception exception)
        {
            _startAtLoginItem.Checked = _startAtLogin.IsEnabled();
            _ = System.Windows.MessageBox.Show(
                exception.Message,
                "Emerald Veil startup setting",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static Icon CreateEmeraldIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var outer = new SolidBrush(Color.FromArgb(255, 4, 28, 15));
            using var inner = new SolidBrush(Color.FromArgb(255, 0, 213, 96));
            graphics.FillEllipse(outer, 2, 2, 28, 28);
            graphics.FillEllipse(inner, 8, 6, 15, 20);
            graphics.FillEllipse(outer, 13, 9, 12, 15);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            _ = NativeMethods.DestroyIcon(handle);
        }
    }
}
