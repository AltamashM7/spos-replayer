namespace Rewind.Media;

/// <summary>Composes the DXGI color, masked-color or monochrome hardware pointer into a BGRA frame.</summary>
internal static class DesktopPointer
{
    public static void Blend(VideoFrame frame, ReadOnlySpan<byte> shape, int width, int height, int pitch, int type, int x, int y)
    {
        var pointerHeight = type == 1 ? height / 2 : height;
        if (width <= 0 || pointerHeight <= 0 || pitch <= 0 || (long)pitch * height > shape.Length) return;
        if ((type == 1 && pitch < (width + 7) / 8) || (type != 1 && pitch < (long)width * 4)) return;
        for (var row = Math.Max(0, -y); row < Math.Min(pointerHeight, frame.Height - y); row++)
            for (var col = Math.Max(0, -x); col < Math.Min(width, frame.Width - x); col++)
            {
                var destination = ((row + y) * frame.Width + col + x) * 4;
                if (type == 1)
                {
                    var mask = 0x80 >> (col % 8);
                    var and = (shape[row * pitch + col / 8] & mask) == 0 ? 0 : 255;
                    var xor = (shape[(row + pointerHeight) * pitch + col / 8] & mask) == 0 ? 0 : 255;
                    for (var channel = 0; channel < 3; channel++)
                        frame.Data[destination + channel] = (byte)((frame.Data[destination + channel] & and) ^ xor);
                }
                else if (type is 2 or 4)
                {
                    var source = row * pitch + col * 4;
                    var alpha = shape[source + 3];
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var value = shape[source + channel];
                        frame.Data[destination + channel] = type == 4
                            ? alpha == 0 ? value : (byte)(frame.Data[destination + channel] ^ value)
                            : (byte)((value * alpha + frame.Data[destination + channel] * (255 - alpha) + 127) / 255);
                    }
                }
            }
    }
}
