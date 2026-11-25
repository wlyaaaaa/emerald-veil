using System.Runtime.InteropServices;

namespace EmeraldVeil.App;

internal sealed class GamepadActivity
{
    private const int LeftStickDeadZone = 7849;
    private const int RightStickDeadZone = 8689;
    private const int TriggerThreshold = 30;
    private readonly ulong[] _nextProbe = new ulong[4];

    internal bool IsActive(ulong now)
    {
        for (uint slot = 0; slot < _nextProbe.Length; slot++)
        {
            if (now < _nextProbe[slot]) continue;
            if (XInputGetState(slot, out var state) != 0)
            {
                _nextProbe[slot] = now + 1_000;
                continue;
            }

            if (state.Buttons != 0 ||
                state.LeftTrigger > TriggerThreshold || state.RightTrigger > TriggerThreshold ||
                OutsideDeadZone(state.LeftX, state.LeftY, LeftStickDeadZone) ||
                OutsideDeadZone(state.RightX, state.RightY, RightStickDeadZone))
                return true;
        }
        return false;
    }

    private static bool OutsideDeadZone(int x, int y, int deadZone) =>
        (long)x * x + (long)y * y > deadZone * deadZone;

    // XINPUT_STATE contains a packet number followed by XINPUT_GAMEPAD.
    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetState(uint userIndex, out State state);
}
