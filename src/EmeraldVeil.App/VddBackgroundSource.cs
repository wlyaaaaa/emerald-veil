using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using EmeraldVeil.Core;

namespace EmeraldVeil.App;

internal sealed record VddBackground(string MonitorId, string Image, uint Color, int Position,
    int EnginePid = 0, string? EngineExe = null, WallpaperSelection? Selection = null)
{
    internal string Kind => Selection is null ? "windows-vdd" : "wallpaper-engine-vdd";
}

internal static class VddBackgroundSource
{
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, [MarshalAs(UnmanagedType.LPWStr)] string path);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitor, out NativeMethods.Rect rect);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
    }
    internal static VddBackground Read()
    {
        var desktop = (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"))!)!;
        VddBackground result;
        Rectangle bounds = default;
        try
        {
            var ids = Enumerable.Range(0, checked((int)desktop.GetMonitorDevicePathCount()))
                .Select(index => desktop.GetMonitorDevicePathAt((uint)index))
                .Where(id => id.Contains("#MTT1337#", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (ids.Length != 1) throw new InvalidOperationException("A unique VDD wallpaper source is required.");
            string id = ids[0];
            if (desktop.GetMonitorRECT(id, out var rect) == 0)
                bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            result = new VddBackground(id, desktop.GetWallpaper(id), desktop.GetBackgroundColor(), desktop.GetPosition());
        }
        finally { Marshal.FinalReleaseComObject(desktop); }

        // No configured executable is launched here. Only an already rendering
        // VDD window in the current session can select the dynamic path.
        var engines = new List<(int Pid, string Path)>();
        foreach (var process in Process.GetProcessesByName("wallpaper64").Concat(Process.GetProcessesByName("wallpaper32")))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != Process.GetCurrentProcess().SessionId || process.HasExited) continue;
                    string? path = process.MainModule?.FileName;
                    if (path is not null && HasDesktopOn(process.Id, bounds)) engines.Add((process.Id, path));
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        if (engines.Count != 1) return result;
        var engine = engines[0];
        try
        {
            string config = Path.Combine(Path.GetDirectoryName(engine.Path)!, "config.json");
            using var stream = new FileStream(config, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var selected = WallpaperSelection.Read(reader.ReadToEnd(), Environment.UserName, result.MonitorId);
            if (selected is not null && Path.IsPathFullyQualified(selected.File) && File.Exists(selected.File))
                return result with { EnginePid = engine.Pid, EngineExe = engine.Path, Selection = selected };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { }
        return result;
    }

    private static bool HasDesktopOn(int processId, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;
        bool found = false;
        NativeMethods.EnumWindowsProc inspect = (handle, unused) =>
        {
            _ = NativeMethods.GetWindowThreadProcessId(handle, out uint owner);
            if (owner != processId || !NativeMethods.IsWindowVisible(handle)) return true;
            var name = new StringBuilder(128);
            _ = GetClassName(handle, name, name.Capacity);
            if (!name.ToString().StartsWith("WPEDesktop", StringComparison.Ordinal)) return true;
            if (NativeMethods.GetWindowRect(handle, out var r) &&
                Math.Abs(r.Left - bounds.Left) <= 2 && Math.Abs(r.Top - bounds.Top) <= 2 &&
                Math.Abs(r.Right - bounds.Right) <= 2 && Math.Abs(r.Bottom - bounds.Bottom) <= 2) found = true;
            return true;
        };
        NativeMethods.EnumWindows((handle, parameter) =>
        {
            var name = new StringBuilder(64); _ = GetClassName(handle, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW") _ = EnumChildWindows(handle, inspect, parameter);
            return true;
        }, nint.Zero);
        return found;
    }

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, NativeMethods.EnumWindowsProc callback, nint parameter);
}