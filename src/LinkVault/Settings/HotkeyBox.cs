using System.Windows.Controls;
using System.Windows.Input;
using LinkVault.Core;

namespace LinkVault.Config;

/// <summary>Read-only text box that records the key combination pressed while it has focus.</summary>
internal sealed class HotkeyBox : TextBox
{
    private Hotkey? _value;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
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

    private void Set(Hotkey? value)
    {
        Value = value;
        ValueChanged?.Invoke();
    }

    private void Render() => Text = _value?.ToString() ?? "(none - click and press keys)";
}
