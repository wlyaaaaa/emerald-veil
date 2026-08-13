namespace EmeraldVeil.Core;

/// <summary>
/// Keeps one narrowly identified synthetic no-op mouse move from resetting the
/// product's idle clock. Every other input remains activity. Only the previous
/// pointer point and latest timestamp classification are retained in memory.
/// </summary>
public sealed class InputActivityFilter
{
    public const int MouseMoveMessage = 0x0200;
    public const uint InjectedMouseFlag = 0x0000_0001;

    private readonly object _stateLock = new();

    private bool _hasClassification;
    private uint _classifiedTimestamp;
    private bool _classifiedAsIgnorable;
    private bool _hasAcceptedTick;
    private uint _lastAcceptedTick;
    private bool _hasPointerPosition;
    private int _pointerX;
    private int _pointerY;

    public void InitializePointerPosition(int x, int y)
    {
        lock (_stateLock)
        {
            _pointerX = x;
            _pointerY = y;
            _hasPointerPosition = true;
        }
    }

    public bool ObserveMouse(
        uint timestamp,
        int message,
        uint flags,
        int x,
        int y)
    {
        lock (_stateLock)
        {
            bool isIgnorable = _hasPointerPosition &&
                message == MouseMoveMessage &&
                (flags & InjectedMouseFlag) != 0 &&
                x == _pointerX &&
                y == _pointerY;

            _pointerX = x;
            _pointerY = y;
            _hasPointerPosition = true;
            ObserveClassificationUnsafe(timestamp, isIgnorable);
            return isIgnorable;
        }
    }

    public void ObserveKeyboard(uint timestamp) =>
        ObserveClassification(timestamp, isIgnorable: false);

    public uint Resolve(uint rawLastInputTick)
    {
        lock (_stateLock)
        {
            if (_hasClassification &&
                _classifiedTimestamp == rawLastInputTick &&
                _classifiedAsIgnorable &&
                _hasAcceptedTick)
            {
                return _lastAcceptedTick;
            }

            _lastAcceptedTick = rawLastInputTick;
            _hasAcceptedTick = true;
            return rawLastInputTick;
        }
    }

    private void ObserveClassification(uint timestamp, bool isIgnorable)
    {
        lock (_stateLock)
        {
            ObserveClassificationUnsafe(timestamp, isIgnorable);
        }
    }

    private void ObserveClassificationUnsafe(uint timestamp, bool isIgnorable)
    {
        if (_hasClassification && _classifiedTimestamp == timestamp)
        {
            _classifiedAsIgnorable &= isIgnorable;
            return;
        }

        _classifiedTimestamp = timestamp;
        _classifiedAsIgnorable = isIgnorable;
        _hasClassification = true;
    }
}
