using FFmpeg.AutoGen;

namespace Rewind.Media;

/// <summary>Independent immutable codec parameters, retained until the session's export has ended.</summary>
internal sealed unsafe class CodecConfiguration : IDisposable
{
    private AVCodecParameters* video;
    private AVCodecParameters* audio;
    public bool HasAudio => audio is not null;

    public CodecConfiguration(AVCodecContext* videoContext, AVCodecContext* audioContext)
    {
        try
        {
            video = ffmpeg.avcodec_parameters_alloc();
            if (video is null) throw new OutOfMemoryException();
            FfmpegError.Check(ffmpeg.avcodec_parameters_from_context(video, videoContext));
            if (audioContext is not null)
            {
                audio = ffmpeg.avcodec_parameters_alloc();
                if (audio is null) throw new OutOfMemoryException();
                FfmpegError.Check(ffmpeg.avcodec_parameters_from_context(audio, audioContext));
            }
        }
        catch { Dispose(); throw; }
    }

    public void CopyVideoTo(AVCodecParameters* destination) => FfmpegError.Check(ffmpeg.avcodec_parameters_copy(destination, video));
    public void CopyAudioTo(AVCodecParameters* destination) => FfmpegError.Check(ffmpeg.avcodec_parameters_copy(destination, audio));
    public void Dispose()
    {
        var v = video; video = null; ffmpeg.avcodec_parameters_free(&v);
        var a = audio; audio = null; ffmpeg.avcodec_parameters_free(&a);
    }
}
