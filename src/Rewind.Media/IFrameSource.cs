using Rewind.Core;

namespace Rewind.Media;

internal interface IFrameSource : IDisposable
{
    BoundedOwnedQueue<VideoFrame> Frames { get; }
    TaskCompletionSource FirstFrame { get; }
    Exception? Error { get; }
    long Captured { get; }
    int Width { get; }
    int Height { get; }
    void Start();
}
