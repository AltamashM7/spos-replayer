using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using Rewind.Media;

namespace Rewind.App.Diagnostics;

/// <summary>Controlled pre-trigger proof, including snapshot eviction and stop/export races.</summary>
internal sealed class ReplayProofWindow : Window
{
    private readonly Stopwatch clock = new();
    private readonly Grid panel = new();
    private readonly TextBlock label = new() { FontSize = 44, Foreground = Brushes.White };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double offset;
    private bool running;
    private readonly int lookbackSeconds;
    private double Seconds => offset + clock.Elapsed.TotalSeconds;

    public ReplayProofWindow(string directory, bool includeAudio, int lookbackSeconds = 3)
    {
        this.lookbackSeconds = lookbackSeconds;
        Title = "Rewind pre-trigger verification";
        Width = 680; Height = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(label);
        Content = panel;
        Paint();
        timer.Tick += (_, _) => Paint();
        ContentRendered += async (_, _) =>
        {
            if (running) return;
            running = true;
            Directory.CreateDirectory(directory);
            ReplaySession? session = null;
            WasapiPlayer? player = null;
            var exitCode = 0;
            try
            {
                if (includeAudio)
                {
                    player = new WasapiPlayerBuilder().WithLatency(40).Build();
                    player.Init(new Tone(() => Seconds).ToWaveProvider());
                }
                session = await ReplaySession.StartAsync(CaptureInterop.ForWindow(new WindowInteropHelper(this).Handle),
                    TimeSpan.FromSeconds(lookbackSeconds), includeAudio);
                offset = session.Statistics.ElapsedSeconds;
                clock.Start(); timer.Start(); player?.Play();
                await UntilAsync(1.6, session);
                var partial = await session.SaveReplayAsync(Path.Combine(directory, "partial.mp4"));
                await UntilAsync(lookbackSeconds + 4.6, session);
                session.ExportDelayForDiagnostics = TimeSpan.FromSeconds(4);
                var export = session.SaveReplayAsync(Path.Combine(directory, "replay.mp4"));
                var busyRejected = false;
                try { await session.SaveReplayAsync(Path.Combine(directory, "busy.mp4")); }
                catch (InvalidOperationException) { busyRejected = true; }
                await UntilAsync(lookbackSeconds + 8.3, session);
                var whilePinned = session.Statistics;
                if (whilePinned.Buffer.SnapshotBytes == 0) throw new InvalidOperationException("The diagnostic snapshot was not held through eviction.");
                var replay = await export;
                if (replay.CapturedAfterExport <= replay.CapturedAtSnapshot + 60)
                    throw new InvalidOperationException("Capture did not continue while the snapshot was pinned.");
                session.ExportDelayForDiagnostics = TimeSpan.Zero;
                var existingLength = new FileInfo(replay.File).Length;
                var existingRejected = false;
                try { await session.SaveReplayAsync(replay.File); }
                catch (IOException) { existingRejected = true; }
                if (new FileInfo(replay.File).Length != existingLength) throw new IOException("A save failure changed the existing clip.");
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel();
                    try
                    {
                        await session.SaveReplayAsync(Path.Combine(directory, "cancelled.mp4"), cancelled.Token);
                        throw new InvalidOperationException("The cancelled save unexpectedly succeeded.");
                    }
                    catch (OperationCanceledException) { }
                }
                await UntilAsync(lookbackSeconds + 9.4, session);
                var repeated = await session.SaveReplayAsync(Path.Combine(directory, "repeated.mp4"));
                session.ExportDelayForDiagnostics = TimeSpan.FromSeconds(1);
                var finalExport = session.SaveReplayAsync(Path.Combine(directory, "stop-export.mp4"));
                while (session.Statistics.Buffer.SnapshotBytes == 0) await Task.Delay(10);
                var stopping = session.DisposeAsync();
                var stoppedExport = await finalExport;
                await stopping;
                var report = new
                {
                    Partial = partial, Replay = replay, Repeated = repeated, StopExport = stoppedExport,
                    BusyRejected = busyRejected, ExistingFileRejected = existingRejected,
                    CancellationPassed = !File.Exists(Path.Combine(directory, "cancelled.mp4")),
                    WhileSnapshotPinned = whilePinned,
                    IncludeAudio = includeAudio, LookbackSeconds = lookbackSeconds
                };
                await File.WriteAllTextAsync(Path.Combine(directory, "replay.json"), JsonSerializer.Serialize(report,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception exception)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "replay-error.txt"), exception.ToString());
                exitCode = 1;
            }
            finally
            {
                timer.Stop();
                if (session is not null) await session.DisposeAsync();
                if (player is not null) await player.DisposeAsync();
            }
            Application.Current.Shutdown(exitCode);
        };
    }

    private void Paint()
    {
        var seconds = Seconds;
        var flash = seconds is >= 2 and < 2.3 or >= 5 and < 5.3;
        var future = seconds >= lookbackSeconds + 8 && seconds < lookbackSeconds + 8.5;
        panel.Background = flash ? Brushes.White : future ? Brushes.Magenta : new SolidColorBrush(Color.FromRgb(20, 28, 36));
        label.Foreground = flash ? Brushes.Black : Brushes.White;
        label.Text = $"{seconds:F2} s\n{(future ? "AFTER SAVE" : flash ? "BEFORE SAVE" : "REWIND")}";
    }

    private async Task UntilAsync(double target, ReplaySession session)
    {
        while (Seconds < target)
        {
            if (session.Completion.IsCompleted) { await session.Completion; throw new IOException("Capture stopped before the fixture finished."); }
            await Task.Delay(20);
        }
    }

    private sealed class Tone(Func<double> seconds) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(Span<float> buffer)
        {
            var start = seconds() + 0.04;
            for (var i = 0; i < buffer.Length; i += 2)
            {
                var time = start + i / (2 * 48000d);
                var value = time is >= 5 and < 5.3 ? (float)(0.04 * Math.Sin(2 * Math.PI * 600 * time)) : 0;
                buffer[i] = value; buffer[i + 1] = value;
            }
            return buffer.Length;
        }
    }
}
