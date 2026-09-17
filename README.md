# Emerald Veil

所有 Windows 屏幕（包括 VDD）的底层壁纸、系统锁屏和泡泡内嵌背景统一使用 **雨林黑猫 · 更绿版 4K 图片**，保留 Wallpaper Engine。运行 `scripts/Set-WindowsBackground.ps1 -Action Apply`，再用 `-Action Verify` 逐屏核对；换机使用相同入口。素材、来源与操作说明见 [Windows 壁纸与锁屏恢复](docs/windows-background.md)。静态壁纸设置独立于下面的泡泡程序，后者更新内嵌图片后需要重新构建安装。

Emerald Veil is a small, reversible Windows-native Bubbles overlay for OLED idle use. After six minutes without meaningful keyboard or mouse input, it shows the copy of `Bubbles.scr` already supplied by Windows above the selected project background. The original Microsoft colors, glass material, size, count policy, and motion stay intact.

The selected background is the project-owned `assets/verdant-rain-4k.png` (3840×2160), shared with the native Windows background selection, embedded in the installed watchdog, and rendered as a full-size click-through layer beneath native Bubbles. While visible, the watchdog keeps that layer immediately below the native Bubbles HWND so Wallpaper Engine cannot be inserted between them. The watchdog does not capture the desktop or depend on the Windows/Wallpaper Engine wallpaper owner.

The watchdog composes its own background before native initialization and never stops, pauses or replays Wallpaper Engine. Failures use bounded cooldown and suspension rather than repeated wallpaper takeovers. Only positively identified physical desktop screens are eligible; VDD, instrument screens and unknown identities are excluded. With the physical screen off, the enabled app keeps measuring idle but draws nothing. Windows runtime timeout/active/secure settings are checked every 30 seconds.

The installed WinExe is a quiet user-session watchdog. It samples `GetLastInputInfo` and uses one narrow in-memory classifier to ignore a `WM_MOUSEMOVE` whose point has not changed at all. It also keeps one isolated injected nonzero move from resetting the idle clock; the event is still delivered normally, and a second injected move within 250 ms confirms real remote movement and becomes activity. A new raw input tick without a matching hook classification is held for one 50 ms sample so a no-op classification arriving just behind the poll cannot become permanent activity; an unclassified or valid tick is accepted on the next sample. The watchdog manually starts `Bubbles.scr /s`, finds the exact child-process window, retains the native glass/background composite at full opacity, and makes that window topmost, non-activating, and click-through. It never color-keys black native pixels. Windows' own automatic screen-saver trigger remains disabled because this app owns the six-minute trigger. No continuous VDD capture, checkerboard, custom bubble renderer, network listener, telemetry, scheduled task, PowerShell watchdog, event log, or product-name allowlist is used.

Startup is deliberately per-user through a direct `HKCU\...\Run` WinExe entry. It must not run as `SYSTEM`: Session 0 cannot draw on the signed-in user's desktop. A crash-safe, cross-process session lease refuses a second renderer without killing or taking over an existing one. Every owned renderer is also assigned to a kill-on-close Windows Job Object, so input, Disable, watchdog exit, crash, or restart cannot leave an old Bubbles instance to overlap the next one.

## Behavior

- Idle timeout: 360 seconds (six minutes).
- Visual: Microsoft Windows native multicolor glass bubbles in full-size `/s` mode.
- Size: the native maximum-radius profile; it is not calculated from DPI, Windows scaling, screen inches, or resolution.
- 4K density: Windows' native default produces roughly 26 large bubbles on a 3840×2160 target instead of thousands of preview-mode miniatures.
- Exit: physical movement, a confirmed injected movement stream, a button, wheel, or keyboard event hides the owned window synchronously and closes its Job Object. An exact zero-displacement move and one isolated injected reposition do not dismiss it.
- Foreground: Codex and other applications remain live and receive normal input; while the veil is active, the selected background image covers the desktop visually beneath the bubbles.
- Remote use: the overlay stays on the current interactive desktop and does not enter secure desktop or lock state. ToDesk, Sunshine, UU/GameViewer, and similar tools are examples only; the program never detects or branches on their names.
- OLED protection: motion reduces fully static exposure while idle, but no software screen saver guarantees prevention of burn-in.

The private `Radius` setting is not a documented Windows API. Emerald Veil records an exact preimage before changing it and provides a full restore operation. The Windows renderer still owns its native edge and collision behavior; the project deliberately does not patch process memory or redraw Microsoft visuals.

## Use

PowerShell 7 on Windows 11 is the tested target. Administrator rights are not required. Building the Bubbles app from source requires the .NET 10 SDK; the independent static-background script needs only Windows PowerShell.

```powershell
# Build the single executable at the installer's default source location.
dotnet publish .\src\EmeraldVeil.App\EmeraldVeil.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\artifacts\publish\win-x64

# Install/update the silent watchdog and direct per-user startup entry.
pwsh -NoProfile -File .\scripts\Install-EmeraldVeil.ps1 -Action Install

# Enable the six-minute native overlay.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Enable

# Verify registry types, radius, timeout, startup, and disabled Windows trigger.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Verify

# Immediately stop Bubbles and disable future idle activation.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Disable

# Remove the watchdog, startup entry, and installed executable completely.
pwsh -NoProfile -File .\scripts\Install-EmeraldVeil.ps1 -Action Remove

# Restore the exact machine state captured before the first Enable.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Restore
```

`Disable` first clears the project-owned enabled flag and keeps Windows' automatic trigger off, then stops matching system-path Bubbles processes in the current user session. Cleanup failure does not automatically re-enable it. `Remove` is the complete uninstall path. A later `Enable` reuses the same six-minute profile.

The first `Enable` stores its rollback record at `%LOCALAPPDATA%\EmeraldVeil\native-bubbles-preimage.json`. Repeated enables do not overwrite it. The record is machine-specific and must not be published.

See [the product design](docs/product-design.md) for the window contract, configuration, acceptance checks, and rollback rules. The project does not redistribute `Bubbles.scr` or Microsoft visual assets.

## License

Project-authored code is released under the [MIT License](LICENSE). `Bubbles.scr` and its visuals remain Windows components under Microsoft's terms.

## 状态与显示范围

泡泡只在已识别的实体桌面屏显示；主屏关闭时保持启用并待命，不转到 VDD 或仪表屏。VDD 的静态底图不受影响。默认登录即启动、屏保默认启用、会话默认不暂停。托盘菜单可查看目标、空闲时间和最近失败；“立即启动屏保”或双击托盘图标会直接进入真正的屏保，`Ctrl+Win+E` 也执行同一动作。退出屏保不会停止壁纸或副屏。

```powershell
& "$env:LOCALAPPDATA\Programs\EmeraldVeil\EmeraldVeil.exe" --status
& "$env:LOCALAPPDATA\Programs\EmeraldVeil\EmeraldVeil.exe" --pause
& "$env:LOCALAPPDATA\Programs\EmeraldVeil\EmeraldVeil.exe" --resume
```

状态命令不会启动缺失的屏保；重复打开已运行的程序不会强制预览。`Ctrl+Win+E` 使用现有低级键盘观察器触发，不新建服务或第二套全局热键注册；组合键本身会被消费，所有按键释放并稳定约 250 ms 后才建立屏保输入基线，避免松键把刚启动的屏保立即退出。预览最多 15 秒；真正屏保一直显示到正常输入、暂停或禁用。实体屏动画、真实远程画面和自然开机属于单独验收，不从进程存在推断。
