using System.Runtime.InteropServices;

namespace EmeraldVeil.App;

internal static class NativeMethods
{
    internal const uint SpiGetScreenSaveActive = 0x0010;
    internal const int GwlExStyle = -20;
    internal const long WsExTransparent = 0x0000_0020L;
    internal const long WsExToolWindow = 0x0000_0080L;
    internal const long WsExLayered = 0x0008_0000L;
    internal const long WsExNoActivate = 0x0800_0000L;

    internal const int WmNchittest = 0x0084;
    internal const int WmMouseActivate = 0x0021;
    internal const int WmDisplayChange = 0x007E;
    internal const int WmDpiChanged = 0x02E0;
    internal const int HtTransparent = -1;
    internal const int MaNoActivate = 3;

    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;

    internal static readonly nint HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInputInfo
    {
        internal uint CbSize;
        internal uint DwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoGet(
        uint action,
        uint parameter,
        ref uint value,
        uint flags);

    [DllImport("kernel32.dll")]
    internal static extern uint GetTickCount();

    [DllImport("kernel32.dll")]
    internal static extern ulong GetTickCount64();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint windowHandle, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint windowHandle, out Rect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint iconHandle);

    internal static nint GetWindowLongPtr(nint windowHandle, int index) =>
        nint.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : new nint(GetWindowLong32(windowHandle, index));

    internal static bool GetScreenSaverActive()
    {
        uint value = 0;
        if (!SystemParametersInfoGet(SpiGetScreenSaveActive, 0, ref value, 0))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        return value != 0;
    }

    internal static void SetWindowLongPtr(nint windowHandle, int index, nint value)
    {
        if (nint.Size == 8)
        {
            _ = SetWindowLongPtr64(windowHandle, index, value);
        }
        else
        {
            _ = SetWindowLong32(windowHandle, index, value.ToInt32());
        }
    }
}
