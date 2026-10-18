using Rewind.Media;
using Xunit;

namespace Rewind.Tests;

public sealed class DesktopPointerTests
{
    [Fact]
    public void ColorPointerBlendsAlphaAndClipsAtMonitorEdge()
    {
        using var frame = Frame(2, 1, 100);
        DesktopPointer.Blend(frame, [200, 200, 200, 128, 255, 255, 255, 255], 2, 1, 8, 2, 1, 0);
        Assert.Equal(100, frame.Data[0]);
        Assert.Equal(150, frame.Data[4]);
    }

    [Fact]
    public void MaskedPointerReplacesOrXorsAccordingToItsMask()
    {
        using var frame = Frame(2, 1, 100);
        DesktopPointer.Blend(frame, [20, 30, 40, 0, 255, 255, 255, 255], 2, 1, 8, 4, 0, 0);
        Assert.Equal(new byte[] { 20, 30, 40 }, frame.Data[..3]);
        Assert.Equal(155, frame.Data[4]);
    }

    [Fact]
    public void MonochromePointerAppliesAndXorPlanes()
    {
        using var frame = Frame(4, 1, 100);
        DesktopPointer.Blend(frame, [0x30, 0x50], 4, 2, 1, 1, 0, 0);
        Assert.Equal(new byte[] { 0, 255, 100, 155 }, Enumerable.Range(0, 4).Select(x => frame.Data[x * 4]).ToArray());
    }

    [Fact]
    public void InvalidShapeIsIgnoredAndNegativePositionIsClipped()
    {
        using var frame = Frame(1, 1, 100);
        DesktopPointer.Blend(frame, [255], 5, 1, 20, 2, 0, 0);
        Assert.Equal(100, frame.Data[0]);
        DesktopPointer.Blend(frame, [0, 0, 0, 255, 200, 200, 200, 255], 2, 1, 8, 2, -1, 0);
        Assert.Equal(200, frame.Data[0]);
    }

    private static VideoFrame Frame(int width, int height, byte value)
    {
        var frame = new VideoFrame(width, height, 0);
        frame.Data.AsSpan(0, width * height * 4).Fill(value);
        return frame;
    }
}
