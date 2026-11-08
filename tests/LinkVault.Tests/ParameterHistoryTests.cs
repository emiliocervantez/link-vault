using System.Text.Json;
using LinkVault.Core;
using Xunit;

namespace LinkVault.Tests;

public class ParameterHistoryTests
{
    private const string Url = "https://x/?q={q}&lang={lang=en}";

    private static Dictionary<string, string> Values(string q, string lang = "") => new() { ["q"] = q, ["lang"] = lang };

    [Fact]
    public void Remembers_most_recent_first_per_link_and_parameter()
    {
        var h = new ParameterHistory();
        Assert.Empty(h.Get(Url, "q"));
        h.Remember(Url, Values("a", "en"), 5);
        h.Remember(Url, Values("b", "de"), 5);
        Assert.Equal(new[] { "b", "a" }, h.Get(Url, "q"));
        Assert.Equal(new[] { "de", "en" }, h.Get(Url, "lang"));
        Assert.Empty(h.Get("https://other/{q}", "q"));
    }

    [Fact]
    public void Repeated_value_moves_to_the_front_and_empty_is_skipped()
    {
        var h = new ParameterHistory();
        h.Remember(Url, Values("a"), 5);
        h.Remember(Url, Values("b"), 5);
        h.Remember(Url, Values("a"), 5);
        h.Remember(Url, Values(""), 5);
        Assert.Equal(new[] { "a", "b" }, h.Get(Url, "q"));
        Assert.Empty(h.Get(Url, "lang"));
    }

    [Fact]
    public void Keeps_at_most_max_values()
    {
        var h = new ParameterHistory();
        foreach (var v in new[] { "1", "2", "3", "4" }) h.Remember(Url, Values(v), 3);
        Assert.Equal(new[] { "4", "3", "2" }, h.Get(Url, "q"));
        h.Remember(Url, Values("5"), 1);
        Assert.Equal(new[] { "5" }, h.Get(Url, "q"));
    }

    [Fact]
    public void Trim_drops_removed_links_and_cuts_lists()
    {
        var h = new ParameterHistory();
        foreach (var v in new[] { "1", "2", "3" }) h.Remember(Url, Values(v), 5);
        h.Remember("https://gone/{q}", Values("x"), 5);
        h.Trim(new[] { Url }, 2);
        Assert.Equal(new[] { "3", "2" }, h.Get(Url, "q"));
        Assert.Empty(h.Get("https://gone/{q}", "q"));
        Assert.Single(h.Values);
    }

    [Fact]
    public void Storage_round_trip_and_corrupt_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LinkVaultTests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new VaultStorage(dir);
            Assert.Empty(storage.LoadHistory().Values);
            var h = new ParameterHistory();
            h.Remember(Url, Values("a b", "en"), 5);
            storage.SaveHistory(h);
            Assert.Equal(new[] { "a b" }, new VaultStorage(dir).LoadHistory().Get(Url, "q"));
            File.WriteAllText(Path.Combine(dir, "history.json"), "{ not json");
            Assert.Empty(storage.LoadHistory().Values);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void History_size_setting_is_1_to_10()
    {
        Assert.Equal(10, new Settings().ParameterHistorySize);
        Assert.Equal(1, new Settings { ParameterHistorySize = 0 }.ParameterHistorySize);
        Assert.Equal(10, new Settings { ParameterHistorySize = 99 }.ParameterHistorySize);
        Assert.Equal(10, JsonSerializer.Deserialize<Settings>("{\"ParameterHistorySize\":50}")!.ParameterHistorySize);
        Assert.Equal(7, new Settings { ParameterHistorySize = 7 }.Clone().ParameterHistorySize);
        var back = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { ParameterHistorySize = 3 }))!;
        Assert.Equal(3, back.ParameterHistorySize);
    }
}
