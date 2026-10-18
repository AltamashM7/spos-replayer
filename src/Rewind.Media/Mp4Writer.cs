namespace Rewind.Media;

/// <summary>Finite-recording adapter sharing the replay encoder and independent packet muxer.</summary>
internal sealed class Mp4Writer : IDisposable
{
    private readonly MediaEncoder encoder;
    private readonly PacketMuxer muxer;
    public int AudioFrameSamples => encoder.AudioFrameSamples;

    public Mp4Writer(string path, int width, int height, bool includeAudio)
    {
        PacketMuxer? createdMuxer = null;
        encoder = new MediaEncoder(width, height, includeAudio, packet => createdMuxer!.Write(packet));
        try
        {
            using var configuration = encoder.CreateConfiguration();
            muxer = createdMuxer = new PacketMuxer(path, configuration);
        }
        catch { encoder.Dispose(); throw; }
    }

    public void WriteVideo(VideoFrame frame, long index) => encoder.WriteVideo(frame, index);
    public void WriteAudio(ReadOnlySpan<float> samples, long index) => encoder.WriteAudio(samples, index);
    public void Complete() { encoder.Complete(); muxer.Complete(); }
    public void Dispose() { muxer.Dispose(); encoder.Dispose(); }
}
