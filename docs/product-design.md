# Product design

## Product contract

The active product is a reversible Windows-native Bubbles preview overlay plus a small no-console idle watchdog:

1. While input is active, Windows leaves the normal desktop visible.
2. The watchdog samples `GetLastInputInfo` every 50 ms and, after 300 seconds of reliable inactivity, creates a transparent click-through host and starts its installed `Bubbles.scr` with `/p <HWND>`.
3. Bubbles use Microsoft's native multicolor glass visual and motion with a larger private radius value.
4. Normal input stops the preview renderer and removes the host; Windows never enters screen-saver or lock state.
5. `Disable` stops the owned preview and clears the project-owned enabled flag; `Enable` turns the same profile back on.

The host uses a black color key, `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `HTTRANSPARENT`, and `MA_NOACTIVATE`. The native preview paints only the bubbles into that host; black pixels remain transparent and the live desktop stays visible. It loads no checkerboard, wallpaper, screenshot, or replacement background. Because `/s` is never used and Windows' own trigger is held inactive, remote-control and streaming tools keep the normal desktop session. ToDesk, Sunshine, and UU/GameViewer are acceptance examples only; runtime behavior never depends on their names.

## Windows configuration

`Enable` writes these per-user values and applies the corresponding runtime state through `SystemParametersInfoW`:

| Location | Name | Type | Value |
| --- | --- | --- | --- |
| `HKCU\Control Panel\Desktop` | `SCRNSAVE.EXE` | `REG_SZ` | `%WINDIR%\System32\Bubbles.scr` resolved to an absolute path |
| `HKCU\Control Panel\Desktop` | `ScreenSaveTimeOut` | `REG_SZ` | `300` |
| `HKCU\Control Panel\Desktop` | `ScreenSaveActive` | `REG_SZ` | `0` |
| `HKCU\Control Panel\Desktop` | `ScreenSaverIsSecure` | `REG_SZ` | `0` |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Screensavers\Bubbles` | `Radius` | `REG_DWORD` | `1130000000` (`0x435A6E80`) |
| `HKCU\Software\EmeraldVeil` | `NativeBubblesEnabled` | `REG_DWORD` | `1` while enabled, `0` while disabled |

The `Radius` bit pattern is approximately `218.43` when interpreted as the private single-precision value used by Bubbles, giving a nominal diameter of about 437 physical pixels. Windows owns rendering and display scaling; the project does not apply WPF DPI conversion, so there is no custom 150%/200% scaling path to drift.

The script calls the standard Windows setters with active=false, timeout=300, and secure=false. This prevents a second full-screen `/s` launch at the same threshold. The installed WinExe is the only helper: its direct `HKCU\...\Run` value starts the application in the signed-in user's interactive session at logon, without PowerShell/cmd/wscript, a scheduled task, capture, network, or telemetry. It is intentionally not a SYSTEM service because Session 0 cannot render into that interactive desktop. It launches only `%WINDIR%\System32\Bubbles.scr /p <owned HWND>` and never draws a replacement bubble visual.

## Reversibility

Before the first mutation, `Enable` captures:

- the three Windows runtime values: active, timeout, and secure exit;
- exact presence, registry kind, and value for the native profile and project-owned enabled flag;
- the legacy Emerald Veil `Run`, `StartupApproved`, and ownership values.

The record is written once to `%LOCALAPPDATA%\EmeraldVeil\native-bubbles-preimage.json` using a flushed same-directory temporary file followed by an atomic move. Repeated `Enable` operations validate but do not overwrite it.

New preimages use schema v2 and include the project-owned enabled flag. Existing v1 preimages remain valid; v1 predates the flag, so restore removes it. `Restore` restores the runtime and registered preimage and then independently reads every value back. `Disable` intentionally has the smaller safety contract: stop the owned preview, prove `NativeBubblesEnabled=0`, and prove Windows active=false even if the radius or other nonessential values have drifted.

The legacy custom startup value is removed during migration, while the new watchdog entry uses the distinct name `Emerald Veil Native Bubbles`. The normal way to stop visible/automatic Bubbles is `Disable`; the complete uninstall path is `Install-EmeraldVeil.ps1 -Action Remove`, not `Restore`.

## Scope and limits

- This profile depends on the Windows-installed `C:\Windows\System32\Bubbles.scr`; the repository does not redistribute it or Microsoft visual assets.
- `Radius` is an undocumented implementation parameter and may change in a future Windows build. `Verify` fails instead of silently accepting a wrong type or value.
- Preview mode is an implementation use of the Windows screen-saver preview contract, not a Windows screen-saver session. It does not provide lock-screen security.
- Moving animation can reduce prolonged static-image exposure, but no software screen saver guarantees protection from OLED burn-in.
- Display-off and sleep timers are separate. If Windows turns the panel off before 300 seconds, the screen saver will not be seen; a powered-off panel is still the stronger OLED-protection state.
- A future Windows build could change `Bubbles.scr` preview or private `Radius` behavior; `Verify` fails closed on configuration drift, while deployment acceptance checks the actual preview process and window.

## Acceptance checklist

- PowerShell parsing and the embedded `SystemParametersInfoW` interop compile successfully.
- `Enable` reads back the absolute native path, all registry kinds and values, enabled flag, and runtime active=false/timeout/secure state.
- The legacy `Emerald Veil` startup entry is absent after migration; the distinct `Emerald Veil Native Bubbles` entry points to the installed direct WinExe and its owner marker is present.
- A short-threshold acceptance observes `Bubbles.scr /p <HWND>`, live desktop pixels beneath the host with no replacement background, and `SPI_GETSCREENSAVERRUNNING=false`.
- The real 300-second boundary is observed in the interactive user session; representative remote-tool processes and established connections remain present before and after activation without name-based behavior.
- `Disable` proves the preview is stopped and automatic activation is off without depending on unrelated profile values.
- `Enable` is idempotent and does not change the durable preimage hash.
- `Restore` reproduces exact value presence, kinds, values, and runtime state, after which `Enable` can safely reapply the native profile.
