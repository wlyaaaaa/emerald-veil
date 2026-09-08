# Windows 壁纸与锁屏恢复

当前选用 **青雨 · 第二幕**的 3840 × 2160 静帧，保留原画与猫的完整轮廓。它设置 Windows 底层桌面壁纸和真正的系统锁屏图片；已有 Wallpaper Engine 继续显示自己的内容。本入口不修改 Wallpaper Engine，不安装或启停泡泡程序。

Windows 这两处采用静态图。动态雨幕仍保存在 `experiments/verdant-rain`，以后可独立打开；失败的猫分层方案不属于选用资产。PNG 是 sRGB SDR，未宣称 HDR 或 10 位输出。

## 现在恢复或换机

下载或克隆本仓库，在项目目录中运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Set-WindowsBackground.ps1 -Action Apply
```

同一条命令既用于第一次设置，也用于以后恢复。只需 Windows 自带的 64 位 Windows PowerShell；从 PowerShell 7 启动也会自动调用它。无需管理员权限、SDK、Wallpaper Engine 或原电脑的盘符与用户名。

恢复需要保留同一项目目录下的三个文件：

- `scripts/Set-WindowsBackground.ps1`
- `assets/windows-background.json`：选图、校验值及当时的雨幕参数。
- `assets/verdant-rain-4k.png`：已验收静帧，直接使用，不重新生成。

脚本把静帧复制到当前用户的 `%LOCALAPPDATA%\EmeraldVeil\windows-background`，因此设置后搬动项目目录不影响当前壁纸。它通过 Windows 桌面接口和锁屏接口设置图片，明确选择固定图片模式，随后回读两处实际状态。只有图像内容、固定模式和持久副本引用都正确时才跳过；确有漂移才重设，并使用新输入文件名避免 Windows 缓存旧图。

只检查而不改动：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Set-WindowsBackground.ps1 -Action Verify
```

两处都符合选图时返回 `status: verified` 和退出码 0；否则返回漂移或实际系统错误。锁屏还会读取图像内容并比较解码后的画面，避免只看路径或把缓存重编码误判成失败。系统接口回读不等于实际进入锁屏后的视觉验收；脚本不会锁定电脑。

## 原设置与现有应用

第一次应用前，在同一用户数据目录中保留 `before-first-apply.json`、原桌面图（可读取时）和原锁屏图片。重复应用不会覆盖这份记录。需要撤回时可在 Windows“设置 → 个性化”选回记录中的原图；这些机器私有记录不进入仓库，也不需要带到新电脑。

Wallpaper Engine 的动画可以覆盖 Windows 底图，属于预期行为。如果以后主动启用了它的“覆盖系统壁纸”或“覆盖锁屏图”，对应原生图片可能再次改变；本脚本不改它的开关，也不在后台争抢。需要时重新运行 `Apply`。本入口不增加开机项、服务或定时任务。
