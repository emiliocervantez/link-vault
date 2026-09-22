using System.Runtime.InteropServices;
using System.Windows.Threading;
using LinkVault.Native;
using static LinkVault.Native.NativeMethods;

namespace LinkVault.Services;

/// <summary>
/// Input for a popup that never takes focus. While installed:
/// keyboard: every non-modifier key is delivered to <see cref="KeyDown"/> and swallowed, so the focused app never sees it;
/// mouse: a button press where <paramref name="isInside"/> is false raises <see cref="ClickedOutside"/>;
/// foreground: any foreground-window change raises <see cref="ForegroundChanged"/>.
/// Install and uninstall on the UI thread; the low-level hooks run on that thread's message loop.
/// </summary>
internal sealed class PopupInputHooks : IDisposable
{
    // Delegates are kept in fields so the GC cannot collect them while Windows holds the pointers.
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private readonly WinEventProc _foregroundProc;
    private readonly Dispatcher _dispatcher;

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private IntPtr _foregroundHook;
    private Func<POINT, bool>? _isInside;

    public PopupInputHooks(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
        _foregroundProc = ForegroundCallback;
    }

    /// <summary>Virtual-key code of a pressed non-modifier key. Return value ignored; the key is always swallowed.</summary>
    public event Action<int>? KeyDown;
    /// <summary>A window-switching chord (Alt+Tab, Alt+Esc, Win+Tab) was pressed; it was passed through, not swallowed.</summary>
    public event Action? SwitchChord;
    public event Action? ClickedOutside;
    public event Action? ForegroundChanged;

    public bool Installed => _keyboardHook != IntPtr.Zero;

    public void Install(Func<POINT, bool> isInside)
    {
        if (Installed) return;
        _isInside = isInside;
        // Low-level hooks are not injected into other processes, so the exe's own module handle is fine
        // (and Marshal.GetHINSTANCE does not work in a single-file publish).
        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Uninstall()
    {
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        _keyboardHook = _mouseHook = _foregroundHook = IntPtr.Zero;
    }

    private static bool IsModifier(int vk) => vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN
        or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;   // L/R Shift, Ctrl, Alt

    private static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Alt+Tab, Alt+Shift+Tab, Alt+Esc, Win+Tab: Windows must see these, so they are never swallowed.</summary>
    private static bool IsSwitchChord(int vk) =>
        vk is VK_TAB or VK_ESCAPE && (Held(VK_MENU) || Held(VK_LWIN) || Held(VK_RWIN));

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        var vk = (int)Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam).vkCode;
        if (IsModifier(vk)) return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        var msg = wParam.ToInt32();
        if (IsSwitchChord(vk))
        {
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN) _dispatcher.BeginInvoke(() => SwitchChord?.Invoke());
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }
        if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            // Handle asynchronously: the hook must return within Windows' low-level hook timeout.
            _dispatcher.BeginInvoke(() => KeyDown?.Invoke(vk));
        }
        return new IntPtr(1);   // swallow key-down and key-up alike so the focused app sees neither
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN && _isInside is not null)
        {
            var pt = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).pt;
            if (!_isInside(pt))
                _dispatcher.BeginInvoke(() => ClickedOutside?.Invoke());
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void ForegroundCallback(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        _dispatcher.BeginInvoke(() => ForegroundChanged?.Invoke());
    }

    public void Dispose() => Uninstall();
}
