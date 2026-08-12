# Product design

## Product contract

The active product is a reversible Windows-native Bubbles overlay plus a small no-console idle watchdog:

1. The watchdog samples `GetLastInputInfo` every 50 ms.
2. After 300 seconds of reliable inactivity, it manually starts the installed `%WINDIR%\System32\Bubbles.scr /s` in the signed-in user's current interactive session.
3. It selects the exact child process window that intersects the primary target display, hides other visible windows from that process, color-keys black pixels, and applies `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, topmost placement, and `SWP_NOACTIVATE`.
4. The live desktop remains visible. Microsoft still owns the bubble assets, material, animation, collision, and boundary behavior.
5. Normal input synchronously hides the selected HWND, then closes a `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` Job Object. Explicit Disable and watchdog termination use the same ownership boundary, so an old native renderer cannot overlap a new one.

Windows' configured automatic screen-saver/lock trigger remains inactive. The application starts `/s` itself only when its own reliable idle clock reaches five minutes; it does not use Windows' foreground `SC_SCREENSAVE` delivery path. The visible output is still a window on the current desktop, not a screenshot, checkerboard, replacement wallpaper, secure desktop, or custom-drawn bubble scene.

## Size and display policy

The product does not derive bubble size from screen inches, resolution, WPF device-independent pixels, or Windows scale percentage. The registry bit pattern `1130000000` represents approximately `218.43`; this Windows build clamps the native `/s` radius to its internal maximum of 200, giving a nominal 400-physical-pixel diameter. Changing Windows scaling among 150%, 175%, and 200% therefore does not resize the bubbles.

Windows retains its native default density. On a 3840×2160 target, the current build's native formula yields roughly 26 maximum-size bubbles—dozens of large bubbles rather than the thousands created by `/p` preview mode. Count remains Windows-owned; the project does not write `SphereDensity`.

Per-Monitor V2 awareness is declared before any HWND is created. `Screen.PrimaryScreen.Bounds` supplies the target's physical rectangle for window placement only; it is not an input to bubble size. A display/DPI change restarts the owned renderer after stopping the prior Job Object.

## Windows configuration

`Enable` writes these per-user values and applies runtime state through `SystemParametersInfoW`:

| Location | Name | Type | Value |
| --- | --- | --- | --- |
| `HKCU\Control Panel\Desktop` | `SCRNSAVE.EXE` | `REG_SZ` | absolute `%WINDIR%\System32\Bubbles.scr` path |
| `HKCU\Control Panel\Desktop` | `ScreenSaveTimeOut` | `REG_SZ` | `300` |
| `HKCU\Control Panel\Desktop` | `ScreenSaveActive` | `REG_SZ` | `0` |
| `HKCU\Control Panel\Desktop` | `ScreenSaverIsSecure` | `REG_SZ` | `0` |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Screensavers\Bubbles` | `Radius` | `REG_DWORD` | `1130000000` (`0x435A6E80`) |
| `HKCU\Software\EmeraldVeil` | `NativeBubblesEnabled` | `REG_DWORD` | `1` enabled, `0` disabled |

Runtime setters use active=false, timeout=300, and secure=false. This prevents Windows from independently launching a second screen saver at the same threshold. The only helper is the direct WinExe `HKCU\...\Run` value; there is no service, SYSTEM process, scheduled task, console, shell interpreter, capture API, network listener, or telemetry.

## Remote and input boundary

Remote-control and streaming products are not detectable as a complete, reliable class, so Emerald Veil does not maintain an allowlist or attempt generic remote-session suppression. Compatibility comes from staying on the user's existing interactive desktop, keeping the Windows automatic/secure trigger disabled, color-keying the background, and avoiding capture or display reconfiguration. ToDesk, Sunshine, UU/GameViewer, and future tools all receive the same behavior.

Local desktop capture has verified that the live Codex desktop and native bubbles are composited together. A real remote client remains the authority for end-to-end connection and image acceptance; process/service presence alone is not such proof.

`WS_EX_TRANSPARENT` and `WS_EX_NOACTIVATE` keep the overlay from becoming an application input surface. Regardless of native exit timing, the watchdog's first observed input synchronously hides the owned HWND before process teardown.

## Reversibility

Before the first mutation, `Enable` captures:

- the runtime active, timeout, and secure values;
- exact presence, registry kind, and value for every touched native/profile setting;
- the project-owned enabled flag;
- legacy Emerald Veil `Run`, `StartupApproved`, and owner values.

The record is flushed to a same-directory temporary file and atomically moved to `%LOCALAPPDATA%\EmeraldVeil\native-bubbles-preimage.json`. Repeated `Enable` operations validate but do not overwrite it. Schema v2 records the enabled flag; v1 remains accepted and restores that flag as absent.

`Disable` has the deliberately small emergency contract: stop the exact native Bubbles process, prove `NativeBubblesEnabled=0`, and prove Windows active=false even if a nonessential profile value drifted. `Restore` reproduces the original presence, kinds, values, and runtime state. `Install-EmeraldVeil.ps1 -Action Remove` removes the owned startup entry and executable.

## Scope and limits

- The repository depends on the Windows-installed `Bubbles.scr` and does not redistribute Microsoft code or assets.
- `Radius` and the observed density formula are undocumented implementation details and may change in a future Windows build. Verification fails closed on configuration drift; release acceptance checks actual rendering.
- Native Bubbles controls edge/collision behavior. Avoiding partially off-edge bubbles would require altering Microsoft motion or drawing a replacement, which is outside this product contract.
- Moving pixels reduce a completely static idle image but do not guarantee OLED burn-in prevention. Brightness, panel pixel shift, display-off timers, and panel maintenance remain relevant.
- On battery, a display-off timeout shorter than 300 seconds may turn the panel off first; that is stronger OLED protection even though no bubbles are visible.

## Acceptance checklist

- PowerShell parser and embedded `SystemParametersInfoW` interop compile.
- Release build and unit tests pass.
- `Enable` reads back the absolute system path, exact registry kinds/values, timeout=300, secure=false, enabled flag=1, and Windows active=false.
- The direct startup entry points to the installed WinExe; no legacy startup entry, service, scheduled task, or shell wrapper remains.
- A bounded visual check observes native maximum-size multicolor bubbles over the live 3840×2160 Codex desktop with no black, grey, checkerboard, or replacement background.
- The selected HWND exactly matches the target physical rectangle and includes layered, transparent, no-activate, tool-window, and topmost styles.
- Input hides the selected window within 100 ms and leaves no Bubbles process.
- Immediate Stop and watchdog process termination both leave zero Bubbles processes, proving Job Object cleanup and no overlap.
- The real 300-second threshold is observed in the interactive user session.
- A real remote client verifies connection continuity and the composited image; tool names do not alter runtime behavior.
- `Disable`, idempotent `Enable`, and exact `Restore` all pass read-back checks.
