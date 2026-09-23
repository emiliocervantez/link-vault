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
    /// <summary>A Divider: a line between Links of a Group, not a Link. Its other properties are unused.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDivider { get; set; }
    /// <summary>
    /// Set when this entry is a Subgroup placed among the Group's Links, not a Link. Its other properties are unused.
    /// Only top-level Groups hold Subgroups.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Group? Subgroup { get; set; }

    [JsonIgnore]
    public bool IsSubgroup => Subgroup is not null;

    /// <summary>A real Link: neither a Divider nor a Subgroup entry.</summary>
    [JsonIgnore]
    public bool IsLink => !IsDivider && !IsSubgroup;

    [JsonIgnore]
    public string Label =>
        IsDivider ? "────────────" : Subgroup is { } sub ? sub.Name + "  ▸" : string.IsNullOrWhiteSpace(Name) ? Url : Name;

    public static Link Divider() => new() { IsDivider = true };

    public static Link ForSubgroup(Group subgroup) => new() { Subgroup = subgroup };

    public Link Clone() => new()
    {
        IsDivider = IsDivider,
        Subgroup = Subgroup?.Clone(),
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
    /// <summary>Only for Inline Groups: list the Links without the header row showing the Group's name.</summary>
    public bool HideName { get; set; }
    public List<Link> Links { get; set; } = new();
    /// <summary>Virtual-key code of a single key that moves to this Group while the Popup is open. Shares the Popup Key space with Links.</summary>
    public int? PopupKey { get; set; }

    /// <summary>True when the Group has at least one real Link, directly or in a Subgroup (Dividers do not count).</summary>
    [JsonIgnore]
    public bool HasLinks => AllLinks.Any();

    /// <summary>Real Links of this Group and of its Subgroups, in order.</summary>
    [JsonIgnore]
    public IEnumerable<Link> AllLinks => Links.SelectMany(l => l.Subgroup?.AllLinks ?? (l.IsLink ? new[] { l } : Enumerable.Empty<Link>()));

    [JsonIgnore]
    public IEnumerable<Group> Subgroups => Links.Where(l => l.IsSubgroup).Select(l => l.Subgroup!);

    public Group Clone() => new()
    {
        Name = Name,
        ShowInline = ShowInline,
        HideName = HideName,
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
    public IEnumerable<Link> AllLinks => Groups.SelectMany(g => g.AllLinks);

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

        string? CheckGroup(Group g, bool isSubgroup)
        {
            if (string.IsNullOrWhiteSpace(g.Name))
                return isSubgroup ? "Every subgroup needs a name." : "Every group needs a name.";
            if (CheckPopupKey(g.PopupKey, $"Group \"{g.Name}\"") is { } groupProblem)
                return groupProblem;
            foreach (var sub in g.Subgroups)
            {
                if (isSubgroup)
                    return $"Subgroup \"{g.Name}\" contains subgroup \"{sub.Name}\"; subgroups cannot be nested further.";
                if (CheckGroup(sub, isSubgroup: true) is { } subProblem)
                    return subProblem;
            }
            foreach (var l in g.Links.Where(l => l.IsLink))
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
            return null;
        }

        foreach (var g in Groups)
        {
            if (CheckGroup(g, isSubgroup: false) is { } problem)
                return problem;
        }
        return null;
    }
}
