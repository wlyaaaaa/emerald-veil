using System.Text.Json;
using EmeraldVeil.Core;
namespace EmeraldVeil.Core.Tests;
public sealed class ResidentResultTests {
    [Fact] public void FreshSuccessReplacesTheFailureReason()
    {
        var path = Path.Combine(Path.GetTempPath(), "EmeraldVeil.Tests", Guid.NewGuid().ToString("N"), "last-result.json");
        ResidentResult.TryWrite(path, "failed", "屏保常驻程序异常退出：测试异常");
        using var failed = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        Assert.Equal("屏保常驻程序异常退出：测试异常", failed.RootElement.GetProperty("reason_zh").GetString());
        var failedTime = failed.RootElement.GetProperty("observed_at").GetDateTimeOffset();
        Thread.Sleep(20);
        ResidentResult.TryWrite(path, "success", "屏保常驻程序已启动");
        using var success = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("success", success.RootElement.GetProperty("state").GetString());
        Assert.Equal("屏保常驻程序已启动", success.RootElement.GetProperty("reason_zh").GetString());
        Assert.True(success.RootElement.GetProperty("observed_at").GetDateTimeOffset() > failedTime);
    }
}
