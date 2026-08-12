# Emerald Veil

Emerald Veil is a small, reversible Windows-native Bubbles overlay for OLED idle use. It hosts the copy of `Bubbles.scr` already supplied by Windows after five minutes of user inactivity, enlarges the bubbles, and disappears on normal input.

The installed WinExe stays as a quiet user-session watchdog. It uses `GetLastInputInfo`, creates a transparent click-through desktop host, and runs the exact Windows saver in its documented `/p <HWND>` preview mode. Windows' own full-screen screen-saver trigger remains disabled, so the live desktop stays visible and remote-control or streaming software keeps a normal interactive session; ToDesk, Sunshine, and UU/GameViewer are examples, not an allowlist. There is no console, capture, network, telemetry, scheduled task, shell wrapper, checkerboard, or replacement background.

Startup is deliberately per-user at interactive logon through `HKCU\...\Run`. The visual process must not run as `SYSTEM`: Session 0 cannot display on the signed-in user's desktop, and a SYSTEM service would add session-switching and remote-access failure modes without improving the idle trigger.

## Important behavior

- Idle timeout: 300 seconds.
- Visual: Microsoft Windows native multicolor glass bubbles with an enlarged radius.
- Exit: normal keyboard or mouse input removes the overlay within the watchdog polling interval.
- Windows screen-saver/lock state: never entered; only preview rendering is used.
- Foreground: Codex and other applications remain live and visible beneath the transparent bubbles.
- Remote access: no product-specific allowlist is used; avoiding the Windows full-screen saver state is the compatibility boundary.
- OLED protection: moving bubbles replace a fully static foreground while idle, but this is not a burn-in guarantee and does not replace sensible brightness, pixel shifting, or panel maintenance.

The private `Radius` setting is not a documented Windows API. This project therefore records an exact preimage before changing it and provides a full restore operation.

## Use

PowerShell 7 on Windows 11 is the tested target. Administrator rights are not required.

```powershell
# Install the no-console idle watchdog and its direct user-level startup entry.
pwsh -NoProfile -File .\scripts\Install-EmeraldVeil.ps1 -Action Install

# Configure native Bubbles and enable the five-minute idle trigger.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Enable

# Confirm preview-host mode, timeout, value types, radius, startup, and Windows runtime state.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Verify

# Immediately disable automatic screen-saver activation while keeping the profile ready.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Disable

# Fully remove the watchdog/startup entry when the product is no longer wanted.
pwsh -NoProfile -File .\scripts\Install-EmeraldVeil.ps1 -Action Remove

# Restore the exact state captured before the first Enable operation.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Restore
```

`Disable` stops an owned running `Bubbles.scr` preview and clears the project-owned enabled flag. Windows' full-screen saver trigger remains off in both enabled and disabled states. `Install-EmeraldVeil.ps1 -Action Remove` is the complete-off path and removes the startup entry, enabled flag, and installed executable. A later `Enable` turns the same five-minute overlay back on.

The first `Enable` stores the rollback record at `%LOCALAPPDATA%\EmeraldVeil\native-bubbles-preimage.json`. It is preserved across repeated enables and must not be published because it is machine-specific. `Restore` may also restore a legacy Emerald Veil startup entry if that entry existed in the captured state; use `Disable` for the normal “turn it off” operation.

See [the product design](docs/product-design.md) for the exact Windows settings and rollback contract. The project does not implement or modify the bubble visual; the visible renderer remains Microsoft's installed `Bubbles.scr`.

## License

The project-authored configuration code is released under the [MIT License](LICENSE). `Bubbles.scr` and its visuals are Windows components and are not redistributed by this repository.
