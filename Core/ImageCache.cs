using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace EchoMusic.Core;

public static class ImageCache
{
    private static readonly object MemLock = new();
    private static readonly Dictionary<string, (Bitmap Image, LinkedListNode<string> Node)> MemCache = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> MemLru = new();
    private static readonly SemaphoreSlim NetworkImageSlots = new(4, 4);
    private static readonly SemaphoreSlim SaverImageSlots = new(2, 2);
    private static readonly ConcurrentDictionary<string, Lazy<Task<Bitmap?>>> Inflight = new(StringComparer.Ordinal);
    private static readonly Regex VideoThumbRegex = new(@"/vi/([A-Za-z0-9_-]{11})/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static int _memCapacity = 72;
    private static volatile bool _resourceSaver;

    public static void SetResourceSaver(bool enabled)
    {
        _resourceSaver = enabled;
        ResourceSaverState.IsActive = enabled;
        lock (MemLock)
        {
            _memCapacity = enabled ? 12 : 72;
            TrimMemoryCache();
        }
    }

    private static Bitmap? TryGetMemory(string key)
    {
        lock (MemLock)
        {
            if (!MemCache.TryGetValue(key, out var item)) return null;
            MemLru.Remove(item.Node);
            MemLru.AddFirst(item.Node);
            return item.Image;
        }
    }

    private static void TrimMemoryCache()
    {
        while (MemCache.Count > _memCapacity && MemLru.Last is { } last)
        {
            MemCache.Remove(last.Value);
            MemLru.RemoveLast();
        }
    }

    public static async Task<Bitmap?> LoadAsync(string? urlOrPath, HttpClient http, string cacheDir)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath)) return null;
        foreach (var candidate in GetCandidates(urlOrPath))
        {
            var bmp = await LoadOneAsync(candidate, http, cacheDir).ConfigureAwait(false);
            if (bmp is not null) return bmp;
        }
        return null;
    }

    private static IEnumerable<string> GetCandidates(string path)
    {
        yield return path;
        var match = VideoThumbRegex.Match(path);
        if (!match.Success) yield break;
        var id = match.Groups[1].Value;
        foreach (var quality in new[] { "hqdefault", "maxresdefault", "sddefault", "mqdefault", "default" })
        {
            var fallback = $"https://i.ytimg.com/vi/{id}/{quality}.jpg";
            if (!string.Equals(fallback, path, StringComparison.OrdinalIgnoreCase)) yield return fallback;
        }
    }

    private static async Task<Bitmap?> LoadOneAsync(string key, HttpClient http, string cacheDir)
    {
        var mem = TryGetMemory(key);
        if (mem is not null) return mem;
        var lazy = Inflight.GetOrAdd(key, k => new Lazy<Task<Bitmap?>>(
            () => LoadCoreAsync(k, http, cacheDir), LazyThreadSafetyMode.ExecutionAndPublication));
        try { return await lazy.Value.ConfigureAwait(false); }
        finally
        {
            if (Inflight.TryGetValue(key, out var current) && ReferenceEquals(current, lazy))
                Inflight.TryRemove(key, out _);
        }
    }

    private static async Task<Bitmap?> LoadCoreAsync(string key, HttpClient http, string cacheDir)
    {
        Bitmap? TryDecode(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes, writable: false);
                return Bitmap.DecodeToWidth(ms, _resourceSaver ? 384 : 640);
            }
            catch { return null; }
        }

        if (File.Exists(key))
        {
            try
            {
                var local = TryDecode(await File.ReadAllBytesAsync(key).ConfigureAwait(false));
                if (local is not null) return AddToMemory(key, local);
            }
            catch { }
            try { File.Delete(key); } catch { }
        }
        if (!Uri.TryCreate(key, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..20];
        var diskPath = Path.Combine(cacheDir, $"img_{hash}.bin");
        if (File.Exists(diskPath))
        {
            try
            {
                var cached = TryDecode(await File.ReadAllBytesAsync(diskPath).ConfigureAwait(false));
                if (cached is not null) return AddToMemory(key, cached);
            }
            catch { }
            try { File.Delete(diskPath); } catch { }
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation("User-Agent", YouTubeClients.WebUa);
            req.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Referer", YouTubeClients.Referer);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            byte[] bytes;
            bool saverRequest = _resourceSaver;
            await NetworkImageSlots.WaitAsync(timeout.Token).ConfigureAwait(false);
            bool saverSlotTaken = false;
            try
            {
                if (saverRequest)
                {
                    await SaverImageSlots.WaitAsync(timeout.Token).ConfigureAwait(false);
                    saverSlotTaken = true;
                }
                using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                long maxBytes = saverRequest ? 8_000_000 : 20_000_000;
                if (!response.IsSuccessStatusCode || (response.Content.Headers.ContentLength is long len && (len <= 0 || len > maxBytes)))
                    return null;
                bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                if (saverSlotTaken) SaverImageSlots.Release();
                NetworkImageSlots.Release();
            }
            if (bytes.Length < 128 || bytes.Length > (saverRequest ? 8_000_000 : 20_000_000)) return null;
            var bmp = TryDecode(bytes);
            if (bmp is null) return null;
            try
            {
                Directory.CreateDirectory(cacheDir);
                var temp = diskPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
                File.Move(temp, diskPath, overwrite: true);
            }
            catch { }
            return AddToMemory(key, bmp);
        }
        catch { return null; }
    }

    private static Bitmap AddToMemory(string key, Bitmap bmp)
    {
        lock (MemLock)
        {
            if (MemCache.TryGetValue(key, out var existing))
            {
                MemLru.Remove(existing.Node);
                MemLru.AddFirst(existing.Node);
                return existing.Image;
            }
            var node = MemLru.AddFirst(key);
            MemCache[key] = (bmp, node);
            TrimMemoryCache();
            return bmp;
        }
    }

    /// <summary>
    /// Extracts a rich vibrant accent color from an album artwork Bitmap for Echo's Dynamic Ambient Backdrop Glow!
    /// </summary>
    public static unsafe Color? ExtractVibrantColor(Bitmap? bmp)
    {
        if (bmp is null) return null;
        try
        {
            using var ms = new MemoryStream();
            bmp.Save(ms);
            ms.Position = 0;
            using var tiny = Bitmap.DecodeToWidth(ms, 24);
            var size = tiny.PixelSize;
            int stride = size.Width * 4;
            var pixels = new byte[stride * size.Height];
            fixed (byte* ptr = pixels)
            {
                tiny.CopyPixels(new Avalonia.PixelRect(0, 0, size.Width, size.Height), (IntPtr)ptr, pixels.Length, stride);
            }

            double bestScore = -1;
            byte bestR = 237, bestG = 85, bestB = 100;
            long sumR = 0, sumG = 0, sumB = 0, cnt = 0;

            for (int i = 0; i <= pixels.Length - 4; i += 4)
            {
                // BGRA8888 or RGBA8888 - check format
                bool isBgra = tiny.Format == Avalonia.Platform.PixelFormat.Bgra8888;
                byte b = isBgra ? pixels[i] : pixels[i + 2];
                byte g = pixels[i + 1];
                byte r = isBgra ? pixels[i + 2] : pixels[i];

                int max = Math.Max(r, Math.Max(g, b));
                int min = Math.Min(r, Math.Min(g, b));
                double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                if (lum is < 0.12 or > 0.88) continue;

                sumR += r; sumG += g; sumB += b; cnt++;
                double sat = max == 0 ? 0 : (double)(max - min) / max;
                double score = sat * 1.6 + (1.0 - Math.Abs(lum - 0.52));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestR = r; bestG = g; bestB = b;
                }
            }

            if (bestScore > 0.35)
                return Color.FromRgb(bestR, bestG, bestB);
            if (cnt > 0)
                return Color.FromRgb((byte)(sumR / cnt), (byte)(sumG / cnt), (byte)(sumB / cnt));
        }
        catch { }
        return null;
    }
}

/// <summary>
/// Native Windows 10/11 integration. Media commands are consumed only when this window is
/// foreground through WM_APPCOMMAND; Echo Music never registers system-wide media hotkeys.
/// Also applies the Windows 11 DWM dark title-bar attribute. Safe no-op on non-Windows.
/// </summary>
public static class WindowsIntegration
{
    private const int WM_APPCOMMAND = 0x0319;

    private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
    private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;
    private const int APPCOMMAND_MEDIA_STOP = 13;
    private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void ApplyDarkTitleBar(IntPtr hwnd, bool isDark)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return;
        try
        {
            int val = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref val, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        }
        catch { }
    }

    public static bool HandleWin32MediaMessage(uint msg, IntPtr wParam, IntPtr lParam, AudioEngine engine)
    {
        if (msg == WM_APPCOMMAND)
        {
            int cmd = (int)(((long)lParam >> 16) & 0xFFF);
            switch (cmd)
            {
                case APPCOMMAND_MEDIA_PLAY_PAUSE: engine.TogglePlayPause(); return true;
                case APPCOMMAND_MEDIA_NEXTTRACK: _ = engine.NextAsync(); return true;
                case APPCOMMAND_MEDIA_PREVIOUSTRACK: _ = engine.PreviousAsync(); return true;
                case APPCOMMAND_MEDIA_STOP: engine.Pause(); return true;
            }
        }
        return false;
    }
}
