using System.Runtime.InteropServices;
using EmeraldVeil.Core;

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
    private readonly InputActivityFilter _filter;

    internal Win32IdleInputSource(InputActivityFilter filter)
    {
        _filter = filter;
    }

    public NativeIdleSample Read()
    {
        var info = new NativeMethods.LastInputInfo
        {
            CbSize = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>(),
        };

        var succeeded = NativeMethods.GetLastInputInfo(ref info);
        uint effectiveLastInputTick = succeeded
            ? _filter.Resolve(info.DwTime)
            : info.DwTime;

        return new NativeIdleSample(
            succeeded,
            NativeMethods.GetTickCount(),
            NativeMethods.GetTickCount64(),
            effectiveLastInputTick);
    }
}
