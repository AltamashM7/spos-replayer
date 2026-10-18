using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using Rewind.Core;
using Rewind.Media;
using Windows.Graphics.Capture;

namespace Rewind.App;

public partial class MainWindow : Window
{
    private readonly PreferencesStore preferencesStore;
    private bool initialized;
    private string outputDirectory = UserPreferences.DefaultOutputDirectory;
    private ReplaySession? session;
    private CancellationTokenSource? startupCancellation;
    private bool stopping;
    private bool saving;
    private bool closeAfterRecording;

    public bool CaptureSupported { get; }

    public MainWindow() : this(new PreferencesStore()) { }

    internal MainWindow(PreferencesStore preferencesStore)
    {
        this.preferencesStore = preferencesStore;
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        var (preferences, warning) = preferencesStore.Load();
        DurationPicker.SelectedIndex = preferences.ReplaySeconds switch { 15 => 0, 60 => 2, _ => 1 };
        AudioToggle.IsChecked = preferences.IncludeSystemAudio;
        try
        {
            var monitors = MonitorCaptureTarget.GetAvailable();
            MonitorPicker.ItemsSource = monitors;
            MonitorPicker.SelectedItem = monitors.FirstOrDefault(x => x.DeviceName == preferences.MonitorDeviceName)
                ?? monitors.FirstOrDefault();
        }
        catch (Exception exception) when (exception is SharpGen.Runtime.SharpGenException or IOException)
        {
            warning ??= "Monitor capture is unavailable. Window capture can still be used.";
        }
        CaptureModePicker.SelectedIndex = preferences.BorderlessMonitorCapture ? 1 : 0;
        MonitorPicker.Visibility = preferences.BorderlessMonitorCapture ? Visibility.Visible : Visibility.Collapsed;
        HotkeyPicker.SelectedIndex = preferences.SaveHotkey - 0x77;
        outputDirectory = preferences.OutputDirectory;
        OutputFolderLabel.Text = outputDirectory;
        ProfileLabel.Text = $"Up to {ReplayDefaults.MaximumHeight}p · {ReplayDefaults.FramesPerSecond} FPS · MP4 · 128 MiB compressed buffer limit";
        try
        {
            CaptureSupported = GraphicsCaptureSession.IsSupported();
            CaptureStatusLabel.Text = CaptureSupported
                ? "Windows screen capture is supported on this PC."
                : "Windows screen capture is unavailable on this PC.";
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or TypeLoadException)
        {
            CaptureStatusLabel.Text = "Windows screen capture could not be checked.";
        }
        PreferenceStatusLabel.Text = warning ?? "Choose a source and start buffering. Save replay keeps footage from before you press Save.";
        RecordButton.IsEnabled = CanCaptureSelection;
        initialized = true;
        SourceInitialized += (_, _) => InitializeDesktop();
    }

    private int SelectedSeconds => int.Parse((string)((ComboBoxItem)DurationPicker.SelectedItem).Tag,
        System.Globalization.CultureInfo.InvariantCulture);

    private bool MonitorMode => CaptureModePicker.SelectedIndex == 1;
    private bool CanCaptureSelection => MonitorMode ? MonitorPicker.SelectedItem is MonitorCaptureTarget : CaptureSupported;

    private void CaptureModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        MonitorPicker.Visibility = MonitorMode ? Visibility.Visible : Visibility.Collapsed;
        RecordButton.IsEnabled = CanCaptureSelection;
        RefreshTray();
        PreferencesChanged(sender, e);
    }

    private void PreferencesChanged(object sender, RoutedEventArgs e)
    {
        if (initialized) PreferenceStatusLabel.Text = "You have unsaved preferences.";
    }

    private void ChooseFolder(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose where Rewind saves clips" };
        if (Directory.Exists(outputDirectory)) picker.InitialDirectory = outputDirectory;
        if (picker.ShowDialog(this) != true) return;
        outputDirectory = picker.FolderName;
        OutputFolderLabel.Text = outputDirectory;
        PreferenceStatusLabel.Text = "You have unsaved preferences.";
    }

    private void SavePreferences(object sender, RoutedEventArgs e)
    {
        try
        {
            if (hotkey?.Key != SelectedHotkey)
            {
                PreferenceStatusLabel.Text = "Choose an available shortcut before saving preferences.";
                return;
            }
            preferencesStore.Save(new UserPreferences(SelectedSeconds, AudioToggle.IsChecked == true, outputDirectory)
                {
                    SaveHotkey = SelectedHotkey, BorderlessMonitorCapture = MonitorMode,
                    MonitorDeviceName = (MonitorPicker.SelectedItem as MonitorCaptureTarget)?.DeviceName
                });
            PreferenceStatusLabel.Text = "Preferences saved on this PC.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PreferenceStatusLabel.Text = "Preferences could not be saved. Check folder permissions and free space.";
        }
    }

    private async void ToggleBuffering(object sender, RoutedEventArgs e)
    {
        if (stopping || closeAfterRecording) return;
        if (session is not null) { await StopBufferingAsync(); return; }
        if (startupCancellation is not null) return;
        RecordButton.IsEnabled = false;
        startupCancellation = new CancellationTokenSource();
        RefreshTray();
        try
        {
            NativeMedia.Initialize();
            SetPreferencesEnabled(false);
            RecordingStateLabel.Text = "Starting…";
            var progress = new Progress<ReplayStatistics>(UpdateStatistics);
            string sourceName;
            if (MonitorMode)
            {
                var monitor = MonitorPicker.SelectedItem as MonitorCaptureTarget
                    ?? throw new InvalidOperationException("Select an available monitor first.");
                sourceName = monitor.ToString();
                session = await ReplaySession.StartMonitorAsync(monitor, TimeSpan.FromSeconds(SelectedSeconds),
                    AudioToggle.IsChecked == true, progress, startupCancellation.Token);
            }
            else
            {
                var picker = new GraphicsCapturePicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, new WindowInteropHelper(this).Handle);
                var item = await picker.PickSingleItemAsync();
                if (item is null || !IsLoaded) return;
                startupCancellation.Token.ThrowIfCancellationRequested();
                sourceName = item.DisplayName;
                session = await ReplaySession.StartAsync(item, TimeSpan.FromSeconds(SelectedSeconds),
                    AudioToggle.IsChecked == true, progress, startupCancellation.Token);
            }
            RecordButton.Content = "Stop buffering";
            RecordingStateLabel.Text = "Buffering";
            CaptureStatusLabel.Text = $"Capturing: {sourceName}";
            PreferenceStatusLabel.Text = "Save replay exports recent footage while buffering continues.";
            _ = ObserveSessionAsync(session);
        }
        catch (OperationCanceledException) { PreferenceStatusLabel.Text = "Buffering cancelled."; }
        catch (Exception exception) { PreferenceStatusLabel.Text = $"Could not start buffering: {exception.GetBaseException().Message}"; }
        finally
        {
            startupCancellation.Dispose(); startupCancellation = null;
            RecordButton.IsEnabled = CanCaptureSelection;
            RefreshTray();
            if (session is null) { SetPreferencesEnabled(true); RecordingStateLabel.Text = "Not recording"; }
            if (closeAfterRecording)
            {
                if (session is not null) await StopBufferingAsync();
                else Close();
            }
        }
    }

    private void UpdateStatistics(ReplayStatistics statistics)
    {
        if (session is null || stopping) return;
        BufferStatusLabel.Text = $"{statistics.Buffer.AvailableSeconds:F1} seconds available · {SelectedSeconds}-second replay selected";
        ResourceStatusLabel.Text = $"Compressed buffer: {statistics.Buffer.RetainedBytes / 1048576d:F1} MiB · " +
            $"Export references: {statistics.Buffer.SnapshotBytes / 1048576d:F1} MiB · " +
            $"Queued frames dropped: {statistics.DroppedFrames}";
        SaveReplayButton.IsEnabled = !saving && statistics.Buffer.AvailableSeconds >= 0.1;
        RefreshTray();
    }

    private async void SaveReplay(object sender, RoutedEventArgs e) => await SaveReplayAsync();

    private async Task SaveReplayAsync()
    {
        var current = session;
        if (current is null || saving || stopping || !SaveReplayButton.IsEnabled)
        {
            if (!IsVisible) tray?.Notify("Replay unavailable", saving ? "A replay is already being saved." : "Start buffering and wait for footage before saving.");
            return;
        }
        saving = true;
        SaveReplayButton.IsEnabled = false;
        SaveReplayButton.Content = "Saving…";
        RefreshTray();
        try
        {
            var file = Path.Combine(outputDirectory, $"Rewind-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.mp4");
            var report = await current.SaveReplayAsync(file);
            PreferenceStatusLabel.Text = $"Saved {report.DurationSeconds:F1} seconds to {report.File}";
            if (!IsVisible) tray?.Notify("Replay saved", $"Saved {report.DurationSeconds:F1} seconds to {report.File}");
        }
        catch (OperationCanceledException) { PreferenceStatusLabel.Text = "Replay save cancelled before export started."; }
        catch (Exception exception)
        {
            PreferenceStatusLabel.Text = $"Could not save replay: {exception.GetBaseException().Message}";
            if (!IsVisible) tray?.Notify("Replay save failed", exception.GetBaseException().Message, error: true);
        }
        finally
        {
            saving = false;
            SaveReplayButton.Content = "Save replay";
            SaveReplayButton.IsEnabled = session is not null && !stopping;
            RefreshTray();
        }
    }

    private async Task ObserveSessionAsync(ReplaySession current)
    {
        try { await current.Completion; }
        catch (Exception exception)
        {
            if (!ReferenceEquals(session, current) || stopping) return;
            await StopBufferingAsync();
            PreferenceStatusLabel.Text = $"Buffering stopped: {exception.GetBaseException().Message}";
            if (!IsVisible) tray?.Notify("Buffering stopped", exception.GetBaseException().Message, error: true);
        }
    }

    private async Task StopBufferingAsync()
    {
        if (session is null || stopping) return;
        stopping = true;
        RecordButton.IsEnabled = false;
        SaveReplayButton.IsEnabled = false;
        RecordingStateLabel.Text = "Stopping…";
        RefreshTray();
        try { await session.DisposeAsync(); }
        catch (Exception exception) { PreferenceStatusLabel.Text = $"Buffering stopped: {exception.GetBaseException().Message}"; }
        finally
        {
            session = null; stopping = false;
            RecordButton.Content = "Start buffering";
            RecordButton.IsEnabled = CanCaptureSelection;
            RecordingStateLabel.Text = "Not recording";
            BufferStatusLabel.Text = "Start buffering to keep recent footage.";
            ResourceStatusLabel.Text = "";
            SetPreferencesEnabled(true);
            RefreshTray();
            if (closeAfterRecording && startupCancellation is null) _ = Dispatcher.BeginInvoke(Close);
        }
    }

    private void SetPreferencesEnabled(bool enabled)
    {
        CaptureModePicker.IsEnabled = enabled;
        MonitorPicker.IsEnabled = enabled;
        DurationPicker.IsEnabled = enabled;
        AudioToggle.IsEnabled = enabled;
        ChooseFolderButton.IsEnabled = enabled;
        SavePreferencesButton.IsEnabled = enabled;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (session is not null || startupCancellation is not null || stopping)
        {
            e.Cancel = true;
            closeAfterRecording = true;
            startupCancellation?.Cancel();
            if (session is not null && !stopping) _ = StopBufferingAsync();
        }
        base.OnClosing(e);
    }
}
