using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rewind.App.Desktop;

namespace Rewind.App;

public partial class MainWindow
{
    // Opt-in desktop integration fixture: no media, user settings or user keystrokes.
    internal async Task RunDesktopProofAsync(string directory)
    {
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        try
        {
            var settingsPath = Path.Combine(directory, "preferences.json");
            var legacy = Path.Combine(directory, "legacy.json");
            File.WriteAllText(legacy, JsonSerializer.Serialize(new
                { ReplaySeconds = 15, IncludeSystemAudio = false, OutputDirectory = directory }));
            var (oldPreferences, warning) = new PreferencesStore(legacy).Load();
            Check(warning is null && oldPreferences.SaveHotkey == 0x77 && oldPreferences.ReplaySeconds == 15,
                "Legacy settings retain values and receive the default shortcut");
            Check(!(oldPreferences with { SaveHotkey = 0x7B }).IsValid, "Unsupported shortcuts rejected");

            Check(hotkey?.Key == 0x77 && tray is { Visible: true }, "Default shortcut and tray initialized");
            using var otherSource = new HwndSource(new HwndSourceParameters("Rewind desktop proof") { Width = 1, Height = 1 });
            using var other = new ReplayHotkey(otherSource);
            Check(!other.TrySet(0x77, out _), "OS rejects a conflicting global registration");
            Check(other.TrySet(0x78, out _), "Competing shortcut reserved for conflict check");
            HotkeyPicker.SelectedIndex = 1;
            Check(hotkey!.Key == 0x77 && HotkeyStatusLabel.Text.Contains("remains active", StringComparison.Ordinal),
                "Failed shortcut replacement preserves the old registration and explains the conflict");
            SavePreferences(this, new RoutedEventArgs());
            Check(!File.Exists(settingsPath), "Unavailable selection cannot be saved as active");
            HotkeyPicker.SelectedIndex = 2;
            Check(hotkey.Key == 0x79 && other.TrySet(0x77, out _), "Successful replacement releases the previous shortcut");
            SavePreferences(this, new RoutedEventArgs());
            var (saved, savedWarning) = preferencesStore.Load();
            Check(savedWarning is null && saved.SaveHotkey == 0x79, "Selected shortcut persists and reloads");
            var pressed = 0;
            hotkey.Pressed += () => pressed++;
            var handle = new WindowInteropHelper(this).Handle;
            SendMessage(handle, 0x0312, 0x5231, 0);
            Check(pressed == 0, "Stale shortcut messages are ignored");
            SendMessage(handle, 0x0312, 0x5232, 0);
            Check(pressed == 1 && session is null && !saving, "Active shortcut routes through the guarded save command");

            var content = (FrameworkElement)Content;
            var dpi = VisualTreeHelper.GetDpi(content);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(directory, "desktop.png"))) encoder.Save(file);
            Check(SavePreferencesButton.TranslatePoint(new Point(), content).Y + SavePreferencesButton.ActualHeight <= content.ActualHeight,
                "Desktop controls fit inside the rendered window");
            WindowState = WindowState.Minimized;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!IsVisible && tray!.Visible, "Minimize hides the window and retains the tray");
            tray!.ClickForDiagnostics(0);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(IsVisible && WindowState == WindowState.Normal, "Tray Open restores the window");

            using (var commands = new ReplayTray())
            {
                var toggleCount = 0;
                var saveCount = 0;
                commands.ToggleRequested += () => toggleCount++;
                commands.SaveRequested += () => saveCount++;
                commands.Update(false, true, false, false);
                commands.ClickForDiagnostics(1);
                commands.ClickForDiagnostics(2);
                Check(toggleCount == 1 && saveCount == 0, "Idle tray enables start and blocks unavailable save");
                commands.Update(true, true, true, false);
                commands.ClickForDiagnostics(1);
                commands.ClickForDiagnostics(2);
                Check(toggleCount == 2 && saveCount == 1, "Recording tray routes stop and save commands");
                commands.Update(true, false, false, true);
                commands.ClickForDiagnostics(1);
                commands.ClickForDiagnostics(2);
                Check(toggleCount == 2 && saveCount == 1, "Busy tray blocks disabled commands");
            }
            WindowState = WindowState.Minimized;
            tray.ClickForDiagnostics(5);
            Check(!tray.Visible && hotkey.Key is null, "Tray Exit disposes the icon and global shortcut");
            Check(other.TrySet(0x79, out _), "Shortcut is available to other windows after exit");
            File.WriteAllText(Path.Combine(directory, "desktop.json"), JsonSerializer.Serialize(new
                { Passed = true, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "desktop-error.txt"), exception + "\nPassed: " + string.Join("; ", checks));
            Close();
            Application.Current.Shutdown(1);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}
