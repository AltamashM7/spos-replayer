using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rewind.Media;

namespace Rewind.App.Diagnostics;

internal sealed class ReliabilityProofWindow : Window
{
    private readonly string directory;
    private readonly int seconds;
    private readonly List<string> checks = [];
    private bool started;

    public ReliabilityProofWindow(string directory, int seconds)
    {
        this.directory = directory; this.seconds = seconds;
        Title = "Rewind reliability verification"; Width = 480; Height = 120;
        Content = new System.Windows.Controls.TextBlock
            { Text = "Rewind is checking its own animated capture window.\nReports are written to the local artifacts folder.", Margin = new Thickness(16) };
        ContentRendered += async (_, _) =>
        {
            if (started) return;
            started = true;
            var code = 0;
            Directory.CreateDirectory(directory);
            try
            {
                if (seconds == 0) await CheckFailuresAsync();
                else await CheckResourcesAsync();
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(directory, "reliability-error.txt"), exception + "\nPassed: " + string.Join("; ", checks));
                code = 1;
            }
            Application.Current.Shutdown(code);
        };
    }

    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        checks.Add(name);
    }

    private async Task<ReplaySession> StartAsync(Window source, bool audio = false)
        => await ReplaySession.StartAsync(CaptureInterop.ForWindow(new WindowInteropHelper(source).Handle),
            TimeSpan.FromSeconds(30), audio);

    private static async Task WaitAsync(ReplaySession session, double elapsed)
    {
        while (session.Statistics.ElapsedSeconds < elapsed)
        {
            if (session.Completion.IsCompleted) { await session.Completion; throw new IOException("Capture ended before the test finished."); }
            await Task.Delay(50);
        }
    }

    private async Task<string> ExpectFailureAsync(ReplaySession session)
    {
        try { await session.Completion.WaitAsync(TimeSpan.FromSeconds(8)); }
        catch (Exception exception) when (exception is not TimeoutException) { return exception.GetBaseException().Message; }
        throw new InvalidOperationException("The source failure did not reach the session.");
    }

    private async Task CheckFailuresAsync()
    {
        var results = new Dictionary<string, object>();
        var source = new AnimatedSourceWindow(false);
        source.Show();
        try
        {
            var session = await StartAsync(source);
            try
            {
                await WaitAsync(session, 1);
                var failedFolder = Path.Combine(directory, "folder-is-a-file");
                File.WriteAllText(failedFolder, "Keep this file intact.");
                try
                {
                    await session.SaveReplayAsync(Path.Combine(failedFolder, "cannot-save.mp4"));
                    throw new InvalidOperationException("Saving through a file as a directory succeeded.");
                }
                catch (IOException) { }
                Check(File.ReadAllText(failedFolder) == "Keep this file intact." && !session.Completion.IsCompleted,
                    "Invalid save directory preserves existing data and leaves capture running");
                var lockedPath = Path.Combine(directory, "locked.mp4");
                using (var locked = new FileStream(lockedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    try { await session.SaveReplayAsync(lockedPath); throw new InvalidOperationException("Locked output was overwritten."); }
                    catch (IOException) { }
                    Check(locked.Length == 0, "Locked destination remains unchanged");
                }
                results["RecoveryExport"] = await session.SaveReplayAsync(Path.Combine(directory, "after-save-errors.mp4"));
                Check(session.Statistics.Buffer.SnapshotBytes == 0, "Failed saves release their snapshot and a later export succeeds");
                source.Width += 100;
                var resizeError = await ExpectFailureAsync(session);
                Check(resizeError.Contains("size changed", StringComparison.OrdinalIgnoreCase), "Real source resize reports an explicit restart error");
                results["ResizeError"] = resizeError;
            }
            finally { await session.DisposeAsync(); }
            session = await StartAsync(source);
            try
            {
                await WaitAsync(session, 0.6);
                source.Close();
                var closeError = await ExpectFailureAsync(session);
                Check(closeError.Contains("closed", StringComparison.OrdinalIgnoreCase), "Real source closure reaches session completion");
                results["CloseError"] = closeError;
            }
            finally { await session.DisposeAsync(); }
        }
        finally { source.Close(); }

        source = new AnimatedSourceWindow(false);
        source.Show();
        try
        {
            var session = await StartAsync(source, audio: true);
            try
            {
                await WaitAsync(session, 0.6);
                session.ChangeAudioEndpointForDiagnostics();
                var audioError = await ExpectFailureAsync(session);
                Check(audioError.Contains("playback device changed", StringComparison.OrdinalIgnoreCase),
                    "Simulated default-device notification stops a real audio/video session");
                results["AudioError"] = audioError;
            }
            finally { await session.DisposeAsync(); }
            session = await StartAsync(source, audio: true);
            try
            {
                session.EncodingDelayForDiagnostics = TimeSpan.FromMilliseconds(130);
                await WaitAsync(session, 4);
                var overloaded = session.Statistics;
                Check(overloaded.DroppedFrames > 0 && !session.Completion.IsCompleted, "Slowed real encoder produces bounded frame drops without crashing");
                session.EncodingDelayForDiagnostics = TimeSpan.Zero;
                await WaitAsync(session, 6);
                var catchUp = Stopwatch.StartNew();
                while (session.Statistics.ElapsedSeconds - session.Statistics.EncodedFrames / 30d > 0.6 && catchUp.Elapsed.TotalSeconds < 12)
                    await Task.Delay(100);
                Check(session.Statistics.ElapsedSeconds - session.Statistics.EncodedFrames / 30d < 0.6,
                    "Encoding catches up after overload ends");
                results["Overloaded"] = overloaded;
                results["AfterOverload"] = await session.SaveReplayAsync(Path.Combine(directory, "after-overload.mp4"));
                var stopTimer = Stopwatch.StartNew();
                await session.DisposeAsync();
                await session.DisposeAsync();
                Check(stopTimer.Elapsed.TotalSeconds < 8, "Repeated shutdown completes after overloaded audio/video capture");
            }
            finally { await session.DisposeAsync(); }
        }
        finally { source.Close(); }
        Check(!Directory.EnumerateFiles(directory, "*.partial.mp4", SearchOption.AllDirectories).Any(), "Failure paths leave no partial exports");
        File.WriteAllText(Path.Combine(directory, "reliability.json"), JsonSerializer.Serialize(new
            { Passed = true, Checks = checks, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task CheckResourcesAsync()
    {
        var source = new AnimatedSourceWindow(true);
        source.Show();
        ReplaySession? session = null;
        using var process = Process.GetCurrentProcess();
        var samples = new List<ResourceSample>();
        try
        {
            session = await StartAsync(source, audio: true);
            using var log = new StreamWriter(Path.Combine(directory, "resources.jsonl")) { AutoFlush = true };
            var previousCpu = process.TotalProcessorTime.TotalSeconds;
            var previousTime = session.Statistics.ElapsedSeconds;
            while (session.Statistics.ElapsedSeconds < seconds)
            {
                await WaitAsync(session, Math.Min(seconds, previousTime + 5));
                process.Refresh();
                var state = session.Statistics;
                var cpu = process.TotalProcessorTime.TotalSeconds;
                var sample = new ResourceSample(state.ElapsedSeconds, process.PrivateMemorySize64, process.WorkingSet64,
                    GC.GetTotalMemory(false), process.HandleCount, process.Threads.Count,
                    (cpu - previousCpu) / (state.ElapsedSeconds - previousTime) / Environment.ProcessorCount * 100,
                    state.EncodedFrames, state.CapturedFrames, state.DroppedFrames, state.Buffer.RetainedBytes,
                    state.Buffer.SnapshotBytes, state.Buffer.AvailableSeconds, state.Buffer.EvictedPackets);
                samples.Add(sample);
                log.WriteLine(JsonSerializer.Serialize(sample));
                CheckBudget(state);
                previousCpu = cpu; previousTime = state.ElapsedSeconds;
            }
            var final = session.Statistics;
            var export = await session.SaveReplayAsync(Path.Combine(directory, "sustained-final.mp4"));
            Check(final.ElapsedSeconds >= seconds, "Requested sustained duration completed");
            Check(final.EncodedFrames / final.ElapsedSeconds >= 28.5, "Sustained encoding averages at least 28.5 FPS");
            Check(export.DurationSeconds is >= 29 and <= 31.1 && export.EndSeconds > seconds - 2,
                "Final replay retains recent history after prolonged eviction");
            // Use medians after warm-up; do not force GC or trim the working set to improve results.
            var baselineTime = Math.Min(300, seconds / 3d);
            var baseline = samples.Where(x => x.Seconds >= baselineTime - Math.Min(60, baselineTime / 2) && x.Seconds <= baselineTime).ToArray();
            var ending = samples.Where(x => x.Seconds >= seconds - Math.Min(300, seconds / 3d)).ToArray();
            static double Median(IEnumerable<double> values) { var array = values.Order().ToArray(); return array[array.Length / 2]; }
            var privateGrowth = Median(ending.Select(x => (double)x.PrivateBytes)) - Median(baseline.Select(x => (double)x.PrivateBytes));
            var handleGrowth = Median(ending.Select(x => (double)x.Handles)) - Median(baseline.Select(x => (double)x.Handles));
            Check(privateGrowth < 64 * 1048576, "Median private-memory growth after warm-up stays below 64 MiB");
            Check(handleGrowth < 32, "Median handle growth after warm-up stays below 32");
            var stopping = Stopwatch.StartNew();
            await session.DisposeAsync();
            Check(stopping.Elapsed.TotalSeconds < 8, "Sustained session shuts down within eight seconds");
            File.WriteAllText(Path.Combine(directory, "reliability.json"), JsonSerializer.Serialize(new
            {
                Passed = true, Checks = checks, RequestedSeconds = seconds, SourceWidth = source.ActualWidth,
                SourceHeight = source.ActualHeight, ProcessorCount = Environment.ProcessorCount,
                Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                OperatingSystem = Environment.OSVersion.VersionString, Samples = samples.Count,
                AverageCpuPercent = samples.Average(x => x.CpuPercent), PeakPrivateMiB = samples.Max(x => x.PrivateBytes) / 1048576d,
                PrivateGrowthMiB = privateGrowth / 1048576, HandleGrowth = handleGrowth,
                Final = final, Export = export, ShutdownMilliseconds = stopping.Elapsed.TotalMilliseconds
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (session is not null) await session.DisposeAsync();
            source.Close();
        }
    }

    private static void CheckBudget(ReplayStatistics state)
    {
        if (state.Buffer.RetainedBytes > state.Buffer.MaximumBytes || state.Buffer.SnapshotBytes > state.Buffer.MaximumBytes || state.Buffer.PacketCount > 8192)
            throw new InvalidOperationException("A sustained sample exceeded a replay storage bound.");
    }

    private sealed record ResourceSample(double Seconds, long PrivateBytes, long WorkingSetBytes, long ManagedBytes,
        int Handles, int Threads, double CpuPercent, long EncodedFrames, long CapturedFrames, long DroppedFrames,
        long BufferBytes, long SnapshotBytes, double AvailableSeconds, long EvictedPackets);
}

internal sealed class AnimatedSourceWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    public AnimatedSourceWindow(bool large)
    {
        Title = "Rewind controlled capture source";
        Width = large ? Math.Min(1920, SystemParameters.WorkArea.Width) : 720;
        Height = large ? Math.Min(1080, SystemParameters.WorkArea.Height) : 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (large) { WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; }
        var visual = new MovingPattern();
        Content = visual;
        timer.Tick += (_, _) => { visual.Frame++; visual.InvalidateVisual(); };
        Loaded += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    private sealed class MovingPattern : FrameworkElement
    {
        private readonly Brush[] colors = Enumerable.Range(0, 64).Select(index =>
        {
            var brush = new SolidColorBrush(Color.FromRgb((byte)(index * 41), (byte)(index * 83), (byte)(index * 127)));
            brush.Freeze(); return (Brush)brush;
        }).ToArray();
        public int Frame { get; set; }
        protected override void OnRender(DrawingContext context)
        {
            var width = ActualWidth / 40;
            var height = ActualHeight / 24;
            for (var row = 0; row < 24; row++)
                for (var col = 0; col < 40; col++)
                    context.DrawRectangle(colors[(row * 7 + col * 3 + Frame / 3) % colors.Length], null,
                        new Rect(col * width, row * height, width + 1, height + 1));
            context.DrawRectangle(Brushes.White, null, new Rect(Frame * 7 % Math.Max(1, ActualWidth - 120), ActualHeight / 2, 120, 80));
        }
    }
}
