using System.Text.Json;
using LinkVault.Core;
using Xunit;

namespace LinkVault.Tests;

public class SettingsTests
{
    private static Settings WithLinks(params Link[] links)
    {
        var s = new Settings();
        s.Groups.Add(new Group { Name = "g", Links = links.ToList() });
        return s;
    }

    [Fact]
    public void Defaults()
    {
        var s = new Settings();
        Assert.Equal("Ctrl+Alt+L", s.PopupHotkey.ToString());
        Assert.False(s.StartWithWindows);
        Assert.Empty(s.Groups);
        Assert.True(new Group().ShowInline);
        Assert.False(new Group().HideName);
        Assert.Null(s.Validate());
    }

    [Fact]
    public void Label_is_name_or_url()
    {
        Assert.Equal("Docs", new Link { Name = "Docs", Url = "https://x" }.Label);
        Assert.Equal("https://x", new Link { Name = " ", Url = "https://x" }.Label);
    }

    [Fact]
    public void Validate_rejects_duplicate_hotkeys()
    {
        var hk = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x41);
        var s = WithLinks(new Link { Url = "https://a", Hotkey = hk }, new Link { Url = "https://b", Hotkey = new Hotkey(hk.Modifiers, hk.VirtualKey) });
        Assert.Contains("more than once", s.Validate());
        Assert.Contains("more than once", WithLinks(new Link { Url = "https://a", Hotkey = Settings.DefaultPopupHotkey() }).Validate());
    }

    [Fact]
    public void Validate_rejects_bad_links_and_groups()
    {
        Assert.Contains("no URL", WithLinks(new Link { Name = "n" }).Validate());
        Assert.Contains("Unmatched", WithLinks(new Link { Url = "https://x/{a" }).Validate());
        var s = new Settings();
        s.Groups.Add(new Group());
        Assert.Contains("group needs a name", s.Validate());
        Assert.Contains("hotkey", new Settings { PopupHotkey = new Hotkey() }.Validate());
    }

    [Fact]
    public void Validate_checks_popup_keys()
    {
        Assert.Null(WithLinks(new Link { Url = "https://a", PopupKey = 0x59 }, new Link { Url = "https://b", PopupKey = 0x31 }).Validate());
        Assert.Contains("Popup key Y is assigned more than once", WithLinks(new Link { Url = "https://a", PopupKey = 0x59 }, new Link { Url = "https://b", PopupKey = 0x59 }).Validate());
        Assert.Contains("Enter is used for navigation", WithLinks(new Link { Url = "https://a", PopupKey = 0x0D }).Validate());
        Assert.Contains("Down is used for navigation", WithLinks(new Link { Url = "https://a", PopupKey = 0x28 }).Validate());

        var s = new Settings();
        s.Groups.Add(new Group { Name = "g1", Links = { new Link { Url = "https://a", PopupKey = 0x59 } } });
        s.Groups.Add(new Group { Name = "g2", ShowInline = false, Links = { new Link { Url = "https://b", PopupKey = 0x59 } } });
        Assert.Contains("more than once", s.Validate());   // unique across groups, collapsed or not
    }

    [Fact]
    public void Group_popup_keys_share_the_link_key_space()
    {
        var s = WithLinks(new Link { Url = "https://a", PopupKey = 0x59 });
        s.Groups[0].PopupKey = 0x44;
        Assert.Null(s.Validate());
        s.Groups[0].PopupKey = 0x59;
        Assert.Contains("Popup key Y is assigned more than once", s.Validate());
        s.Groups[0].PopupKey = 0x1B;
        Assert.Contains("Group \"g\": Esc is used for navigation", s.Validate());

        s.Groups[0].PopupKey = 0x44;
        var back = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s))!;
        Assert.Equal(0x44, back.Groups[0].PopupKey);
        Assert.Equal(0x44, s.Clone().Groups[0].PopupKey);
    }

    [Fact]
    public void Dividers_are_not_links()
    {
        var s = WithLinks(Link.Divider(), new Link { Url = "https://a" }, Link.Divider(), Link.Divider());
        Assert.Null(s.Validate());   // no URL needed
        Assert.Single(s.AllLinks);
        Assert.True(s.Groups[0].HasLinks);
        Assert.False(new Group { Name = "d", Links = { Link.Divider() } }.HasLinks);
        Assert.True(s.Clone().Groups[0].Links[0].IsDivider);

        var json = JsonSerializer.Serialize(s);
        Assert.Equal(3, json.Split("IsDivider").Length - 1);   // written only for Dividers
        var back = JsonSerializer.Deserialize<Settings>(json)!;
        Assert.Equal(new[] { true, false, true, true }, back.Groups[0].Links.Select(l => l.IsDivider));
    }

    [Fact]
    public void Subgroups_mix_with_links_and_count_everywhere()
    {
        var docs = new Group { Name = "Docs", PopupKey = 0x44, Links = { new Link { Url = "https://d", PopupKey = 0x57 }, Link.Divider() } };
        var s = WithLinks(new Link { Url = "https://a" }, Link.ForSubgroup(docs), new Link { Url = "https://b" });
        Assert.Null(s.Validate());
        Assert.Equal(new[] { "https://a", "https://d", "https://b" }, s.AllLinks.Select(l => l.Url));
        Assert.Equal("Docs  ▸", s.Groups[0].Links[1].Label);
        Assert.False(s.Groups[0].Links[1].IsLink);
        Assert.Single(s.Groups[0].Subgroups);

        // Only Subgroups with Links make the parent non-empty.
        Assert.False(new Group { Name = "p", Links = { Link.ForSubgroup(new Group { Name = "e" }) } }.HasLinks);
        Assert.True(new Group { Name = "p", Links = { Link.ForSubgroup(docs) } }.HasLinks);

        // Keys and names are checked inside Subgroups too.
        docs.PopupKey = 0x57;
        Assert.Contains("Popup key W is assigned more than once", s.Validate());
        docs.PopupKey = null;
        docs.Name = " ";
        Assert.Contains("subgroup needs a name", s.Validate());
        docs.Name = "Docs";
        docs.Links.Add(new Link { Name = "bad" });
        Assert.Contains("has no URL", s.Validate());
        docs.Links.RemoveAt(docs.Links.Count - 1);

        // One level only.
        docs.Links.Add(Link.ForSubgroup(new Group { Name = "deep", Links = { new Link { Url = "https://x" } } }));
        Assert.Contains("cannot be nested further", s.Validate());
        docs.Links.RemoveAt(docs.Links.Count - 1);

        var json = JsonSerializer.Serialize(s);
        Assert.Equal(1, json.Split("\"Subgroup\"").Length - 1);   // written only for the Subgroup entry
        var back = JsonSerializer.Deserialize<Settings>(json)!;
        Assert.Equal("Docs", back.Groups[0].Links[1].Subgroup!.Name);
        Assert.Equal("https://d", back.Groups[0].Links[1].Subgroup!.Links[0].Url);

        var clone = s.Clone();
        clone.Groups[0].Links[1].Subgroup!.Name = "changed";
        Assert.Equal("Docs", docs.Name);
    }

    [Fact]
    public void Links_without_hotkey_or_name_are_fine()
    {
        Assert.Null(WithLinks(new Link { Url = "https://a" }, new Link { Url = "mailto:x@y.z" }).Validate());
    }

    [Fact]
    public void Json_round_trip()
    {
        var s = WithLinks(new Link { Name = "n", Url = "https://x/{q=1}", Hotkey = new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x41), PopupKey = 0x59 });
        s.Groups[0].ShowInline = false;
        s.Groups[0].HideName = true;
        s.StartWithWindows = true;
        var back = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s))!;
        Assert.True(back.StartWithWindows);
        Assert.False(back.Groups[0].ShowInline);
        Assert.True(back.Groups[0].HideName);
        Assert.True(s.Clone().Groups[0].HideName);
        Assert.Equal("n", back.Groups[0].Links[0].Name);
        Assert.Equal("https://x/{q=1}", back.Groups[0].Links[0].Url);
        Assert.Equal("Shift+Win+A", back.Groups[0].Links[0].Hotkey!.ToString());
        Assert.Equal(0x59, back.Groups[0].Links[0].PopupKey);
        Assert.DoesNotContain("Label", JsonSerializer.Serialize(s));
        Assert.DoesNotContain("AllLinks", JsonSerializer.Serialize(s));
    }

    [Fact]
    public void Clone_is_deep()
    {
        var s = WithLinks(new Link { Name = "t", Url = "https://x", Hotkey = new Hotkey(HotkeyModifiers.Alt, 0x41), PopupKey = 0x59 });
        var c = s.Clone();
        Assert.Equal(0x59, c.Groups[0].Links[0].PopupKey);
        c.Groups[0].Name = "changed";
        c.Groups[0].Links[0].Name = "changed";
        c.Groups[0].Links[0].Hotkey!.VirtualKey = 1;
        c.PopupHotkey.VirtualKey = 1;
        Assert.Equal("g", s.Groups[0].Name);
        Assert.Equal("t", s.Groups[0].Links[0].Name);
        Assert.Equal(0x41, s.Groups[0].Links[0].Hotkey!.VirtualKey);
        Assert.Equal(0x4C, s.PopupHotkey.VirtualKey);
    }

    [Fact]
    public void Storage_round_trip_and_corrupt_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LinkVaultTests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new VaultStorage(dir);
            Assert.Empty(storage.LoadSettings().Groups);
            storage.SaveSettings(WithLinks(new Link { Url = "https://a" }));
            Assert.Equal("https://a", new VaultStorage(dir).LoadSettings().Groups[0].Links[0].Url);
            Assert.False(File.Exists(storage.CorruptBackupFile));
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ not json");
            Assert.Empty(storage.LoadSettings().Groups);
            Assert.Equal("{ not json", File.ReadAllText(storage.CorruptBackupFile));
            Assert.True(Directory.Exists(storage.IconsDir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
