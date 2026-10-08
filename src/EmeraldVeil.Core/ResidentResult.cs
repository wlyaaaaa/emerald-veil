using System.Text.Json;
namespace EmeraldVeil.Core;
public static class ResidentResult {
    public static void TryWrite(string path, string state, string reason) {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new {
                schema = "emerald-veil.result.v1", state, result = "resident_" + state, reason_zh = reason,
                observed_at = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)), process_id = Environment.ProcessId }));
            File.Move(path + ".tmp", path, overwrite: true);
        } catch (Exception) { }
    }
}
