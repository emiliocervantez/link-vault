using System.Text.Json.Serialization;

namespace LinkVault.Core;

public sealed class Link
{
    /// <summary>Optional. Shown in the Popup instead of the URL when set.</summary>
    public string Name { get; set; } = "";
    /// <summary>A Url Template: may contain Parameters.</summary>
    public string Url { get; set; } = "";
    public Hotkey? Hotkey { get; set; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Name) ? Url : Name;

    public Link Clone() => new()
    {
        Name = Name,
        Url = Url,
        Hotkey = Hotkey is null ? null : new Hotkey(Hotkey.Modifiers, Hotkey.VirtualKey),
    };
}

public sealed class Group
{
    public string Name { get; set; } = "";
    /// <summary>True: the Group's Links are listed in the Popup itself. False: the Popup shows a submenu entry.</summary>
    public bool ShowInline { get; set; } = true;
    public List<Link> Links { get; set; } = new();

    public Group Clone() => new()
    {
        Name = Name,
        ShowInline = ShowInline,
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
        foreach (var g in Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Name))
                return "Every group needs a name.";
            foreach (var l in g.Links)
            {
                if (string.IsNullOrWhiteSpace(l.Url))
                    return $"Link \"{l.Label}\" in group \"{g.Name}\" has no URL.";
                if (UrlTemplate.TryParse(l.Url, out var error) is null)
                    return $"Link \"{l.Label}\" in group \"{g.Name}\": {error}";
                if (l.Hotkey is { IsEmpty: false } hk && !seen.Add(hk))
                    return $"Hotkey {hk} is assigned more than once.";
            }
        }
        return null;
    }
}
