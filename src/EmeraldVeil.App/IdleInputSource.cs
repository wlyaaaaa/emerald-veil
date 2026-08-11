using System.Runtime.InteropServices;

namespace EmeraldVeil.App;

internal readonly record struct NativeIdleSample(
    bool Succeeded,
    uint CurrentTick32,
    ulong CurrentTick64,
    uint LastInputTick32);

internal interface IIdleInputSource
{
    NativeIdleSample Read();
}

internal sealed class Win32IdleInputSource : IIdleInputSource
{
    public NativeIdleSample Read()
    {
        var info = new NativeMethods.LastInputInfo
        {
            CbSize = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>(),
        };

        var succeeded = NativeMethods.GetLastInputInfo(ref info);
        return new NativeIdleSample(
            succeeded,
            NativeMethods.GetTickCount(),
            NativeMethods.GetTickCount64(),
            info.DwTime);
    }
}
