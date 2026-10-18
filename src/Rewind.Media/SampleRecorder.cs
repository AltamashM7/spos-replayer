using Windows.Graphics.Capture;

namespace Rewind.Media;

public sealed record RecordingProgress(double Seconds, int VideoFrames, long CapturedFrames);
public sealed record RecordingReport(string File, string NativeVersion, int Width, int Height,
    int VideoFrames, long CapturedFrames, long DroppedFrames, long DroppedAudioPackets,
    long ReceivedAudioSamples, long SilentAudioSamples, string? AudioDevice, double Seconds,
    double MaximumCaptureDeliveryMilliseconds);

/// <summary>A finite recording proof using the same capture/codec boundaries as the future replay engine.</summary>
public static class SampleRecorder
{
    public static async Task<RecordingReport> RecordAsync(GraphicsCaptureItem item, string outputFile,
        bool includeAudio, TimeSpan duration, IProgress<RecordingProgress>? progress = null,
        Action? started = null, CancellationToken cancellationToken = default)
    {
        if (duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(duration), "Sample duration must be between 1 and 60 seconds.");
        var version = NativeMedia.Initialize();
        outputFile = Path.GetFullPath(outputFile);
        if (File.Exists(outputFile)) throw new IOException("The destination file already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        var temporary = Path.Combine(Path.GetDirectoryName(outputFile)!, $".{Path.GetFileNameWithoutExtension(outputFile)}-{Guid.NewGuid():N}.partial.mp4");
        using var capture = new ScreenCaptureSource(item);
        await using var audio = includeAudio ? new SystemAudioSource() : null;
        capture.Start();
        await capture.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        audio?.Start();
        var origin = MediaClock.NowTicks;
        started?.Invoke();
        var scale = Math.Min(1d, Math.Min(1920d / capture.Width, 1080d / capture.Height));
        var width = Math.Max(2, (int)(capture.Width * scale) / 2 * 2);
        var height = Math.Max(2, (int)(capture.Height * scale) / 2 * 2);
        try
        {
            var report = await Task.Run(async () =>
            {
                using var writer = new Mp4Writer(temporary, width, height, includeAudio);
                using var audioReader = audio is null ? null : new AudioTimelineReader(audio.Packets, origin);
                var audioSamples = new float[writer.AudioFrameSamples * 2];
                VideoFrame? current = null;
                VideoFrame? pending = null;
                var videoCount = (int)Math.Ceiling(duration.TotalSeconds * 30);
                var audioCount = (long)Math.Ceiling(duration.TotalSeconds * 48000);
                var videoIndex = 0;
                long audioIndex = 0;
                try
                {
                    while (videoIndex < videoCount || (includeAudio && audioIndex < audioCount))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (capture.Error is { } videoError) throw new IOException("Screen capture stopped.", videoError);
                        if (audio?.Error is { } audioError) throw new IOException("System audio capture stopped.", audioError);
                        var videoTicks = videoIndex < videoCount ? videoIndex * TimeSpan.TicksPerSecond / 30 : long.MaxValue;
                        var audioTicks = includeAudio && audioIndex < audioCount ? audioIndex * TimeSpan.TicksPerSecond / 48000 : long.MaxValue;
                        var nextTicks = Math.Min(videoTicks, audioTicks);
                        // Keep a short bounded capture lead so the corresponding audio packet has arrived.
                        var remaining = origin + nextTicks + TimeSpan.TicksPerMillisecond * 200 - MediaClock.NowTicks;
                        if (remaining > 0) await Task.Delay(TimeSpan.FromTicks(remaining), cancellationToken).ConfigureAwait(false);
                        if (videoTicks <= audioTicks)
                        {
                            while (true)
                            {
                                pending ??= capture.Frames.TryDequeue();
                                if (pending is null || (current is not null && pending.Timestamp > origin + videoTicks)) break;
                                current?.Dispose(); current = pending; pending = null;
                            }
                            if (current is null) throw new IOException("No screen frame was received.");
                            writer.WriteVideo(current, videoIndex++);
                            if (videoIndex % 15 == 0) progress?.Report(new RecordingProgress(videoIndex / 30d, videoIndex, capture.Captured));
                        }
                        else
                        {
                            audioReader!.Read(audioIndex, audioSamples);
                            writer.WriteAudio(audioSamples, audioIndex);
                            audioIndex += writer.AudioFrameSamples;
                        }
                    }
                    writer.Complete();
                    return new RecordingReport(outputFile, version, width, height, videoIndex, capture.Captured,
                        capture.Frames.Dropped, audio?.Packets.Dropped ?? 0, audio?.ReceivedSamples ?? 0,
                        audioReader?.SilentSamples ?? 0, audio?.DeviceName, videoIndex / 30d,
                        capture.MaximumDeliveryMilliseconds);
                }
                finally { current?.Dispose(); pending?.Dispose(); }
            }, cancellationToken);
            File.Move(temporary, outputFile);
            return report;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
