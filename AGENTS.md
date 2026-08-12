# Emerald Veil project rules

- Product behavior is defined in `docs/product-design.md`; keep it synchronized with material runtime changes.
- The supported product entry point is `scripts/Set-NativeBubbles.ps1`. The custom WPF/D3D overlay is legacy source and is not the active installed product.
- Native Bubbles configuration must remain per-user and reversible. The only allowed helper startup is the project's owned direct WinExe Run value (`Emerald Veil Native Bubbles`); never add a scheduled task, shell wrapper, PowerShell wrapper, or second helper. Preserve exact registry value presence, kind, value, and Windows runtime state in the durable preimage.
- `Disable` is the emergency path: it must turn automatic screen-saver activation off even when unrelated Bubbles settings have drifted.
- Do not vendor `Bubbles.scr`, Microsoft assets, or copied Microsoft visual code. Use the Windows-installed system component and verify it exists before configuration.
- Do not add screen capture, telemetry, networking, persistent activity logging, or global input hooks.
- The repository is public. Never add credentials, machine snapshots, raw logs, private screenshots, local databases, personal paths, or a captured preimage.
