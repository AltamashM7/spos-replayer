using Rewind.Core;

namespace Rewind.Media;

/// <summary>Places captured PCM on the common QPC timeline and fills missing time with silence.</summary>
internal sealed class AudioTimelineReader(BoundedOwnedQueue<AudioPacket> packets, long origin) : IDisposable
{
    private AudioPacket? pending;
    private AudioPacket? next;
    private long? mappedStart;
    private long? mappedEnd;
    private long? previousDeviceEnd;
    private long previousMappedEnd;
    private bool previousDiscontinuity;
    private double clockError;
    public long SilentSamples { get; private set; }
    public long ResampledPackets { get; private set; }

    public void Read(long startSample, Span<float> destination)
    {
        destination.Clear();
        var end = startSample + destination.Length / 2;
        var position = startSample;
        while (position < end)
        {
            pending ??= packets.TryDequeue();
            if (pending is null) { SilentSamples += end - position; break; }
            next ??= packets.TryDequeue();
            var sourceFrames = pending.SampleCount / 2;
            if (mappedEnd is null)
            {
                var rawStart = MediaClock.Samples(pending.Timestamp - origin);
                mappedStart = rawStart;
                var adjustment = 0;
                if (!pending.Discontinuity && !previousDiscontinuity && previousDeviceEnd is not null &&
                    pending.DevicePosition == previousDeviceEnd && Math.Abs(rawStart - previousMappedEnd) <= 48)
                {
                    // Device positions prove continuity. Reject short QPC jitter and follow sustained
                    // clock drift gradually, instead of adding zeros or changing pitch every packet.
                    mappedStart = previousMappedEnd;
                    clockError += (rawStart - previousMappedEnd - clockError) * 0.01;
                    if (next is not null && !next.Discontinuity &&
                        next.DevicePosition == pending.DevicePosition + sourceFrames && Math.Abs(clockError) >= 1)
                        adjustment = Math.Sign(clockError);
                }
                else clockError = 0;
                mappedEnd = mappedStart + sourceFrames + adjustment;
                if (mappedStart != rawStart || adjustment != 0) ResampledPackets++;
            }
            var packetStart = mappedStart!.Value;
            var packetEnd = mappedEnd.Value;
            if (packetEnd <= position) { Advance(); continue; }
            if (packetStart > position)
            {
                var silence = Math.Min(packetStart, end) - position;
                SilentSamples += silence;
                position += silence;
                continue;
            }
            var count = (int)Math.Min(end - position, packetEnd - position);
            var outputOffset = checked((int)(position - startSample) * 2);
            var outputFrames = packetEnd - packetStart;
            if (outputFrames == sourceFrames)
                pending.Data.AsSpan(checked((int)(position - packetStart) * 2), count * 2)
                    .CopyTo(destination.Slice(outputOffset, count * 2));
            else
                for (var frame = 0; frame < count; frame++)
                {
                    var source = (position + frame - packetStart) * sourceFrames / (double)outputFrames;
                    var first = Math.Min((int)source, sourceFrames - 1);
                    var fraction = (float)(source - first);
                    for (var channel = 0; channel < 2; channel++)
                    {
                        var a = pending.Data[first * 2 + channel];
                        var b = first + 1 < sourceFrames ? pending.Data[(first + 1) * 2 + channel] : next!.Data[channel];
                        destination[outputOffset + frame * 2 + channel] = a + (b - a) * fraction;
                    }
                }
            position += count;
            if (position >= packetEnd) Advance();
        }
    }

    private void Advance()
    {
        previousDeviceEnd = pending!.DevicePosition + pending.SampleCount / 2;
        previousDiscontinuity = pending.Discontinuity;
        previousMappedEnd = mappedEnd!.Value;
        pending?.Dispose();
        pending = next;
        next = null;
        mappedEnd = null;
        mappedStart = null;
    }

    public void Dispose() { pending?.Dispose(); next?.Dispose(); pending = null; next = null; }
}
