using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Rewind.Media;

namespace Rewind.App.Diagnostics;

internal static class PackageProof
{
    // Deployment check: exercise the packaged native libraries, capture and export in one short sample.
    public static async Task RunAsync(Window window, string directory)
    {
        var code = 0;
        Directory.CreateDirectory(directory);
        try
        {
            ReplaySaveReport report;
            await using (var session = await ReplaySession.StartAsync(
                CaptureInterop.ForWindow(new WindowInteropHelper(window).Handle), TimeSpan.FromSeconds(30), true))
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                if (session.Completion.IsCompleted) await session.Completion;
                report = await session.SaveReplayAsync(Path.Combine(directory, "package-replay.mp4"));
            }
            using var process = Process.GetCurrentProcess();
            var native = process.Modules.Cast<ProcessModule>()
                .Where(module => module.ModuleName is "avcodec-63.dll" or "avformat-63.dll" or "avutil-61.dll" or "swscale-10.dll" or "swresample-7.dll")
                .Select(module => new { module.ModuleName, module.FileName }).ToArray();
            File.WriteAllText(Path.Combine(directory, "package-proof.json"), JsonSerializer.Serialize(new
            {
                Passed = true, ApplicationDirectory = AppContext.BaseDirectory,
                RuntimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(),
                CoreLibrary = typeof(object).Assembly.Location,
                Runtime = Environment.Version.ToString(), NativeVersion = NativeMedia.Initialize(),
                NativeModules = native, Replay = report
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "package-error.txt"), exception.ToString());
            code = 1;
        }
        Application.Current.Shutdown(code);
    }
}
