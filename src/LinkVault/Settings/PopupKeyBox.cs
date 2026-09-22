using System.Windows.Controls;
using System.Windows.Input;
using LinkVault.Core;

namespace LinkVault.Config;

/// <summary>Read-only text box that records a single key pressed without modifiers (a Popup Key).</summary>
internal sealed class PopupKeyBox : TextBox
{
    private int? _value;

    public PopupKeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Render();
    }

    public event Action? ValueChanged;

    public int? Value
    {
        get => _value;
        set
        {
            _value = value;
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
        if (Keyboard.Modifiers != ModifierKeys.None) return;   // popup keys have no modifiers

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0 || Link.IsReservedPopupKey(vk)) return;
        Set(vk);
    }

    private void Set(int? value)
    {
        Value = value;
        ValueChanged?.Invoke();
    }

    private void Render() => Text = _value is { } vk ? KeyNames.Name(vk) : "(none - click and press a key)";
}
