using System.IO;
using System.Text.Json;
using Rewind.Core;

namespace Rewind.App;

internal sealed record UserPreferences(
    int ReplaySeconds,
    bool IncludeSystemAudio,
    string OutputDirectory)
{
    public int SaveHotkey { get; init; } = 0x77; // Ctrl+Shift+F8; old preference files retain this default.
    public bool BorderlessMonitorCapture { get; init; }
    public string? MonitorDeviceName { get; init; }
    public static string DefaultOutputDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Rewind");

    public static UserPreferences Default => new(
        ReplayDefaults.DurationSeconds, ReplayDefaults.CaptureSystemAudio, DefaultOutputDirectory);

    public bool IsValid => ReplaySeconds is 15 or 30 or 60
        && !string.IsNullOrWhiteSpace(OutputDirectory)
        && Path.IsPathFullyQualified(OutputDirectory)
        && SaveHotkey is >= 0x77 and <= 0x79;
}

internal sealed class PreferencesStore(string? filePath = null)
{
    private string PreferencesPath => filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rewind", "preferences.json");

    public (UserPreferences Preferences, string? Warning) Load()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return (UserPreferences.Default, null);
            var preferences = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(PreferencesPath));
            return preferences is { IsValid: true }
                ? (preferences, null)
                : (UserPreferences.Default, "Saved preferences are invalid. Defaults are in use.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return (UserPreferences.Default, "Saved preferences could not be read. Defaults are in use.");
        }
    }

    public void Save(UserPreferences preferences)
    {
        if (!preferences.IsValid) throw new ArgumentException("Invalid replay preferences.", nameof(preferences));
        var path = PreferencesPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
