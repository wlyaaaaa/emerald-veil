# Product design

## Product contract

Emerald Veil protects a Windows OLED display without replacing the foreground experience. Its default state machine is:

1. **Hidden** — while the session has had input in the last five minutes.
2. **Idle** — a click-through, non-activating emerald veil covers the primary display.
3. **Hidden again** — any newly observed keyboard or mouse input dismisses the veil.
4. **Preview** — an explicit tray command shows the same effect for at most 15 seconds; input also dismisses it.
5. **Paused** — automatic activation stays disabled until resumed.

The veil is intentionally not a conventional lock-screen screensaver: the original application remains visible and continues running.

## Visual system

The base layer is translucent black at roughly 26% opacity. Four oversized radial fields use low-alpha near-black emerald colors and independent multi-minute paths. They have no sharp boundary, text, logo, blur, or bright highlight. The result is a quiet dark-green atmospheric shift rather than visible objects moving across the screen.

The base dimming is the meaningful OLED intervention. The moving fields keep the added layer from being static, but cannot move or erase the original pixels underneath. The product therefore avoids claiming burn-in prevention or guaranteed panel-life extension.

## Architecture

```text
GetLastInputInfo + GetTickCount/GetTickCount64
                |
                v
        IdleTimeline (pure core)
                |
                v
       VeilActivationPolicy
                |
                v
   VeilController (50 ms monitor)
                |
                v
  VeilWindow + VeilSurface (WPF)
```

- `EmeraldVeil.Core` reconstructs a conservative 64-bit idle timeline from Win32's 32-bit last-input timestamp. A failed or ambiguous read keeps the veil hidden.
- `EmeraldVeil.App` owns the Windows session monitor, tray icon, startup setting, and WPF presentation.
- The WPF window uses `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, and `WS_EX_TOOLWINDOW`, plus `HTTRANSPARENT` and `MA_NOACTIVATE` message handling.
- The rendering timer runs at about 15 FPS only while the veil is visible. Hidden mode stops animation and hides the window.
- Startup is a direct per-user WinExe launch from a stable LocalAppData installation directory.

## Input-clock rules

`GetLastInputInfo` is session-specific and its 32-bit timestamp is not guaranteed to increase monotonically. Emerald Veil therefore:

- treats any timestamp change, including a backward change, as activity;
- uses unsigned modulo arithmetic for the initial short-duration reconstruction;
- treats an initial future/ambiguous timestamp conservatively as unknown;
- carries continuous runtime duration on `GetTickCount64`;
- stays hidden when the API read fails.

Polling a last-known state is best effort rather than a mathematical event-stream guarantee. The 50 ms cadence is chosen to dismiss quickly without installing global keyboard or mouse hooks.

## Scope and known limits

- v0.1 protects the Windows primary display only.
- Windows secure desktop, UAC prompts, lock screen, exclusive fullscreen applications, and other topmost windows can appear above it.
- Touch and pen behavior is not claimed until separately tested.
- An unresponsive WPF UI thread can delay hiding.
- Initial idle age older than the 32-bit tick horizon cannot always be reconstructed exactly; ambiguous state fails closed by keeping the veil hidden.

## Acceptance checklist

- Core unit tests pass.
- Release publish produces a self-contained x64 WinExe.
- Visible overlay has the required extended styles, primary-screen bounds, and `HTTRANSPARENT` response.
- Showing the veil does not change the foreground window.
- Injected benign input hides the veil within the measured runtime target.
- The installed process has no PowerShell, cmd, wscript, or console host child.
- The HKCU Run value points directly to the installed executable and passes read-back verification.
