using Vortice.DXGI;

namespace Rewind.Media;

public sealed record MonitorCaptureTarget(uint AdapterIndex, uint OutputIndex, string DeviceName,
    int Left, int Top, int Width, int Height)
{
    public static IReadOnlyList<MonitorCaptureTarget> GetAvailable()
    {
        var monitors = new List<MonitorCaptureTarget>();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out var adapter).Success; adapterIndex++)
        {
            using (adapter)
                for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var output).Success; outputIndex++)
                {
                    using (output)
                    {
                        var description = output.Description;
                        if (!description.AttachedToDesktop) continue;
                        var bounds = description.DesktopCoordinates;
                        monitors.Add(new MonitorCaptureTarget(adapterIndex, outputIndex, description.DeviceName,
                            bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
                    }
                }
        }
        return monitors;
    }

    public override string ToString() => $"{DeviceName.Replace(@"\\.\", "", StringComparison.Ordinal)} · {Width} × {Height}";
}
