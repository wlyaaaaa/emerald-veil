# Product design

## Scope and display policy

Emerald Veil preserves the selected 4K green rainforest-and-cat asset, the one-shot Windows wallpaper/lock-screen recovery entry, and the original Windows-native Bubbles visual effect. The static image contract is [Windows background recovery](windows-background.md). Static wallpaper still covers all Windows monitors, including VDD and remembered detached screens. Changing that image does not start, stop or reconfigure Wallpaper Engine or Bubbles and does not require rebuilding the app; Bubbles uses the VDD-selected material/fallback at presentation entry. The rejected cat-layer experiment is not part of the product.

Automatic and explicit Bubbles display use one **physical-desktop-only** target policy. The resolver uses the current Windows device identity, prefers the primary eligible physical desktop screen, and excludes VDD, instrument displays, unknown identities and active blackout targets. It never substitutes an instrument panel or VDD for a powered-off physical screen. With no eligible screen, the installed app remains enabled and keeps measuring idle time, but launches no renderer and shows no background.

VDD is intentionally excluded because this machine's native Bubbles build did not render reliably with its display-device enumeration. The accepted fallback is no automatic Bubbles on VDD, not custom-drawn bubbles, `/t`, a compatibility DLL, a Windows binary patch or a display-driver reset. Ordinary Windows wallpaper on VDD remains enabled.

## Rendering and lifetime

After 360 seconds of reliable inactivity, the user-session watchdog manually starts the installed `%WINDIR%\System32\Bubbles.scr /s`. Before launch, the selected VDD material is composed on the eligible physical target and a DWM composition barrier completes: use the existing Wallpaper Engine VDD material/properties when a real VDD renderer selection is available, otherwise use VDD Windows wallpaper/color/fit. The native renderer then takes its normal entry snapshot. **Wallpaper Engine is never globally stopped, paused, replayed or reconfigured by this app**, including on initialization failure.

The selected VDD material is prepared by a full-size, click-through, non-activating WPF background. For Wallpaper Engine material, one exact offscreen pop-out and DWM thumbnail may be used as the material source; this is not continuous VDD screen capture. The native Bubbles window is selected only from the exact owned child process; other visible windows from that child are hidden. The HWND keeps the complete Windows-native glass/background composite with full-opacity layering, no-activate, tool-window and topmost styles at the target's physical rectangle. It is never black-color-keyed because that corrupts partially transparent glass into opaque dark rings. Microsoft continues to own the bubble colors, glass, motion, collisions, density and boundary behavior. The project does not draw replacement bubbles.

Per-Monitor V2 awareness is established before the first HWND. Window placement uses physical pixels, not guessed monitor indices or WPF DIPs. Display/DPI messages invalidate a target check rather than unconditionally restarting a launch. Actual target changes remove the old presentation before re-selection. A 100 ms background maintenance tick keeps the image immediately below the native HWND; the native layer checks its bounds/styles at 250 ms. Both verify exact process ownership, and native placement is serialized with cancellation so an old maintenance tick cannot re-show a cancelled window.

A current-session cross-process file lease and process check refuse duplicate native instances without killing an unknown owner. Every owned child is placed in a kill-on-close Job Object. Normal dismissal removes the background before native teardown and hides only a HWND still belonging to the owned process. Exit, crash and replacement cannot leave a second renderer behind. Session 0 is rejected before startup-registration or desktop commands.

Initialization is bounded: the native window deadline is five seconds and the presenter has a six-second cancellation-aware limit. Hidden, initializing and usable-window states are distinct. Failures receive a five-second cooldown, then a thirty-second cooldown; the third failure suspends automatic attempts for the current idle episode. New meaningful input, an explicit permitted retry or a target change resets recovery. Thirty seconds of stable presentation clears old failures. A one-second reconciliation check does not authorize endless relaunches.

## Input, preview and controls

The watchdog samples `GetLastInputInfo` every 50 ms. The narrow low-level in-memory classifier ignores an exact zero-displacement `WM_MOUSEMOVE`. For idle accounting it also defers a lone injected nonzero reposition; a second injected move within 250 ms confirms remote movement. The event itself is still delivered. A changed raw tick can remain pending for one sample while its hook classification arrives. Valid or still-unclassified activity is accepted by the next sample. Physical motion, a confirmed injected movement stream, buttons, wheel and keyboard dismiss the presentation. Hook installation failure does not make changed input indefinitely invisible.

The classifier retains only the previous point and minimal sequence/tick state. It records no key contents, program identities, activity logs or persistent coordinates. There is no remote-product allowlist. Remote tools share the normal interactive desktop; the app never locks the session or enters secure desktop.

Disable, unreliable input, target exclusion and meaningful activity outrank every explicit display request. The product has no user/session pause mode. `--preview` is input-dismissible and lasts at most 15 seconds. `--show-now` remains input-dismissible and cannot bypass Disable or target exclusion. Preview expiry without user input cannot immediately become another idle launch. Reopening an already running executable with no arguments queries status rather than forcing a preview.

The existing tray offers status/target/idle/last failure, true “start screen saver now”, bounded preview and per-user startup. There is deliberately no pause or resident-exit item: the sole resident owns `Ctrl+Win+E`, so casually terminating it would silently disable the shortcut. The resident registers the same executable with Windows application restart for unexpected crash/hang or Restart Manager recovery; this adds no second process, service or task. Double-clicking the tray icon starts the true screen saver. `Ctrl+Win+E` does the same through the already-required `WH_KEYBOARD_LL` observer; no second hotkey service/task is installed. The exact chord is consumed, then the controller waits for every chord key to be released and for a 250 ms stable-input interval before baselining, so its own key-up cannot dismiss the new presentation. `--status` uses a bounded current-user/current-session named pipe and does not create a missing controller. No service, new task, network listener, persistent telemetry or extra watchdog is introduced.

## Windows configuration and restoration

`Set-NativeBubbles.ps1 -Action Enable` maintains the following per-user values and reads back their types and runtime state:

| Location | Value | Required setting |
| --- | --- | --- |
| `HKCU\Control Panel\Desktop` | `SCRNSAVE.EXE` | absolute system `Bubbles.scr` path, REG_SZ |
| same | `ScreenSaveTimeOut` | `360`, REG_SZ |
| same | `ScreenSaveActive` | `0`, REG_SZ |
| same | `ScreenSaverIsSecure` | `0`, REG_SZ |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Screensavers\Bubbles` | `Radius` | `1130000000`, REG_DWORD |
| same Bubbles key | `ShowBubbles` | `1`, REG_DWORD |
| same Bubbles key | `MaterialGlass` | `1`, REG_DWORD |
| `HKCU\Software\EmeraldVeil` | `NativeBubblesEnabled` | `1` enabled, `0` explicitly disabled, REG_DWORD |

Windows' own automatic trigger stays off to prevent a competing screen saver. **This is not the same as disabling Emerald Veil.** The app's enabled flag and direct `HKCU\...\Run` value `Emerald Veil Native Bubbles` remain active. A normal login starts the app enabled; there is no separate session-pause state. The app checks runtime active/timeout/secure settings every 30 seconds and repairs only observed drift to false/360/false. The native radius is an undocumented Windows implementation detail; the app does not derive size or density from DPI, monitor inches or a replacement renderer, and updates require real visual verification.

The first Enable flushes exact registry presence/kind/value and runtime state to the durable local `native-bubbles-preimage.json`; repeated enables validate but never overwrite it. Restore supports the existing v1/v2/v3/v4 preimages. Migration preserves the original value of each newly managed setting before first write. Disable first prevents relaunch by clearing the enabled flag and Windows automatic trigger, then stops matching system-path renderers only in the current user session. Cleanup failure must not roll the flags back to enabled. Remove deletes only the owned startup registration and installation. Installation stages and hashes the single executable, and rollback can restore `.previous` only if the current attempt actually replaced the target; an unrelated older backup is never used after a pre-replacement failure.

## Blackout coordination

A blackout owner holds the lifetime marker `Local\EmeraldVeil.ExternalProtectionPause` with `initiallyOwned:false`; no wait or ReleaseMutex is required. A legacy unscoped marker excludes all targets. A scoped per-monitor marker excludes that monitor. The shared idle clock continues; removing the last marker returns to the normal target/idle policy. No blackout marker changes the enabled flag, startup registration, screen-saver timeout or topology. The physical-only policy means an excluded physical screen does not redirect Bubbles to VDD.

## Deferred visual refinements

A left-edge background wrap or offset can still occur with Wallpaper Engine on a multi-display desktop. Exact source-client geometry and DWM sampling have a tested candidate, but it has not completed publish-build, deployment and physical-screen acceptance; it is not part of the installed runtime. Do not describe the defect as fixed, conceal it with a mask, or apply a universal 200-pixel offset without checking the actual material.

A modest native Bubbles speed increase is also deferred. The shipped profile does not manage `TurbulenceSpeed` or `TurbulenceForce`; no custom renderer, binary patch or unverified motion preset is shipped. Resume these refinements only for an explicit follow-up, not through a new automatic task. Deferral closes the bounded maintenance scope, not the missing visual acceptance.

## Acceptance and limits

Release tests cover input classification, preview cancellation/deadlines, `Ctrl+Win+E` source contract and release-safe baseline, positive physical identity, VDD/instrument/unknown exclusions, recovery delays/suspension, source teardown order, exact ownership, reversible native visual-profile migration, bounded control and emergency disable. Build, policy tests, script parsing, installed/source SHA-256 parity and resident/hotkey acceptance are separate checks. Installer `Verify` must see exactly one healthy interactive resident with `hotKeyAvailable=true`. Final delivery then performs a real host-injected `Ctrl+Win+E` after every install/cleanup action and observes native Bubbles appear and dismiss on input; a prior synthetic E2E cannot stand in for this final steady-state check. The installer refuses before replacement when its host job has JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE because a child resident would otherwise appear healthy briefly and die when the deployment command returns. Normal user PowerShell and logon are unaffected; automated deployment must use an out-of-job interactive-user launcher.

A VDD-only live acceptance keeps the app enabled, exercises status and duplicate startup, and observes a real six-minute interval with zero renderer launches and no background takeover. It must not be reported as successful physical-screen animation. With the physical main powered on, separately observe native moving pixels over the selected VDD material/fallback at the real 360-second threshold, input exit, reconnect/mixed-DPI behavior, a real remote client and natural login/reboot. A valid process or HWND is not proof of moving pixels; unperformed observations remain explicitly unverified.

The project does not redistribute Microsoft components. It does not alter native collision behavior or guarantee OLED burn-in prevention. Display-off timers, brightness and hardware panel maintenance remain independent. Machine-specific evidence, screenshots, snapshots and original settings stay outside this public repository.