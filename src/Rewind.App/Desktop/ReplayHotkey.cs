using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Rewind.App.Desktop;

internal sealed class ReplayHotkey : IDisposable
{
    private const int FirstId = 0x5231;
    private readonly HwndSource source;
    private int? registeredId;
    public int? Key { get; private set; }
    public event Action? Pressed;

    public ReplayHotkey(HwndSource source)
    {
        this.source = source;
        source.AddHook(ProcessMessage);
    }

    public bool TrySet(int key, out string? error)
    {
        if (Key == key) { error = null; return true; }
        // Acquire the replacement first so a conflict leaves the previous shortcut usable.
        var nextId = registeredId == FirstId ? FirstId + 1 : FirstId;
        if (!RegisterHotKey(source.Handle, nextId, 0x4006, (uint)key))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }
        if (registeredId is int previous) UnregisterHotKey(source.Handle, previous);
        registeredId = nextId;
        Key = key;
        error = null;
        return true;
    }

    private nint ProcessMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && registeredId is int id && wParam == id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return 0;
    }

    public void Dispose()
    {
        if (registeredId is int id) UnregisterHotKey(source.Handle, id);
        registeredId = null;
        Key = null;
        source.RemoveHook(ProcessMessage);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
