using Rewind.Core;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Rewind.Media;

internal sealed class ScreenCaptureSource : IFrameSource
{
    private readonly object gate = new();
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly IDirect3DDevice winrtDevice;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private readonly GraphicsCaptureItem item;
    private ID3D11Texture2D? staging;
    private bool disposed;
    private long lastTimestamp;
    private Exception? error;
    private long captured;
    private long maximumDeliveryTicks;
    private int width;
    private int height;

    public BoundedOwnedQueue<VideoFrame> Frames { get; } = new(12);
    public TaskCompletionSource FirstFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Exception? Error => Volatile.Read(ref error);
    public long Captured => Interlocked.Read(ref captured);
    public int Width => Volatile.Read(ref width);
    public int Height => Volatile.Read(ref height);
    public double MaximumDeliveryMilliseconds => Interlocked.Read(ref maximumDeliveryTicks) / (double)TimeSpan.TicksPerMillisecond;

    public ScreenCaptureSource(GraphicsCaptureItem item)
    {
        this.item = item;
        try
        {
            var result = D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware,
                DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out device, out context);
            if (result.Failure)
            {
                context?.Dispose(); device?.Dispose();
                D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Warp,
                    DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out device, out context).CheckError();
            }
            using var dxgi = device.QueryInterface<IDXGIDevice>();
            winrtDevice = CaptureInterop.CreateWinRtDevice(dxgi.NativePointer);
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            session = pool.CreateCaptureSession(item);
            pool.FrameArrived += FrameArrived;
            item.Closed += SourceClosed;
        }
        catch
        {
            session?.Dispose(); pool?.Dispose(); winrtDevice?.Dispose();
            context?.Dispose(); device?.Dispose(); Frames.Dispose();
            throw;
        }
    }

    public void Start() => session.StartCapture();
    private void SourceClosed(GraphicsCaptureItem sender, object args)
    {
        var failure = new InvalidOperationException("The capture window was closed. Select a source and start buffering again.");
        Interlocked.CompareExchange(ref error, failure, null);
        FirstFrame.TrySetException(failure);
    }

    private unsafe void FrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (gate)
        {
            if (disposed || error is not null) return;
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null) return;
                if (frame.ContentSize.Width <= 0 || frame.ContentSize.Height <= 0) return;
                var timestamp = frame.SystemRelativeTime.Ticks;
                // Leave timing tolerance around the 30 FPS target instead of dropping a frame for sub-ms jitter.
                if (timestamp - lastTimestamp < TimeSpan.TicksPerSecond * 3 / (ReplayDefaults.FramesPerSecond * 4)) return;
                // On Windows 10 the initial item size can include non-client margins that frames omit.
                // Freeze the actual first-frame geometry, rather than assuming the item's initial bounds.
                if (width == 0)
                {
                    width = frame.ContentSize.Width;
                    height = frame.ContentSize.Height;
                    if ((long)width * height > 4096L * 2160)
                        throw new NotSupportedException("This recording profile supports capture sources up to approximately 4K.");
                }
                if (frame.ContentSize.Width != width || frame.ContentSize.Height != height)
                    throw new InvalidOperationException($"The capture size changed from {width}×{height} to {frame.ContentSize.Width}×{frame.ContentSize.Height}. Start a new recording for the resized source.");
                using var texture = new ID3D11Texture2D(CaptureInterop.GetTexture(frame.Surface));
                if (texture.Description.Width < width || texture.Description.Height < height)
                    throw new InvalidOperationException("The screen source grew during startup. Select it again after resizing finishes.");
                if (staging is null)
                {
                    var description = texture.Description;
                    description.Usage = ResourceUsage.Staging;
                    description.BindFlags = BindFlags.None;
                    description.CPUAccessFlags = CpuAccessFlags.Read;
                    description.MiscFlags = ResourceOptionFlags.None;
                    staging = device.CreateTexture2D(description);
                }
                context.CopyResource(staging, texture);
                context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
                VideoFrame? copy = null;
                try
                {
                    copy = new VideoFrame(frame.ContentSize.Width, frame.ContentSize.Height, timestamp);
                    fixed (byte* destination = copy.Data)
                        for (var row = 0; row < copy.Height; row++)
                            Buffer.MemoryCopy((byte*)mapped.DataPointer + row * mapped.RowPitch,
                                destination + row * copy.Width * 4, copy.Width * 4L, copy.Width * 4L);
                    Frames.Enqueue(copy);
                    copy = null;
                    lastTimestamp = timestamp;
                    Interlocked.Increment(ref captured);
                    maximumDeliveryTicks = Math.Max(maximumDeliveryTicks, MediaClock.NowTicks - timestamp);
                    FirstFrame.TrySetResult();
                }
                finally { copy?.Dispose(); context.Unmap(staging, 0); }
            }
            catch (Exception exception)
            {
                Volatile.Write(ref error, exception);
                FirstFrame.TrySetException(exception);
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        // Do not hold the callback lock while closing the frame pool.
        Exception? failure = null;
        void Release(Action action)
        {
            try { action(); } catch (Exception exception) { failure ??= exception; }
        }
        Release(() => pool.FrameArrived -= FrameArrived);
        Release(() => item.Closed -= SourceClosed);
        Release(session.Dispose);
        Release(pool.Dispose);
        Release(Frames.Dispose);
        Release(() => staging?.Dispose());
        Release(winrtDevice.Dispose);
        Release(context.Dispose);
        Release(device.Dispose);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
