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

/// <summary>Opt-in integration fixture: captures only this window and a short generated sound.</summary>
internal sealed class MediaProofWindow : Window
{
    private readonly TextBlock timerLabel = new() { FontSize = 52, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
    private readonly TextBlock signalLabel = new() { FontSize = 24, Foreground = Brushes.White };
    private readonly Grid panel = new() { Background = new SolidColorBrush(Color.FromRgb(20, 28, 36)) };
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool running;

    public MediaProofWindow(string outputDirectory, bool includeAudio = true, bool cancel = false, bool silence = false)
    {
        Title = "Rewind capture verification";
        Width = 680; Height = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = "REWIND · LOCAL MEDIA PROOF", Foreground = Brushes.LightGray, FontSize = 16 });
        stack.Children.Add(timerLabel);
        stack.Children.Add(signalLabel);
        panel.Children.Add(stack);
        Content = panel;
        timer.Tick += (_, _) =>
        {
            var seconds = clock.Elapsed.TotalSeconds;
            timerLabel.Text = $"{seconds:0.00} s";
            var flash = seconds is >= 2 and < 2.3 or >= 5 and < 5.3;
            panel.Background = flash ? Brushes.White : new SolidColorBrush(Color.FromRgb(20, 28, 36));
            timerLabel.Foreground = flash ? Brushes.Black : Brushes.White;
            signalLabel.Foreground = flash ? Brushes.Black : Brushes.White;
            signalLabel.Text = flash ? "SYNC PULSE" : "Screen capture + system audio";
        };
        ContentRendered += async (_, _) =>
        {
            if (running) return;
            running = true;
            Directory.CreateDirectory(outputDirectory);
            WasapiPlayer? player = null;
            var exitCode = 0;
            using var cancellation = new CancellationTokenSource();
            try
            {
                if (includeAudio && !silence)
                {
                    player = new WasapiPlayerBuilder().WithLatency(40).Build();
                    player.Init(new ProofTone(clock).ToWaveProvider());
                }
                var item = CaptureInterop.ForWindow(new WindowInteropHelper(this).Handle);
                var report = await SampleRecorder.RecordAsync(item, Path.Combine(outputDirectory, "sample.mp4"),
                    includeAudio, TimeSpan.FromSeconds(8), started: () =>
                    {
                        clock.Start(); timer.Start(); player?.Play();
                        if (cancel) cancellation.CancelAfter(TimeSpan.FromSeconds(2));
                    }, cancellationToken: cancellation.Token);
                await File.WriteAllTextAsync(Path.Combine(outputDirectory, "recording.json"), JsonSerializer.Serialize(report,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (OperationCanceledException) when (cancel)
            {
                await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cancelled.json"), "{\"Cancelled\":true}");
            }
            catch (Exception exception)
            {
                await File.WriteAllTextAsync(Path.Combine(outputDirectory, "recording-error.txt"), exception.ToString());
                exitCode = 1;
            }
            finally
            {
                timer.Stop();
                if (player is not null) await player.DisposeAsync();
            }
            Application.Current.Shutdown(exitCode);
        };
    }

    private sealed class ProofTone(Stopwatch clock) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(Span<float> buffer)
        {
            // Generate against wall time, so a playback underrun cannot shift later test tones.
            var startTime = clock.Elapsed.TotalSeconds + 0.04;
            for (var index = 0; index < buffer.Length; index += 2)
            {
                var time = startTime + index / (2 * 48000d);
                var active = time is >= 2 and < 2.3 or >= 5 and < 5.3;
                var value = active ? (float)(0.04 * Math.Sin(2 * Math.PI * 600 * time)) : 0;
                buffer[index] = value;
                buffer[index + 1] = value;
            }
            return buffer.Length;
        }
    }
}
