using System.Diagnostics;
using System.Runtime.InteropServices;
using EmeraldVeil.Core;

namespace EmeraldVeil.App;

/// <summary>
/// Classifies only the latest low-level mouse/keyboard timestamp so the idle
/// source can distinguish an injected zero-displacement mouse move. Hooks are
/// observational except for suppressing that exact no-op event. Every other
/// event is passed to the next hook; no event is logged or persisted.
/// </summary>
internal sealed class LowLevelInputObserver : IDisposable
{
    private readonly InputActivityFilter _filter;
    private readonly NativeMethods.LowLevelHookProc _mouseCallback;
    private readonly NativeMethods.LowLevelHookProc _keyboardCallback;

    private nint _mouseHook;
    private nint _keyboardHook;
    private bool _disposed;

    internal LowLevelInputObserver(InputActivityFilter filter)
    {
        _filter = filter;
        _mouseCallback = MouseHook;
        _keyboardCallback = KeyboardHook;
    }

    internal bool TryStart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mouseHook != nint.Zero && _keyboardHook != nint.Zero)
        {
            return true;
        }

        if (!NativeMethods.GetPhysicalCursorPos(out var cursor))
        {
            Debug.WriteLine("Unable to establish the initial pointer position.");
            return false;
        }

        _filter.InitializePointerPosition(cursor.X, cursor.Y);
        nint moduleHandle = NativeMethods.GetModuleHandle(moduleName: null);
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseCallback,
            moduleHandle,
            threadId: 0);
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _keyboardCallback,
            moduleHandle,
            threadId: 0);

        if (_mouseHook != nint.Zero && _keyboardHook != nint.Zero)
        {
            return true;
        }

        Stop();
        Debug.WriteLine("Unable to install the narrow injected-input observer.");
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private void Stop()
    {
        if (_mouseHook != nint.Zero)
        {
            _ = NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = nint.Zero;
        }

        if (_keyboardHook != nint.Zero)
        {
            _ = NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = nint.Zero;
        }
    }

    private nint MouseHook(int code, nint message, nint dataPointer)
    {
        if (code >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.MouseLowLevelHookData>(
                    dataPointer);
                bool shouldSuppress = _filter.ObserveMouse(
                    data.Time,
                    message.ToInt32(),
                    data.Flags,
                    data.Point.X,
                    data.Point.Y);
                if (shouldSuppress)
                {
                    return new nint(1);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Unable to classify low-level mouse input: {exception}");
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, code, message, dataPointer);
    }

    private nint KeyboardHook(int code, nint message, nint dataPointer)
    {
        if (code >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.KeyboardLowLevelHookData>(
                    dataPointer);
                _filter.ObserveKeyboard(data.Time);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Unable to classify low-level keyboard input: {exception}");
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, message, dataPointer);
    }
}
