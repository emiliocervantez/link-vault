using LinkVault.Core;
using Xunit;

namespace LinkVault.Tests;

public class UrlTemplateTests
{
    private static UrlTemplate Parse(string url)
    {
        var t = UrlTemplate.TryParse(url, out var error);
        Assert.Null(error);
        return t!;
    }

    private static string Error(string url)
    {
        Assert.Null(UrlTemplate.TryParse(url, out var error));
        return error!;
    }

    [Fact]
    public void Url_without_parameters_is_unchanged()
    {
        var t = Parse("https://example.com/a?b=c&d=e#f");
        Assert.Empty(t.Parameters);
        Assert.Equal("https://example.com/a?b=c&d=e#f", t.Expand());
    }

    [Fact]
    public void Parameters_are_found_in_order_with_defaults()
    {
        var t = Parse("https://youtu.be/{id}?t={seconds=30}");
        Assert.Equal(new[] { new UrlParameter("id", null), new UrlParameter("seconds", "30") }, t.Parameters);
    }

    [Fact]
    public void Names_are_trimmed_and_may_contain_spaces()
    {
        var t = Parse("https://x/{ search term }");
        Assert.Equal("search term", t.Parameters.Single().Name);
    }

    [Fact]
    public void Expand_encodes_values()
    {
        var t = Parse("https://www.google.com/search?q={query}");
        var url = t.Expand(new Dictionary<string, string> { ["query"] = "a b&c=d/é" });
        Assert.Equal("https://www.google.com/search?q=a%20b%26c%3Dd%2F%C3%A9", url);
    }

    [Fact]
    public void Expand_falls_back_to_default_then_empty()
    {
        var t = Parse("https://x/{a=1}/{b}");
        Assert.Equal("https://x/1/", t.Expand());
        Assert.Equal("https://x/2/3", t.Expand(new Dictionary<string, string> { ["a"] = "2", ["b"] = "3" }));
    }

    [Fact]
    public void Same_name_is_one_parameter_filled_everywhere()
    {
        var t = Parse("https://x/{id}/y/{id}");
        Assert.Single(t.Parameters);
        Assert.Equal("https://x/7/y/7", t.Expand(new Dictionary<string, string> { ["id"] = "7" }));
    }

    [Fact]
    public void Default_on_any_occurrence_applies()
    {
        var t = Parse("https://x/{id}/{id=5}");
        Assert.Equal("5", t.Parameters.Single().Default);
        Assert.Equal("5", Parse("https://x/{id=5}/{id=5}").Parameters.Single().Default);
    }

    [Fact]
    public void Default_may_be_empty_or_contain_equals()
    {
        Assert.Equal("", Parse("https://x/{a=}").Parameters.Single().Default);
        Assert.Equal("b=c", Parse("https://x/{a=b=c}").Parameters.Single().Default);
    }

    [Fact]
    public void Double_braces_are_literal()
    {
        var t = Parse("https://x/{{json}}/{{{a}}}");
        Assert.Single(t.Parameters);
        Assert.Equal("https://x/{json}/{1}", t.Expand(new Dictionary<string, string> { ["a"] = "1" }));
    }

    [Fact]
    public void Malformed_templates_are_rejected()
    {
        Assert.Contains("Unmatched '{'", Error("https://x/{id"));
        Assert.Contains("Unmatched '{'", Error("https://x/{a{b}"));
        Assert.Contains("Unmatched '}'", Error("https://x/id}"));
        Assert.Contains("no name", Error("https://x/{}"));
        Assert.Contains("no name", Error("https://x/{ =3}"));
        Assert.Contains("two different defaults", Error("https://x/{id=1}/{id=2}"));
    }
}
