using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using Forms = System.Windows.Forms;

namespace EmeraldVeil.App;

internal sealed class TrayIconHost : IDisposable
{
    private readonly VeilController _controller;
    private readonly StartAtLoginService _startAtLogin;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly Forms.ToolStripMenuItem _startAtLoginItem;
    private bool _disposed;

    internal TrayIconHost(
        VeilController controller,
        StartAtLoginService startAtLogin)
    {
        _controller = controller;
        _startAtLogin = startAtLogin;

        _icon = CreateEmeraldIcon();
        _startAtLoginItem = new Forms.ToolStripMenuItem("登录 Windows 时启动")
        {
            Checked = startAtLogin.IsEnabled(),
        };
        _startAtLoginItem.Click += (_, _) => ToggleStartAtLogin();

        var startNowItem = new Forms.ToolStripMenuItem("立即启动屏保（Ctrl+Win+E，输入即退出）");
        startNowItem.Click += (_, _) => _controller.RequestImmediateDisplay();
        var previewItem = new Forms.ToolStripMenuItem("预览原生泡泡（最多 15 秒）");
        previewItem.Click += (_, _) => _controller.RequestPreview();


        var menu = new Forms.ContextMenuStrip();
        var statusItem = new Forms.ToolStripMenuItem("查看状态与显示范围");
        statusItem.Click += (_, _) => ShowStatus();
        menu.Items.Add(statusItem);
        menu.Items.Add(startNowItem);
        menu.Items.Add(previewItem);
        menu.Items.Add(_startAtLoginItem);

        menu.Opening += (_, _) =>
        {
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
        string target = presentation.GetProperty("eligibleTarget").GetString()?.Replace(@"\\.\", "") ?? "无可用目标";
        string state = presentation.GetProperty("state").GetString() switch
        {
            "disabled" => "已停用",
            "waiting-for-idle" => "等待空闲",
            "initializing" => "正在启动",
            "visible" => "正在显示",
            "no-eligible-physical-display" => "等待可用屏幕",
            "retry-cooldown" => "稍后重试",
            "waiting-for-input-or-manual-retry" => "等待操作后再试",
            "background-source-changed" => "壁纸来源已变化",
            "display-target-changed" => "目标屏已变化",
            _ => "状态待确认",
        };
        bool enabled = status.GetProperty("enabled").GetBoolean();
        var page = new Forms.TaskDialogPage
        {
            Caption = "翠幕屏保",
            Heading = enabled ? "屏保已启用" : "屏保已停用",
            AllowCancel = true,
            Text = $"现在：{state}\n显示：仅实体桌面屏 · {target}\n已空闲 {status.GetProperty("idleSeconds").GetDouble():0} 秒，启动门槛 {_controller.ActivationDelay.TotalSeconds:0} 秒",
            Footnote = new("Ctrl+Win+E 立即启动 · 有操作即退出\n保持壁纸运行，不锁屏。"),
            Buttons = { Forms.TaskDialogButton.Close },
        };
        if (presentation.GetProperty("lastFailure").GetString() is { Length: > 0 } failure)
            page.Expander = new("最近一次失败：" + failure) { CollapsedButtonText = "查看上次失败", ExpandedButtonText = "收起" };
        Forms.TaskDialog.ShowDialog(page);
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
