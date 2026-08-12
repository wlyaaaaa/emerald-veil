# Emerald Veil

Emerald Veil is now a small, reversible Windows-native Bubbles profile for OLED idle use. It selects the copy of `Bubbles.scr` already supplied by Windows, starts it after five minutes of user inactivity, enlarges the bubbles, and exits on normal input.

The installed WinExe stays as a quiet user-session watchdog because Chromium/WebView2 foreground apps can prevent Windows from delivering the normal screen-saver command. It uses `GetLastInputInfo` and starts the exact Windows saver path directly; it has no console, capture, network, telemetry, scheduled task, or shell wrapper. The owned `Emerald Veil Native Bubbles` Run value persists across reboot.

## Important behavior

- Idle timeout: 300 seconds.
- Visual: Microsoft Windows native multicolor glass bubbles with an enlarged radius.
- Exit: normal keyboard or mouse input ends the screen saver.
- Unlock prompt: disabled by this profile.
- Foreground: the native Bubbles screen saver uses a frozen desktop image. Codex or another foreground app can continue working, but its visual updates are not shown until the screen saver exits.
- OLED protection: moving bubbles replace a fully static foreground while idle, but this is not a burn-in guarantee and does not replace sensible brightness, pixel shifting, or panel maintenance.

The private `Radius` setting is not a documented Windows API. This project therefore records an exact preimage before changing it and provides a full restore operation.

## Use

PowerShell 7 on Windows 11 is the tested target. Administrator rights are not required.

```powershell
# Install the no-console idle watchdog and its direct user-level startup entry.
pwsh -NoProfile -File .\scripts\Install-EmeraldVeil.ps1 -Action Install

# Configure native Bubbles and enable the five-minute idle trigger.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Enable

# Confirm the selected saver, timeout, value types, radius, and Windows runtime state.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Verify

# Immediately disable automatic screen-saver activation while keeping the profile ready.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Disable

# Fully remove the watchdog/startup entry when the product is no longer wanted.
pwsh -NoProfile -File .\scripts\Install-EmeraldVeil.ps1 -Action Remove

# Restore the exact state captured before the first Enable operation.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Restore
```

`Disable` stops an owned running `Bubbles.scr` process and proves that automatic activation is off, without depending on unrelated profile drift. The watchdog remains installed but will not launch the saver while `ScreenSaveActive=0`; `Install-EmeraldVeil.ps1 -Action Remove` is the fast complete-off path and removes its startup entry and installed executable. A later `Enable` turns the same five-minute profile back on.

The first `Enable` stores the rollback record at `%LOCALAPPDATA%\EmeraldVeil\native-bubbles-preimage.json`. It is preserved across repeated enables and must not be published because it is machine-specific. `Restore` may also restore a legacy Emerald Veil startup entry if that entry existed in the captured state; use `Disable` for the normal “turn it off” operation.

See [the product design](docs/product-design.md) for the exact Windows settings and rollback contract. The older custom WPF/D3D experiment remains legacy source only and is not shown by this profile; the WPF process is only the no-console idle watchdog/tray host.

## License

The project-authored configuration code is released under the [MIT License](LICENSE). `Bubbles.scr` and its visuals are Windows components and are not redistributed by this repository.
