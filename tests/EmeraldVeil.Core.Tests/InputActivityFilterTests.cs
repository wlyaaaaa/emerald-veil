using EmeraldVeil.Core;

namespace EmeraldVeil.Core.Tests;

public sealed class InputActivityFilterTests
{
    [Theory]
    [InlineData(InputActivityFilter.InjectedMouseFlag)]
    [InlineData(0u)]
    public void ZeroDisplacementMouseMoveKeepsLastAcceptedTick(uint flags)
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));
        filter.InitializePointerPosition(640, 480);

        bool shouldSuppress = filter.ObserveMouse(
            timestamp: 200,
            message: InputActivityFilter.MouseMoveMessage,
            flags,
            x: 640,
            y: 480);

        Assert.True(shouldSuppress);
        Assert.Equal(100u, filter.Resolve(200));
    }

    [Theory]
    [InlineData(InputActivityFilter.MouseMoveMessage, 0u, 641, 480)]
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
    public void IsolatedInjectedMovementPassesThroughButKeepsLastAcceptedTick()
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));
        filter.InitializePointerPosition(640, 480);

        bool shouldSuppress = filter.ObserveMouse(
            timestamp: 200,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 641,
            y: 480);

        Assert.False(shouldSuppress);
        Assert.Equal(100u, filter.Resolve(200));
    }

    [Fact]
    public void SecondInjectedMovementWithinConfirmationWindowAdvancesTick()
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));
        filter.InitializePointerPosition(640, 480);

        Assert.False(filter.ObserveMouse(
            timestamp: 200,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 641,
            y: 480));
        Assert.Equal(100u, filter.Resolve(200));

        Assert.False(filter.ObserveMouse(
            timestamp: 300,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 642,
            y: 480));
        Assert.Equal(300u, filter.Resolve(300));
    }

    [Fact]
    public void InjectedMovementOutsideConfirmationWindowStartsANewSequence()
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));
        filter.InitializePointerPosition(640, 480);

        Assert.False(filter.ObserveMouse(
            timestamp: 200,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 641,
            y: 480));
        Assert.Equal(100u, filter.Resolve(200));

        Assert.False(filter.ObserveMouse(
            timestamp: 500,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 642,
            y: 480));
        Assert.Equal(100u, filter.Resolve(500));
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
    public void ConfirmedInjectedMovementIsAcceptedAndOnlyAFollowingNoOpIsIgnored()
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
        Assert.Equal(100u, filter.Resolve(200));

        moved = filter.ObserveMouse(
            timestamp: 300,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 642,
            y: 480);
        Assert.False(moved);
        Assert.Equal(300u, filter.Resolve(300));

        bool noOp = filter.ObserveMouse(
            timestamp: 400,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 642,
            y: 480);
        Assert.True(noOp);
        Assert.Equal(300u, filter.Resolve(400));
    }

    [Fact]
    public void RawTickBeforeIgnoredClassificationDoesNotBecomeAcceptedActivity()
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));
        filter.InitializePointerPosition(640, 480);

        Assert.Equal(100u, filter.Resolve(200));
        bool shouldSuppress = filter.ObserveMouse(
            timestamp: 200,
            message: InputActivityFilter.MouseMoveMessage,
            flags: InputActivityFilter.InjectedMouseFlag,
            x: 640,
            y: 480);

        Assert.True(shouldSuppress);
        Assert.Equal(100u, filter.Resolve(200));
    }

    [Fact]
    public void UnclassifiedRawTickBecomesActivityOnSecondObservation()
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));

        Assert.Equal(100u, filter.Resolve(200));
        Assert.Equal(200u, filter.Resolve(200));
    }

    [Fact]
    public void AcceptedClassificationCommitsPendingRawTickImmediately()
    {
        var filter = new InputActivityFilter();
        Assert.Equal(100u, filter.Resolve(100));

        Assert.Equal(100u, filter.Resolve(200));
        filter.ObserveKeyboard(timestamp: 200);

        Assert.Equal(200u, filter.Resolve(200));
    }
}
