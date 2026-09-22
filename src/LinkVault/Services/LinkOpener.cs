using System.ComponentModel;
using System.Diagnostics;
using LinkVault.Core;
using LinkVault.Popup;

namespace LinkVault.Services;

internal static class LinkOpener
{
    /// <summary>
    /// Opens the Link with its default handler (the default browser for web links). Asks for Parameter values
    /// first when the Link has any. Returns a problem to report, or null on success or cancel.
    /// </summary>
    public static string? Open(Link link)
    {
        var template = UrlTemplate.TryParse(link.Url, out var error);
        if (template is null) return $"\"{link.Label}\": {error}";

        IReadOnlyDictionary<string, string>? values = null;
        if (template.Parameters.Count > 0)
        {
            var window = new ParamWindow(link.Label, template.Parameters);
            if (window.ShowDialog() != true) return null;
            values = window.Values;
        }

        var target = template.Expand(values);
        Trace.Log($"open: {target}");
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"Could not open \"{target}\": {ex.Message}";
        }
    }
}
