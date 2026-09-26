# Emerald Veil

1. **这是什么：**在 Windows 11 实体桌面屏空闲六分钟后显示系统自带的彩色泡泡；另把选定的雨林黑猫图设为各屏壁纸和锁屏，保留现有 Wallpaper Engine。
2. **我怎么用：**先按 [产品说明](docs/product-design.md)构建并安装程序，再运行 `pwsh -File .\scripts\Set-NativeBubbles.ps1 -Action Enable`；设置静态壁纸用 `powershell.exe -File .\scripts\Set-WindowsBackground.ps1 -Action Apply`。
3. **怎么知道它正常：**运行 `pwsh -File .\scripts\Set-NativeBubbles.ps1 -Action Verify` 和壁纸脚本的 `-Action Verify`；实体屏上实际看到泡泡运动，才算画面验收。
4. **坏了怎么提醒我：**托盘可看最近失败，但没有主动通知；发现异常直接跟 AI 说，紧急停用运行 `pwsh -File .\scripts\Set-NativeBubbles.ps1 -Action Disable`。
5. **让 AI 做什么：**先读 [项目约定](AGENTS.md)、[产品说明](docs/product-design.md)及[壁纸恢复](docs/windows-background.md)，帮我核实、修复和换机恢复，并区分脚本检查与真实画面。

项目代码使用 [MIT 许可](LICENSE)；Windows 自带泡泡组件及画面仍归微软。
