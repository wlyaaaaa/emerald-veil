# Emerald Veil

Emerald Veil is a lightweight, click-through OLED protection layer for Windows. After five minutes without keyboard or mouse input, it places a dark, slowly drifting emerald veil over the primary display. Everything underneath remains visible and interactive. The first new input hides the veil immediately.

> 中文：无操作 5 分钟后，主屏会出现一层低亮、缓慢漂移的深绿色保护层；Codex 和其他窗口仍然可见。鼠标或键盘一有操作，保护层立即消失。

## Why this design

The effect combines two ideas:

- a uniform dark layer reduces sustained OLED light output while preserving readability;
- large, low-contrast emerald fields move slowly so the veil does not add a fixed high-contrast pattern of its own.

This is deliberately calmer than a bubble screensaver. It has no labels, sharp edges, particles, blur, or bright objects. The animation runs at 15 FPS only while visible and stops completely when hidden.

Emerald Veil is an extra risk-reduction layer, not a substitute for the display's built-in pixel refresh, pixel orbiting, taskbar dimming, or normal brightness management. Because the original image remains visible, its pixels are still active; the protective benefit comes primarily from reduced luminance.

## Behavior

- Activates after 5 minutes of session keyboard/mouse inactivity.
- Covers the Windows primary display in v0.1.
- Never takes focus and is absent from Alt+Tab and the taskbar.
- Passes mouse input through to the application underneath.
- Polls the Windows last-input clock every 50 ms and hides at high dispatcher priority.
- Includes a tray menu for a 15-second preview, pause, start-with-Windows, and exit.
- Captures no screen content, stores no activity history, opens no network connection, and has no telemetry.

## Install

Windows 11 x64 is the tested target. PowerShell 7 and the .NET 10 SDK are needed to build from source.

```powershell
dotnet publish .\src\EmeraldVeil.App\EmeraldVeil.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  --output .\artifacts\publish\win-x64

pwsh -NoProfile -File .\scripts\Install-EmeraldVeil.ps1 -Action Install
```

The installer copies the single executable to `%LOCALAPPDATA%\Programs\EmeraldVeil\EmeraldVeil.exe` and creates a direct per-user `HKCU Run` entry. It does not require administrator rights and does not use a PowerShell, cmd, or script wrapper at logon.

Verify or remove it with:

```powershell
pwsh -NoProfile -File .\scripts\Install-EmeraldVeil.ps1 -Action Verify
pwsh -NoProfile -File .\scripts\Install-EmeraldVeil.ps1 -Action Remove
```

## Development

```powershell
dotnet build .\EmeraldVeil.slnx --configuration Debug
dotnet test .\EmeraldVeil.slnx --configuration Debug
```

For a fast local idle test, run the Debug executable with `--idle-seconds=2`. `--preview` displays the effect immediately for at most 15 seconds.

The core idle-clock logic is separated from WPF and covered by tests for the five-minute boundary, Win32 tick rollover, non-monotonic input timestamps, read failure, pause, and preview behavior. See [the product design](docs/product-design.md) for the architecture and limitations.

## License

[MIT](LICENSE)
