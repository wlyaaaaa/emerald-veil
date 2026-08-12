# Product design

## Product contract

The active product is a reversible Windows-native Bubbles profile plus a small no-console idle watchdog:

1. While input is active, Windows leaves the normal desktop visible.
2. The watchdog samples `GetLastInputInfo` every 50 ms and, after 300 seconds of reliable inactivity, starts its installed `Bubbles.scr` directly. The Windows screen-saver settings remain configured as a fallback and for normal system integration.
3. Bubbles use Microsoft's native multicolor glass visual and motion with a larger private radius value.
4. Normal input exits the screen saver without an unlock prompt.
5. `Disable` stops an owned running saver and immediately turns automatic activation off; `Enable` turns the same profile back on.

The native saver displays a frozen desktop image behind its animation. The foreground application may continue computing, but its new frames are not visible until the saver exits. This accepted tradeoff replaces the rejected custom transparent-overlay design.

## Windows configuration

`Enable` writes these per-user values and applies the corresponding runtime state through `SystemParametersInfoW`:

| Location | Name | Type | Value |
| --- | --- | --- | --- |
| `HKCU\Control Panel\Desktop` | `SCRNSAVE.EXE` | `REG_SZ` | `%WINDIR%\System32\Bubbles.scr` resolved to an absolute path |
| `HKCU\Control Panel\Desktop` | `ScreenSaveTimeOut` | `REG_SZ` | `300` |
| `HKCU\Control Panel\Desktop` | `ScreenSaveActive` | `REG_SZ` | `1` |
| `HKCU\Control Panel\Desktop` | `ScreenSaverIsSecure` | `REG_SZ` | `0` |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Screensavers\Bubbles` | `Radius` | `REG_DWORD` | `1130000000` (`0x435A6E80`) |

The `Radius` bit pattern is approximately `218.43` when interpreted as the private single-precision value used by Bubbles, giving a nominal diameter of about 437 physical pixels. Windows owns rendering and display scaling; the project does not apply WPF DPI conversion, so there is no custom 150%/200% scaling path to drift.

The script calls the standard Windows screen-saver setters for active state, timeout, and secure-exit state with persistent settings and a settings-change broadcast. The installed WinExe is the only helper: its direct `HKCU\...\Run` value starts the existing application at logon, without PowerShell/cmd/wscript, a scheduled task, capture, network, or telemetry. It launches only `%WINDIR%\System32\Bubbles.scr` and never draws a replacement visual.

## Reversibility

Before the first mutation, `Enable` captures:

- the three Windows runtime values: active, timeout, and secure exit;
- exact presence, registry kind, and value for all five native profile values;
- the legacy Emerald Veil `Run`, `StartupApproved`, and ownership values.

The record is written once to `%LOCALAPPDATA%\EmeraldVeil\native-bubbles-preimage.json` using a flushed same-directory temporary file followed by an atomic move. Repeated `Enable` operations validate but do not overwrite it.

`Restore` accepts only the fixed eight-value registry identity set, restores the runtime and registry preimage, and then independently reads every value back. `Disable` intentionally has the smaller safety contract: write and prove `ScreenSaveActive=0` and runtime active=false even if the radius or other nonessential values have drifted.

The legacy custom startup value is removed during migration, while the new watchdog entry uses the distinct name `Emerald Veil Native Bubbles`. The normal way to stop visible/automatic Bubbles is `Disable`; the complete uninstall path is `Install-EmeraldVeil.ps1 -Action Remove`, not `Restore`.

## Scope and limits

- This profile depends on the Windows-installed `C:\Windows\System32\Bubbles.scr`; the repository does not redistribute it or Microsoft visual assets.
- `Radius` is an undocumented implementation parameter and may change in a future Windows build. `Verify` fails instead of silently accepting a wrong type or value.
- The frozen desktop means the user cannot watch live Codex output while the saver is visible.
- Moving animation can reduce prolonged static-image exposure, but no software screen saver guarantees protection from OLED burn-in.
- Display-off and sleep timers are separate. If Windows turns the panel off before 300 seconds, the screen saver will not be seen; a powered-off panel is still the stronger OLED-protection state.
- Group policy, device management, secure desktop, remote sessions, and future Windows changes can override or alter screen-saver behavior.

## Acceptance checklist

- PowerShell parsing and the embedded `SystemParametersInfoW` interop compile successfully.
- `Enable` reads back the absolute native path, all registry kinds and values, and runtime active/timeout/secure state.
- The legacy `Emerald Veil` startup entry is absent after migration; the distinct `Emerald Veil Native Bubbles` entry points to the installed direct WinExe and its owner marker is present.
- `Disable` proves automatic activation is off without depending on unrelated profile values.
- `Enable` is idempotent and does not change the durable preimage hash.
- `Restore` reproduces exact value presence, kinds, values, and runtime state, after which `Enable` can safely reapply the native profile.
