using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Rewind.Media;

namespace Rewind.App.Diagnostics;

internal static class AudioQualityProof
{
    public static async Task RunAsync(Window window, string directory)
    {
        Directory.CreateDirectory(directory);
        var code = 0;
        try
        {
            // Exercise full-scale and above-full-scale PCM without playing loud audio through the speakers.
            using (var writer = new Mp4Writer(Path.Combine(directory, "headroom.mp4"), 64, 64, true))
            using (var frame = new VideoFrame(64, 64, 0))
            {
                frame.Data.AsSpan(0, 64 * 64 * 4).Clear();
                var samples = new float[writer.AudioFrameSamples * 2];
                for (var index = 0; index < 90; index++) writer.WriteVideo(frame, index);
                for (long start = 0; start < 144000; start += writer.AudioFrameSamples)
                {
                    for (var index = 0; index < writer.AudioFrameSamples; index++)
                    {
                        var sample = (float)(1.4 * Math.Sin(2 * Math.PI * 997 * (start + index) / 48000d));
                        samples[index * 2] = sample;
                        samples[index * 2 + 1] = sample;
                    }
                    writer.WriteAudio(samples, start);
                }
                writer.Complete();
            }
            var packets = new ConcurrentQueue<AudioPacketObservation>();
            var pauseBefore = GC.GetTotalPauseDuration();
            ReplaySaveReport replay;
            long corrections;
            await using (var player = new WasapiPlayerBuilder().WithLatency(100).WithMmcssThreadPriority("Audio").Build())
            {
                player.Init(new ContinuousTone().ToWaveProvider());
                await using (var session = await ReplaySession.StartAsync(
                    CaptureInterop.ForWindow(new WindowInteropHelper(window).Handle), TimeSpan.FromSeconds(30), true))
                {
                    session.ObserveAudioForDiagnostics(packets.Enqueue);
                    player.Play();
                    await Task.Delay(TimeSpan.FromSeconds(12));
                    replay = await session.SaveReplayAsync(Path.Combine(directory, "audio-quality.mp4"));
                    corrections = session.AudioCorrectionsForDiagnostics;
                }
            }
            var observations = packets.ToArray();
            var deltas = observations.Zip(observations.Skip(1), (a, b) => new
            {
                QpcDeltaFrames = MediaClock.Samples(b.Timestamp - a.Timestamp) - a.Frames,
                DeviceDeltaFrames = b.DevicePosition - a.DevicePosition - a.Frames
            }).ToArray();
            File.WriteAllText(Path.Combine(directory, "audio-quality.json"), JsonSerializer.Serialize(new
            {
                Replay = replay, Packets = observations.Length, JitterAdjustedPackets = corrections,
                TimestampDeltas = deltas.GroupBy(x => x.QpcDeltaFrames).OrderBy(x => x.Key).Select(x => new { Frames = x.Key, Count = x.Count() }),
                DeviceDeltas = deltas.GroupBy(x => x.DeviceDeltaFrames).OrderBy(x => x.Key).Select(x => new { Frames = x.Key, Count = x.Count() }),
                Discontinuities = observations.Count(x => (x.Flags & (int)AudioClientBufferFlags.DataDiscontinuity) != 0),
                Peak = observations.Max(x => x.Peak), OverFullScaleSamples = observations.Sum(x => x.OverFullScale),
                ManagedPauseMilliseconds = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds,
                Observations = observations
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "audio-quality-error.txt"), exception.ToString());
            code = 1;
        }
        Application.Current.Shutdown(code);
    }

    private sealed class ContinuousTone : ISampleProvider
    {
        private long sample;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(Span<float> buffer)
        {
            for (var index = 0; index < buffer.Length; index += 2)
            {
                var value = (float)(0.025 * Math.Sin(2 * Math.PI * 730 * sample++ / 48000d));
                buffer[index] = value;
                buffer[index + 1] = value;
            }
            return buffer.Length;
        }
    }
}
