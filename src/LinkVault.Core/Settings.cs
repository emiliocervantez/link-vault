using System.Text.Json.Serialization;

namespace LinkVault.Core;

public sealed class Link
{
    /// <summary>Optional. Shown in the Popup instead of the URL when set.</summary>
    public string Name { get; set; } = "";
    /// <summary>A Url Template: may contain Parameters.</summary>
    public string Url { get; set; } = "";
    public Hotkey? Hotkey { get; set; }
    /// <summary>Virtual-key code of a single key (no modifiers) that Opens the Link while the Popup is open.</summary>
    public int? PopupKey { get; set; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Name) ? Url : Name;

    public Link Clone() => new()
    {
        Name = Name,
        Url = Url,
        Hotkey = Hotkey is null ? null : new Hotkey(Hotkey.Modifiers, Hotkey.VirtualKey),
        PopupKey = PopupKey,
    };

    /// <summary>Keys the Popup uses for navigation (Tab, Enter, Esc, Space, PageUp/Down, End, Home, arrows), plus modifiers; never a Popup Key.</summary>
    public static bool IsReservedPopupKey(int vk) =>
        vk is 0x09 or 0x0D or 0x1B or (>= 0x20 and <= 0x28)
        or 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);
}

public sealed class Group
{
    public string Name { get; set; } = "";
    /// <summary>True: the Group's Links are listed in the Popup itself. False: the Popup shows a submenu entry.</summary>
    public bool ShowInline { get; set; } = true;
    public List<Link> Links { get; set; } = new();
    /// <summary>Virtual-key code of a single key that moves to this Group while the Popup is open. Shares the Popup Key space with Links.</summary>
    public int? PopupKey { get; set; }

    public Group Clone() => new()
    {
        Name = Name,
        ShowInline = ShowInline,
        PopupKey = PopupKey,
        Links = Links.Select(l => l.Clone()).ToList(),
    };
}

public sealed class Settings
{
    private const int VkL = 0x4C;

    public bool StartWithWindows { get; set; }
    public Hotkey PopupHotkey { get; set; } = DefaultPopupHotkey();
    public List<Group> Groups { get; set; } = new();

    public static Hotkey DefaultPopupHotkey() => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkL);

    [JsonIgnore]
    public IEnumerable<Link> AllLinks => Groups.SelectMany(g => g.Links);

    public Settings Clone() => new()
    {
        StartWithWindows = StartWithWindows,
        PopupHotkey = new Hotkey(PopupHotkey.Modifiers, PopupHotkey.VirtualKey),
        Groups = Groups.Select(g => g.Clone()).ToList(),
    };

    /// <summary>Returns a human-readable problem, or null when the settings are consistent.</summary>
    public string? Validate()
    {
        if (PopupHotkey.IsEmpty)
            return "The popup hotkey must be set.";

        var seen = new HashSet<Hotkey> { PopupHotkey };
        var seenKeys = new HashSet<int>();   // Popup Keys of Links and Groups together
        string? CheckPopupKey(int? key, string owner)
        {
            if (key is not { } vk) return null;
            if (Link.IsReservedPopupKey(vk))
                return $"{owner}: {KeyNames.Name(vk)} is used for navigation in the popup and cannot be a popup key.";
            return seenKeys.Add(vk) ? null : $"Popup key {KeyNames.Name(vk)} is assigned more than once.";
        }

        foreach (var g in Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Name))
                return "Every group needs a name.";
            if (CheckPopupKey(g.PopupKey, $"Group \"{g.Name}\"") is { } groupProblem)
                return groupProblem;
            foreach (var l in g.Links)
            {
                if (string.IsNullOrWhiteSpace(l.Url))
                    return $"Link \"{l.Label}\" in group \"{g.Name}\" has no URL.";
                if (UrlTemplate.TryParse(l.Url, out var error) is null)
                    return $"Link \"{l.Label}\" in group \"{g.Name}\": {error}";
                if (l.Hotkey is { IsEmpty: false } hk && !seen.Add(hk))
                    return $"Hotkey {hk} is assigned more than once.";
                if (CheckPopupKey(l.PopupKey, $"Link \"{l.Label}\"") is { } linkProblem)
                    return linkProblem;
            }
        }
        return null;
    }
}
