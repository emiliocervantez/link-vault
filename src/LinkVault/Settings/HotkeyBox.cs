using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using LinkVault.Core;
using LinkVault.Services;
using static LinkVault.Native.NativeMethods;

namespace LinkVault.Config;

/// <summary>Read-only text box that records the key combination pressed while it has focus.</summary>
internal sealed class HotkeyBox : TextBox
{
    private Hotkey? _value;

    // The shell takes Win+key chords before the focused window sees them, so while the box has focus a low-level
    // hook catches them first. The delegate is kept in a field so the GC cannot collect it while Windows holds it.
    private readonly HookProc _keyboardProc;
    private IntPtr _keyboardHook;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        _keyboardProc = KeyboardCallback;
        GotKeyboardFocus += (_, _) => InstallHook();
        LostKeyboardFocus += (_, _) => UninstallHook();
        Unloaded += (_, _) => UninstallHook();
        Render();
    }

    public event Action? ValueChanged;

    public Hotkey? Value
    {
        get => _value;
        set
        {
            _value = value is { IsEmpty: true } ? null : value;
            Render();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            Set(null);
            return;
        }
        if (key is Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            e.Handled = false;   // keep focus navigation working
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None)
            return;

        var mods = HotkeyModifiers.None;
        var current = Keyboard.Modifiers;
        if (current.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (current.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (current.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (WinHeld()) mods |= HotkeyModifiers.Win;

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (mods == HotkeyModifiers.None && !KeyNames.IsFunctionKey(vk)) return;   // a bare letter is not a hotkey

        Set(new Hotkey(mods, vk));
    }

    /// <summary>WPF's Keyboard.Modifiers never includes ModifierKeys.Windows, so the Win keys are read directly.</summary>
    internal static bool WinHeld() => Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

    private void InstallHook()
    {
        if (_keyboardHook != IntPtr.Zero) return;
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
    }

    private void UninstallHook()
    {
        if (_keyboardHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
    }

    private static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Swallows Win+key chords (down and up) and records them; every other key goes the normal way.</summary>
    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        var vk = (int)Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam).vkCode;
        if (PopupInputHooks.IsModifier(vk) || !(Held(VK_LWIN) || Held(VK_RWIN)))
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        if (wParam.ToInt32() is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            var mods = HotkeyModifiers.Win;
            if (Held(VK_CONTROL)) mods |= HotkeyModifiers.Control;
            if (Held(VK_MENU)) mods |= HotkeyModifiers.Alt;
            if (Held(VK_SHIFT)) mods |= HotkeyModifiers.Shift;
            // Handle asynchronously: the hook must return within Windows' low-level hook timeout.
            Dispatcher.BeginInvoke(() =>
            {
                Set(new Hotkey(mods, vk));
                InputSender.MaskHeldModifiers();   // the shell never saw the key, so releasing Win would open Start
            });
        }
        return new IntPtr(1);
    }

    private void Set(Hotkey? value)
    {
        Value = value;
        ValueChanged?.Invoke();
    }

    private void Render() => Text = _value?.ToString() ?? "(none - click and press keys)";
}
