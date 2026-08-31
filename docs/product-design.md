# Product design

## Product contract

The active product is a reversible Windows-native Bubbles overlay plus a small no-console idle watchdog:

1. The watchdog samples `GetLastInputInfo` every 50 ms. A low-level in-memory classifier compares per-monitor-aware mouse-event points. It ignores an exact zero-displacement `WM_MOUSEMOVE`; for idle accounting only, it also defers one isolated injected nonzero move until a second injected move arrives within 250 ms. The injected event itself still reaches the system. When a changed raw tick arrives before its hook classification, it remains pending for one sample; a matching ignored classification preserves the prior accepted tick, while a valid or still-unclassified tick becomes activity on the next sample.
2. After 360 seconds of reliable inactivity, it manually starts the installed `%WINDIR%\System32\Bubbles.scr /s` in the signed-in user's current interactive session.
3. It selects the exact child process window that intersects the primary target display, hides other visible windows from that process, color-keys black pixels, and applies `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, topmost placement, and `SWP_NOACTIVATE`.
4. The live desktop remains visible. Microsoft still owns the bubble assets, material, animation, collision, and boundary behavior.
5. Before launch, a crash-safe cross-process session lease and a read-only current-session process check refuse a second native renderer. They never kill or take over an existing instance.
6. Physical movement, a confirmed injected movement stream, buttons, wheel, and keyboard activity hide the selected HWND no later than the next 50 ms sampling decision, then close a `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` Job Object. A lone injected movement remains delivered to applications but does not dismiss the veil. Explicit Disable and watchdog termination use the same ownership boundary, so an old native renderer cannot overlap a new one.
7. While the desired mode remains visible, the watchdog compares that desired state with the owned renderer process every poll. If native Bubbles exits or a launch is temporarily refused, it retries after a one-second backoff; input changes the desired mode to hidden and cancels recovery.

Windows' configured automatic screen-saver/lock trigger remains inactive. The application starts `/s` itself only when its own reliable idle clock reaches six minutes; it does not use Windows' foreground `SC_SCREENSAVE` delivery path. The visible output is still a window on the current desktop, not a screenshot, checkerboard, replacement wallpaper, secure desktop, or custom-drawn bubble scene.

## Size and display policy

The product does not derive bubble size from screen inches, resolution, WPF device-independent pixels, or Windows scale percentage. The registry bit pattern `1130000000` represents approximately `218.43`; this Windows build clamps the native `/s` radius to its internal maximum of 200, giving a nominal 400-physical-pixel diameter. Changing Windows scaling among 150%, 175%, and 200% therefore does not resize the bubbles.

Windows retains its native default density. On a 3840×2160 target, the current build's native formula yields roughly 26 maximum-size bubbles—dozens of large bubbles rather than the thousands created by `/p` preview mode. Count remains Windows-owned; the project does not write `SphereDensity`.

Per-Monitor V2 awareness is declared before any HWND is created. `Screen.PrimaryScreen.Bounds` supplies the target's physical rectangle for window placement only; it is not an input to bubble size. A display/DPI change restarts the owned renderer after stopping the prior Job Object.

## Windows configuration

`Enable` writes these per-user values and applies runtime state through `SystemParametersInfoW`:

| Location | Name | Type | Value |
| --- | --- | --- | --- |
| `HKCU\Control Panel\Desktop` | `SCRNSAVE.EXE` | `REG_SZ` | absolute `%WINDIR%\System32\Bubbles.scr` path |
| `HKCU\Control Panel\Desktop` | `ScreenSaveTimeOut` | `REG_SZ` | `360` |
| `HKCU\Control Panel\Desktop` | `ScreenSaveActive` | `REG_SZ` | `0` |
| `HKCU\Control Panel\Desktop` | `ScreenSaverIsSecure` | `REG_SZ` | `0` |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Screensavers\Bubbles` | `Radius` | `REG_DWORD` | `1130000000` (`0x435A6E80`) |
| `HKCU\Software\EmeraldVeil` | `NativeBubblesEnabled` | `REG_DWORD` | `1` enabled, `0` disabled |

Runtime setters use active=false, timeout=360, and secure=false. Windows can reconstruct active=true during sign-in even while the registry remains `ScreenSaveActive=0`, so the enabled watchdog reasserts runtime active=false on every login start before monitoring idle time. This prevents Windows from independently launching a second screen saver at the same threshold. The only helper is the direct WinExe `HKCU\...\Run` value; there is no service, SYSTEM process, scheduled task, console, shell interpreter, capture API, network listener, or telemetry.

## Remote and input boundary

Remote-control and streaming products are not detectable as a complete, reliable class, so Emerald Veil does not maintain an allowlist or attempt generic remote-session suppression. Compatibility comes from staying on the user's existing interactive desktop, keeping the Windows automatic/secure trigger disabled, color-keying the background, and avoiding capture or display reconfiguration. ToDesk, Sunshine, UU/GameViewer, and future tools all receive the same behavior.

Remote, streaming, device-sharing, and virtual-HID software can periodically emit a mouse move without changing the pointer position, and can also emit a single injected nonzero reposition while an overlay is being established. An exact no-op must neither reset the six-minute idle clock nor reach applications. A lone injected reposition is still passed through, but it does not reset the idle clock unless another injected move follows within 250 ms. The comparison uses the previous per-monitor-aware event point, not WPF DIPs or display scaling. Physical movement, continued remote movement, buttons, wheel events, and keyboard input still dismiss Bubbles. This event-level rule deliberately cannot infer the source program.

The classifier keeps only the previous mouse-event point plus minimal injected-sequence, accepted, pending, and latest-classification tick state in process memory. The one-sample pending state prevents a `GetLastInputInfo`/low-level-hook ordering race from committing an ignored event before its classification arrives. It does not retain key values, write activity logs, identify a remote-control product, or persist coordinates. If the narrow hooks cannot be installed, an unclassified changed tick is accepted on its second sample rather than being ignored indefinitely.

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
- On battery, a display-off timeout shorter than 360 seconds may turn the panel off first; that is stronger OLED protection even though no bubbles are visible.

## Acceptance checklist

- PowerShell parser and embedded `SystemParametersInfoW` interop compile.
- Release build and unit tests pass.
- `Enable` reads back the absolute system path, exact registry kinds/values, timeout=360, secure=false, enabled flag=1, and Windows active=false.
- The direct startup entry points to the installed WinExe; no legacy startup entry, service, scheduled task, or shell wrapper remains.
- A bounded visual check observes native maximum-size multicolor bubbles over the live 3840×2160 Codex desktop with no black, grey, checkerboard, or replacement background.
- The selected HWND exactly matches the target physical rectangle and includes layered, transparent, no-activate, tool-window, and topmost styles.
- Input hides the selected window within 100 ms and leaves no Bubbles process.
- Unit tests cover exact zero-displacement `WM_MOUSEMOVE` events both with and without the injected flag, including the race where the raw tick is sampled before its ignored classification. They prove a lone injected nonzero move is delivered but ignored for idle, a second injected move within 250 ms confirms activity, an unclassified tick is accepted on its second observation, and a valid classification commits immediately. A PMv2 acceptance helper injects an exact zero-displacement move and the same native Bubbles PID remains visible; an injected test key then dismisses it.
- Immediate Stop and watchdog process termination both leave zero Bubbles processes, proving Job Object cleanup and no overlap.
- A deterministic reconciliation test simulates the native renderer exiting while idle, proves no retry occurs before the bounded delay, proves retries continue while it remains absent, and proves input still hides immediately.
- The real 360-second threshold is observed in the interactive user session.
- A real remote client verifies connection continuity and the composited image; tool names do not alter runtime behavior.
- `Disable`, idempotent `Enable`, and exact `Restore` all pass read-back checks.
