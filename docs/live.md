# 实况接线

`docs/live.contract.json` 只声明公开安全的字段。原常驻的现有采样循环每两秒把小输出原子写到 `%LOCALAPPDATA%\EmeraldVeil\live.json`；正常退出写“没在运行”，强制终止留下的旧值由五秒阈值标旧。输出不含显示器身份、输入内容、持续坐标或错误中的路径，不保存历史；它不证明泡泡已通过实体屏运动验收。旧版常驻须先安装当前源码，单次手工采样不算自动接线。

原入口 `scripts/Set-WindowsBackground.ps1 -Action Verify`（或 Apply）同步更新 `%LOCALAPPDATA%\EmeraldVeil\background-live.json`，可用 `-LiveOutput` 指定位置。只有完整核验才给值，异常给 null 和原因；不改变原命令退出码。PCConfig 已有的交互刷新每 30 秒调用一次 Verify。手动核验仍走同一入口。壁纸记录只说明核验时的图，不能替代屏保常驻自己的两秒状态。

静态值由 PCConfig 的项目夜检运行 `python scripts/Update-LiveSnapshot.py`，需要时也能运行同一命令。统计 Git 已跟踪的自有 Markdown、C#/PowerShell/Python/XAML 源码与配置，代码含测试，排除二进制、生成数据和第三方；文件名单放展开记录。内容不变不重写快照、不刷新 `at`。新增文件先加入 Git 索引再取数，仓库内只保存静态公开数。两件图片产物用各自 Git 内容改动时刻，入口仍须本机存在检查；选图说明见项目页链接。

名下自动行为只有已登记的登录常驻“屏保与壁纸”，无专属技能。没有成功展示历史，不能据本次尝试／故障数推算成功次数、全历史总量或成功率。PCConfig 已登记屏保状态、壁纸核验和静态规模三份来源。
