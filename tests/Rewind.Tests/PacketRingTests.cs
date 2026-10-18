using Rewind.Core;
using Xunit;

namespace Rewind.Tests;

public sealed class PacketRingTests
{
    private const long Second = TimeSpan.TicksPerSecond;

    [Fact]
    public void SnapshotStartsAtEarlierKeyframeAndExcludesFutureFrames()
    {
        using var ring = new PacketRingBuffer(TimeSpan.FromSeconds(30));
        for (var i = 0; i < 90; i++) ring.Append(Video(i));
        using var snapshot = ring.Snapshot(Second * 25 / 10, TimeSpan.FromSeconds(1));
        Assert.Equal(Second, snapshot.StartTicks);
        Assert.Equal(Second * 25 / 10, snapshot.EndTicks);
        Assert.True(snapshot.Packets[0].IsKeyframe);
        Assert.All(snapshot.Packets, packet => Assert.True(packet.EndTicks <= Second * 25 / 10));
    }

    [Fact]
    public void PartialHistoryIsSavedInsteadOfWaitingForFullConfiguredDuration()
    {
        using var ring = new PacketRingBuffer(TimeSpan.FromSeconds(30));
        for (var i = 0; i < 60; i++) ring.Append(Video(i));
        using var snapshot = ring.Snapshot(2 * Second, TimeSpan.FromSeconds(30));
        Assert.Equal(0, snapshot.StartTicks);
        Assert.Equal(60, snapshot.Packets.Count);
        Assert.Equal(2, snapshot.Duration.TotalSeconds);
    }

    [Fact]
    public void SnapshotRemainsValidAfterRingEvictionAndDisposal()
    {
        var ring = new PacketRingBuffer(TimeSpan.FromSeconds(1));
        for (var i = 0; i < 30; i++) ring.Append(Video(i));
        using var snapshot = ring.Snapshot(Second, TimeSpan.FromSeconds(1));
        var data = snapshot.Packets.Select(packet => packet.Pts).ToArray();
        for (var i = 30; i < 300; i++) ring.Append(Video(i));
        Assert.True(ring.Status.EvictedPackets > 0);
        Assert.True(ring.Status.SnapshotBytes > 0);
        Assert.Equal(data, snapshot.Packets.Select(packet => packet.Pts));
        ring.Dispose();
        Assert.Equal(data, snapshot.Packets.Select(packet => packet.Pts));
        snapshot.Dispose();
        Assert.Equal(0, ring.Status.SnapshotBytes);
    }

    [Fact]
    public void TimeRetentionDoesNotGrowWithSessionLength()
    {
        using var ring = new PacketRingBuffer(TimeSpan.FromSeconds(3));
        for (var i = 0; i < 3000; i++) ring.Append(Video(i));
        Assert.InRange(ring.Status.AvailableSeconds, 3, 4.01);
        Assert.InRange(ring.Status.PacketCount, 90, 123);
        using var snapshot = ring.Snapshot(100 * Second, TimeSpan.FromSeconds(3));
        Assert.Equal(97 * Second, snapshot.StartTicks);
    }

    [Fact]
    public void BytePressureDropsWholeGopsAndWaitsForADecodableRestart()
    {
        using var ring = new PacketRingBuffer(TimeSpan.FromSeconds(60), maximumBytes: 1000);
        for (var i = 0; i < 90; i++)
        {
            ring.Append(Video(i));
            Assert.InRange(ring.Status.RetainedBytes, 0, 1000);
        }
        Assert.Throws<InvalidOperationException>(() => ring.Snapshot(3 * Second, TimeSpan.FromSeconds(60)));
        ring.Append(Video(90));
        using var snapshot = ring.Snapshot(91 * Second / 30, TimeSpan.FromSeconds(60));
        Assert.True(snapshot.Packets[0].IsKeyframe);
        Assert.Equal(90, snapshot.Packets[0].Pts);
    }

    [Fact]
    public void SlotLimitRemainsBoundedAndOnlyOneSnapshotLeaseIsAllowed()
    {
        using var ring = new PacketRingBuffer(TimeSpan.FromSeconds(60), maximumPackets: 5);
        for (var i = 0; i < 100; i++)
        {
            ring.Append(new EncodedPacket(MediaTrack.Video, i, i, 1, 1, 1, true, [1]));
            Assert.InRange(ring.Status.PacketCount, 0, 5);
        }
        using var first = ring.Snapshot(100 * Second, TimeSpan.FromSeconds(60));
        Assert.Throws<InvalidOperationException>(() => ring.Snapshot(100 * Second, TimeSpan.FromSeconds(60)));
        first.Dispose();
        using var second = ring.Snapshot(100 * Second, TimeSpan.FromSeconds(60));
        Assert.NotEmpty(second.Packets);
    }

    [Fact]
    public async Task SaveWatermarkWaitsForDelayedAudioAndSupportsCancellation()
    {
        using var ring = new PacketRingBuffer(TimeSpan.FromSeconds(30));
        var waiting = ring.WaitForWatermarkAsync(Second, true, CancellationToken.None);
        for (var i = 0; i < 30; i++) ring.Append(Video(i));
        Assert.False(waiting.IsCompleted);
        ring.Append(new EncodedPacket(MediaTrack.Audio, 0, 0, 48000, 1, 48000, true, [1]));
        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();
        var cancelled = ring.WaitForWatermarkAsync(2 * Second, true, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
    }

    [Fact]
    public async Task DisposingRingWakesWatermarkWaiters()
    {
        var ring = new PacketRingBuffer(TimeSpan.FromSeconds(30));
        var waiting = ring.WaitForWatermarkAsync(Second, false, CancellationToken.None);
        ring.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
    }

    private static EncodedPacket Video(int frame) => new(MediaTrack.Video, frame, frame, 1, 1, 30, frame % 30 == 0, [1, 2, 3]);
}
