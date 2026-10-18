using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Rewind.Media;

/// <summary>Small ABI bridge for the documented Win32/WinRT graphics interop interfaces.</summary>
public static unsafe class CaptureInterop
{
    public static GraphicsCaptureItem ForWindow(nint window)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", 44, out var name));
        nint factory = 0;
        nint item = 0;
        try
        {
            var factoryId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref factoryId, out factory));
            var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[3];
            Marshal.ThrowExceptionForHR(create(factory, window, &itemId, &item));
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            WindowsDeleteString(name);
        }
    }

    internal static IDirect3DDevice CreateWinRtDevice(nint dxgiDevice)
    {
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var pointer));
        try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    internal static nint GetTexture(IDirect3DSurface surface)
    {
        var surfacePointer = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        nint access = 0;
        try
        {
            var accessId = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfacePointer, in accessId, out access));
            var textureId = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
            nint texture = 0;
            var get = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)access)[3];
            Marshal.ThrowExceptionForHR(get(access, &textureId, &texture));
            return texture;
        }
        finally
        {
            if (access != 0) Marshal.Release(access);
            WinRT.MarshalInterface<IDirect3DSurface>.DisposeAbi(surfacePointer);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);
}
