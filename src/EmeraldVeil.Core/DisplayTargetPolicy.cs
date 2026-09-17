using System.Drawing;

namespace EmeraldVeil.Core;

public sealed record VeilDisplay(string DeviceName, Rectangle Bounds,
    bool IsPrimary, bool IsVirtual, bool IsInstrument, bool IsBlackedOut, bool IsIdentityKnown = true);

public static class DisplayTargetPolicy
{
    public static VeilDisplay? Select(IEnumerable<VeilDisplay> displays, bool unscopedProtection = false)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (unscopedProtection) return null;
        return displays.Where(display => display.IsIdentityKnown && !display.IsVirtual && !display.IsInstrument && !display.IsBlackedOut &&
                display.Bounds.Width > 0 && display.Bounds.Height > 0)
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
