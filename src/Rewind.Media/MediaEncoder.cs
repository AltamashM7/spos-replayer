using FFmpeg.AutoGen;
using Rewind.Core;

namespace Rewind.Media;

/// <summary>Single-worker-owned codec and muxer state. Capture callbacks never enter this class.</summary>
internal sealed unsafe class MediaEncoder : IDisposable
{
    private readonly Action<EncodedPacket> output;
    private AVCodecContext* videoCodec;
    private AVCodecContext* audioCodec;
    private AVFrame* videoFrame;
    private AVFrame* audioFrame;
    private AVPacket* packet;
    private SwsContext* scaler;
    public int AudioFrameSamples => audioCodec is null ? 0 : audioCodec->frame_size;
    public int Width { get; }
    public int Height { get; }

    public MediaEncoder(int width, int height, bool includeAudio, Action<EncodedPacket> output)
    {
        NativeMedia.Initialize();
        Width = width;
        Height = height;
        this.output = output;
        try
        {
            var encoder = ffmpeg.avcodec_find_encoder_by_name("libx264");
            if (encoder is null) throw new NotSupportedException("The FFmpeg build has no H.264 encoder.");
            videoCodec = ffmpeg.avcodec_alloc_context3(encoder);
            if (videoCodec is null) throw new OutOfMemoryException();
            videoCodec->width = width;
            videoCodec->height = height;
            videoCodec->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            videoCodec->time_base = new AVRational { num = 1, den = 30 };
            videoCodec->framerate = new AVRational { num = 30, den = 1 };
            videoCodec->gop_size = 30;
            videoCodec->max_b_frames = 0;
            videoCodec->thread_count = 2;
            videoCodec->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            AVDictionary* options = null;
            try
            {
                Check(ffmpeg.av_dict_set(&options, "preset", "ultrafast", 0));
                Check(ffmpeg.av_dict_set(&options, "tune", "zerolatency", 0));
                Check(ffmpeg.av_dict_set(&options, "crf", "23", 0));
                Check(ffmpeg.avcodec_open2(videoCodec, encoder, &options));
            }
            finally { ffmpeg.av_dict_free(&options); }
            videoFrame = ffmpeg.av_frame_alloc();
            if (videoFrame is null) throw new OutOfMemoryException();
            videoFrame->format = (int)videoCodec->pix_fmt;
            videoFrame->width = width;
            videoFrame->height = height;
            videoFrame->duration = 1;
            Check(ffmpeg.av_frame_get_buffer(videoFrame, 32));

            if (includeAudio) InitializeAudio();
            packet = ffmpeg.av_packet_alloc();
            if (packet is null) throw new OutOfMemoryException();
        }
        catch { Dispose(); throw; }
    }

    private void InitializeAudio()
    {
        var encoder = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
        if (encoder is null) throw new NotSupportedException("The FFmpeg build has no AAC encoder.");
        audioCodec = ffmpeg.avcodec_alloc_context3(encoder);
        if (audioCodec is null) throw new OutOfMemoryException();
        audioCodec->sample_rate = 48000;
        audioCodec->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        audioCodec->bit_rate = AudioEncodingProfile.BitRate;
        audioCodec->time_base = new AVRational { num = 1, den = 48000 };
        ffmpeg.av_channel_layout_default(&audioCodec->ch_layout, 2);
        audioCodec->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
        Check(ffmpeg.avcodec_open2(audioCodec, encoder, null));
        audioFrame = ffmpeg.av_frame_alloc();
        if (audioFrame is null) throw new OutOfMemoryException();
        audioFrame->nb_samples = audioCodec->frame_size;
        audioFrame->format = (int)audioCodec->sample_fmt;
        audioFrame->sample_rate = 48000;
        Check(ffmpeg.av_channel_layout_copy(&audioFrame->ch_layout, &audioCodec->ch_layout));
        Check(ffmpeg.av_frame_get_buffer(audioFrame, 0));
    }

    public void WriteVideo(VideoFrame source, long frameNumber)
    {
        Check(ffmpeg.av_frame_make_writable(videoFrame));
        scaler = ffmpeg.sws_getCachedContext(scaler, source.Width, source.Height, AVPixelFormat.AV_PIX_FMT_BGRA,
            Width, Height, AVPixelFormat.AV_PIX_FMT_YUV420P, (int)SwsFlags.SWS_BILINEAR, null, null, null);
        if (scaler is null) throw new InvalidOperationException("Could not create the video colour converter.");
        fixed (byte* data = source.Data)
        {
            var input = new byte_ptrArray4();
            input[0] = data;
            var strides = new int_array4();
            strides[0] = source.Width * 4;
            Check(ffmpeg.sws_scale(scaler, input, strides, 0, source.Height, videoFrame->data, videoFrame->linesize));
        }
        videoFrame->pts = frameNumber;
        Check(ffmpeg.avcodec_send_frame(videoCodec, videoFrame));
        Drain(videoCodec, MediaTrack.Video);
    }

    public void WriteAudio(ReadOnlySpan<float> interleaved, long startSample)
    {
        Check(ffmpeg.av_frame_make_writable(audioFrame));
        for (var sample = 0; sample < audioFrame->nb_samples; sample++)
        {
            ((float*)audioFrame->data[0])[sample] = AudioEncodingProfile.Prepare(interleaved[sample * 2]);
            ((float*)audioFrame->data[1])[sample] = AudioEncodingProfile.Prepare(interleaved[sample * 2 + 1]);
        }
        audioFrame->pts = startSample;
        Check(ffmpeg.avcodec_send_frame(audioCodec, audioFrame));
        Drain(audioCodec, MediaTrack.Audio);
    }

    private void Drain(AVCodecContext* codec, MediaTrack track)
    {
        while (true)
        {
            var result = ffmpeg.avcodec_receive_packet(codec, packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF) return;
            Check(result);
            try
            {
                if (codec == videoCodec && packet->duration <= 0) packet->duration = 1;
                output(new EncodedPacket(track, packet->pts, packet->dts, packet->duration,
                    codec->time_base.num, codec->time_base.den, (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0,
                    new ReadOnlySpan<byte>(packet->data, packet->size)));
            }
            finally { ffmpeg.av_packet_unref(packet); }
        }
    }

    public void Complete()
    {
        Check(ffmpeg.avcodec_send_frame(videoCodec, null));
        Drain(videoCodec, MediaTrack.Video);
        if (audioCodec is not null) { Check(ffmpeg.avcodec_send_frame(audioCodec, null)); Drain(audioCodec, MediaTrack.Audio); }
    }

    public void Dispose()
    {
        // Finalization is explicit: failure disposal releases resources without publishing a partial clip.
        var p = packet; ffmpeg.av_packet_free(&p); packet = null;
        var vf = videoFrame; ffmpeg.av_frame_free(&vf); videoFrame = null;
        var af = audioFrame; ffmpeg.av_frame_free(&af); audioFrame = null;
        var vc = videoCodec; ffmpeg.avcodec_free_context(&vc); videoCodec = null;
        var ac = audioCodec; ffmpeg.avcodec_free_context(&ac); audioCodec = null;
        ffmpeg.sws_freeContext(scaler); scaler = null;
    }

    public CodecConfiguration CreateConfiguration() => new(videoCodec, audioCodec);

    private static void Check(int result) => FfmpegError.Check(result);
}
