using System.Diagnostics;
using Rewind.Core;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Rewind.Media;

/// <summary>One worker owns DXGI duplication and D3D context access. No Windows capture session/border is created.</summary>
internal sealed class DesktopCaptureSource : IFrameSource
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly IDXGIOutputDuplication duplication;
    private ID3D11Texture2D? staging;
    private Task? worker;
    private int stopped;
    private int disposed;
    private Exception? error;
    private long captured;
    public BoundedOwnedQueue<VideoFrame> Frames { get; } = new(12);
    public TaskCompletionSource FirstFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Exception? Error => Volatile.Read(ref error);
    public long Captured => Interlocked.Read(ref captured);
    public int Width { get; }
    public int Height { get; }

    public DesktopCaptureSource(MonitorCaptureTarget target)
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            factory.EnumAdapters1(target.AdapterIndex, out var adapter).CheckError();
            using (adapter)
            {
                adapter.EnumOutputs(target.OutputIndex, out var output).CheckError();
                using (output)
                {
                    var description = output.Description;
                    if (!description.AttachedToDesktop || description.DeviceName != target.DeviceName)
                        throw new IOException("The selected monitor changed. Restart Rewind and select it again.");
                    if (description.Rotation is not ModeRotation.Identity and not ModeRotation.Unspecified)
                        throw new NotSupportedException("Border-free capture currently requires a landscape monitor without display rotation.");
                    Width = description.DesktopCoordinates.Right - description.DesktopCoordinates.Left;
                    Height = description.DesktopCoordinates.Bottom - description.DesktopCoordinates.Top;
                    if (Width <= 0 || Height <= 0 || (long)Width * Height > 4096L * 2160)
                        throw new NotSupportedException("This recording profile supports monitors up to approximately 4K.");
                    D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                        [FeatureLevel.Level_11_0], out device, out context).CheckError();
                    using var output1 = output.QueryInterface<IDXGIOutput1>();
                    duplication = output1.DuplicateOutput(device);
                }
            }
        }
        catch { duplication?.Dispose(); context?.Dispose(); device?.Dispose(); Frames.Dispose(); throw; }
    }

    public void Start()
    {
        if (worker is not null) throw new InvalidOperationException("Monitor capture is already running.");
        worker = Task.Run(Capture);
    }

    private unsafe void Capture()
    {
        var pointer = Array.Empty<byte>();
        OutduplPointerShapeInfo pointerInfo = default;
        var pointerVisible = false;
        var pointerX = 0;
        var pointerY = 0;
        long lastFrame = 0;
        try
        {
            while (Volatile.Read(ref stopped) == 0)
            {
                var result = duplication.AcquireNextFrame(100, out var info, out var resource);
                if (result == Vortice.DXGI.ResultCode.WaitTimeout) continue;
                result.CheckError();
                try
                {
                    using (resource)
                    {
                        if (info.LastMouseUpdateTime != 0)
                        {
                            pointerVisible = info.PointerPosition.Visible;
                            pointerX = info.PointerPosition.Position.X;
                            pointerY = info.PointerPosition.Position.Y;
                        }
                        if (info.PointerShapeBufferSize > 0)
                        {
                            if (info.PointerShapeBufferSize > 4 * 1024 * 1024) throw new IOException("The monitor pointer shape exceeded its capture budget.");
                            if (pointer.Length < info.PointerShapeBufferSize) pointer = new byte[info.PointerShapeBufferSize];
                            fixed (byte* shape = pointer)
                                duplication.GetFramePointerShape((uint)pointer.Length, (nint)shape, out _, out pointerInfo).CheckError();
                        }
                        // A pointer-only first notification does not initialize the desktop surface.
                        if (staging is null && info.LastPresentTime == 0) continue;
                        var timestamp = MediaClock.NowTicks;
                        if (timestamp - lastFrame < TimeSpan.TicksPerSecond * 3 / (ReplayDefaults.FramesPerSecond * 4)) continue;
                        using var texture = resource.QueryInterface<ID3D11Texture2D>();
                        var description = texture.Description;
                        if (description.Width != Width || description.Height != Height)
                            throw new IOException("The monitor size changed. Start buffering again after the display change.");
                        if (staging is null)
                        {
                            description.Usage = ResourceUsage.Staging;
                            description.BindFlags = BindFlags.None;
                            description.CPUAccessFlags = CpuAccessFlags.Read;
                            description.MiscFlags = ResourceOptionFlags.None;
                            staging = device.CreateTexture2D(description);
                        }
                        context.CopyResource(staging, texture);
                        context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
                        VideoFrame? frame = null;
                        try
                        {
                            // DXGI timestamps are QPC counts; convert into the audio/video shared 100 ns clock.
                            var updateTime = Math.Max(info.LastPresentTime, info.LastMouseUpdateTime);
                            if (updateTime > 0)
                                timestamp = (long)(updateTime * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency));
                            frame = new VideoFrame(Width, Height, timestamp);
                            fixed (byte* destination = frame.Data)
                                for (var row = 0; row < Height; row++)
                                    Buffer.MemoryCopy((byte*)mapped.DataPointer + row * mapped.RowPitch,
                                        destination + row * Width * 4, Width * 4L, Width * 4L);
                            if (pointerVisible && pointer.Length > 0)
                                DesktopPointer.Blend(frame, pointer, (int)pointerInfo.Width, (int)pointerInfo.Height,
                                    (int)pointerInfo.Pitch, (int)pointerInfo.Type, pointerX, pointerY);
                            Frames.Enqueue(frame);
                            frame = null;
                            lastFrame = MediaClock.NowTicks;
                            Interlocked.Increment(ref captured);
                            FirstFrame.TrySetResult();
                        }
                        finally { frame?.Dispose(); context.Unmap(staging, 0); }
                    }
                }
                finally { duplication.ReleaseFrame().CheckError(); }
            }
        }
        catch (Exception exception)
        {
            var failure = new IOException("Monitor capture stopped after a display or desktop change. Start buffering again.", exception);
            Volatile.Write(ref error, failure);
            FirstFrame.TrySetException(failure);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref stopped, 1);
        worker?.GetAwaiter().GetResult();
        Exception? failure = null;
        void Release(Action action) { try { action(); } catch (Exception exception) { failure ??= exception; } }
        Release(Frames.Dispose); Release(() => staging?.Dispose()); Release(duplication.Dispose);
        Release(context.Dispose); Release(device.Dispose);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
