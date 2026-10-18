using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Rewind.App;

public partial class MainWindow
{
    internal async Task RunLayoutProofAsync(string directory)
    {
        var checks = new List<string>();
        try
        {
            var area = SystemParameters.WorkArea;
            foreach (var compact in new[] { false, true })
            {
                Height = compact ? Math.Min(540, area.Height) : Math.Min(760, area.Height);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                UpdateLayout();
                if (Left < area.Left || Top < area.Top || Left + ActualWidth > area.Right + 1 || Top + ActualHeight > area.Bottom + 1)
                    throw new InvalidOperationException("Window exceeds the desktop work area.");
                checks.Add($"{ActualHeight:F0}-pixel-tall window fits the desktop work area");
                LayoutViewport.ScrollToBottom();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                UpdateLayout();
                var button = SavePreferencesButton.TranslatePoint(new Point(), LayoutViewport);
                if (button.Y < 0 || button.Y + SavePreferencesButton.ActualHeight > LayoutViewport.ActualHeight + 1)
                    throw new InvalidOperationException("Footer controls cannot be reached in the viewport.");
                checks.Add($"Footer controls are reachable at height {ActualHeight:F0}");
                if (compact && LayoutViewport.ScrollableHeight <= 0)
                    throw new InvalidOperationException("Compact layout did not exercise vertical scrolling.");
                if (compact) checks.Add("Compact layout scrolls rather than clipping controls");
                else
                {
                    if (LayoutViewport.ScrollableHeight > 1) throw new InvalidOperationException("Default layout requires unnecessary scrolling.");
                    checks.Add("Default layout fits without scrolling");
                }
                var content = (FrameworkElement)Content;
                var dpi = VisualTreeHelper.GetDpi(content);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
                    (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, compact ? "layout-compact.png" : "layout-default.png"));
                encoder.Save(file);
            }
            File.WriteAllText(Path.Combine(directory, "layout.json"), JsonSerializer.Serialize(new
                { Passed = true, WorkArea = new { area.Width, area.Height }, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "layout-error.txt"), exception.ToString());
            Close();
            Application.Current.Shutdown(1);
        }
    }
}
