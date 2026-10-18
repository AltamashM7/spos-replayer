namespace Rewind.Core;

/// <summary>One reference-protected lease. Dispose releases both its payload references and export slot.</summary>
public sealed class ReplaySnapshot : IDisposable
{
    private EncodedPacket[]? packets;
    private Action? release;
    public IReadOnlyList<EncodedPacket> Packets => packets ?? throw new ObjectDisposedException(nameof(ReplaySnapshot));
    public long StartTicks { get; }
    public long EndTicks { get; }
    public long AccountedBytes { get; }
    public TimeSpan Duration => TimeSpan.FromTicks(EndTicks - StartTicks);

    internal ReplaySnapshot(EncodedPacket[] packets, long start, long end, Action release)
    {
        this.packets = packets; this.release = release;
        StartTicks = start; EndTicks = end;
        AccountedBytes = packets.Sum(packet => packet.AccountedBytes);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref packets, null);
        Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
