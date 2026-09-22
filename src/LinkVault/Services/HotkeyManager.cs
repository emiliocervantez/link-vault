using LinkVault.Core;
using LinkVault.Native;

namespace LinkVault.Services;

/// <summary>Registers global hotkeys on the message window and dispatches their actions.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.HotkeyPressed += OnHotkey;
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
        if (!NativeMethods.RegisterHotKey(_window.Handle, id, (uint)hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)hotkey.VirtualKey))
            return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) NativeMethods.UnregisterHotKey(_window.Handle, id);
        _actions.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.HotkeyPressed -= OnHotkey;
    }
}
