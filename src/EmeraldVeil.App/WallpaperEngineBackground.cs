using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace EmeraldVeil.App;

/// <summary>One temporary official Wallpaper Engine window, never its desktop instances.</summary>
internal sealed class WallpaperEngineBackground : IDisposable
{
    private readonly VddBackground _source;
    private readonly string _name = $"EmeraldVeil.Background.{Environment.ProcessId}.{Guid.NewGuid():N}";
    private nint _handle;
    private nint _thumbnail;
    private System.Drawing.Size _thumbnailSize;
    private bool _closed;
    private Task _closeTask = Task.CompletedTask;
    internal nint Handle => IsAlive ? _handle : nint.Zero;
    internal string WindowName => _name;
    internal bool IsAlive
    {
        get
        {
            if (_closed || _handle == nint.Zero || !NativeMethods.IsWindowVisible(_handle)) return false;
            _ = NativeMethods.GetWindowThreadProcessId(_handle, out uint owner);
            return owner == _source.EnginePid && ReadTitle(_handle) == _name;
        }
    }
    internal WallpaperEngineBackground(VddBackground source) => _source = source;
    private WallpaperEngineBackground(VddBackground source, string name, nint handle)
    { _source = source; _name = name; _handle = handle; }

    internal static async Task RecoverStaleAsync()
    {
        // This is a one-shot startup repair, not another watchdog or service.
        foreach (var engine in Process.GetProcessesByName("wallpaper64").Concat(Process.GetProcessesByName("wallpaper32")))
        {
            using (engine)
            {
                try
                {
                    if (engine.HasExited || engine.SessionId != Process.GetCurrentProcess().SessionId) continue;
                    string? executable = engine.MainModule?.FileName;
                    if (executable is null) continue;
                    var stale = new List<(string Name, nint Handle)>();
                    NativeMethods.EnumWindows((handle, unused) =>
                    {
                        _ = NativeMethods.GetWindowThreadProcessId(handle, out uint pid);
                        if (pid != engine.Id) return true;
                        string name = ReadTitle(handle);
                        string[] parts = name.Split('.');
                        if (parts.Length != 4 || parts[0] != "EmeraldVeil" || parts[1] != "Background" ||
                            !int.TryParse(parts[2], out int owner) || !Guid.TryParseExact(parts[3], "N", out _)) return true;
                        try { using var process = Process.GetProcessById(owner); if (!process.HasExited) return true; }
                        catch (ArgumentException) { }
                        stale.Add((name, handle));
                        return true;
                    }, nint.Zero);
                    foreach (var window in stale)
                    {
                        var orphan = new WallpaperEngineBackground(new VddBackground("", "", 0, 0,
                            engine.Id, executable), window.Name, window.Handle);
                        orphan.Dispose();
                        await orphan.Closed.ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or
                    System.ComponentModel.Win32Exception or ArgumentException) { }
            }
        }
    }


    internal async Task OpenAsync(nint ownerWindow, Rectangle bounds, CancellationToken cancellation)
    {
        try
        {
            await CommandAsync(cancellation, "-control", "openWallpaper", "-file", _source.Selection!.File,
                "-playInWindow", _name, "-width", bounds.Width.ToString(), "-height", bounds.Height.ToString(),
                "-x", "-32000", "-y", "-32000", "-borderless");
            long deadline = Environment.TickCount64 + 3000;
            while (_handle == nint.Zero && Environment.TickCount64 < deadline)
            {
                cancellation.ThrowIfCancellationRequested();
                NativeMethods.EnumWindows((handle, unused) =>
                {
                    _ = NativeMethods.GetWindowThreadProcessId(handle, out uint pid);
                    if (pid == _source.EnginePid && ReadTitle(handle) == _name) _handle = handle;
                    return true;
                }, nint.Zero);
                if (_handle == nint.Zero) await Task.Delay(25, cancellation);
            }
            if (_handle == nint.Zero) throw new InvalidOperationException("Wallpaper Engine did not create the named background.");
            await CommandAsync(cancellation, "-control", "applyProperties", "-location", _name,
                "-properties", "RAW~(" + _source.Selection.Properties + ")~END");
            cancellation.ThrowIfCancellationRequested();
            int result = DwmRegisterThumbnail(ownerWindow, _handle, out _thumbnail);
            if (result != 0) Marshal.ThrowExceptionForHR(result);
            UpdateSize(bounds);

        }
        catch { Dispose(); throw; }
    }

    internal void UpdateSize(Rectangle bounds)
    {
        if (!IsAlive || _thumbnail == nint.Zero)
            throw new InvalidOperationException("The named Wallpaper Engine background is no longer available.");
        if (_thumbnailSize == bounds.Size) return;
        var properties = new ThumbnailProperties {
            Flags = 0x1D, Destination = new NativeMethods.Rect { Right=bounds.Width, Bottom=bounds.Height },
            Opacity = 255, Visible = true, SourceClientAreaOnly = true,
        };
        int result = DwmUpdateThumbnailProperties(_thumbnail, ref properties);
        if (result != 0) Marshal.ThrowExceptionForHR(result);
        _thumbnailSize = bounds.Size;
    }

    private async Task CommandAsync(CancellationToken cancellation, params string[] arguments)
    {
        using var parent = Process.GetProcessById(_source.EnginePid);
        if (parent.HasExited || parent.SessionId != Process.GetCurrentProcess().SessionId ||
            !string.Equals(parent.MainModule?.FileName, _source.EngineExe, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The original user-session Wallpaper Engine is unavailable.");
        var start = new ProcessStartInfo(_source.EngineExe!) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(_source.EngineExe!)! };
        if (arguments.Length == 6 && arguments[1] == "applyProperties")
        {
            // RAW is parsed by Wallpaper Engine before ordinary Windows argument
            // unescaping. The only location here is this invocation's GUID name.
            start.Arguments = "-control applyProperties -location " + _name + " -properties " + arguments[5];
        }
        else foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var command = Process.Start(start) ?? throw new InvalidOperationException("Wallpaper command did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try { await command.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch { if (!command.HasExited) command.Kill(); throw; }
        if (command.ExitCode != 0) throw new InvalidOperationException($"Wallpaper {arguments[1]} command returned {command.ExitCode}.");
    }

    public void Dispose()
    {
        if (_closed) return;
        if (_thumbnail != nint.Zero) { _ = DwmUnregisterThumbnail(_thumbnail); _thumbnail = nint.Zero; }
        // The external popup never leaves its offscreen coordinates. Even a
        // controller crash cannot leave it covering the user's physical display.
        _closed = true;
        // The name belongs only to this invocation. A late close cannot affect a
        // new presentation or a desktop/another application's wallpaper window.
        _closeTask = CloseAsync();
    }
    internal Task Closed => _closeTask;
    private async Task CloseAsync()
    {
        try { await CommandAsync(CancellationToken.None, "-control", "closeWallpaper", "-location", _name).ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or
            System.ComponentModel.Win32Exception or OperationCanceledException) { }
        if (_handle != nint.Zero)
        {
            _ = NativeMethods.GetWindowThreadProcessId(_handle, out uint owner);
            if (owner == _source.EnginePid && ReadTitle(_handle) == _name)
                _ = PostMessage(_handle, 0x0010, nint.Zero, nint.Zero);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        internal uint Flags;
        internal NativeMethods.Rect Destination, Source;
        internal byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] internal bool Visible;
        [MarshalAs(UnmanagedType.Bool)] internal bool SourceClientAreaOnly;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    private static string ReadTitle(nint handle)
    {
        var text = new StringBuilder(256); _ = GetWindowText(handle, text, text.Capacity); return text.ToString();
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int count);
}