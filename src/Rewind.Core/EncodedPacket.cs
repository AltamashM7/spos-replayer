namespace Rewind.Core;

public enum MediaTrack { Video, Audio }

/// <summary>Immutable encoded payload, independent of native encoder lifetimes.</summary>
public sealed class EncodedPacket
{
    public MediaTrack Track { get; }
    public long Pts { get; }
    public long Dts { get; }
    public long Duration { get; }
    public int TimeBaseNumerator { get; }
    public int TimeBaseDenominator { get; }
    public bool IsKeyframe { get; }
    public ReadOnlyMemory<byte> Data { get; }
    public long AccountedBytes => Data.Length + 128L;
    public long StartTicks => ToTicks(Pts);
    public long EndTicks => ToTicks(checked(Pts + Duration));

    public EncodedPacket(MediaTrack track, long pts, long dts, long duration, int numerator,
        int denominator, bool isKeyframe, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        Track = track; Pts = pts; Dts = dts; Duration = duration;
        TimeBaseNumerator = numerator; TimeBaseDenominator = denominator;
        IsKeyframe = isKeyframe; Data = data.ToArray();
    }

    private long ToTicks(long value) => checked((long)((Int128)value * TimeBaseNumerator * TimeSpan.TicksPerSecond / TimeBaseDenominator));
}
