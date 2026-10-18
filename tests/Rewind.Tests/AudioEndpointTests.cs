using NAudio.CoreAudioApi;
using Rewind.Media;
using Xunit;

namespace Rewind.Tests;

public sealed class AudioEndpointTests
{
    [Fact]
    public void UnrelatedNotificationsDoNotStopCapture()
    {
        var state = new AudioEndpointState("selected");
        state.DefaultChanged(DataFlow.Capture, Role.Multimedia, "other");
        state.DefaultChanged(DataFlow.Render, Role.Communications, "other");
        state.DefaultChanged(DataFlow.Render, Role.Multimedia, "selected");
        state.StateChanged("other", DeviceState.Disabled);
        state.StateChanged("selected", DeviceState.Active);
        state.Removed("other");
        Assert.Null(state.Error);
    }

    [Theory]
    [InlineData("other")]
    [InlineData(null)]
    public void NewOrMissingDefaultStopsTheFixedEndpoint(string? replacement)
    {
        var state = new AudioEndpointState("selected");
        state.DefaultChanged(DataFlow.Render, Role.Multimedia, replacement);
        Assert.Contains("default playback device changed", Assert.IsType<IOException>(state.Error).Message);
    }

    [Theory]
    [InlineData(DeviceState.Disabled)]
    [InlineData(DeviceState.Unplugged)]
    [InlineData(DeviceState.NotPresent)]
    public void LosingTheSelectedEndpointStopsCapture(DeviceState next)
    {
        var state = new AudioEndpointState("selected");
        state.StateChanged("selected", next);
        Assert.NotNull(state.Error);
        var first = state.Error;
        state.Removed("selected");
        Assert.Same(first, state.Error);
    }

    [Fact]
    public void RemovalReportsActionableError()
    {
        var state = new AudioEndpointState("selected");
        state.Removed("selected");
        Assert.Contains("start buffering again", Assert.IsType<IOException>(state.Error).Message);
    }
}
