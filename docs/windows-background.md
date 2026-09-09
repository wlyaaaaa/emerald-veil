# Windows 壁纸与锁屏恢复

当前选用 **雨林黑猫 · 更绿版**的 3840 × 2160 图片，保留用户选定的绿色雨林、苔石和右上角黑猫。所有 Windows 屏幕（包括 VDD）的底层桌面壁纸、真正的系统锁屏，以及泡泡程序内嵌背景，共用 `assets/verdant-rain-4k.png`。已有 Wallpaper Engine 继续显示自己的内容。本设置入口不修改 Wallpaper Engine，不安装或启停泡泡程序；泡泡内嵌素材更新后仍需重新构建并安装程序。

Windows 与泡泡底板采用同一静态图。来源尺寸与处理方式记录在选图清单中，不把放大后的尺寸当作原生生成分辨率。动态雨幕仍保存在 `experiments/verdant-rain`，其原画参考继续保留；失败的猫分层方案不属于选用资产。PNG 是 sRGB SDR，未宣称 HDR 或 10 位输出。

## 现在恢复或换机

下载或克隆本仓库，在项目目录中运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Set-WindowsBackground.ps1 -Action Apply
```

同一条命令既用于第一次设置，也用于以后恢复。只需 Windows 自带的 64 位 Windows PowerShell；从 PowerShell 7 启动也会自动调用它。无需管理员权限、SDK、Wallpaper Engine 或原电脑的盘符与用户名。

恢复需要保留同一项目目录下的三个文件：

- `scripts/Set-WindowsBackground.ps1`
- `assets/windows-background.json`：选图、校验值、来源尺寸和处理方式。
- `assets/verdant-rain-4k.png`：已验收静帧，直接使用，不重新生成。

脚本把 Windows 使用的静帧复制到当前用户的 `%USERPROFILE%\Pictures\EmeraldVeil`，把回滚记录和原图备份留在 `%LOCALAPPDATA%\EmeraldVeil\windows-background`，因此设置后搬动项目目录不影响当前壁纸。Windows 的桌面接口在部分 Windows 11 版本会拒绝从 `AppData\Local` 读取壁纸，所以显示文件使用用户图片目录，仍是同一份 PNG 和同一校验值。脚本通过 Windows 桌面接口和锁屏接口设置图片，明确选择固定图片模式，并通过 `IDesktopWallpaper` 设置共同底图、清除每屏旧选图，逐块回读已连接显示器及 Windows 仍记得的离线显示器。VDD 无需固定设备编号，也不改变显示器连接或排列。只有所有屏幕的图像内容、固定模式和持久副本引用以及锁屏均正确时才跳过；确有漂移才重设，并使用新输入文件名避免 Windows 缓存旧图。

只检查而不改动：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Set-WindowsBackground.ps1 -Action Verify
```

所有屏幕与锁屏都符合选图时返回 `status: verified` 和退出码 0；`desktop.monitors` 列出每块屏幕的连接状态、路径和匹配结果，否则返回漂移或实际系统错误。锁屏还会读取图像内容并比较解码后的画面，避免只看路径或把缓存重编码误判成失败。系统接口回读不等于实际进入锁屏后的视觉验收；脚本不会锁定电脑。

## 原设置与现有应用

第一次应用前，在同一用户数据目录中保留 `before-first-apply.json`、原桌面图（可读取时）、每屏原图和原锁屏图片。已有记录仅补录此前没有采集的每屏设置，不覆盖原恢复信息。需要撤回时可在 Windows“设置 → 个性化”选回记录中的原图；这些机器私有记录不进入仓库，也不需要带到新电脑。

Wallpaper Engine 的动画可以覆盖 Windows 底图，属于预期行为。如果以后主动启用了它的“覆盖系统壁纸”或“覆盖锁屏图”，对应原生图片可能再次改变；本脚本不改它的开关，也不在后台争抢。需要时重新运行 `Apply`。本入口不增加开机项、服务或定时任务。
