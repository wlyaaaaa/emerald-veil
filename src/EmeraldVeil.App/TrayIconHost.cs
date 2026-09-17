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
        _pauseItem = new Forms.ToolStripMenuItem("暂停本次会话")
        {
            Checked = controller.IsPaused,
        };
        _pauseItem.Click += (_, _) => TogglePause();

        _startAtLoginItem = new Forms.ToolStripMenuItem("登录 Windows 时启动")
        {
            Checked = startAtLogin.IsEnabled(),
        };
        _startAtLoginItem.Click += (_, _) => ToggleStartAtLogin();

        var startNowItem = new Forms.ToolStripMenuItem("立即启动屏保（Ctrl+Win+E，输入即退出）");
        startNowItem.Click += (_, _) => _controller.RequestImmediateDisplay();
        var previewItem = new Forms.ToolStripMenuItem("预览原生泡泡（最多 15 秒）");
        previewItem.Click += (_, _) => _controller.RequestPreview();

        var exitItem = new Forms.ToolStripMenuItem("退出屏保（壁纸和副屏继续运行）");
        exitItem.Click += (_, _) => _exitApplication();

        var menu = new Forms.ContextMenuStrip();
        var statusItem = new Forms.ToolStripMenuItem("查看状态与显示范围");
        statusItem.Click += (_, _) => ShowStatus();
        menu.Items.Add(statusItem);
        menu.Items.Add(startNowItem);
        menu.Items.Add(previewItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_startAtLoginItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) =>
        {
            _pauseItem.Checked = _controller.IsPaused;
            _startAtLoginItem.Checked = _startAtLogin.IsEnabled();
        };
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = $"Emerald Veil | 实体主屏空闲 {controller.ActivationDelay.TotalMinutes:0} 分钟",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => _controller.RequestImmediateDisplay();
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

    private void ShowStatus()
    {
        var status = System.Text.Json.JsonSerializer.SerializeToElement(_controller.ReadStatus());
        var presentation = status.GetProperty("presentation");
        string target = presentation.GetProperty("eligibleTarget").GetString() ?? "当前没有可用的实体主屏，保持待命";
        string state = presentation.GetProperty("state").GetString() ?? "unknown";
        string text = $"原生泡泡：{(status.GetProperty("enabled").GetBoolean() ? "已开启" : "已禁用")}\n" +
            $"会话暂停：{(_controller.IsPaused ? "是" : "否")}\n" +
            "范围：实体桌面屏；VDD 和仪表屏不显示屏保。\n" +
            $"目标：{target}\n空闲：{status.GetProperty("idleSeconds").GetDouble():0} 秒 / {_controller.ActivationDelay.TotalSeconds:0} 秒\n" +
            $"状态：{state}\n" +
            "快捷键：Ctrl+Win+E 立即启动；键鼠输入、暂停或退出均会撤掉屏保；不锁屏、不停止动态壁纸。";
        if (presentation.GetProperty("lastFailure").GetString() is { Length: > 0 } failure)
            text += "\n最近一次失败：" + failure;
        _ = System.Windows.MessageBox.Show(text, "Emerald Veil 状态", MessageBoxButton.OK, MessageBoxImage.Information);
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
