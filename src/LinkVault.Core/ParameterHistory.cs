namespace LinkVault.Core;

/// <summary>
/// The values entered in the Input Window, most recent first, per Link URL and Parameter name.
/// Kept apart from the settings: it changes on every Open and is not part of an export.
/// </summary>
public sealed class ParameterHistory
{
    /// <summary>Link URL -> Parameter name -> values, most recent first.</summary>
    public Dictionary<string, Dictionary<string, List<string>>> Values { get; set; } = new();

    public IReadOnlyList<string> Get(string url, string parameter) =>
        Values.TryGetValue(url, out var byName) && byName.TryGetValue(parameter, out var list) ? list : Array.Empty<string>();

    /// <summary>Puts each non-empty value first in its Parameter's list and keeps at most <paramref name="max"/> values.</summary>
    public void Remember(string url, IReadOnlyDictionary<string, string> values, int max)
    {
        foreach (var (name, value) in values)
        {
            if (value.Length == 0) continue;
            if (!Values.TryGetValue(url, out var byName)) Values[url] = byName = new();
            if (!byName.TryGetValue(name, out var list)) byName[name] = list = new();
            list.Remove(value);
            list.Insert(0, value);
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
        }
    }

    /// <summary>Drops the values of URLs that are no longer Links and cuts every list to <paramref name="max"/> values.</summary>
    public void Trim(IEnumerable<string> urls, int max)
    {
        var keep = urls.ToHashSet();
        foreach (var url in Values.Keys.Where(u => !keep.Contains(u)).ToList()) Values.Remove(url);
        foreach (var list in Values.Values.SelectMany(byName => byName.Values))
        {
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
        }
    }
}
