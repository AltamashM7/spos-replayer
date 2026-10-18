namespace Rewind.Core;

public sealed record ReplayBufferStatus(int PacketCount, long RetainedBytes, long SnapshotBytes,
    long MaximumBytes, double AvailableSeconds, long VideoEndTicks, long AudioEndTicks, long EvictedPackets);

/// <summary>A circular packet array bounded by time, bytes and slots. All I/O happens outside its lock.</summary>
public sealed class PacketRingBuffer : IDisposable
{
    private const long AudioPrerollTicks = TimeSpan.TicksPerMillisecond * 50;
    private readonly object gate = new();
    private readonly EncodedPacket?[] slots;
    private readonly long durationTicks;
    private readonly long maximumBytes;
    private int head;
    private int count;
    private long bytes;
    private long snapshotBytes;
    private long videoEnd;
    private long audioEnd = long.MinValue;
    private long evicted;
    private bool snapshotActive;
    private bool disposed;
    private TaskCompletionSource changed = NewSignal();

    public PacketRingBuffer(TimeSpan duration, long maximumBytes = 128L * 1024 * 1024, int maximumPackets = 8192)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPackets);
        durationTicks = duration.Ticks; this.maximumBytes = maximumBytes;
        slots = new EncodedPacket[maximumPackets];
    }

    public void Append(EncodedPacket packet)
    {
        TaskCompletionSource signal;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (packet.Track == MediaTrack.Video) videoEnd = Math.Max(videoEnd, packet.EndTicks);
            else audioEnd = Math.Max(audioEnd, packet.EndTicks);

            if (packet.AccountedBytes > maximumBytes) Clear();
            else
            {
                if (count == slots.Length) DropOldestGroup();
                if (count > 0 || packet is { Track: MediaTrack.Video, IsKeyframe: true })
                {
                    slots[(head + count) % slots.Length] = packet;
                    count++; bytes += packet.AccountedBytes;
                    TrimByTime();
                    while (bytes > maximumBytes) DropOldestGroup();
                }
            }
            signal = changed; changed = NewSignal();
        }
        signal.TrySetResult();
    }

    public async Task WaitForWatermarkAsync(long cutoff, bool includeAudio, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task signal;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (videoEnd >= cutoff && (!includeAudio || audioEnd >= cutoff)) return;
                signal = changed.Task;
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ReplaySnapshot Snapshot(long cutoff, TimeSpan lookback)
    {
        if (lookback <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lookback));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (snapshotActive) throw new InvalidOperationException("A replay export is already in progress.");
            var wanted = cutoff - lookback.Ticks;
            EncodedPacket? firstKeyframe = null;
            EncodedPacket? selected = null;
            for (var i = 0; i < count; i++)
            {
                var packet = At(i);
                if (packet is not { Track: MediaTrack.Video, IsKeyframe: true } || packet.EndTicks > cutoff) continue;
                firstKeyframe ??= packet;
                if (packet.StartTicks <= wanted) selected = packet;
            }
            selected ??= firstKeyframe;
            if (selected is null) throw new InvalidOperationException("No decodable replay is available yet.");
            var start = selected.StartTicks;
            var list = new List<EncodedPacket>();
            long end = start;
            for (var i = 0; i < count; i++)
            {
                var packet = At(i);
                if (packet.Track == MediaTrack.Video)
                {
                    if (packet.StartTicks >= start && packet.EndTicks <= cutoff)
                    { list.Add(packet); end = Math.Max(end, packet.EndTicks); }
                }
                else if (packet.EndTicks > start - AudioPrerollTicks && packet.StartTicks < cutoff) list.Add(packet);
            }
            // The end is the last complete video frame, never a frame captured after the trigger.
            var packets = list.Where(packet => packet.Track == MediaTrack.Video || packet.StartTicks < end).ToArray();
            var snapshot = new ReplaySnapshot(packets, start, end, ReleaseSnapshot);
            snapshotActive = true; snapshotBytes = snapshot.AccountedBytes;
            return snapshot;
        }
    }

    public ReplayBufferStatus Status
    {
        get
        {
            lock (gate)
            {
                var first = FindKeyframe(0);
                var seconds = first < 0 ? 0 : Math.Max(0, videoEnd - At(first).StartTicks) / (double)TimeSpan.TicksPerSecond;
                return new(count, bytes, snapshotBytes, maximumBytes, seconds, videoEnd, audioEnd, evicted);
            }
        }
    }

    private EncodedPacket At(int index) => slots[(head + index) % slots.Length]!;
    private int FindKeyframe(int from)
    {
        for (var i = from; i < count; i++) if (At(i) is { Track: MediaTrack.Video, IsKeyframe: true }) return i;
        return -1;
    }

    private void TrimByTime()
    {
        var cutoff = videoEnd - durationTicks;
        long? start = null;
        for (var i = 0; i < count; i++)
            if (At(i) is { Track: MediaTrack.Video, IsKeyframe: true } packet && packet.StartTicks <= cutoff)
                start = packet.StartTicks;
        if (start is { } value) RemoveBefore(value - AudioPrerollTicks);
    }

    private void DropOldestGroup()
    {
        var first = FindKeyframe(0);
        var next = first < 0 ? -1 : FindKeyframe(first + 1);
        if (next < 0) { Clear(); return; }
        var before = count;
        RemoveBefore(At(next).StartTicks - AudioPrerollTicks);
        // A packet/slot budget must always make progress, including very short GOPs.
        if (count == before) Clear();
    }

    private void RemoveBefore(long cutoff)
    {
        while (count > 0 && At(0).EndTicks <= cutoff) RemoveHead();
    }

    private void RemoveHead()
    {
        bytes -= slots[head]!.AccountedBytes;
        slots[head] = null; head = (head + 1) % slots.Length; count--; evicted++;
    }

    private void Clear() { while (count > 0) RemoveHead(); }
    private void ReleaseSnapshot() { lock (gate) { snapshotActive = false; snapshotBytes = 0; } }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        TaskCompletionSource signal;
        lock (gate)
        {
            if (disposed) return;
            disposed = true; Clear(); signal = changed;
        }
        signal.TrySetResult();
    }
}
