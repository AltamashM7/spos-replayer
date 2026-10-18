using Rewind.Core;
using Rewind.Media;
using Xunit;

namespace Rewind.Tests;

public sealed class AudioQualityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(-9)]
    public void ContinuousDeviceSamplesDoNotAcquireZeroGapsFromTimestampJitter(int jitter)
    {
        using var queue = new BoundedOwnedQueue<AudioPacket>(4);
        queue.Enqueue(Packet(0, 0));
        queue.Enqueue(Packet(480 + jitter, 480));
        using var reader = new AudioTimelineReader(queue, 0);
        var output = new float[960 * 2];
        reader.Read(0, output.AsSpan(0, 512));
        reader.Read(256, output.AsSpan(512));
        Assert.All(output, sample => Assert.Equal(0.75f, sample));
        Assert.Equal(0, reader.SilentSamples);
        Assert.Equal(1, reader.ResampledPackets);
    }

    [Fact]
    public void TimingJitterDoesNotChangeContinuousWaveformSamples()
    {
        using var queue = new BoundedOwnedQueue<AudioPacket>(32);
        var expected = new List<float>();
        for (var index = 0; index < 20; index++)
        {
            var packet = Packet(index * 480 + (index % 3 == 0 ? 0 : index % 2 == 0 ? 9 : -9), index * 480);
            for (var sample = 0; sample < 480; sample++)
            {
                var value = (float)Math.Sin((index * 480 + sample) * Math.PI * 2 * 730 / 48000);
                packet.Data[sample * 2] = packet.Data[sample * 2 + 1] = value;
                expected.Add(value); expected.Add(value);
            }
            queue.Enqueue(packet);
        }
        using var reader = new AudioTimelineReader(queue, 0);
        var actual = new float[expected.Count];
        reader.Read(0, actual);
        Assert.Equal(expected, actual);
        Assert.Equal(0, reader.SilentSamples);
    }

    [Fact]
    public void SustainedClockDriftIsFollowedWithoutAccumulatingAnAudioOffset()
    {
        using var queue = new BoundedOwnedQueue<AudioPacket>(1001);
        for (var index = 0; index < 1000; index++)
            queue.Enqueue(Packet((int)Math.Round(index * 480 * 1.0001), index * 480));
        using var reader = new AudioTimelineReader(queue, 0);
        var actual = new float[480040 * 2];
        reader.Read(0, actual);
        Assert.Equal(0, reader.SilentSamples);
        Assert.All(actual, sample => Assert.Equal(.75f, sample));
    }

    [Theory]
    [InlineData(false, 960, 481)]
    [InlineData(true, 480, 481)]
    [InlineData(false, 480, 600)]
    public void MissingSamplesDiscontinuitiesAndRealSilenceAreNotStretched(bool discontinuity, long device, int start)
    {
        using var queue = new BoundedOwnedQueue<AudioPacket>(4);
        queue.Enqueue(Packet(0, 0));
        queue.Enqueue(Packet(start, device, discontinuity));
        using var reader = new AudioTimelineReader(queue, 0);
        var output = new float[start * 2];
        reader.Read(0, output);
        Assert.Equal(start - 480, reader.SilentSamples);
        Assert.Equal(0, reader.ResampledPackets);
    }

    [Fact]
    public void HeadroomRetainsWaveShapeAndSanitizesInvalidValues()
    {
        Assert.Equal(0.5f, AudioEncodingProfile.Prepare(1));
        Assert.Equal(-0.5f, AudioEncodingProfile.Prepare(-1));
        Assert.Equal(0.95f, AudioEncodingProfile.Prepare(1.9f));
        Assert.Equal(1f, AudioEncodingProfile.Prepare(4));
        Assert.Equal(0f, AudioEncodingProfile.Prepare(float.NaN));
        Assert.Equal(0f, AudioEncodingProfile.Prepare(float.PositiveInfinity));
    }

    private static AudioPacket Packet(int start, long device, bool discontinuity = false)
    {
        var packet = new AudioPacket((long)Math.Round(start * TimeSpan.TicksPerSecond / 48000d), 960, device, discontinuity);
        packet.Data.AsSpan(0, 960).Fill(0.75f);
        return packet;
    }
}
