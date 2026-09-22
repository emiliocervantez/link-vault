using System.Windows.Interop;
using LinkVault.Native;

namespace LinkVault.Services;

/// <summary>Hidden top-level window that receives hotkey messages.</summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public MessageWindow()
    {
        var p = new HwndSourceParameters("LinkVaultMessageWindow")
        {
            WindowStyle = 0,   // WS_OVERLAPPED, never shown
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(Hook);
    }

    public IntPtr Handle => _source.Handle;

    public event Action<int>? HotkeyPressed;

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
