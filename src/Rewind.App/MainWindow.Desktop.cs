using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Rewind.App.Desktop;

namespace Rewind.App;

public partial class MainWindow
{
    private ReplayHotkey? hotkey;
    private ReplayTray? tray;
    private int SelectedHotkey => int.Parse((string)((ComboBoxItem)HotkeyPicker.SelectedItem).Tag,
        System.Globalization.CultureInfo.InvariantCulture);

    private void InitializeDesktop()
    {
        hotkey = new ReplayHotkey(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle));
        hotkey.Pressed += async () => await SaveReplayAsync();
        ApplyHotkey();
        try
        {
            tray = new ReplayTray();
            tray.OpenRequested += RestoreFromTray;
            tray.ToggleRequested += () =>
            {
                if (session is null) RestoreFromTray();
                ToggleBuffering(this, new RoutedEventArgs());
            };
            tray.SaveRequested += async () => await SaveReplayAsync();
            tray.FolderRequested += OpenClipsFolder;
            tray.ExitRequested += Close;
            RefreshTray();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            PreferenceStatusLabel.Text = "Tray controls are unavailable. Keep the window open to control recording.";
        }
    }

    private void HotkeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        ApplyHotkey();
        PreferencesChanged(sender, e);
    }

    private void ApplyHotkey()
    {
        if (hotkey is null) return;
        HotkeyStatusLabel.Text = hotkey.TrySet(SelectedHotkey, out var error)
            ? "Works while minimized. Minimize to the system tray; close to exit."
            : $"Shortcut unavailable: {error}. Choose another. " +
              (hotkey.Key is int previous ? $"Ctrl + Shift + F{previous - 0x6F} remains active." : "Use Save replay in the window.");
    }

    private void RefreshTray() => tray?.Update(session is not null, CanCaptureSelection &&
        startupCancellation is null && !stopping && !closeAfterRecording,
        SaveReplayButton.IsEnabled && !stopping && !saving, saving);

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OpenClipsFolder()
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            Process.Start(new ProcessStartInfo(outputDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            RestoreFromTray();
            PreferenceStatusLabel.Text = $"Could not open clips folder: {exception.Message}";
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && tray is { Visible: true }) Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        tray?.Dispose();
        hotkey?.Dispose();
        base.OnClosed(e);
    }
}
