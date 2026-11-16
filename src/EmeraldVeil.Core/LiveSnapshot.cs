using System.Text.Json;

namespace EmeraldVeil.Core;

public static class LiveSnapshot
{
    private static readonly string Output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmeraldVeil", "live.json");

    public static object Project(JsonElement status, DateTimeOffset since, DateTimeOffset at)
    {
        var p = status.GetProperty("presentation");
        bool enabled = status.GetProperty("enabled").GetBoolean(), visible = p.GetProperty("visible").GetBoolean();
        bool target = p.GetProperty("eligibleTarget").ValueKind != JsonValueKind.Null;
        bool suspended = p.GetProperty("automaticRetrySuspended").GetBoolean();
        int attempts = p.GetProperty("launchCount").GetInt32(), failures = p.GetProperty("failureCount").GetInt32();
        string? failure = p.GetProperty("lastFailure").GetString();
        var beijing = since.ToOffset(TimeSpan.FromHours(8));
        return new { v = 1, at, scope = "现役程序只读状态；可用窗口不等于已观察到原生运动", session_scope = $"从 {beijing:yyyy-MM-dd HH:mm:ss zzz} 起，仅本次常驻；重启清零，不算成功率", metrics = new {
            status = !enabled ? "关着" : suspended ? "出错停了" : visible ? "正在放" : !target ? "待命：没有能放的显示器" : "待命",
            enabled = enabled ? "开着" : "关着", target = target ? "有" : "没有",
            hotkey = status.GetProperty("hotKeyAvailable").GetBoolean() ? "能" : "不能",
            session = new { text = $"{beijing:MM-dd HH:mm} 起，试着放 {attempts} 次，出错 {failures} 次", attempts, failures, since },
            last_failure = failures == 0 ? (object)0 : failure?.Contains("initialization deadline", StringComparison.Ordinal) == true ? "Windows 自带泡泡没按时出窗口" : "画面启动或维护失败",
            retry = suspended ? "不会（已暂停自动重试）" : "会（没暂停）", consecutive_failures = p.GetProperty("consecutiveFailures").GetInt32()
        }};
    }

    public static void Write(object status, DateTimeOffset since) => Save(Project(JsonSerializer.SerializeToElement(status), since, DateTimeOffset.UtcNow));
    public static void Stopped() => Save(new { v = 1, at = DateTimeOffset.UtcNow, scope = "常驻程序已退出；其他状态未取", metrics = new { status = "没在运行" } });

    private static void Save(object value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
            File.WriteAllText(Output + ".tmp", JsonSerializer.Serialize(value));
            File.Move(Output + ".tmp", Output, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Debug.WriteLine($"Live snapshot unavailable: {error.GetType().Name}"); }
    }
}
