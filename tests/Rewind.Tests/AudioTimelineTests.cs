using Rewind.Core;
using Rewind.Media;
using Xunit;

namespace Rewind.Tests;

public sealed class AudioTimelineTests
{
    private const long Origin = TimeSpan.TicksPerSecond;

    [Fact]
    public void MissingCallbacksProduceSilenceForTheFullRequestedInterval()
    {
        using var queue = new BoundedOwnedQueue<AudioPacket>(4);
        using var reader = new AudioTimelineReader(queue, Origin);
        var samples = Enumerable.Repeat(1f, 2048).ToArray();
        reader.Read(0, samples);
        Assert.All(samples, sample => Assert.Equal(0, sample));
        Assert.Equal(1024, reader.SilentSamples);
    }

    [Fact]
    public void LeadingAndMiddleGapsRetainTheirTimeAndPartialPacketsCarryForward()
    {
        using var queue = new BoundedOwnedQueue<AudioPacket>(4);
        queue.Enqueue(Packet(2, [1, -1, 2, -2]));
        queue.Enqueue(Packet(5, [3, -3, 4, -4]));
        using var reader = new AudioTimelineReader(queue, Origin);
        var first = new float[12];
        reader.Read(0, first);
        Assert.Equal([0f, 0, 0, 0, 1, -1, 2, -2, 0, 0, 3, -3], first);
        var second = new float[2];
        reader.Read(6, second);
        Assert.Equal([4f, -4], second);
        Assert.Equal(3, reader.SilentSamples);
    }

    [Fact]
    public void PacketsArrivingTooLateDoNotShiftLaterAudioEarlier()
    {
        using var queue = new BoundedOwnedQueue<AudioPacket>(4);
        queue.Enqueue(Packet(0, [9, 9, 9, 9]));
        queue.Enqueue(Packet(5, [1, -1]));
        using var reader = new AudioTimelineReader(queue, Origin);
        var samples = new float[6];
        reader.Read(4, samples);
        Assert.Equal([0f, 0, 1, -1, 0, 0], samples);
        Assert.Equal(2, reader.SilentSamples);
    }

    [Fact]
    public void OverlappingPacketPrefixIsSkippedWithoutRepeatingSamples()
    {
        using var queue = new BoundedOwnedQueue<AudioPacket>(4);
        queue.Enqueue(Packet(0, [1, -1, 2, -2, 3, -3]));
        using var reader = new AudioTimelineReader(queue, Origin);
        var samples = new float[4];
        reader.Read(1, samples);
        Assert.Equal([2f, -2, 3, -3], samples);
        Assert.Equal(0, reader.SilentSamples);
    }

    private static AudioPacket Packet(int startSample, float[] samples)
    {
        var timestamp = Origin + (long)Math.Round(startSample * (TimeSpan.TicksPerSecond / 48000d));
        var packet = new AudioPacket(timestamp, samples.Length);
        samples.CopyTo(packet.Data, 0);
        return packet;
    }
}
