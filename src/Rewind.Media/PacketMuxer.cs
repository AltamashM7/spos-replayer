using FFmpeg.AutoGen;
using Rewind.Core;

namespace Rewind.Media;

/// <summary>Writes existing packets to a fresh MP4 without accessing a live encoder or re-encoding.</summary>
internal sealed unsafe class PacketMuxer : IDisposable
{
    private AVFormatContext* format;
    private AVStream* video;
    private AVStream* audio;
    private AVPacket* nativePacket;
    private readonly long startTicks;
    private readonly long endTicks;

    public PacketMuxer(string path, CodecConfiguration configuration, long startTicks = 0, long endTicks = long.MaxValue)
    {
        this.startTicks = startTicks; this.endTicks = endTicks;
        try
        {
            AVFormatContext* output = null;
            FfmpegError.Check(ffmpeg.avformat_alloc_output_context2(&output, null, "mp4", path));
            format = output;
            video = ffmpeg.avformat_new_stream(format, null);
            if (video is null) throw new OutOfMemoryException();
            configuration.CopyVideoTo(video->codecpar);
            video->codecpar->codec_tag = 0;
            video->time_base = new AVRational { num = 1, den = 30 };
            video->avg_frame_rate = new AVRational { num = 30, den = 1 };
            if (configuration.HasAudio)
            {
                audio = ffmpeg.avformat_new_stream(format, null);
                if (audio is null) throw new OutOfMemoryException();
                configuration.CopyAudioTo(audio->codecpar);
                audio->codecpar->codec_tag = 0;
                audio->time_base = new AVRational { num = 1, den = 48000 };
            }
            nativePacket = ffmpeg.av_packet_alloc();
            if (nativePacket is null) throw new OutOfMemoryException();
            FfmpegError.Check(ffmpeg.avio_open(&format->pb, path, ffmpeg.AVIO_FLAG_WRITE));
            FfmpegError.Check(ffmpeg.avformat_write_header(format, null));
        }
        catch { Dispose(); throw; }
    }

    public void Write(EncodedPacket packet)
    {
        var stream = packet.Track == MediaTrack.Video ? video : audio;
        if (stream is null) throw new InvalidOperationException("The clip has no matching stream.");
        var timeBase = new AVRational { num = packet.TimeBaseNumerator, den = packet.TimeBaseDenominator };
        var tickBase = new AVRational { num = 1, den = (int)TimeSpan.TicksPerSecond };
        var origin = ffmpeg.av_rescale_q(startTicks, tickBase, timeBase);
        FfmpegError.Check(ffmpeg.av_new_packet(nativePacket, packet.Data.Length));
        try
        {
            packet.Data.Span.CopyTo(new Span<byte>(nativePacket->data, packet.Data.Length));
            nativePacket->pts = packet.Pts - origin;
            nativePacket->dts = packet.Dts - origin;
            nativePacket->duration = packet.Duration;
            if (endTicks != long.MaxValue)
            {
                var end = ffmpeg.av_rescale_q(endTicks, tickBase, timeBase);
                nativePacket->duration = Math.Min(packet.Duration, end - packet.Pts);
                if (nativePacket->duration <= 0) return;
            }
            nativePacket->flags = packet.IsKeyframe ? ffmpeg.AV_PKT_FLAG_KEY : 0;
            nativePacket->stream_index = stream->index;
            nativePacket->pos = -1;
            ffmpeg.av_packet_rescale_ts(nativePacket, timeBase, stream->time_base);
            FfmpegError.Check(ffmpeg.av_interleaved_write_frame(format, nativePacket));
        }
        finally { ffmpeg.av_packet_unref(nativePacket); }
    }

    public void Complete() => FfmpegError.Check(ffmpeg.av_write_trailer(format));

    public void Dispose()
    {
        var packet = nativePacket; nativePacket = null; ffmpeg.av_packet_free(&packet);
        if (format is null) return;
        if (format->pb is not null) ffmpeg.avio_closep(&format->pb);
        ffmpeg.avformat_free_context(format); format = null;
    }
}
