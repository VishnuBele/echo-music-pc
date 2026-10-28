using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EchoMusic.Core;

public sealed record ClientSpec(
    string Name,
    string Version,
    string ClientId,
    string UserAgent,
    string? OsName = null,
    string? OsVersion = null,
    string? DeviceMake = null,
    string? DeviceModel = null,
    string? AndroidSdk = null,
    bool LoginSupported = false
);

public static class YouTubeClients
{
    public const string Origin = "https://music.youtube.com";
    public const string Referer = "https://music.youtube.com/";
    public const string WebUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public static ClientSpec WebRemix { get; set; } = new(
        "WEB_REMIX", "1.20260213.01.00", "67", WebUa, LoginSupported: true
    );

    // Exact upstream Echo-Music YTPlayerUtils lead stream client: full-file 206 capable
    public static ClientSpec VisionOs { get; set; } = new(
        "VISIONOS", "0.1", "101",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15",
        OsName: "visionOS", OsVersion: "1.3.21O771", DeviceMake: "Apple", DeviceModel: "RealityDevice14,1"
    );

    // Exact upstream Echo-Music secondary whole-file client
    public static ClientSpec AndroidVr { get; set; } = new(
        "ANDROID_VR", "1.65.10", "28",
        "com.google.android.apps.youtube.vr.oculus/1.65.10 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip",
        OsName: "Android", OsVersion: "12L", DeviceMake: "Oculus", DeviceModel: "Quest 3", AndroidSdk: "32"
    );

    public static ClientSpec AndroidVrLegacy { get; set; } = new(
        "ANDROID_VR", "1.43.32", "28",
        "com.google.android.apps.youtube.vr.oculus/1.43.32 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip",
        OsName: "Android", OsVersion: "12L", DeviceMake: "Oculus", DeviceModel: "Quest 3", AndroidSdk: "32"
    );

    public static ClientSpec IpadOs { get; set; } = new(
        "IOS", "21.29.1", "5",
        "com.google.ios.youtube/21.29.1 (iPad7,6; U; CPU iPadOS 18_5 like Mac OS X;)",
        OsName: "iPadOS", OsVersion: "18.5.22F76", DeviceMake: "Apple", DeviceModel: "iPad7,6"
    );

    public static ClientSpec Ios { get; set; } = new(
        "IOS", "21.29.1", "5",
        "com.google.ios.youtube/21.29.1 (iPhone16,2; U; CPU iOS 18_5 like Mac OS X;)",
        OsName: "iPhone", OsVersion: "18.5.22F76", DeviceMake: "Apple", DeviceModel: "iPhone16,2"
    );

    public static readonly IReadOnlyDictionary<string, string> SearchFilterParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["songs"] = "EgWKAQIIAWoKEAkQBRAKEAMQBA%3D%3D",
        ["videos"] = "EgWKAQIQAWoKEAkQChAFEAMQBA%3D%3D",
        ["albums"] = "EgWKAQIYAWoKEAkQChAFEAMQBA%3D%3D",
        ["artists"] = "EgWKAQIgAWoKEAkQChAFEAMQBA%3D%3D",
        ["playlists"] = "EgeKAQQoADgBagwQDhAKEAMQBRAJEAQ%3D",
    };
}

public sealed class InnerTubeClient
{
    private readonly HttpClient _http;
    private static readonly IReadOnlyDictionary<string, string> TopSongChartPlaylists = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["IN"] = "PL4fGSI1pDJn4pTWyM3t61lOyZ6_4jcNOw",
        ["US"] = "PL4fGSI1pDJn6O1LS0XSdF3RyO0Rq_LDeI",
        ["GB"] = "PL4fGSI1pDJn6_f5P3MnzXg9l3GDfnSlXa",
        ["JP"] = "PL4fGSI1pDJn4-UIb6RKHdxam-oAUULIGB",
        ["KR"] = "PL4fGSI1pDJn6jXS_Tv_N9B8Z0HTRVJE0m",
        ["DE"] = "PL4fGSI1pDJn6KpOXlp0MH8qA9tngXaUJ-",
        ["BR"] = "PL4fGSI1pDJn7rGBE8kEC0CqTa1nMh9AKB",
    };
    private static readonly SemaphoreSlim WebVersionLock = new(1, 1);
    private static DateTimeOffset _webVersionLastSuccessAt = DateTimeOffset.MinValue;
    private static DateTimeOffset _webVersionLastAttemptAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _visitorLock = new(1, 1);
    private string? _visitorData;
    private DateTimeOffset _visitorFetchedAt = DateTimeOffset.MinValue;
    private Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    public string RegionGl { get; set; } = "IN";
    public string LanguageHl { get; set; } = "en";
    public string? CookieHeader { get; private set; }
    public string? DataSyncId { get; set; }
    public bool IsSignedIn => !string.IsNullOrWhiteSpace(CookieHeader) && (_cookies.ContainsKey("SAPISID") || _cookies.ContainsKey("__Secure-3PAPISID"));

    public InnerTubeClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(4),
            ConnectTimeout = TimeSpan.FromSeconds(12),
            // Prefer IPv4 consistently across both InnerTube API and googlevideo.com CDN!
            // Prevents dual-stack ISPs (Jio/Airtel/Comcast) from minting an IPv6-bound stream URL
            // and then fetching media over IPv4 (which triggers instant HTTP 403 Forbidden on CDN).
            ConnectCallback = async (ctx, token) =>
            {
                var addrs = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, token);
                var ipv4 = addrs.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToArray();
                var chosen = ipv4.Length > 0 ? ipv4 : addrs;
                var sock = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
                {
                    NoDelay = true
                };
                try
                {
                    await sock.ConnectAsync(chosen, ctx.DnsEndPoint.Port, token);
                    return new System.Net.Sockets.NetworkStream(sock, ownsSocket: true);
                }
                catch
                {
                    sock.Dispose();
                    throw;
                }
            }
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(22),
        };
    }

    public HttpClient SharedHttp => _http;

    public void SetCookies(string? cookieHeader, string? dataSyncId = null)
    {
        CookieHeader = string.IsNullOrWhiteSpace(cookieHeader) ? null : cookieHeader.Trim();
        DataSyncId = dataSyncId;
        _cookies.Clear();
        if (CookieHeader is null) return;
        foreach (var part in CookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var k = part[..eq].Trim();
            var v = part[(eq + 1)..].Trim();
            _cookies[k] = v;
        }
    }

    public async Task<string?> EnsureVisitorDataAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && !string.IsNullOrEmpty(_visitorData) && DateTimeOffset.UtcNow - _visitorFetchedAt < TimeSpan.FromMinutes(45))
            return _visitorData;

        await _visitorLock.WaitAsync(ct);
        try
        {
            if (!force && !string.IsNullOrEmpty(_visitorData) && DateTimeOffset.UtcNow - _visitorFetchedAt < TimeSpan.FromMinutes(45))
                return _visitorData;

            // 1. Primary: sw.js_data JSON [0][2] exact element (identical to upstream YouTube.kt:2106)
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{YouTubeClients.Origin}/sw.js_data");
                req.Headers.TryAddWithoutValidation("User-Agent", YouTubeClients.WebUa);
                req.Headers.TryAddWithoutValidation("Accept-Language", $"{LanguageHl},{RegionGl};q=0.9,en;q=0.8");
                using var res = await _http.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    var txt = await res.Content.ReadAsStringAsync(ct);
                    if (txt.StartsWith(")]}'", StringComparison.Ordinal))
                        txt = txt[4..].TrimStart();
                    var parsed = JsonNode.Parse(txt);
                    if (parsed?[0]?[2] is JsonArray arr)
                    {
                        foreach (var el in arr)
                        {
                            if (el is JsonValue v && v.TryGetValue<string>(out var cand) &&
                                Regex.IsMatch(cand, @"^Cg[ts][A-Za-z0-9_\-%=]+$"))
                            {
                                _visitorData = cand;
                                _visitorFetchedAt = DateTimeOffset.UtcNow;
                                return _visitorData;
                            }
                        }
                    }
                }
            }
            catch { /* fall through */ }

            // 2. Fallback: homepage HTML
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.youtube.com/");
                req.Headers.TryAddWithoutValidation("User-Agent", YouTubeClients.WebUa);
                using var res = await _http.SendAsync(req, ct);
                var html = await res.Content.ReadAsStringAsync(ct);
                var m = Regex.Match(html, @"""visitorData""\s*:\s*""([^""]+)""");
                if (m.Success)
                {
                    _visitorData = Uri.UnescapeDataString(m.Groups[1].Value.Replace("\\u003d", "="));
                    _visitorFetchedAt = DateTimeOffset.UtcNow;
                }
            }
            catch { /* optional */ }

            return _visitorData;
        }
        finally
        {
            _visitorLock.Release();
        }
    }

    private JsonObject BuildContext(ClientSpec client, bool useLogin)
    {
        var c = new JsonObject
        {
            ["clientName"] = client.Name,
            ["clientVersion"] = client.Version,
            ["gl"] = RegionGl,
            ["hl"] = LanguageHl,
        };
        if (client.OsName is not null) c["osName"] = client.OsName;
        if (client.OsVersion is not null) c["osVersion"] = client.OsVersion;
        if (client.DeviceMake is not null) c["deviceMake"] = client.DeviceMake;
        if (client.DeviceModel is not null) c["deviceModel"] = client.DeviceModel;
        if (client.AndroidSdk is not null) c["androidSdkVersion"] = client.AndroidSdk;
        if (!string.IsNullOrEmpty(_visitorData)) c["visitorData"] = _visitorData;

        var ctx = new JsonObject { ["client"] = c };
        if (useLogin && client.LoginSupported && !string.IsNullOrEmpty(DataSyncId))
        {
            ctx["user"] = new JsonObject { ["onBehalfOfUser"] = DataSyncId };
        }
        return ctx;
    }

    private async Task RefreshWebRemixVersionAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // Refresh a confirmed version every six hours, but do not let one transient network
        // failure suppress discovery for the full cache window; failed attempts retry after 2 min.
        if (now - _webVersionLastSuccessAt < TimeSpan.FromHours(6) ||
            now - _webVersionLastAttemptAt < TimeSpan.FromMinutes(2)) return;
        await WebVersionLock.WaitAsync(ct);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (now - _webVersionLastSuccessAt < TimeSpan.FromHours(6) ||
                now - _webVersionLastAttemptAt < TimeSpan.FromMinutes(2)) return;
            _webVersionLastAttemptAt = now;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var req = new HttpRequestMessage(HttpMethod.Get, YouTubeClients.Origin + "/");
                req.Headers.TryAddWithoutValidation("User-Agent", YouTubeClients.WebUa);
                req.Headers.TryAddWithoutValidation("Accept-Language", $"{LanguageHl},{RegionGl};q=0.9,en;q=0.8");
                using var response = await _http.SendAsync(req, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync(timeout.Token);
                    var match = Regex.Match(html, "\"INNERTUBE_CLIENT_VERSION\"\\s*:\\s*\"([^\"]+)\"");
                    if (match.Success && Regex.IsMatch(match.Groups[1].Value, @"^\d+(?:\.\d+){2,4}$"))
                    {
                        YouTubeClients.WebRemix = YouTubeClients.WebRemix with { Version = match.Groups[1].Value };
                        _webVersionLastSuccessAt = DateTimeOffset.UtcNow;
                    }
                }
            }
            catch { /* Keep the last known compatible version and retry discovery after the short failure cooldown. */ }
        }
        finally { WebVersionLock.Release(); }
    }

    public async Task<JsonNode?> PostAsync(
        string endpoint,
        JsonObject body,
        ClientSpec? client = null,
        string? queryExtra = null,
        bool allowLogin = true,
        CancellationToken ct = default)
    {
        if (client is null || ReferenceEquals(client, YouTubeClients.WebRemix))
            await RefreshWebRemixVersionAsync(ct);
        var spec = client ?? YouTubeClients.WebRemix;
        if (string.IsNullOrEmpty(_visitorData))
            await EnsureVisitorDataAsync(false, ct);

        var useLogin = allowLogin && spec.LoginSupported && IsSignedIn;
        body["context"] = BuildContext(spec, useLogin);
        var jsonPayload = body.ToJsonString();

        var url = $"{YouTubeClients.Origin}/youtubei/v1/{endpoint}?prettyPrint=false"
                  + (string.IsNullOrEmpty(queryExtra) ? "" : $"&{queryExtra}");

        Exception? lastErr = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("User-Agent", spec.UserAgent);
                req.Headers.TryAddWithoutValidation("X-Goog-Api-Format-Version", "1");
                req.Headers.TryAddWithoutValidation("X-YouTube-Client-Name", spec.ClientId);
                req.Headers.TryAddWithoutValidation("X-YouTube-Client-Version", spec.Version);
                req.Headers.TryAddWithoutValidation("X-Origin", YouTubeClients.Origin);
                req.Headers.TryAddWithoutValidation("Referer", YouTubeClients.Referer);
                if (spec.LoginSupported)
                    req.Headers.TryAddWithoutValidation("Origin", YouTubeClients.Origin);
                req.Headers.TryAddWithoutValidation("Accept-Language", $"{LanguageHl},{RegionGl};q=0.9,en;q=0.8");
                if (!string.IsNullOrEmpty(_visitorData))
                    req.Headers.TryAddWithoutValidation("X-Goog-Visitor-Id", _visitorData);

                if (useLogin && CookieHeader is not null)
                {
                    req.Headers.TryAddWithoutValidation("Cookie", CookieHeader);
                    var sapisid = _cookies.GetValueOrDefault("SAPISID") ?? _cookies.GetValueOrDefault("__Secure-3PAPISID");
                    if (!string.IsNullOrEmpty(sapisid))
                    {
                        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var raw = $"{ts} {sapisid} {YouTubeClients.Origin}";
                        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
                        req.Headers.TryAddWithoutValidation("Authorization", $"SAPISIDHASH {ts}_{hash}");
                    }
                }

                using var res = await _http.SendAsync(req, ct);
                if ((int)res.StatusCode >= 500 || res.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    lastErr = new HttpRequestException($"HTTP {(int)res.StatusCode} on {endpoint}");
                    await Task.Delay(350 * (attempt + 1), ct);
                    continue;
                }
                res.EnsureSuccessStatusCode();
                var text = await res.Content.ReadAsStringAsync(ct);
                return JsonNode.Parse(text);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastErr = ex;
                if (attempt < 2)
                    await Task.Delay(350 * (attempt + 1), ct);
            }
        }
        throw lastErr ?? new InvalidOperationException($"InnerTube {endpoint} failed.");
    }

    // --------------------------------------------------------- High-level APIs

    public async Task<HomeFeedResult> GetHomeAsync(
        string? moodParams = null,
        string? continuation = null,
        string? moodBrowseId = null,
        CancellationToken ct = default)
    {
        await EnsureVisitorDataAsync(false, ct);

        if (!string.IsNullOrEmpty(continuation))
        {
            var enc = Uri.EscapeDataString(continuation);
            var contRoot = await PostAsync(
                "browse",
                new JsonObject(),
                queryExtra: $"ctoken={enc}&continuation={enc}&type=next",
                ct: ct);
            return InnertubeParsers.ParseHome(contRoot);
        }

        var targetBrowseId = string.IsNullOrWhiteSpace(moodBrowseId) ? "FEmusic_home" : moodBrowseId!;
        var body = new JsonObject { ["browseId"] = targetBrowseId };
        if (!string.IsNullOrEmpty(moodParams)) body["params"] = moodParams;
        var root = await PostAsync("browse", body, ct: ct);

        var result = InnertubeParsers.ParseHome(root);
        var seenTitles = new HashSet<string>(
            result.Shelves.Select(s => s.Title).Where(t => !string.IsNullOrWhiteSpace(t)),
            StringComparer.OrdinalIgnoreCase);

        // PC Extended Home Feed: automatically pull 2 continuation pages so Home has 9–12+ shelves instead of ending after 1–2 scrolls!
        int maxAutoPages = string.IsNullOrEmpty(moodParams) ? 2 : 1;
        for (int page = 0; page < maxAutoPages && !string.IsNullOrEmpty(result.Continuation); page++)
        {
            try
            {
                var enc = Uri.EscapeDataString(result.Continuation!);
                var nextRoot = await PostAsync(
                    "browse",
                    new JsonObject(),
                    queryExtra: $"ctoken={enc}&continuation={enc}&type=next",
                    ct: ct);
                var nextFeed = InnertubeParsers.ParseHome(nextRoot);
                result.Continuation = nextFeed.Continuation;
                if (nextFeed.Shelves.Count == 0) break;

                foreach (var s in nextFeed.Shelves)
                {
                    if (string.IsNullOrWhiteSpace(s.Title) || seenTitles.Add(s.Title))
                        result.Shelves.Add(s);
                }
            }
            catch
            {
                break;
            }
        }

        // Enrich default PC Home with Charts & New Release Albums if still under 9 shelves (matches OG Echo YouTube.explore + HomeViewModel)
        if (string.IsNullOrEmpty(moodParams) && result.Shelves.Count < 9)
        {
            try
            {
                var chartsTask = GetTopChartShelvesAsync(ct);
                var newRelTask = PostAsync("browse", new JsonObject { ["browseId"] = "FEmusic_new_releases_albums" }, ct: ct);
                await Task.WhenAll(chartsTask, newRelTask);
                foreach (var s in chartsTask.Result.Take(3))
                {
                    if (!string.IsNullOrWhiteSpace(s.Title) && seenTitles.Add(s.Title))
                        result.Shelves.Add(s);
                }
                foreach (var s in InnertubeParsers.ParseShelves(newRelTask.Result).Take(2))
                {
                    if (string.IsNullOrWhiteSpace(s.Title)) s.Title = "New Release Albums";
                    if (seenTitles.Add(s.Title))
                        result.Shelves.Add(s);
                }
            }
            catch { /* non-fatal enrichment */ }
        }
        return result;
    }

    public async Task<List<string>> SuggestAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        try
        {
            var root = await PostAsync("music/get_search_suggestions", new JsonObject { ["input"] = query }, ct: ct);
            return InnertubeParsers.ParseSuggestions(root);
        }
        catch
        {
            return new();
        }
    }

    public async Task<SearchPageResult> SearchAsync(string query, string filter = "all", string? continuation = null, CancellationToken ct = default)
    {
        var body = new JsonObject();
        string? extra = null;
        if (!string.IsNullOrEmpty(continuation))
        {
            var enc = Uri.EscapeDataString(continuation);
            extra = $"continuation={enc}&ctoken={enc}";
        }
        else
        {
            body["query"] = query;
            if (!string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase) &&
                YouTubeClients.SearchFilterParams.TryGetValue(filter, out var p))
            {
                body["params"] = Uri.UnescapeDataString(p);
            }
        }
        var root = await PostAsync("search", body, queryExtra: extra, ct: ct);
        return InnertubeParsers.ParseSearch(root);
    }

    public async Task<DetailPageResult> GetAlbumAsync(string browseId, CancellationToken ct = default)
    {
        var id = browseId.StartsWith("MPRE", StringComparison.Ordinal) ? browseId : $"MPREb_{browseId}";
        var root = await PostAsync("browse", new JsonObject { ["browseId"] = id }, ct: ct);
        var page = InnertubeParsers.ParseAlbumPage(root);
        page.Id = id;
        return page;
    }

    public async Task<DetailPageResult> GetPlaylistAsync(string playlistOrBrowseId, CancellationToken ct = default)
    {
        var id = playlistOrBrowseId.StartsWith("VL", StringComparison.Ordinal) ? playlistOrBrowseId : $"VL{playlistOrBrowseId}";
        var root = await PostAsync("browse", new JsonObject { ["browseId"] = id }, ct: ct);
        var page = InnertubeParsers.ParsePlaylistPage(root);
        page.Id = id;

        // Follow 1 continuation page automatically on PC so large playlists (>100 tracks) display rich tracklists
        if (!string.IsNullOrEmpty(page.Continuation))
        {
            try
            {
                var enc = Uri.EscapeDataString(page.Continuation!);
                var contRoot = await PostAsync("browse", new JsonObject(), queryExtra: $"ctoken={enc}&continuation={enc}&type=next", ct: ct);
                var more = InnertubeParsers.ParsePlaylistPage(contRoot);
                var existingIds = new HashSet<string>(page.Tracks.Select(t => t.Id), StringComparer.Ordinal);
                foreach (var tr in more.Tracks)
                    if (existingIds.Add(tr.Id)) page.Tracks.Add(tr);
                page.Continuation = more.Continuation;
            }
            catch { }
        }
        return page;
    }

    public async Task<DetailPageResult> GetPlaylistContinuationAsync(string continuation, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(continuation);
        var root = await PostAsync("browse", new JsonObject(),
            queryExtra: $"ctoken={encoded}&continuation={encoded}&type=next", ct: ct);
        return InnertubeParsers.ParsePlaylistPage(root);
    }

    public async Task<DetailPageResult> GetBrowseContinuationAsync(string continuation, string browseId, string title, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(continuation);
        var root = await PostAsync("browse", new JsonObject(),
            queryExtra: $"ctoken={encoded}&continuation={encoded}&type=next", ct: ct);
        var parsed = InnertubeParsers.ParseSearch(root);
        var shelves = InnertubeParsers.ParseShelves(root);
        return new DetailPageResult
        {
            Id = browseId,
            Kind = "browse",
            Title = title,
            Tracks = parsed.Tracks.Concat(parsed.Videos).DistinctBy(t => t.Id).ToList(),
            RelatedShelves = shelves,
            Continuation = InnertubeParsers.FindSectionContinuation(root),
        };
    }

    public async Task<DetailPageResult> GetArtistAsync(string channelId, CancellationToken ct = default)
    {
        var root = await PostAsync("browse", new JsonObject { ["browseId"] = channelId }, ct: ct);
        var page = InnertubeParsers.ParseArtistPage(root);
        page.Id = channelId;

        // Enrich Artist PC page with up to 24 top songs via the artist's Songs browseId (VL...) just like OG Echo ArtistSongsScreen!
        if (page.Tracks.Count < 12 && !string.IsNullOrEmpty(page.Continuation) && page.Continuation.StartsWith("VL", StringComparison.Ordinal))
        {
            try
            {
                var songsPl = await GetPlaylistAsync(page.Continuation, ct);
                var seen = new HashSet<string>(page.Tracks.Select(t => t.Id), StringComparer.Ordinal);
                foreach (var tr in songsPl.Tracks)
                {
                    if (page.Tracks.Count >= 24) break;
                    if (seen.Add(tr.Id)) page.Tracks.Add(tr);
                }
            }
            catch { }
        }
        return page;
    }

    public async Task<List<ShelfSection>> GetMoodAndGenresAsync(CancellationToken ct = default)
    {
        var root = await PostAsync("browse", new JsonObject { ["browseId"] = "FEmusic_moods_and_genres" }, ct: ct);
        return InnertubeParsers.ParseMoodGrid(root);
    }

    private async Task<List<ShelfSection>> GetTopChartShelvesAsync(CancellationToken ct)
    {
        var root = await PostAsync("browse", new JsonObject { ["browseId"] = "FEmusic_charts" }, ct: ct);
        var shelves = InnertubeParsers.ParseShelves(root);

        // The anonymous charts browse page exposes video playlists and artist cards, not the Top Songs playlist.
        // Fetch the actual country chart playlist separately so the Top Songs view is not silently limited to a short home shelf.
        if (TopSongChartPlaylists.TryGetValue(RegionGl, out var songsPlaylistId))
        {
            try
            {
                string chartBrowseId = "VL" + songsPlaylistId;
                var chartRoot = await PostAsync("browse", new JsonObject { ["browseId"] = chartBrowseId }, ct: ct);
                var songChart = InnertubeParsers.ParsePlaylistPage(chartRoot, allowVideoChartRows: true);
                songChart.Id = chartBrowseId;
                if (songChart.Tracks.Count > 0)
                    shelves.Insert(0, new ShelfSection { Title = "Top Songs Chart", Tracks = songChart.Tracks });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* Keep video/album/artist chart shelves if the country song playlist is temporarily unavailable. */ }
        }

        string? continuation = InnertubeParsers.FindSectionContinuation(root);
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        var seenTracks = new HashSet<string>(shelves.SelectMany(s => s.Tracks).Select(t => t.Id), StringComparer.Ordinal);

        // Charts frequently expose the long list through page continuation, not the first response.
        // Follow it until we have up to 100 distinct tracks or the service has no more pages.
        for (int page = 0; page < 12 && seenTracks.Count < 100 && !string.IsNullOrWhiteSpace(continuation); page++)
        {
            ct.ThrowIfCancellationRequested();
            if (!seenTokens.Add(continuation!)) break;
            try
            {
                string encoded = Uri.EscapeDataString(continuation!);
                var nextRoot = await PostAsync("browse", new JsonObject(),
                    queryExtra: $"ctoken={encoded}&continuation={encoded}&type=next", ct: ct);
                var nextShelves = InnertubeParsers.ParseShelves(nextRoot);
                if (nextShelves.Count == 0)
                {
                    var parsedContinuation = InnertubeParsers.ParseSearch(nextRoot);
                    var continuationTracks = parsedContinuation.Tracks
                        .Concat(parsedContinuation.Videos)
                        .DistinctBy(t => t.Id)
                        .Where(t => seenTracks.Add(t.Id))
                        .ToList();
                    if (continuationTracks.Count == 0) break;
                    var continuationShelf = shelves.FirstOrDefault(s => s.Tracks.Count > 0);
                    if (continuationShelf is null)
                    {
                        continuationShelf = new ShelfSection { Title = "Top charts · more tracks" };
                        shelves.Add(continuationShelf);
                    }
                    continuationShelf.Tracks.AddRange(continuationTracks);
                    continuation = InnertubeParsers.FindSectionContinuation(nextRoot);
                    continue;
                }
                foreach (var next in nextShelves)
                {
                    var current = shelves.FirstOrDefault(s => s.Title.Equals(next.Title, StringComparison.OrdinalIgnoreCase));
                    if (current is null)
                    {
                        shelves.Add(next);
                        foreach (var track in next.Tracks) seenTracks.Add(track.Id);
                        continue;
                    }
                    foreach (var track in next.Tracks)
                        if (seenTracks.Add(track.Id)) current.Tracks.Add(track);
                    var cardIds = new HashSet<string>(current.Cards.Select(c => c.Id), StringComparer.Ordinal);
                    foreach (var card in next.Cards)
                        if (cardIds.Add(card.Id)) current.Cards.Add(card);
                }
                continuation = InnertubeParsers.FindSectionContinuation(nextRoot);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { break; }
        }
        return shelves;
    }

    public async Task<List<ShelfSection>> GetExploreAndChartsAsync(CancellationToken ct = default)
    {
        var shelves = new List<ShelfSection>();
        var chartsTask = GetTopChartShelvesAsync(ct);
        var moodsTask = PostAsync("browse", new JsonObject { ["browseId"] = "FEmusic_moods_and_genres" }, ct: ct);
        var newTask = PostAsync("browse", new JsonObject { ["browseId"] = "FEmusic_new_releases_albums" }, ct: ct);
        try { await Task.WhenAll(chartsTask, moodsTask, newTask); } catch { /* partial ok */ }

        if (chartsTask.IsCompletedSuccessfully)
            shelves.AddRange(chartsTask.Result);
        if (newTask.IsCompletedSuccessfully)
        {
            foreach (var s in InnertubeParsers.ParseShelves(newTask.Result))
            {
                if (string.IsNullOrWhiteSpace(s.Title)) s.Title = "New Releases";
                shelves.Add(s);
            }
        }
        if (moodsTask.IsCompletedSuccessfully)
            shelves.AddRange(InnertubeParsers.ParseMoodGrid(moodsTask.Result));

        return shelves;
    }

    public async Task<DetailPageResult> BrowseGenericAsync(string browseId, string? browseParams = null, string? titleHint = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["browseId"] = browseId };
        if (!string.IsNullOrEmpty(browseParams)) body["params"] = browseParams;
        JsonNode? root;
        try
        {
            root = await PostAsync("browse", body, ct: ct);
        }
        catch (HttpRequestException ex) when (!string.IsNullOrWhiteSpace(browseParams) && ex.StatusCode == HttpStatusCode.BadRequest)
        {
            // Some shelf endpoints become stale while their browseId remains valid; retry the standard
            // browse form without the cached params rather than surfacing a hard 400 immediately.
            root = await PostAsync("browse", new JsonObject { ["browseId"] = browseId }, ct: ct);
        }
        var searchLike = InnertubeParsers.ParseSearch(root);
        var shelves = InnertubeParsers.ParseShelves(root);
        shelves.AddRange(InnertubeParsers.ParseMoodGrid(root));

        // Browse pages often return collection tiles in search-like/grid renderers instead of
        // carousel shelves. Preserve these cards so "Show all" pages cannot render as title-only.
        var cards = searchLike.Playlists.Concat(searchLike.Albums).Concat(searchLike.Artists).ToList();
        if (searchLike.TopResultCard is { } topCard) cards.Insert(0, topCard);
        cards = cards
            .Where(c => !string.IsNullOrWhiteSpace(c.Id) || !string.IsNullOrWhiteSpace(c.Title))
            .DistinctBy(c => string.IsNullOrWhiteSpace(c.Id) ? c.Title : c.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (cards.Count > 0)
        {
            shelves.Insert(0, new ShelfSection
            {
                Title = titleHint ?? "Featured playlists",
                Cards = cards,
            });
        }

        return new DetailPageResult
        {
            Id = browseId,
            Kind = "browse",
            Title = titleHint ?? InnertubeParsers.ExtractAnyHeaderTitle(root) ?? "Browse",
            Tracks = searchLike.Tracks.Concat(searchLike.Videos).DistinctBy(t => t.Id).ToList(),
            RelatedShelves = shelves,
            Continuation = InnertubeParsers.FindSectionContinuation(root),
        };
    }

    public async Task<List<TrackItem>> GetRadioQueueAsync(string videoId, string? playlistId = null, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["videoId"] = videoId,
            ["playlistId"] = playlistId ?? $"RDAMVM{videoId}",
            ["params"] = "wAEB",
        };
        var root = await PostAsync("next", body, ct: ct);
        return InnertubeParsers.ParseRadioQueue(root);
    }

    public async Task<(string SourceVideoId, List<LyricTimingSegment> Segments)?> GetCounterpartTimingMapAsync(
        string videoId, string? playlistId = null, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["videoId"] = videoId,
            ["playlistId"] = string.IsNullOrWhiteSpace(playlistId) ? $"RDAMVM{videoId}" : playlistId,
            ["params"] = "wAEB",
        };
        try
        {
            var root = await PostAsync("next", body, ct: ct);
            return InnertubeParsers.ParseCounterpartTimingMap(root, videoId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    public async Task<JsonNode?> GetPlayerRawAsync(string videoId, ClientSpec client, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["videoId"] = videoId,
            ["contentCheckOk"] = true,
            ["racyCheckOk"] = true,
        };
        return await PostAsync("player", body, client: client, allowLogin: client.LoginSupported, ct: ct);
    }

    public async Task<LyricsData?> GetMusicLyricsAsync(string videoId, CancellationToken ct = default)
    {
        // Echo Mobile resolves YouTube.next(WatchEndpoint(videoId)).lyricsEndpoint directly.
        // Web Remix can expose the same official endpoint at top-level or under a watch-next tab;
        // accept both shapes and recursively inspect the response for schema changes.
        var next = await PostAsync("next", new JsonObject { ["videoId"] = videoId }, ct: ct);
        var browse = FindLyricsBrowseEndpoint(next);
        if (browse is null)
        {
            var tabs = next?["contents"]?["singleColumnMusicWatchNextResultsRenderer"]?["tabbedRenderer"]?["watchNextTabbedResultsRenderer"]?["tabs"] as JsonArray;
            browse = tabs?.Skip(1).Select(x => x?["tabRenderer"]?["endpoint"]?["browseEndpoint"])
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x?["browseId"]?.GetValue<string>()));
        }
        var browseId = browse?["browseId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(browseId)) return null;
        var body = new JsonObject { ["browseId"] = browseId };
        var browseParams = browse?["params"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(browseParams)) body["params"] = browseParams;
        var root = await PostAsync("browse", body, ct: ct);
        return InnertubeParsers.ParseMusicLyricsPage(root, videoId);
    }

    private static JsonNode? FindLyricsBrowseEndpoint(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("lyricsEndpoint", out var endpoint) && endpoint is not null)
            {
                if (endpoint["browseEndpoint"] is JsonNode browseEndpoint) return browseEndpoint;
                if (!string.IsNullOrWhiteSpace(endpoint["browseId"]?.GetValue<string>())) return endpoint;
            }
            foreach (var pair in obj)
            {
                var found = FindLyricsBrowseEndpoint(pair.Value);
                if (found is not null) return found;
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var found = FindLyricsBrowseEndpoint(item);
                if (found is not null) return found;
            }
        }
        return null;
    }

    public async Task<List<LyricLine>> GetTranscriptAsync(string videoId, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["videoId"] = videoId,
            ["params"] = "engagement-panel-searchable-transcript-search-panel"
        };
        var root = await PostAsync("get_transcript", body, ct: ct);
        return InnertubeParsers.ParseTranscript(root);
    }

    public async Task<(string Name, string Handle)?> VerifyAccountAsync(CancellationToken ct = default)
    {
        var root = await PostAsync("account/account_menu", new JsonObject(), allowLogin: true, ct: ct);
        return InnertubeParsers.ParseAccountInfo(root);
    }

    public async Task RateSongOnCloudAsync(string videoId, bool liked, CancellationToken ct = default)
    {
        if (!IsSignedIn) return;
        var ep = liked ? "like/like" : "like/removelike";
        await PostAsync(ep, new JsonObject { ["target"] = new JsonObject { ["videoId"] = videoId } }, allowLogin: true, ct: ct);
    }
}
