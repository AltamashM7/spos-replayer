using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Rewind.App.Diagnostics;
using Rewind.Media;

namespace Rewind.App;

public partial class MainWindow
{
    internal async Task RunWorkflowProofAsync(string directory)
    {
        var checks = new List<string>();
        var source = new AnimatedSourceWindow(false);
        var closed = false;
        Closed += (_, _) => closed = true;
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
                if (timer.Elapsed.TotalSeconds > 15) throw new TimeoutException("Live desktop command did not finish.");
                await Task.Delay(25);
            }
        }
        try
        {
            outputDirectory = directory;
            OutputFolderLabel.Text = directory;
            source.Show();
            // Supply only this fixture's source; exercise the production command handlers below.
            session = await ReplaySession.StartAsync(CaptureInterop.ForWindow(new WindowInteropHelper(source).Handle),
                TimeSpan.FromSeconds(SelectedSeconds), true, new Progress<ReplayStatistics>(UpdateStatistics));
            var current = session;
            current.ExportDelayForDiagnostics = TimeSpan.FromSeconds(1);
            SetPreferencesEnabled(false);
            RecordingStateLabel.Text = "Buffering";
            RecordButton.Content = "Stop buffering";
            _ = ObserveSessionAsync(current);
            await UntilAsync(() => SaveReplayButton.IsEnabled);
            Check(!DurationPicker.IsEnabled && !AudioToggle.IsEnabled, "Capture preferences are locked during the live session");
            WindowState = WindowState.Minimized;
            Check(!IsVisible, "Live recording window minimizes to the tray");
            var handle = new WindowInteropHelper(this).Handle;
            SendMessage(handle, 0x0312, 0x5231, 0);
            Check(saving, "Hotkey message starts a save while the recording window is hidden");
            SendMessage(handle, 0x0312, 0x5231, 0);
            await UntilAsync(() => !saving);
            Check(Directory.EnumerateFiles(directory, "*.mp4").Count() == 1 && !current.Completion.IsCompleted,
                "Repeated hotkey while busy produces one clip and capture continues");
            tray!.ClickForDiagnostics(0);
            Check(IsVisible, "Tray Open restores a live recording window");
            await UntilAsync(() => SaveReplayButton.IsEnabled);
            tray.ClickForDiagnostics(2);
            Check(saving, "Tray Save starts a live replay export");
            await UntilAsync(() => !saving);
            Check(Directory.EnumerateFiles(directory, "*.mp4").Count() == 2, "Tray Save completes a second playable export");
            await UntilAsync(() => SaveReplayButton.IsEnabled);
            tray.ClickForDiagnostics(2);
            await UntilAsync(() => current.Statistics.Buffer.SnapshotBytes > 0);
            WindowState = WindowState.Minimized;
            tray.ClickForDiagnostics(5);
            await UntilAsync(() => closed);
            Check(!saving && session is null && !tray.Visible && hotkey!.Key is null,
                "Tray Exit during export waits for cleanup and releases desktop resources");
            tray.Update(true, true, true, true);
            tray.Notify("Late completion", "Ignored after exit");
            tray.Dispose();
            Check(!tray.Visible, "Late tray callbacks and repeated disposal are harmless after exit");
            Check(Directory.EnumerateFiles(directory, "*.mp4").Count() == 3 &&
                !Directory.EnumerateFiles(directory, "*.partial.mp4").Any(), "Exit preserves the in-flight clip and leaves no partial file");
            File.WriteAllText(Path.Combine(directory, "workflow.json"), JsonSerializer.Serialize(new
                { Passed = true, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "workflow-error.txt"), exception + "\nPassed: " + string.Join("; ", checks));
            if (session is not null) await StopBufferingAsync();
            if (!closed) Close();
            Application.Current.Shutdown(1);
        }
        finally { source.Close(); }
    }
}
