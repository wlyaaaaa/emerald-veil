# Emerald Veil

Emerald Veil is a small, reversible Windows-native Bubbles overlay for OLED idle use. After ten minutes without meaningful keyboard or mouse input, it shows the copy of `Bubbles.scr` already supplied by Windows. The original Microsoft colors, glass material, size, count policy, and motion stay intact while the live desktop remains visible underneath.

The installed WinExe is a quiet user-session watchdog. It samples `GetLastInputInfo` and uses one narrow in-memory classifier to ignore a `WM_MOUSEMOVE` whose point has not changed at all. It also keeps one isolated injected nonzero move from resetting the idle clock; the event is still delivered normally, and a second injected move within 250 ms confirms real remote movement and becomes activity. A new raw input tick without a matching hook classification is held for one 50 ms sample so a no-op classification arriving just behind the poll cannot become permanent activity; an unclassified or valid tick is accepted on the next sample. The watchdog manually starts `Bubbles.scr /s`, finds the exact child process window, turns black background pixels transparent, and makes that window topmost, non-activating, and click-through. Windows' own automatic screen-saver and lock trigger remains disabled. No screenshot, checkerboard, replacement background, custom bubble renderer, network, telemetry, scheduled task, PowerShell wrapper, event log, or product-name allowlist is used.

Startup is deliberately per-user through a direct `HKCU\...\Run` WinExe entry. It must not run as `SYSTEM`: Session 0 cannot draw on the signed-in user's desktop. A crash-safe, cross-process session lease refuses a second renderer without killing or taking over an existing one. Every owned renderer is also assigned to a kill-on-close Windows Job Object, so input, Disable, watchdog exit, crash, or restart cannot leave an old Bubbles instance to overlap the next one.

## Behavior

- Idle timeout: 600 seconds.
- Visual: Microsoft Windows native multicolor glass bubbles in full-size `/s` mode.
- Size: the native maximum-radius profile; it is not calculated from DPI, Windows scaling, screen inches, or resolution.
- 4K density: Windows' native default produces roughly 26 large bubbles on a 3840×2160 target instead of thousands of preview-mode miniatures.
- Exit: physical movement, a confirmed injected movement stream, a button, wheel, or keyboard event hides the owned window synchronously and closes its Job Object. An exact zero-displacement move and one isolated injected reposition do not dismiss it.
- Foreground: Codex and other applications remain live and visible under the color-keyed window.
- Remote use: the overlay stays on the current interactive desktop and does not enter secure desktop or lock state. ToDesk, Sunshine, UU/GameViewer, and similar tools are examples only; the program never detects or branches on their names.
- OLED protection: motion reduces fully static exposure while idle, but no software screen saver guarantees prevention of burn-in.

The private `Radius` setting is not a documented Windows API. Emerald Veil records an exact preimage before changing it and provides a full restore operation. The Windows renderer still owns its native edge and collision behavior; the project deliberately does not patch process memory or redraw Microsoft visuals.

## Use

PowerShell 7 on Windows 11 is the tested target. Administrator rights are not required.

```powershell
# Install/update the silent watchdog and direct per-user startup entry.
pwsh -NoProfile -File .\scripts\Install-EmeraldVeil.ps1 -Action Install

# Enable the ten-minute native overlay.
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

`Disable` is the fast-off path. It stops any Windows Bubbles process at the exact system path, clears the project-owned enabled flag, and keeps Windows' automatic trigger off. `Remove` is the complete uninstall path. A later `Enable` reuses the same ten-minute profile.

The first `Enable` stores its rollback record at `%LOCALAPPDATA%\EmeraldVeil\native-bubbles-preimage.json`. Repeated enables do not overwrite it. The record is machine-specific and must not be published.

See [the product design](docs/product-design.md) for the window contract, configuration, acceptance checks, and rollback rules. The project does not redistribute `Bubbles.scr` or Microsoft visual assets.

## License

Project-authored code is released under the [MIT License](LICENSE). `Bubbles.scr` and its visuals remain Windows components under Microsoft's terms.
