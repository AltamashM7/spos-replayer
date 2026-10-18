using NAudio.CoreAudioApi;

namespace Rewind.Media;

/// <summary>Nonblocking notification policy; recorder teardown stays on its owning lifecycle.</summary>
internal sealed class AudioEndpointState(string deviceId)
{
    private Exception? error;
    public Exception? Error => Volatile.Read(ref error);
    private void Fail(string message) => Interlocked.CompareExchange(ref error, new IOException(message), null);

    public void DefaultChanged(DataFlow flow, Role role, string? replacement)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia && replacement != deviceId)
            Fail("The default playback device changed. Start buffering again to record the new output device.");
    }

    public void StateChanged(string changedId, DeviceState state)
    {
        if (changedId == deviceId && state != DeviceState.Active)
            Fail("The playback device became unavailable. Reconnect it and start buffering again.");
    }

    public void Removed(string removedId)
    {
        if (removedId == deviceId) Fail("The playback device was removed. Reconnect it and start buffering again.");
    }
}
