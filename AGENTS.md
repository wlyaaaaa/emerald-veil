# Emerald Veil project rules

- Product behavior is defined in `docs/product-design.md`; keep it synchronized with material runtime changes.
- The supported build and test entry points are `dotnet build EmeraldVeil.slnx` and `dotnet test EmeraldVeil.slnx`.
- `artifacts/`, `bin/`, and `obj/` are generated and must remain untracked.
- Keep input-clock decisions in `EmeraldVeil.Core` so rollover and failure behavior remains unit-testable without WPF.
- The foreground veil must remain non-activating, absent from Alt+Tab, and click-through. Do not add global input hooks, screen capture, telemetry, networking, or persistent activity logging without an explicit product decision and corresponding documentation.
- The repository is public. Never add credentials, machine snapshots, raw logs, private screenshots, local databases, or personal paths.
