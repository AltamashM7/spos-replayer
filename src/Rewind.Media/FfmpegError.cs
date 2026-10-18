using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Rewind.Media;

internal static unsafe class FfmpegError
{
    public static void Check(int result)
    {
        if (result >= 0) return;
        var text = stackalloc byte[512];
        ffmpeg.av_strerror(result, text, 512);
        throw new IOException($"Media operation failed: {Marshal.PtrToStringUTF8((nint)text)} ({result}).");
    }
}
