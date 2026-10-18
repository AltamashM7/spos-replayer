using System.Buffers;
using System.Diagnostics;

namespace Rewind.Media;

internal static class MediaClock
{
    public static long NowTicks => (long)(Stopwatch.GetTimestamp() * (10_000_000d / Stopwatch.Frequency));
    public static long Samples(long ticks) => (long)Math.Round(ticks * (48_000d / 10_000_000));
}

internal sealed class VideoFrame(int width, int height, long timestamp) : IDisposable
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public long Timestamp { get; } = timestamp;
    public byte[] Data { get; } = ArrayPool<byte>.Shared.Rent(checked(width * height * 4));
    public void Dispose() => ArrayPool<byte>.Shared.Return(Data);
}

internal sealed class AudioPacket(long timestamp, int sampleCount, long? devicePosition = null, bool discontinuity = false) : IDisposable
{
    public long Timestamp { get; } = timestamp;
    public int SampleCount { get; } = sampleCount;
    public long? DevicePosition { get; } = devicePosition;
    public bool Discontinuity { get; } = discontinuity;
    public float[] Data { get; } = ArrayPool<float>.Shared.Rent(sampleCount);
    public void Dispose() => ArrayPool<float>.Shared.Return(Data);
}
