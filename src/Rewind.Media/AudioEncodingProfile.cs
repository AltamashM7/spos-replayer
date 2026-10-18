namespace Rewind.Media;

internal static class AudioEncodingProfile
{
    public const int BitRate = 192000;
    // Leave 6 dB for floating-point mix peaks and AAC reconstruction overshoot.
    public static float Prepare(float sample) => float.IsFinite(sample) ? Math.Clamp(sample * 0.5f, -1f, 1f) : 0;
}
