using System.Runtime.InteropServices;
using EmeraldVeil.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace EmeraldVeil.App;

internal static class DisplayTargetResolver
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);

    internal static VeilDisplay? Resolve()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(@"Software\EmeraldVeil");
        var excluded = settings?.GetValue("ExcludedMonitorIds") as string[] ?? [];
        var screens = Forms.Screen.AllScreens.Select(screen =>
        {
            string identity = ReadIdentity(screen.DeviceName);
            bool instrument = identity.StartsWith(@"MONITOR\TUR0000\", StringComparison.OrdinalIgnoreCase) ||
                excluded.Any(prefix => !string.IsNullOrWhiteSpace(prefix) &&
                    identity.StartsWith(prefix.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
            return new VeilDisplay(screen.DeviceName, screen.Bounds, screen.Primary,
                identity.StartsWith(@"MONITOR\MTT1337\", StringComparison.OrdinalIgnoreCase),
                instrument, ExternalProtectionPause.IsActive(ExternalProtectionPause.ForMonitor(screen.DeviceName)),
                IsIdentityKnown: !string.IsNullOrWhiteSpace(identity));
        }).ToArray();
        // Old blackout clients remain safe. A scoped marker allows every other display to keep working.
        bool legacyOnly = ExternalProtectionPause.IsActive() && !screens.Any(screen => screen.IsBlackedOut);
        return DisplayTargetPolicy.Select(screens, legacyOnly);
    }

    private static string ReadIdentity(string deviceName)
    {
        for (uint index = 0; index < 16; index++)
        {
            var info = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(deviceName, index, ref info, 0)) break;
            if ((info.Flags & 1) != 0) return info.Id ?? string.Empty;
        }
        return string.Empty;
    }
}
