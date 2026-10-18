using System.Diagnostics;
using Rewind.Core;
using Windows.Graphics.Capture;

namespace Rewind.Media;

public sealed record ReplayStatistics(double ElapsedSeconds, long EncodedFrames, long CapturedFrames,
    long DroppedFrames, ReplayBufferStatus Buffer);
public sealed record ReplaySaveReport(string File, double StartSeconds, double EndSeconds, double TriggerSeconds,
    double DurationSeconds, int VideoFrames, long SnapshotBytes, double ExportMilliseconds,
    long CapturedAtTrigger, long CapturedAtSnapshot, long CapturedAfterExport);

/// <summary>UI-thread-owned capture lifetime, worker-owned encoders, and independent background exports.</summary>
public sealed class ReplaySession : IAsyncDisposable
{
    private readonly object lifecycle = new();
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PacketRingBuffer buffer;
    private readonly TimeSpan lookback;
    private readonly bool includeAudio;
    private readonly IProgress<ReplayStatistics>? progress;
    private IFrameSource? capture;
    private SystemAudioSource? audio;
    private CodecConfiguration? configuration;
    private Task? pump;
    private Task<ReplaySaveReport>? activeExport;
    private long origin;
    private long encodedFrames;
    private long audioCorrections;
    internal long AudioCorrectionsForDiagnostics => Interlocked.Read(ref audioCorrections);
    private int disposing;
    // The local integration fixture can simulate an export stalled by storage while its snapshot is pinned.
    internal TimeSpan ExportDelayForDiagnostics { get; set; }
    internal TimeSpan EncodingDelayForDiagnostics { get; set; }
    internal void ObserveAudioForDiagnostics(Action<AudioPacketObservation> observer) => audio!.PacketObserved += observer;
    internal void ChangeAudioEndpointForDiagnostics() => audio!.EndpointState.DefaultChanged(
        NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia, "diagnostic replacement");

    private ReplaySession(TimeSpan lookback, bool includeAudio, IProgress<ReplayStatistics>? progress, long maximumBytes)
    {
        this.lookback = lookback; this.includeAudio = includeAudio; this.progress = progress;
        buffer = new PacketRingBuffer(lookback, maximumBytes);
    }

    public ReplayStatistics Statistics => new((MediaClock.NowTicks - origin) / (double)TimeSpan.TicksPerSecond,
        Interlocked.Read(ref encodedFrames), capture?.Captured ?? 0, capture?.Frames.Dropped ?? 0, buffer.Status);
    public Task Completion => pump ?? Task.CompletedTask;

    public static Task<ReplaySession> StartAsync(GraphicsCaptureItem item, TimeSpan lookback, bool includeAudio,
        IProgress<ReplayStatistics>? progress = null, CancellationToken cancellationToken = default,
        long maximumBytes = 128L * 1024 * 1024)
        => StartCoreAsync(() => new ScreenCaptureSource(item), lookback, includeAudio, progress, cancellationToken, maximumBytes);

    public static Task<ReplaySession> StartMonitorAsync(MonitorCaptureTarget monitor, TimeSpan lookback, bool includeAudio,
        IProgress<ReplayStatistics>? progress = null, CancellationToken cancellationToken = default,
        long maximumBytes = 128L * 1024 * 1024)
        => StartCoreAsync(() => new DesktopCaptureSource(monitor), lookback, includeAudio, progress, cancellationToken, maximumBytes);

    private static async Task<ReplaySession> StartCoreAsync(Func<IFrameSource> createSource, TimeSpan lookback, bool includeAudio,
        IProgress<ReplayStatistics>? progress, CancellationToken cancellationToken, long maximumBytes)
    {
        if (lookback <= TimeSpan.Zero || lookback > TimeSpan.FromSeconds(60)) throw new ArgumentOutOfRangeException(nameof(lookback));
        NativeMedia.Initialize();
        var session = new ReplaySession(lookback, includeAudio, progress, maximumBytes);
        try
        {
            session.capture = createSource();
            if (includeAudio) session.audio = new SystemAudioSource();
            session.capture.Start();
            await session.capture.FirstFrame.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            session.audio?.Start();
            session.origin = MediaClock.NowTicks;
            session.pump = Task.Run(session.PumpAsync);
            await session.ready.Task.WaitAsync(cancellationToken);
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    public Task<ReplaySaveReport> SaveReplayAsync(string file, CancellationToken cancellationToken = default)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposing) != 0, this);
            if (stop.IsCancellationRequested) throw new InvalidOperationException("Screen capture is no longer running.");
            if (activeExport is { IsCompleted: false }) throw new InvalidOperationException("A replay export is already in progress.");
            var triggerTicks = MediaClock.NowTicks - origin;
            var cutoff = triggerTicks * 30 / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond / 30;
            var capturedAtTrigger = capture!.Captured;
            activeExport = SaveCoreAsync(file, triggerTicks, cutoff, capturedAtTrigger, cancellationToken);
            return activeExport;
        }
    }

    private async Task<ReplaySaveReport> SaveCoreAsync(string file, long triggerTicks, long cutoff,
        long capturedAtTrigger, CancellationToken cancellationToken)
    {
        // Wait for already-captured frames and delayed AAC packets to reach the fixed trigger cutoff.
        using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, cancellationToken))
        {
            waiting.CancelAfter(TimeSpan.FromSeconds(10));
            try { await buffer.WaitForWatermarkAsync(cutoff, includeAudio, waiting.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Recording could not catch up to the Save time. Stop buffering and select a smaller source.");
            }
        }
        using var snapshot = buffer.Snapshot(cutoff, lookback);
        var capturedAtSnapshot = capture!.Captured;
        file = Path.GetFullPath(file);
        var temporary = Path.Combine(Path.GetDirectoryName(file)!, $".{Path.GetFileNameWithoutExtension(file)}-{Guid.NewGuid():N}.partial.mp4");
        var timer = Stopwatch.StartNew();
        if (ExportDelayForDiagnostics > TimeSpan.Zero)
            await Task.Delay(ExportDelayForDiagnostics, cancellationToken).ConfigureAwait(false);
        await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(file)) throw new IOException("The destination file already exists.");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                using (var muxer = new PacketMuxer(temporary, configuration!, snapshot.StartTicks, snapshot.EndTicks))
                {
                    foreach (var packet in snapshot.Packets)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        muxer.Write(packet);
                    }
                    muxer.Complete();
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, file);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, cancellationToken).ConfigureAwait(false);
        return new ReplaySaveReport(file, snapshot.StartTicks / (double)TimeSpan.TicksPerSecond,
            snapshot.EndTicks / (double)TimeSpan.TicksPerSecond, triggerTicks / (double)TimeSpan.TicksPerSecond,
            snapshot.Duration.TotalSeconds, snapshot.Packets.Count(packet => packet.Track == MediaTrack.Video),
            snapshot.AccountedBytes, timer.Elapsed.TotalMilliseconds, capturedAtTrigger, capturedAtSnapshot, capture!.Captured);
    }

    private async Task PumpAsync()
    {
        VideoFrame? current = null;
        VideoFrame? pending = null;
        try
        {
            var screen = capture!;
            var scale = Math.Min(1d, Math.Min(1920d / screen.Width, 1080d / screen.Height));
            using var encoder = new MediaEncoder(Math.Max(2, (int)(screen.Width * scale) / 2 * 2),
                Math.Max(2, (int)(screen.Height * scale) / 2 * 2), includeAudio, buffer.Append);
            configuration = encoder.CreateConfiguration();
            using var audioReader = audio is null ? null : new AudioTimelineReader(audio.Packets, origin);
            var audioSamples = new float[encoder.AudioFrameSamples * 2];
            long videoIndex = 0;
            long audioIndex = 0;
            ready.TrySetResult();
            try
            {
                while (true)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    if (screen.Error is { } videoError) throw new IOException("Screen capture stopped.", videoError);
                    if (audio?.Error is { } audioError) throw new IOException("System audio capture stopped.", audioError);
                    var videoTicks = videoIndex * TimeSpan.TicksPerSecond / 30;
                    var audioTicks = includeAudio ? audioIndex * TimeSpan.TicksPerSecond / 48000 : long.MaxValue;
                    var remaining = origin + Math.Min(videoTicks, audioTicks) + TimeSpan.TicksPerMillisecond * 200 - MediaClock.NowTicks;
                    if (remaining > 0) await Task.Delay(TimeSpan.FromTicks(remaining), stop.Token).ConfigureAwait(false);
                    if (videoTicks <= audioTicks)
                    {
                        while (true)
                        {
                            pending ??= screen.Frames.TryDequeue();
                            if (pending is null || (current is not null && pending.Timestamp > origin + videoTicks)) break;
                            current?.Dispose(); current = pending; pending = null;
                        }
                        if (current is null) throw new IOException("No screen frame was received.");
                        if (EncodingDelayForDiagnostics > TimeSpan.Zero)
                            await Task.Delay(EncodingDelayForDiagnostics, stop.Token).ConfigureAwait(false);
                        encoder.WriteVideo(current, videoIndex++);
                        Interlocked.Exchange(ref encodedFrames, videoIndex);
                        if (videoIndex % 15 == 0) progress?.Report(Statistics);
                    }
                    else
                    {
                        audioReader!.Read(audioIndex, audioSamples);
                        Interlocked.Exchange(ref audioCorrections, audioReader.ResampledPackets);
                        encoder.WriteAudio(audioSamples, audioIndex);
                        audioIndex += encoder.AudioFrameSamples;
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            encoder.Complete();
        }
        catch (Exception exception) { ready.TrySetException(exception); throw; }
        finally { current?.Dispose(); pending?.Dispose(); stop.Cancel(); }
    }

    public ValueTask DisposeAsync() => Interlocked.CompareExchange(ref disposing, 1, 0) == 0
        ? new ValueTask(StopCoreAsync()) : new ValueTask(stopped.Task);

    private async Task StopCoreAsync()
    {
        try
        {
            stop.Cancel();
            if (pump is not null)
                try { await pump; } catch (Exception) { /* Completion carries the original capture/codec error. */ }
            Exception? cleanupError = null;
            try { if (audio is not null) await audio.DisposeAsync(); }
            catch (Exception exception) { cleanupError = exception; }
            try { capture?.Dispose(); }
            catch (Exception exception) { cleanupError ??= exception; }
            // Capture stops immediately; the immutable snapshot and codec parameters outlive any export.
            Task? export;
            lock (lifecycle) export = activeExport;
            if (export is not null)
                try { await export; } catch (Exception) { /* The save caller observes export failure. */ }
            configuration?.Dispose();
            buffer.Dispose();
            stop.Dispose();
            if (cleanupError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupError).Throw();
        }
        finally { stopped.TrySetResult(); }
    }
}
