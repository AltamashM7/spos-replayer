using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rewind.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--audio-quality-proof", var audioDirectory])
        {
            var audio = new Diagnostics.AnimatedSourceWindow(true);
            MainWindow = audio;
            var audioStarted = false;
            audio.ContentRendered += async (_, _) =>
            {
                if (audioStarted) return;
                audioStarted = true;
                await Diagnostics.AudioQualityProof.RunAsync(audio, audioDirectory);
            };
            audio.Show();
            return;
        }
        if (e.Args is ["--package-proof", var packageDirectory])
        {
            var package = new Diagnostics.AnimatedSourceWindow(false);
            MainWindow = package;
            var packageStarted = false;
            package.ContentRendered += async (_, _) =>
            {
                if (packageStarted) return;
                packageStarted = true;
                await Diagnostics.PackageProof.RunAsync(package, packageDirectory);
            };
            package.Show();
            return;
        }
        if (e.Args is ["--reliability-proof", var reliabilityDirectory, var duration])
        {
            if (!int.TryParse(duration, out var seconds) || (seconds != 0 && seconds < 30) || seconds > 3600)
                throw new ArgumentException("Reliability duration must be 0 (failure checks) or 30–3600 seconds.");
            var reliability = new Diagnostics.ReliabilityProofWindow(reliabilityDirectory, seconds);
            MainWindow = reliability;
            reliability.Show();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] is "--desktop-proof" or "--workflow-proof" or "--layout-proof" or "--monitor-proof")
        {
            var desktopDirectory = e.Args[1];
            Directory.CreateDirectory(desktopDirectory);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var desktop = new MainWindow(new PreferencesStore(Path.Combine(desktopDirectory, "preferences.json")));
            MainWindow = desktop;
            var started = false;
            desktop.ContentRendered += async (_, _) =>
            {
                if (started) return;
                started = true;
                if (e.Args[0] == "--workflow-proof") await desktop.RunWorkflowProofAsync(desktopDirectory);
                else if (e.Args[0] == "--layout-proof") await desktop.RunLayoutProofAsync(desktopDirectory);
                else if (e.Args[0] == "--monitor-proof") await desktop.RunMonitorProofAsync(desktopDirectory);
                else await desktop.RunDesktopProofAsync(desktopDirectory);
            };
            desktop.Show();
            return;
        }
        if (e.Args is ["--replay-proof", var replayDirectory, ..])
        {
            var proof = new Diagnostics.ReplayProofWindow(replayDirectory, !e.Args.Contains("--video-only"),
                e.Args.Contains("--default-duration") ? 30 : 3);
            MainWindow = proof;
            proof.Show();
            return;
        }
        if (e.Args is ["--media-proof", var proofDirectory, ..])
        {
            var proof = new Diagnostics.MediaProofWindow(proofDirectory,
                !e.Args.Contains("--video-only"), e.Args.Contains("--cancel"), e.Args.Contains("--silence"));
            MainWindow = proof;
            proof.Show();
            return;
        }
        var window = new MainWindow();
        MainWindow = window;

        // A local, explicit diagnostic mode verifies XAML loading and the WPF render path.
        if (e.Args is ["--smoke-test", var outputDirectory])
        {
            window.ContentRendered += (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(outputDirectory);
                    var content = (FrameworkElement)window.Content;
                    var dpi = VisualTreeHelper.GetDpi(content);
                    var bitmap = new RenderTargetBitmap(
                        (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
                        (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY),
                        dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var image = File.Create(Path.Combine(outputDirectory, "startup.png")))
                    {
                        encoder.Save(image);
                    }
                    File.WriteAllText(Path.Combine(outputDirectory, "startup.json"),
                        JsonSerializer.Serialize(new
                        {
                            WindowRendered = true,
                            window.Title,
                            CaptureSupported = window.CaptureSupported,
                            OperatingSystem = Environment.OSVersion.VersionString,
                            Runtime = Environment.Version.ToString()
                        }, new JsonSerializerOptions { WriteIndented = true }));
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(outputDirectory, "startup-error.txt"), exception.ToString());
                    Shutdown(1);
                }
            };
        }

        window.Show();
    }
}
