using System.Runtime.InteropServices;
using System.Windows.Threading;
using LinkVault.Core;
using LinkVault.Native;
using static LinkVault.Native.NativeMethods;

namespace LinkVault.Services;

/// <summary>
/// Registers global hotkeys on the message window and dispatches their actions. Win combinations that Windows keeps
/// for itself (Win+C, Win+E, ...) cannot be registered; those are caught with a low-level keyboard hook instead,
/// which is installed only while there is at least one such hotkey.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    // Delegate kept in a field so the GC cannot collect it while Windows holds the pointer.
    private readonly HookProc _keyboardProc;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Hotkey, Action> _hooked = new();
    private readonly HashSet<int> _swallowed = new();   // keys whose key-down was swallowed, so their key-up is too
    private IntPtr _keyboardHook;

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.HotkeyPressed += OnHotkey;
        _keyboardProc = KeyboardCallback;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    private void OnHotkey(int id)
    {
        Trace.Log($"hotkey {id} pressed, foreground {Trace.Foreground()}");
        if (_actions.TryGetValue(id, out var action)) action();
    }

    /// <summary>Returns false when Windows refuses the combination (typically because another app owns it).</summary>
    public bool TryRegister(Hotkey hotkey, Action action)
    {
        if (hotkey.IsEmpty) return false;
        var id = _nextId++;
        if (NativeMethods.RegisterHotKey(_window.Handle, id, (uint)hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)hotkey.VirtualKey))
        {
            _actions[id] = action;
            return true;
        }
        if (!hotkey.Modifiers.HasFlag(HotkeyModifiers.Win) || _hooked.ContainsKey(hotkey)) return false;

        if (_keyboardHook == IntPtr.Zero)
        {
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
            if (_keyboardHook == IntPtr.Zero) return false;
        }
        _hooked[hotkey] = action;
        Trace.Log($"hotkey {hotkey} is reserved by Windows; caught with the keyboard hook");
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) NativeMethods.UnregisterHotKey(_window.Handle, id);
        _actions.Clear();
        _hooked.Clear();
        _swallowed.Clear();
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
    }

    private static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        var vk = (int)Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam).vkCode;

        if (wParam.ToInt32() is not (WM_KEYDOWN or WM_SYSKEYDOWN))
            return _swallowed.Remove(vk) ? new IntPtr(1) : CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        if (_swallowed.Contains(vk)) return new IntPtr(1);   // auto-repeat of a hotkey that already fired

        var mods = HotkeyModifiers.None;
        if (Held(VK_CONTROL)) mods |= HotkeyModifiers.Control;
        if (Held(VK_MENU)) mods |= HotkeyModifiers.Alt;
        if (Held(VK_SHIFT)) mods |= HotkeyModifiers.Shift;
        if (Held(VK_LWIN) || Held(VK_RWIN)) mods |= HotkeyModifiers.Win;
        if (!_hooked.TryGetValue(new Hotkey(mods, vk), out var action))
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        _swallowed.Add(vk);
        // Handle asynchronously: the hook must return within Windows' low-level hook timeout.
        _dispatcher.BeginInvoke(() =>
        {
            Trace.Log($"hooked hotkey {new Hotkey(mods, vk)} pressed, foreground {Trace.Foreground()}");
            InputSender.MaskHeldModifiers();   // Windows never saw the key, so releasing Win would open Start
            action();
        });
        return new IntPtr(1);
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.HotkeyPressed -= OnHotkey;
    }
}
