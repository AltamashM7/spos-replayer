using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Rewind.Core;

namespace Rewind.Media;

internal sealed class SystemAudioSource : IAsyncDisposable
{
    private readonly WasapiRecorder recorder;
    private readonly MMDeviceEnumerator enumerator;
    private readonly MMDevice endpoint;
    private readonly MMDeviceNotificationClient notifications;
    internal AudioEndpointState EndpointState { get; }
    private Exception? error;
    private int disposing;
    private long receivedSamples;
    internal event Action<AudioPacketObservation>? PacketObserved;
    public BoundedOwnedQueue<AudioPacket> Packets { get; } = new(64);
    public Exception? Error => EndpointState.Error ?? Volatile.Read(ref error);
    public long ReceivedSamples => Interlocked.Read(ref receivedSamples);
    public string DeviceName => recorder.DeviceFriendlyName;

    public SystemAudioSource()
    {
        try
        {
            enumerator = new MMDeviceEnumerator();
            endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            EndpointState = new AudioEndpointState(endpoint.ID);
            recorder = new WasapiRecorderBuilder().WithDevice(endpoint).WithLoopbackCapture().WithPollingSync()
                .WithBufferLength(100).WithMmcssThreadPriority("Audio")
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)).Build();
            recorder.DataAvailable += DataAvailable;
            recorder.RecordingStopped += (_, args) =>
            {
                if (Volatile.Read(ref disposing) == 0)
                    Interlocked.CompareExchange(ref error, args.Exception ?? new IOException("System audio stopped unexpectedly. Start buffering again."), null);
            };
            notifications = enumerator.CreateNotificationClient(useSynchronizationContext: false);
            notifications.DefaultDeviceChanged += (_, args) => EndpointState.DefaultChanged(args.Flow, args.Role, args.DeviceId);
            notifications.DeviceStateChanged += (_, args) => EndpointState.StateChanged(args.DeviceId, args.NewState);
            notifications.DeviceRemoved += (_, args) => EndpointState.Removed(args.DeviceId);
            // Close the gap between selecting the endpoint and subscribing to changes.
            using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            EndpointState.DefaultChanged(DataFlow.Render, Role.Multimedia, current.ID);
        }
        catch
        {
            notifications?.Dispose(); recorder?.Dispose(); endpoint?.Dispose(); enumerator?.Dispose(); Packets.Dispose();
            throw;
        }
    }

    public void Start() => recorder.StartRecording();

    private void DataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (data.IsEmpty) return;
        var timestamp = (flags & AudioClientBufferFlags.TimestampError) == 0 && qpcPosition > 0
            ? qpcPosition : MediaClock.NowTicks - data.Length / 8 * TimeSpan.TicksPerSecond / 48000;
        var samples = data.Length / sizeof(float);
        var packet = new AudioPacket(timestamp, samples, devicePosition,
            (flags & (AudioClientBufferFlags.DataDiscontinuity | AudioClientBufferFlags.TimestampError)) != 0);
        if ((flags & AudioClientBufferFlags.Silent) != 0) packet.Data.AsSpan(0, samples).Clear();
        else MemoryMarshal.Cast<byte, float>(data).CopyTo(packet.Data);
        Packets.Enqueue(packet);
        Interlocked.Add(ref receivedSamples, samples / 2);
        if (PacketObserved is { } observe)
        {
            var peak = 0f;
            var over = 0;
            if ((flags & AudioClientBufferFlags.Silent) == 0)
                foreach (var value in MemoryMarshal.Cast<byte, float>(data))
                {
                    peak = Math.Max(peak, Math.Abs(value));
                    if (Math.Abs(value) > 1) over++;
                }
            observe(new AudioPacketObservation(timestamp, devicePosition, samples / 2, (int)flags, peak, over));
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref disposing, 1);
        try { await recorder.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { notifications.Dispose(); }
            finally
            {
                try { endpoint.Dispose(); }
                finally { enumerator.Dispose(); Packets.Dispose(); }
            }
        }
    }
}

internal readonly record struct AudioPacketObservation(long Timestamp, long DevicePosition, int Frames, int Flags, float Peak, int OverFullScale);
