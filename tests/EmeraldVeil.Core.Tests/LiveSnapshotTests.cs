using System.Text.Json;
using EmeraldVeil.Core;

namespace EmeraldVeil.Core.Tests;

public class LiveSnapshotTests
{
    [Theory]
    [InlineData(false, false, false, false, "关着")]
    [InlineData(true, false, false, true, "出错停了")]
    [InlineData(true, false, false, false, "待命：没有能放的显示器")]
    [InlineData(true, true, false, false, "待命")]
    [InlineData(true, true, true, false, "正在放")]
    public void ReportsSessionCountsWithoutPublishingIdentityOrFailurePaths(bool enabled, bool target, bool visible, bool suspended, string expected)
    {
        var status = JsonSerializer.SerializeToElement(new { enabled, hotKeyAvailable = true, presentation = new {
            visible, eligibleTarget = target ? "PRIVATE-MONITOR" : null, automaticRetrySuspended = suspended,
            launchCount = 3, failureCount = 2, consecutiveFailures = 0, lastFailure = @"C:\PRIVATE\failed.png" } });
        string json = JsonSerializer.Serialize(LiveSnapshot.Project(status, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        var metrics = JsonDocument.Parse(json).RootElement.GetProperty("metrics");
        Assert.Equal(expected, metrics.GetProperty("status").GetString());
        Assert.Equal(3, metrics.GetProperty("session").GetProperty("attempts").GetInt32());
        Assert.Equal(2, metrics.GetProperty("session").GetProperty("failures").GetInt32());
        Assert.DoesNotContain("PRIVATE", json);
    }
}
