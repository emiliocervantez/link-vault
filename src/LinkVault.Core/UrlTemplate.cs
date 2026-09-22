using System.Text;

namespace LinkVault.Core;

/// <summary>A Parameter of a Url Template: <c>{name}</c> or <c>{name=default}</c>.</summary>
public sealed record UrlParameter(string Name, string? Default);

/// <summary>
/// A link URL that may contain Parameters. <c>{name}</c> and <c>{name=default}</c> are Parameters;
/// <c>{{</c> and <c>}}</c> are literal braces. The same name used twice is one Parameter.
/// </summary>
public sealed class UrlTemplate
{
    // Each part is either literal text or a parameter name.
    private readonly List<(string Text, bool IsParameter)> _parts;

    private UrlTemplate(List<(string, bool)> parts, List<UrlParameter> parameters)
    {
        _parts = parts;
        Parameters = parameters;
    }

    /// <summary>Distinct Parameters in order of first appearance.</summary>
    public IReadOnlyList<UrlParameter> Parameters { get; }

    /// <summary>Returns null and a human-readable error when the template is malformed.</summary>
    public static UrlTemplate? TryParse(string url, out string? error)
    {
        var parts = new List<(string, bool)>();
        var parameters = new List<UrlParameter>();
        var literal = new StringBuilder();
        error = null;

        for (var i = 0; i < url.Length; i++)
        {
            var c = url[i];
            if (c == '{' && i + 1 < url.Length && url[i + 1] == '{') { literal.Append('{'); i++; continue; }
            if (c == '}' && i + 1 < url.Length && url[i + 1] == '}') { literal.Append('}'); i++; continue; }
            if (c == '}') { error = $"Unmatched '}}' at position {i + 1}. Use '}}}}' for a literal brace."; return null; }
            if (c != '{') { literal.Append(c); continue; }

            var close = url.IndexOf('}', i + 1);
            var nextOpen = url.IndexOf('{', i + 1);
            if (close < 0 || (nextOpen >= 0 && nextOpen < close))
            {
                error = $"Unmatched '{{' at position {i + 1}. Use '{{{{' for a literal brace.";
                return null;
            }

            var inner = url.Substring(i + 1, close - i - 1);
            var eq = inner.IndexOf('=');
            var name = (eq < 0 ? inner : inner[..eq]).Trim();
            string? def = eq < 0 ? null : inner[(eq + 1)..];
            if (name.Length == 0) { error = $"Parameter at position {i + 1} has no name."; return null; }

            var index = parameters.FindIndex(p => p.Name == name);
            if (index < 0) parameters.Add(new UrlParameter(name, def));
            else if (def is not null)
            {
                var existing = parameters[index].Default;
                if (existing is not null && existing != def)
                {
                    error = $"Parameter '{name}' has two different defaults.";
                    return null;
                }
                parameters[index] = new UrlParameter(name, def);
            }

            if (literal.Length > 0) { parts.Add((literal.ToString(), false)); literal.Clear(); }
            parts.Add((name, true));
            i = close;
        }
        if (literal.Length > 0) parts.Add((literal.ToString(), false));
        return new UrlTemplate(parts, parameters);
    }

    /// <summary>Fills in the Parameters, URL-encoding each value. A missing value falls back to the default, then to empty.</summary>
    public string Expand(IReadOnlyDictionary<string, string>? values = null)
    {
        var sb = new StringBuilder();
        foreach (var (text, isParameter) in _parts)
        {
            if (!isParameter) { sb.Append(text); continue; }
            var value = values is not null && values.TryGetValue(text, out var v)
                ? v
                : Parameters.First(p => p.Name == text).Default ?? "";
            sb.Append(Uri.EscapeDataString(value));
        }
        return sb.ToString();
    }
}
