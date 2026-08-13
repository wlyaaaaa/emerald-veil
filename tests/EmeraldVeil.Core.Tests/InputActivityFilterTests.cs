using EmeraldVeil.Core;

namespace EmeraldVeil.Core.Tests;

public sealed class InputActivityFilterTests
{
    [Fact]
    public void InjectedZeroDisplacementMouseMoveKeepsLastAcceptedTick()
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));
        filter.InitializePointerPosition(640, 480);

        bool shouldSuppress = filter.ObserveMouse(
            timestamp: 200,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 640,
            y: 480);

        Assert.True(shouldSuppress);
        Assert.Equal(100u, filter.Resolve(200));
    }

    [Theory]
    [InlineData(InputActivityFilter.MouseMoveMessage, InputActivityFilter.InjectedMouseFlag, 641, 480)]
    [InlineData(InputActivityFilter.MouseMoveMessage, 0u, 640, 480)]
    [InlineData(0x0201, InputActivityFilter.InjectedMouseFlag, 640, 480)]
    public void EveryOtherMouseInputAdvancesLastAcceptedTick(
        int message,
        uint flags,
        int x,
        int y)
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));
        filter.InitializePointerPosition(640, 480);

        bool shouldSuppress = filter.ObserveMouse(
            timestamp: 200,
            message,
            flags,
            x,
            y);

        Assert.False(shouldSuppress);
        Assert.Equal(200u, filter.Resolve(200));
    }

    [Fact]
    public void KeyboardAtSameTickOverridesAnIgnoredMouseMove()
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));
        filter.InitializePointerPosition(640, 480);
        _ = filter.ObserveMouse(
            timestamp: 200,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 640,
            y: 480);

        filter.ObserveKeyboard(timestamp: 200);

        Assert.Equal(200u, filter.Resolve(200));
    }

    [Fact]
    public void IgnoredMouseMoveCannotOverrideValidInputAtSameTick()
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));
        filter.InitializePointerPosition(640, 480);
        filter.ObserveKeyboard(timestamp: 200);

        bool shouldSuppress = filter.ObserveMouse(
            timestamp: 200,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 640,
            y: 480);

        Assert.True(shouldSuppress);
        Assert.Equal(200u, filter.Resolve(200));
    }

    [Fact]
    public void FirstObservationRemainsConservativeWhenNoAcceptedBaselineExists()
    {
        var filter = new InputActivityFilter();
        _ = filter.ObserveMouse(
            timestamp: 200,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 640,
            y: 480);

        Assert.Equal(200u, filter.Resolve(200));
    }

    [Fact]
    public void InjectedMovementIsAcceptedAndOnlyAFollowingNoOpIsIgnored()
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));
        filter.InitializePointerPosition(640, 480);

        bool moved = filter.ObserveMouse(
            timestamp: 200,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 641,
            y: 480);
        Assert.False(moved);
        Assert.Equal(200u, filter.Resolve(200));

        bool noOp = filter.ObserveMouse(
            timestamp: 300,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 641,
            y: 480);
        Assert.True(noOp);
        Assert.Equal(200u, filter.Resolve(300));
    }
}
