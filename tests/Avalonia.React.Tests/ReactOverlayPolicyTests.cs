using Avalonia.React;

namespace Avalonia.React.Tests;

public sealed class ReactOverlayPolicyTests
{
    [Theory]
    [InlineData(ReactLoadState.Idle, false, false, true)]
    [InlineData(ReactLoadState.Loading, false, false, true)]
    [InlineData(ReactLoadState.Failed, false, false, true)]
    public void Overlay_is_visible_until_the_page_loads(ReactLoadState state, bool bridgeReady, bool timedOut, bool expected) =>
        Assert.Equal(expected, ReactOverlayPolicy.ShouldShow(true, state, bridgeReady, false, timedOut));

    [Fact]
    public void Overlay_hides_when_the_page_loads_without_waiting_for_the_bridge() =>
        Assert.False(ReactOverlayPolicy.ShouldShow(true, ReactLoadState.Loaded, bridgeReady: false, waitForBridgeReady: false, timedOut: false));

    [Fact]
    public void Overlay_waits_for_the_bridge_when_configured() =>
        Assert.True(ReactOverlayPolicy.ShouldShow(true, ReactLoadState.Loaded, bridgeReady: false, waitForBridgeReady: true, timedOut: false));

    [Fact]
    public void Overlay_hides_when_the_bridge_becomes_ready() =>
        Assert.False(ReactOverlayPolicy.ShouldShow(true, ReactLoadState.Loaded, bridgeReady: true, waitForBridgeReady: true, timedOut: false));

    [Fact]
    public void Overlay_hides_when_the_bridge_handshake_times_out() =>
        Assert.False(ReactOverlayPolicy.ShouldShow(true, ReactLoadState.Loaded, bridgeReady: false, waitForBridgeReady: true, timedOut: true));

    [Fact]
    public void Overlay_can_be_disabled() =>
        Assert.False(ReactOverlayPolicy.ShouldShow(false, ReactLoadState.Loading, bridgeReady: false, waitForBridgeReady: true, timedOut: false));
}
