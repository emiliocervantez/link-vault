using LinkVault.Core;
using Xunit;

namespace LinkVault.Tests;

public class LinkIconTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=x", "https://www.youtube.com/")]
    [InlineData("http://example.com:8080/a/{id}", "http://example.com:8080/")]
    [InlineData("https://www.google.com/search?q={query=dotnet}", "https://www.google.com/")]
    public void SiteRoot_of_web_links(string url, string root) => Assert.Equal(new Uri(root), LinkIcon.SiteRoot(url));

    [Theory]
    [InlineData("https://{tenant}.sharepoint.com/x")]
    [InlineData("{url}")]
    [InlineData("mailto:kahjud@if.ee")]
    [InlineData("ms-settings:display")]
    [InlineData(@"C:\Users\x\file.md")]
    [InlineData("https://x/{bad")]
    public void SiteRoot_is_null_without_fixed_web_host(string url) => Assert.Null(LinkIcon.SiteRoot(url));

    [Fact]
    public void CacheFileName_is_safe()
    {
        Assert.Equal("example.com_8080.png", LinkIcon.CacheFileName(new Uri("http://Example.com:8080/")));
        Assert.Equal("www.youtube.com.png", LinkIcon.CacheFileName(new Uri("https://www.youtube.com/")));
    }

    [Fact]
    public void LocalPath_for_files_only()
    {
        Assert.Equal(@"C:\Users\x\file.md", LinkIcon.LocalPath(@"C:\Users\x\file.md"));
        Assert.Equal(@"C:\a b\c.txt", LinkIcon.LocalPath("file:///C:/a%20b/c.txt"));
        Assert.Null(LinkIcon.LocalPath(@"C:\Users\{user}\file.md"));
        Assert.Null(LinkIcon.LocalPath("https://x"));
        Assert.Null(LinkIcon.LocalPath("relative\\path"));
    }

    [Fact]
    public void IconHrefs_resolves_and_orders_candidates()
    {
        const string html = """
            <html><head>
            <link rel="stylesheet" href="/s.css">
            <link rel="apple-touch-icon" href="/apple.png">
            <LINK REL='shortcut icon' HREF='/favicon.ico?v=2&amp;x=1'>
            <link href=https://cdn.example.net/i.png rel=icon type=image/png>
            <link rel="icon" type="image/svg+xml" href="/i.svg">
            <link rel="icon" href="data:image/png;base64,AAAA">
            </head></html>
            """;
        var hrefs = LinkIcon.IconHrefs(html, new Uri("https://example.com/"));
        Assert.Equal(new[]
        {
            new Uri("https://example.com/favicon.ico?v=2&x=1"),
            new Uri("https://cdn.example.net/i.png"),
            new Uri("https://example.com/apple.png"),
        }, hrefs);
    }

    [Fact]
    public void IconHrefs_empty_when_none() => Assert.Empty(LinkIcon.IconHrefs("<html></html>", new Uri("https://x/")));
}
