namespace Rewind.Core;

/// <summary>Initial recording profile; runtime resource limits belong to the recording engine.</summary>
public static class ReplayDefaults
{
    public const int DurationSeconds = 30;
    public const int FramesPerSecond = 30;
    public const int MaximumHeight = 1080;
    public const bool CaptureSystemAudio = true;
}
