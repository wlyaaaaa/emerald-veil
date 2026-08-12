# Emerald Veil

Emerald Veil is now a small, reversible Windows-native Bubbles profile for OLED idle use. It selects the copy of `Bubbles.scr` already supplied by Windows, starts it after five minutes of user inactivity, enlarges the bubbles, and exits on normal input.

Windows owns the idle trigger, so there is no background Emerald Veil process, `Run` entry, scheduled task, or console window at sign-in. The setting persists across reboot and becomes active for the user session after login.

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
# Configure native Bubbles and enable the five-minute idle trigger.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Enable

# Confirm the selected saver, timeout, value types, radius, and Windows runtime state.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Verify

# Immediately disable automatic screen-saver activation while keeping the profile ready.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Disable

# Restore the exact state captured before the first Enable operation.
pwsh -NoProfile -File .\scripts\Set-NativeBubbles.ps1 -Action Restore
```

`Disable` is deliberately independent of bubble-size or other profile drift: it only needs to prove that Windows automatic screen-saver activation is off. A later `Enable` turns the same five-minute profile back on.

The first `Enable` stores the rollback record at `%LOCALAPPDATA%\EmeraldVeil\native-bubbles-preimage.json`. It is preserved across repeated enables and must not be published because it is machine-specific. `Restore` may also restore a legacy Emerald Veil startup entry if that entry existed in the captured state; use `Disable` for the normal “turn it off” operation.

See [the product design](docs/product-design.md) for the exact Windows settings and rollback contract. The older custom WPF/D3D experiment remains legacy source only and is not installed or started by this profile.

## License

The project-authored configuration code is released under the [MIT License](LICENSE). `Bubbles.scr` and its visuals are Windows components and are not redistributed by this repository.
