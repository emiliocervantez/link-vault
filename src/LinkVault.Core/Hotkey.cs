using System.Text.Json.Serialization;

namespace LinkVault.Core;

/// <summary>Modifier flags. Values match the Win32 MOD_* constants used by RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A global key combination: modifiers plus a Win32 virtual-key code.</summary>
public sealed class Hotkey : IEquatable<Hotkey>
{
    public Hotkey() { }

    public Hotkey(HotkeyModifiers modifiers, int virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HotkeyModifiers Modifiers { get; set; }

    public int VirtualKey { get; set; }

    [JsonIgnore]
    public bool IsEmpty => VirtualKey == 0;

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyNames.Name(VirtualKey));
        return string.Join("+", parts);
    }

    public bool Equals(Hotkey? other) =>
        other is not null && other.Modifiers == Modifiers && other.VirtualKey == VirtualKey;

    public override bool Equals(object? obj) => Equals(obj as Hotkey);

    public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);
}

public static class KeyNames
{
    private static readonly Dictionary<int, string> Special = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "CapsLock",
        [0x1B] = "Esc", [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End",
        [0x24] = "Home", [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete",
        [0x6A] = "Num*", [0x6B] = "Num+", [0x6D] = "Num-", [0x6E] = "Num.", [0x6F] = "Num/",
        [0x90] = "NumLock", [0x91] = "ScrollLock",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/",
        [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
    };

    public static string Name(int vk)
    {
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();          // 0-9
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();          // A-Z
        if (vk is >= 0x60 and <= 0x69) return "Num" + (vk - 0x60);            // numpad 0-9
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);          // F1-F24
        return Special.TryGetValue(vk, out var name) ? name : $"Key{vk:X2}";
    }

    public static bool IsFunctionKey(int vk) => vk is >= 0x70 and <= 0x87;
}
