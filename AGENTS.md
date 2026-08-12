# Emerald Veil project rules

- Product behavior is defined in `docs/product-design.md`; keep it synchronized with material runtime changes.
- The supported product entry point is `scripts/Set-NativeBubbles.ps1`. The visible renderer is the Windows-installed `Bubbles.scr` in `/p <owned HWND>` preview mode; `/s` full-screen mode and the custom WPF/D3D visual are not active product paths.
- Native Bubbles configuration must remain per-user and reversible. The only allowed helper startup is the project's owned direct WinExe Run value (`Emerald Veil Native Bubbles`); never add a scheduled task, shell wrapper, PowerShell wrapper, or second helper. Preserve exact registry value presence, kind, value, and Windows runtime state in the durable preimage.
- `Disable` is the emergency path: it must stop the owned preview, clear the project enabled flag, and keep Windows' own full-screen trigger off even when unrelated Bubbles settings have drifted.
- Do not vendor `Bubbles.scr`, Microsoft assets, or copied Microsoft visual code. Use the Windows-installed system component and verify it exists before configuration.
- Do not add screen capture, telemetry, networking, persistent activity logging, or global input hooks.
- Remote compatibility is architectural: remain a normal click-through preview window and never special-case a fixed list of remote-control product names.
- The repository is public. Never add credentials, machine snapshots, raw logs, private screenshots, local databases, personal paths, or a captured preimage.
