using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using Rewind.Media;

namespace Rewind.App;

public partial class MainWindow
{
    internal async Task RunMonitorProofAsync(string directory)
    {
        Window? source = null;
        var animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        var checks = new List<string>();
        var reports = new List<ReplaySaveReport>();
        void Check(bool value, string description)
        {
            if (!value) throw new InvalidOperationException(description);
            checks.Add(description);
        }
        async Task UntilAsync(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition())
            {
                if (timer.Elapsed.TotalSeconds > 15) throw new TimeoutException("Monitor command did not finish.");
                await Task.Delay(25);
            }
        }
        try
        {
            var monitor = MonitorPicker.SelectedItem as MonitorCaptureTarget
                ?? throw new InvalidOperationException("No desktop monitor is available.");
            source = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, Topmost = true, Background = new SolidColorBrush(Color.FromRgb(210, 30, 40))
            };
            var counter = new TextBlock { FontSize = 24, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            source.Content = counter;
            var ticks = 0;
            animation.Tick += (_, _) => counter.Text = (++ticks).ToString(System.Globalization.CultureInfo.InvariantCulture);
            source.SourceInitialized += (_, _) =>
            {
                if (!SetWindowPos(new WindowInteropHelper(source).Handle, new nint(-1),
                    monitor.Left, monitor.Top, monitor.Width, monitor.Height, 0x0040))
                    throw new System.ComponentModel.Win32Exception();
            };
            source.Show();
            animation.Start();
            await Task.Delay(500);
            CaptureModePicker.SelectedIndex = 1;
            Check(MonitorPicker.Visibility == Visibility.Visible && RecordButton.IsEnabled,
                "Border-free mode exposes an available monitor");
            SavePreferences(this, new RoutedEventArgs());
            var saved = preferencesStore.Load().Preferences;
            Check(saved.BorderlessMonitorCapture && saved.MonitorDeviceName == monitor.DeviceName,
                "Capture mode and monitor are persisted in isolated preferences");
            for (var run = 0; run < 2; run++)
            {
                AudioToggle.IsChecked = run == 0;
                ToggleBuffering(this, new RoutedEventArgs());
                await UntilAsync(() => startupCancellation is null);
                Check(session is not null, $"Monitor session {run + 1} starts through the production UI command");
                Check(!CaptureModePicker.IsEnabled && !MonitorPicker.IsEnabled,
                    $"Monitor selection is locked during session {run + 1}");
                await Task.Delay(1500);
                source.Background = new SolidColorBrush(Color.FromRgb(30, 80, 210));
                await Task.Delay(2000);
                reports.Add(await session!.SaveReplayAsync(Path.Combine(directory, $"monitor-{run + 1}.mp4")));
                Check(!session.Completion.IsCompleted, $"Monitor session {run + 1} continues after export");
                await StopBufferingAsync();
                Check(CaptureModePicker.IsEnabled && MonitorPicker.IsEnabled && session is null,
                    $"Monitor session {run + 1} releases capture and restores controls");
                source.Background = new SolidColorBrush(Color.FromRgb(210, 30, 40));
                await Task.Delay(250);
            }
            File.WriteAllText(Path.Combine(directory, "monitor.json"), JsonSerializer.Serialize(new
            {
                Passed = true, Monitor = monitor, Checks = checks, Replays = reports,
                Note = "Only the fixture's opaque full-monitor color surface was recorded."
            }, new JsonSerializerOptions { WriteIndented = true }));
            source.Close();
            Close();
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "monitor-error.txt"), exception + "\nPassed: " + string.Join("; ", checks));
            if (session is not null) await StopBufferingAsync();
            source?.Close();
            Close();
            Application.Current.Shutdown(1);
        }
        finally { animation.Stop(); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
