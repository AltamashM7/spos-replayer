using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Rewind.Media;

public static class NativeMedia
{
    private static readonly object Gate = new();
    private static string? version;
    public static string Initialize()
    {
        lock (Gate)
        {
            if (version is not null) return version;
            var location = Environment.GetEnvironmentVariable("REWIND_FFMPEG_PATH");
            if (string.IsNullOrWhiteSpace(location)) location = null;
            var bundled = Path.Combine(AppContext.BaseDirectory, "native", "ffmpeg");
            if (location is null && File.Exists(Path.Combine(bundled, "avcodec-63.dll"))) location = bundled;
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); location is null && directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, ".tools", "ffmpeg", "ffmpeg-9.0.2-full_build-shared", "bin");
                if (File.Exists(Path.Combine(candidate, "avcodec-63.dll"))) location = candidate;
            }
            if (location is null || !File.Exists(Path.Combine(location, "avcodec-63.dll")))
                throw new FileNotFoundException("FFmpeg libraries are missing. Re-extract the complete portable package, or run scripts/setup-media.ps1 in the source checkout.");
            location = Path.GetFullPath(location);
            if (!SetDllDirectory(location)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            ffmpeg.RootPath = location;
            unsafe
            {
                var foundVersion = ffmpeg.av_version_info();
                if (ffmpeg.avcodec_version() >> 16 != 63 || ffmpeg.avutil_version() >> 16 != 61)
                    throw new InvalidOperationException("The FFmpeg native libraries do not match the pinned C# bindings.");
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                version = foundVersion;
            }
            return version;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string path);
}
