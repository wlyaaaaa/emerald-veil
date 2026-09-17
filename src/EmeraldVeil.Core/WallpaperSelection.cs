using System.Text.Json;
namespace EmeraldVeil.Core;

public sealed record WallpaperSelection(string Location, string File, string Properties)
{
    public static WallpaperSelection? Read(string json, string user, string monitorId)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(user, out var profile) ||
            !profile.TryGetProperty("general", out var general) ||
            !general.TryGetProperty("user", out var settings) ||
            !settings.TryGetProperty("monitormap", out var map)) return null;
        static string Normalize(string value) => value.Replace('\\', '/').ToUpperInvariant();
        var exact = map.EnumerateObject().Where(item => Normalize(item.Name) == Normalize(monitorId)).ToArray();
        if (exact.Length != 1 || !exact[0].Value.TryGetProperty("location", out var index) ||
            !index.TryGetInt32(out int number) || number < 0) return null;
        string location = "Monitor" + number;
        if (!general.TryGetProperty("wallpaperconfig", out var configuration) ||
            !configuration.TryGetProperty("selectedwallpapers", out var selected) ||
            !selected.TryGetProperty(location, out var wallpaper) ||
            !wallpaper.TryGetProperty("file", out var fileValue)) return null;
        string? file = fileValue.GetString();
        if (string.IsNullOrWhiteSpace(file)) return null;
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (profile.TryGetProperty("wproperties", out var allProperties))
        {
            foreach (var item in allProperties.EnumerateObject())
            {
                if (Normalize(item.Name) != Normalize(file) ||
                    !item.Value.TryGetProperty(location, out var values)) continue;
                foreach (var property in values.EnumerateObject()) properties[property.Name] = property.Value.Clone();
                break;
            }
        }
        // Original VDD audio is preserved; a second visual must not double it.
        properties["volume"] = JsonSerializer.SerializeToElement(0);
        return new WallpaperSelection(location, file, JsonSerializer.Serialize(properties).Replace(")~END", @"\u0029~END", StringComparison.Ordinal));
    }
}