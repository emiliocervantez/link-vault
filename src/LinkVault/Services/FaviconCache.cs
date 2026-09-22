using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinkVault.Core;
using LinkVault.Native;
using static LinkVault.Native.NativeMethods;

namespace LinkVault.Services;

/// <summary>
/// Icons for Popup rows. Web links: the site's favicon, fetched in the background and cached as a 32 px PNG in
/// the icons folder; the Popup never waits for a fetch. File links: the Windows shell icon. Everything else, and
/// any site whose icon is not cached yet: the generic internet-shortcut icon. UI-thread only.
/// </summary>
internal sealed class FaviconCache
{
    private const int IconPx = 32;
    private const int FetchTimeoutSeconds = 5;
    private const int MaxIconBytes = 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    private readonly string _dir;
    private readonly Dictionary<string, ImageSource> _images = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Sites fetched (or being fetched) this session; not retried until a forced refresh.</summary>
    private readonly HashSet<string> _attempted = new(StringComparer.OrdinalIgnoreCase);
    private ImageSource? _generic;

    public FaviconCache(string dir) => _dir = dir;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(FetchTimeoutSeconds) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LinkVault/1.0");
        return client;
    }

    public ImageSource? For(Link link)
    {
        if (LinkIcon.SiteRoot(link.Url) is { } root)
        {
            var file = Path.Combine(_dir, LinkIcon.CacheFileName(root));
            if (_images.TryGetValue(file, out var cached)) return cached;
            if (File.Exists(file) && LoadPng(file) is { } png) return _images[file] = png;
            return Generic();
        }
        if (LinkIcon.LocalPath(link.Url) is { } path)
        {
            var key = "path:" + path;
            if (_images.TryGetValue(key, out var cached)) return cached;
            var icon = ShellIcon(path, 0, 0) ?? Generic();
            if (icon is not null) _images[key] = icon;
            return icon;
        }
        return Generic();
    }

    /// <summary>Starts background fetches for sites without a cached icon. <paramref name="force"/> refetches every site.</summary>
    public void Fetch(IEnumerable<Link> links, bool force)
    {
        if (force) _attempted.Clear();
        var roots = links.Select(l => LinkIcon.SiteRoot(l.Url)).OfType<Uri>().DistinctBy(r => r.Authority.ToLowerInvariant());
        foreach (var root in roots)
        {
            var file = Path.Combine(_dir, LinkIcon.CacheFileName(root));
            if (!_attempted.Add(root.Authority)) continue;
            if (!force && File.Exists(file)) continue;
            Trace.Log($"favicon: fetching {root}");
            _ = FetchAsync(root, file);
        }
    }

    private async Task FetchAsync(Uri root, string file)
    {
        try
        {
            var png = await Task.Run(() => DownloadPng(root));
            if (png is null)
            {
                Trace.Log($"favicon: none found for {root}");
                return;
            }
            await File.WriteAllBytesAsync(file, png);
            _images.Remove(file);   // back on the UI thread: the next Popup picks up the new file
            Trace.Log($"favicon: cached {file}");
        }
        catch (Exception ex)
        {
            Trace.Log($"favicon: {root} failed: {ex.Message}");
        }
    }

    private static async Task<byte[]?> DownloadPng(Uri root)
    {
        var candidates = new List<Uri>();
        try
        {
            using var response = await Http.GetAsync(root);
            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync();
                // Relative hrefs resolve against the final URL after redirects.
                candidates.AddRange(LinkIcon.IconHrefs(html, response.RequestMessage?.RequestUri ?? root));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Trace.Log($"favicon: page {root} failed: {ex.Message}");
        }
        candidates.Add(new Uri(root, "/favicon.ico"));

        foreach (var uri in candidates.Distinct())
        {
            try
            {
                Trace.Log($"favicon: trying {uri}");
                using var response = await Http.GetAsync(uri);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxIconBytes) continue;
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (ToPng(bytes) is { } png) return png;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Trace.Log($"favicon: {uri} failed: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>Decodes ICO/PNG/GIF/JPEG/BMP, picks the frame closest to 32 px and re-encodes it as a 32 px PNG.</summary>
    private static byte[]? ToPng(byte[] bytes)
    {
        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames
                .OrderBy(f => f.PixelWidth >= IconPx ? f.PixelWidth - IconPx : 1000 + IconPx - f.PixelWidth)
                .First();
            BitmapSource scaled = frame.PixelWidth == IconPx && frame.PixelHeight == IconPx
                ? frame
                : new TransformedBitmap(frame, new ScaleTransform((double)IconPx / frame.PixelWidth, (double)IconPx / frame.PixelHeight));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(scaled));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception)
        {
            return null;   // not an image (e.g. an HTML error page served as favicon.ico)
        }
    }

    private static ImageSource? LoadPng(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // do not keep the file open
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private ImageSource? Generic() => _generic ??= ShellIcon(".url", FILE_ATTRIBUTE_NORMAL, SHGFI_USEFILEATTRIBUTES);

    private static ImageSource? ShellIcon(string path, uint attributes, uint flags)
    {
        var info = new SHFILEINFO();
        SHGetFileInfo(path, attributes, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON | flags);
        if (info.hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
