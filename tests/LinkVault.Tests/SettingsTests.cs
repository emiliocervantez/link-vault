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
    public void Links_without_hotkey_or_name_are_fine()
    {
        Assert.Null(WithLinks(new Link { Url = "https://a" }, new Link { Url = "mailto:x@y.z" }).Validate());
    }

    [Fact]
    public void Json_round_trip()
    {
        var s = WithLinks(new Link { Name = "n", Url = "https://x/{q=1}", Hotkey = new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x41) });
        s.Groups[0].ShowInline = false;
        s.StartWithWindows = true;
        var back = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s))!;
        Assert.True(back.StartWithWindows);
        Assert.False(back.Groups[0].ShowInline);
        Assert.Equal("n", back.Groups[0].Links[0].Name);
        Assert.Equal("https://x/{q=1}", back.Groups[0].Links[0].Url);
        Assert.Equal("Shift+Win+A", back.Groups[0].Links[0].Hotkey!.ToString());
        Assert.DoesNotContain("Label", JsonSerializer.Serialize(s));
        Assert.DoesNotContain("AllLinks", JsonSerializer.Serialize(s));
    }

    [Fact]
    public void Clone_is_deep()
    {
        var s = WithLinks(new Link { Name = "t", Url = "https://x", Hotkey = new Hotkey(HotkeyModifiers.Alt, 0x41) });
        var c = s.Clone();
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
