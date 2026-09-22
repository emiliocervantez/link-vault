using System.Net;
using System.Text.RegularExpressions;

namespace LinkVault.Core;

/// <summary>Decides where a Link's icon comes from: a website's favicon, a local file's shell icon, or neither.</summary>
public static partial class LinkIcon
{
    /// <summary>
    /// scheme://host[:port]/ of an http(s) link, or null when there is none or when a Parameter changes the host.
    /// </summary>
    public static Uri? SiteRoot(string url)
    {
        var template = UrlTemplate.TryParse(url, out _);
        if (template is null) return null;
        var a = Root(template, "a");
        var b = Root(template, "b");
        return a is not null && a == b ? a : null;
    }

    private static Uri? Root(UrlTemplate template, string fill)
    {
        var values = template.Parameters.ToDictionary(p => p.Name, _ => fill);
        if (!Uri.TryCreate(template.Expand(values), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    /// <summary>File name of the cached favicon for a site, e.g. "www.example.com_8080.png".</summary>
    public static string CacheFileName(Uri siteRoot) => siteRoot.Authority.ToLowerInvariant().Replace(':', '_') + ".png";

    /// <summary>Full local path of a file or folder link without Parameters, else null.</summary>
    public static string? LocalPath(string url)
    {
        var template = UrlTemplate.TryParse(url, out _);
        if (template is null || template.Parameters.Count > 0) return null;
        var text = template.Expand();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.IsFile) return uri.LocalPath;
        return Path.IsPathFullyQualified(text) ? text : null;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"([\w-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))")]
    private static partial Regex Attribute();

    /// <summary>
    /// Icon URLs declared by an HTML page (<c>&lt;link rel="icon" href=...&gt;</c>), resolved against the page,
    /// best candidates first. SVG icons are skipped (not decodable here).
    /// </summary>
    public static IReadOnlyList<Uri> IconHrefs(string html, Uri page)
    {
        var icons = new List<Uri>();
        var touchIcons = new List<Uri>();
        foreach (Match tag in LinkTag().Matches(html))
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match a in Attribute().Matches(tag.Value))
                attrs[a.Groups[1].Value] = WebUtility.HtmlDecode(a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value);

            if (!attrs.TryGetValue("rel", out var rel) || !attrs.TryGetValue("href", out var href)) continue;
            var tokens = rel.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var isIcon = tokens.Contains("icon");
            var isTouch = tokens.Contains("apple-touch-icon") || tokens.Contains("apple-touch-icon-precomposed");
            if (!isIcon && !isTouch) continue;
            if (attrs.TryGetValue("type", out var type) && type.Contains("svg", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(page, href.Trim(), out var uri)) continue;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) continue;
            if (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
            (isIcon ? icons : touchIcons).Add(uri);
        }
        return icons.Concat(touchIcons).Distinct().ToList();
    }
}
