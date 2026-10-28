using System.Collections.Concurrent;
using System.Net;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EchoMusic.Core;

public sealed class MusicDnaAndExtras
{
    private readonly InnerTubeClient _yt;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, DnaInfo> _dnaCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SpotifyPublicProfile> _spotifyProfileCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (string Name, string Owner, List<TrackItem> Tracks)> _spotifyImportCache = new(StringComparer.OrdinalIgnoreCase);

    public MusicDnaAndExtras(InnerTubeClient yt)
    {
        _yt = yt;
        _http = yt.SharedHttp;
    }

    // --------------------------------------------------------- 1. Music DNA (Song Backstory & Artist Lore)

    public async Task<DnaInfo> GetMusicDnaAsync(TrackItem track, CancellationToken ct = default)
    {
        if (_dnaCache.TryGetValue(track.Id, out var cached)) return cached;

        var cleanTitle = Regex.Replace(track.Title, @"\s*[\(\[].*?[\)\]]", "").Trim();
        var primaryArtist = track.Artists.FirstOrDefault()?.Name ?? "";

        var dna = new DnaInfo
        {
            VideoId = track.Id,
            TrackHeadline = $"{track.Title} - {track.ArtistNames}",
        };
        if (track.Album is not null) dna.Tags.Add($"Album: {track.Album.Name}");
        if (track.DurationSeconds > 0) dna.Tags.Add($"Length: {TrackItem.FormatSeconds(track.DurationSeconds)}");
        if (track.Explicit) dna.Tags.Add("Explicit");

        try
        {
            // Query Wikipedia for song article + artist biography in parallel
            var songQuery = string.IsNullOrEmpty(primaryArtist) ? $"{cleanTitle} song" : $"\"{cleanTitle}\" \"{primaryArtist}\" song";
            var songTask = SearchWikiExtractAsync(songQuery, cleanTitle, ct);
            var artistTask = !string.IsNullOrEmpty(primaryArtist)
                ? SearchWikiExtractAsync($"{primaryArtist} musician", primaryArtist, ct)
                : Task.FromResult<(string Extract, string Url)?>(null);

            await Task.WhenAll(songTask, artistTask);

            if (songTask.Result is { } sHit && !string.IsNullOrWhiteSpace(sHit.Extract))
            {
                dna.TrackStory = sHit.Extract;
                dna.SourceUrl = sHit.Url;
                dna.Tags.Add("Verified Song Lore");
            }
            else
            {
                dna.TrackStory = $"\"{track.Title}\" is performed by {track.ArtistNames}" +
                                 (track.Album is not null ? $" from {track.Album.Name}." : ".");
            }

            if (artistTask.Result is { } aHit && !string.IsNullOrWhiteSpace(aHit.Extract))
            {
                dna.ArtistBio = aHit.Extract;
                if (string.IsNullOrEmpty(dna.SourceUrl)) dna.SourceUrl = aHit.Url;
            }
        }
        catch
        {
            dna.TrackStory = $"\"{track.Title}\" by {track.ArtistNames}.";
        }

        _dnaCache[track.Id] = dna;
        return dna;
    }

    private async Task<(string Extract, string Url)?> SearchWikiExtractAsync(string query, string mustMention, CancellationToken ct)
    {
        var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&srlimit=3&format=json";
        using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        req.Headers.TryAddWithoutValidation("User-Agent", "EchoMusicDesktop/2.0 (https://github.com/EchoMusicApp/Echo-Music)");
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        var root = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        var results = root?["query"]?["search"] as JsonArray;
        if (results is null || results.Count == 0) return null;

        string? pageTitle = null;
        foreach (var r in results)
        {
            var t = r?["title"]?.GetValue<string>() ?? "";
            var snip = WebUtility.HtmlDecode(Regex.Replace(r?["snippet"]?.GetValue<string>() ?? "", "<.*?>", ""));
            if (t.Contains(mustMention, StringComparison.OrdinalIgnoreCase) || snip.Contains(mustMention, StringComparison.OrdinalIgnoreCase))
            {
                pageTitle = t;
                break;
            }
        }
        pageTitle ??= results[0]?["title"]?.GetValue<string>();
        if (string.IsNullOrEmpty(pageTitle)) return null;

        var sumUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(pageTitle.Replace(' ', '_'))}";
        using var req2 = new HttpRequestMessage(HttpMethod.Get, sumUrl);
        req2.Headers.TryAddWithoutValidation("User-Agent", "EchoMusicDesktop/2.0 (https://github.com/EchoMusicApp/Echo-Music)");
        using var res2 = await _http.SendAsync(req2, ct);
        if (!res2.IsSuccessStatusCode) return null;

        var sumJson = JsonNode.Parse(await res2.Content.ReadAsStringAsync(ct));
        var extract = sumJson?["extract"]?.GetValue<string>() ?? "";
        var pageUrl = sumJson?["content_urls"]?["desktop"]?["page"]?.GetValue<string>() ?? "";
        return string.IsNullOrWhiteSpace(extract) ? null : (extract, pageUrl);
    }

    // --------------------------------------------------------- 2. Echo Brain (On-Device Momentum Recommendation Engine)

    public async Task<ShelfSection?> BuildEchoBrainShelfAsync(AppStore store, CancellationToken ct = default)
    {
        // Score user's top affinity tracks/artists from local history + likes with recency decay & skip penalty
        var seeds = new List<(string VideoId, string Title, double Score)>();
        var likedIds = store.LikedSongs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var group in store.History.GroupBy(h => h.VideoId))
        {
            var sample = group.First();
            double score = 0;
            foreach (var h in group)
            {
                double daysAgo = Math.Max(0, (now - h.PlayedAt) / 86_400_000.0);
                double recency = Math.Exp(-daysAgo / 14.0);
                score += h.WasSkipped ? -0.8 * recency : (1.5 + Math.Min(2.0, h.MsPlayed / 120_000.0)) * recency;
            }
            if (likedIds.Contains(group.Key)) score += 4.0;
            if (score > 0.5 && !group.Key.StartsWith("local:", StringComparison.Ordinal))
                seeds.Add((group.Key, sample.Title, score));
        }

        foreach (var liked in store.LikedSongs.Where(l => !l.IsLocal && seeds.All(s => s.VideoId != l.Id)).Take(5))
            seeds.Add((liked.Id, liked.Title, 3.5));

        if (seeds.Count == 0) return null;

        var topSeeds = seeds.OrderByDescending(s => s.Score).Take(2).ToList();
        var recs = new List<TrackItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var seed in topSeeds)
        {
            try
            {
                var radio = await _yt.GetRadioQueueAsync(seed.VideoId, ct: ct);
                foreach (var r in radio)
                {
                    if (seen.Add(r.Id)) recs.Add(r);
                    if (recs.Count >= 16) break;
                }
            }
            catch { }
        }

        if (recs.Count == 0) return null;
        return new ShelfSection
        {
            Title = "Echo Brain • Made For You",
            Tracks = recs,
        };
    }

    // --------------------------------------------------------- Public Spotify profile browsing (no sign-in)

    public async Task<SpotifyPublicProfile> GetPublicSpotifyProfileAsync(string profileUrl, Action<string, double>? onProgress = null, CancellationToken ct = default)
    {
        var match = Regex.Match(profileUrl.Trim(), @"spotify\.com/(?:intl-[a-z-]+/)?user/([A-Za-z0-9._-]+)", RegexOptions.IgnoreCase);
        if (!match.Success) throw new ArgumentException("Paste a public Spotify profile URL, for example https://open.spotify.com/user/username");
        string id = match.Groups[1].Value;
        if (_spotifyProfileCache.TryGetValue(id, out var cached))
        {
            onProgress?.Invoke("Profile loaded from cache", 1);
            return cached;
        }

        onProgress?.Invoke("Connecting to Spotify public profile", 0.08);
        // Reuse the configured client used by fast playlist imports. A separate default HttpClient
        // bypassed its connection pool and any platform proxy settings.
        var endpoints = new[]
        {
            ($"https://open.spotify.com/user/{Uri.EscapeDataString(id)}", "Mozilla/5.0"),
            ($"https://open.spotify.com/user/{Uri.EscapeDataString(id)}/playlists", "Mozilla/5.0"),
            ($"https://open.spotify.com/embed/user/{Uri.EscapeDataString(id)}", "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 Chrome/133.0.0.0 Mobile Safari/537.36"),
        };
        var fetches = endpoints.Select(async endpoint =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint.Item1);
                req.Headers.TryAddWithoutValidation("User-Agent", endpoint.Item2);
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
                req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!res.IsSuccessStatusCode) return (Url: endpoint.Item1, Html: "");
                return (Url: endpoint.Item1, Html: await res.Content.ReadAsStringAsync(timeout.Token));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return (Url: endpoint.Item1, Html: ""); }
        }).ToArray();
        var pages = await Task.WhenAll(fetches);
        ct.ThrowIfCancellationRequested();
        onProgress?.Invoke("Reading public profile and playlist links", 0.72);

        string html = pages.OrderByDescending(page => CountSpotifyPlaylistIds(page.Html))
            .FirstOrDefault(page => !string.IsNullOrWhiteSpace(page.Html)).Html ?? "";
        if (string.IsNullOrWhiteSpace(html))
            throw new HttpRequestException("Spotify did not return a public profile page. Check the link and try again.");
        html = html.Replace("\\/", "/", StringComparison.Ordinal);
        string title = ReadMetaContent(html, "og:title");
        if (string.IsNullOrWhiteSpace(title))
        {
            var titleTag = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            title = titleTag.Success ? WebUtility.HtmlDecode(titleTag.Groups[1].Value).Replace(" on Spotify", "", StringComparison.OrdinalIgnoreCase).Trim() : "Spotify Profile";
        }
        title = Regex.Replace(title, @"\s*[|·]\s*Spotify\s*$", "", RegexOptions.IgnoreCase).Trim();
        string image = ReadMetaContent(html, "og:image");
        var playlists = new List<SpotifyPublicPlaylist>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var anchors = Regex.Matches(html,
            "<a\\b[^>]*href\\s*=\\s*[\\\"'](?<href>[^\\\"']*(?:open\\.spotify\\.com)?/playlist/(?<id>[A-Za-z0-9]+)[^\\\"']*)[\\\"'][^>]*>(?<body>.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match anchor in anchors)
        {
            string playlistId = anchor.Groups["id"].Value;
            if (!seen.Add(playlistId)) continue;
            string body = anchor.Groups["body"].Value;
            string name = "";
            foreach (Match textMatch in Regex.Matches(body, @"<span\b[^>]*>(?<text>.*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var clean = WebUtility.HtmlDecode(Regex.Replace(textMatch.Groups["text"].Value, "<[^>]+>", " "));
                clean = Regex.Replace(clean, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(clean)) { name = clean; break; }
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]+>", " "));
                name = Regex.Replace(name, @"\b\d+\s+likes?\b", "", RegexOptions.IgnoreCase);
                name = Regex.Replace(name, @"\s+", " ").Trim();
            }
            var imageMatch = Regex.Match(body, "<img\\b[^>]*src\\s*=\\s*[\\\"'](?<src>[^\\\"']+)", RegexOptions.IgnoreCase);
            playlists.Add(new SpotifyPublicPlaylist(playlistId,
                string.IsNullOrWhiteSpace(name) ? "Spotify Playlist" : name,
                imageMatch.Success ? WebUtility.HtmlDecode(imageMatch.Groups["src"].Value) : ""));
        }
        // Some profile responses contain playlist URLs only in embedded JSON, not anchor elements.
        foreach (Match link in Regex.Matches(html, @"(?:open\.spotify\.com)?/playlist/(?<id>[A-Za-z0-9]+)", RegexOptions.IgnoreCase))
        {
            string playlistId = link.Groups["id"].Value;
            if (seen.Add(playlistId)) playlists.Add(new SpotifyPublicPlaylist(playlistId, "Spotify Playlist", ""));
        }

        var profile = new SpotifyPublicProfile(id, string.IsNullOrWhiteSpace(title) ? "Spotify Profile" : title, image, playlists);
        _spotifyProfileCache[id] = profile;
        onProgress?.Invoke($"Found {playlists.Count} public playlist(s)", 1);
        return profile;
    }

    private static int CountSpotifyPlaylistIds(string html) =>
        Regex.Matches(html.Replace("\\/", "/", StringComparison.Ordinal), @"(?:open\.spotify\.com)?/playlist/[A-Za-z0-9]+", RegexOptions.IgnoreCase).Count;

    private static string ReadMetaContent(string html, string property)
    {
        var match = Regex.Match(html, $"<meta\\b(?=[^>]*(?:property|name)=[\"']{Regex.Escape(property)}[\"'])[^>]*content=[\"'](?<value>[^\"']*)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : "";
    }

    // --------------------------------------------------------- 3. Spotify Public URL Importer (Zero API Key!)

    private static long ReadSpotifyDurationMilliseconds(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in new[] { "duration_ms", "durationMs", "duration_milliseconds", "durationMilliseconds" })
            {
                if (obj.TryGetPropertyValue(key, out var value))
                {
                    long parsed = ParseDurationNode(value, millisecondsByDefault: true);
                    if (parsed > 0) return parsed;
                }
            }
            foreach (var key in new[] { "duration", "length" })
            {
                if (obj.TryGetPropertyValue(key, out var value))
                {
                    long parsed = ParseDurationNode(value, millisecondsByDefault: false);
                    if (parsed > 0) return parsed;
                }
            }
            if (obj.TryGetPropertyValue("track", out var nested) && nested is not null)
                return ReadSpotifyDurationMilliseconds(nested);
        }
        return 0;
    }

    private static long ParseDurationNode(JsonNode? value, bool millisecondsByDefault)
    {
        if (value is JsonObject obj)
        {
            foreach (var key in new[] { "totalMilliseconds", "milliseconds", "duration_ms", "durationMs" })
                if (obj.TryGetPropertyValue(key, out var ms) && ms is not null)
                {
                    long n = ParseDurationNode(ms, true);
                    if (n > 0) return n;
                }
            foreach (var key in new[] { "total_seconds", "seconds", "duration" })
                if (obj.TryGetPropertyValue(key, out var sec) && sec is not null)
                {
                    long n = ParseDurationNode(sec, false);
                    if (n > 0) return n;
                }
            return 0;
        }
        if (value is null) return 0;
        string raw = value.ToString().Trim();
        if (raw.Contains(':'))
        {
            var parts = raw.Split(':');
            if (parts.Length is 2 or 3 && parts.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                double seconds = 0;
                foreach (var part in parts) seconds = seconds * 60 + double.Parse(part, CultureInfo.InvariantCulture);
                return (long)Math.Round(seconds * 1000.0);
            }
        }
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) || numeric <= 0) return 0;
        if (millisecondsByDefault || numeric >= 10000) return (long)Math.Round(numeric);
        return (long)Math.Round(numeric * 1000.0);
    }

    public async Task<(string Name, string Owner, List<TrackItem> MatchedTracks)> ImportSpotifyUrlAsync(
        string spotifyUrl,
        Action<string, double>? onProgress = null,
        CancellationToken ct = default)
    {
        // Supports https://open.spotify.com/playlist/ID, /album/ID, /track/ID
        var m = Regex.Match(spotifyUrl.Trim(), @"spotify\.com/(?:intl-[a-z-]+/)?(playlist|album|track)/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            throw new ArgumentException("Paste a valid Spotify playlist, album, or track link (https://open.spotify.com/playlist/...).");

        var kind = m.Groups[1].Value.ToLowerInvariant();
        var id = m.Groups[2].Value;
        string cacheKey = kind + ":" + id;
        if (_spotifyImportCache.TryGetValue(cacheKey, out var cachedImport))
        {
            onProgress?.Invoke($"Loaded {cachedImport.Tracks.Count} matched tracks from cache", 1);
            return (cachedImport.Name, cachedImport.Owner, cachedImport.Tracks.Select(t => t.Clone()).ToList());
        }
        var embedUrl = $"https://open.spotify.com/embed/{kind}/{id}";

        onProgress?.Invoke("Fetching Spotify metadata…", 0.03);
        using var req = new HttpRequestMessage(HttpMethod.Get, embedUrl);
        req.Headers.TryAddWithoutValidation("User-Agent", YouTubeClients.WebUa);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var html = await res.Content.ReadAsStringAsync(ct);

        string collectionTitle = $"Spotify {char.ToUpper(kind[0]) + kind[1..]}";
        string collectionOwner = "";
        var rawEntries = new List<(string Title, string Artist, long DurationMs)>();

        // Parse __NEXT_DATA__ JSON inside Spotify's public embed page
        string? anonAccessToken = null;
        var nextMatch = Regex.Match(html, @"<script id=""__NEXT_DATA__""[^>]*>([\s\S]*?)</script>");
        if (nextMatch.Success)
        {
            var root = JsonNode.Parse(nextMatch.Groups[1].Value);
            anonAccessToken = root?["props"]?["pageProps"]?["state"]?["settings"]?["session"]?["accessToken"]?.GetValue<string>();
            var entity = root?["props"]?["pageProps"]?["state"]?["data"]?["entity"];
            if (entity is not null)
            {
                collectionTitle = entity["name"]?.GetValue<string>() ?? entity["title"]?.GetValue<string>() ?? collectionTitle;
                collectionOwner = entity["subtitle"]?.GetValue<string>() ?? "";
                if (entity["authors"] is JsonArray authors && authors.Count > 0)
                {
                    var firstAuthor = authors[0];
                    var authorName = firstAuthor is JsonValue av ? av.ToString() : firstAuthor?["name"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(authorName)) collectionOwner = authorName;
                }
                if (kind == "track")
                {
                    var tName = entity["name"]?.GetValue<string>() ?? entity["title"]?.GetValue<string>() ?? "";
                    var tArt = (entity["artists"] as JsonArray)?.FirstOrDefault()?["name"]?.GetValue<string>() ?? "";
                    if (!string.IsNullOrEmpty(tName)) rawEntries.Add((tName, tArt, ReadSpotifyDurationMilliseconds(entity)));
                }
                else if (entity["trackList"] is JsonArray trackList)
                {
                    foreach (var item in trackList)
                    {
                        var t = item?["title"]?.GetValue<string>() ?? "";
                        var sub = item?["subtitle"]?.GetValue<string>() ?? "";
                        if (!string.IsNullOrEmpty(t)) rawEntries.Add((WebUtility.HtmlDecode(t), WebUtility.HtmlDecode(sub), ReadSpotifyDurationMilliseconds(item)));
                    }
                }
            }
        }

        // If playlist/album has >100 tracks (or embed truncated at 100), paginate full Spotify Web API using session token!
        if (kind is "playlist" or "album" && !string.IsNullOrEmpty(anonAccessToken) && rawEntries.Count >= 95)
        {
            try
            {
                var fullList = new List<(string Title, string Artist, long DurationMs)>();
                int offset = 0;
                while (offset < 2000)
                {
                    var ep = kind == "playlist"
                        ? $"https://api.spotify.com/v1/playlists/{id}/tracks?offset={offset}&limit=100"
                        : $"https://api.spotify.com/v1/albums/{id}/tracks?offset={offset}&limit=50";
                    using var apiReq = new HttpRequestMessage(HttpMethod.Get, ep);
                    apiReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {anonAccessToken}");
                    apiReq.Headers.TryAddWithoutValidation("User-Agent", YouTubeClients.WebUa);
                    using var apiRes = await _http.SendAsync(apiReq, ct);
                    if (!apiRes.IsSuccessStatusCode) break;
                    var j = JsonNode.Parse(await apiRes.Content.ReadAsStringAsync(ct));
                    var items = j?["items"] as JsonArray;
                    if (items is null || items.Count == 0) break;
                    foreach (var it in items)
                    {
                        var trNode = kind == "playlist" ? it?["track"] : it;
                        var tName = trNode?["name"]?.GetValue<string>() ?? "";
                        var tArt = (trNode?["artists"] as JsonArray)?.FirstOrDefault()?["name"]?.GetValue<string>() ?? "";
                        if (!string.IsNullOrEmpty(tName)) fullList.Add((tName, tArt, ReadSpotifyDurationMilliseconds(trNode)));
                    }
                    int total = j?["total"]?.GetValue<int>() ?? 0;
                    offset += items.Count;
                    if (offset >= total) break;
                }
                if (fullList.Count > rawEntries.Count)
                    rawEntries = fullList;
            }
            catch { /* keep embed entries on rate-limit */ }
        }

        if (rawEntries.Count == 0)
            throw new InvalidOperationException("Could not extract tracks from that Spotify link (make sure the playlist is Public).");

        var matched = new List<TrackItem>();
        int done = 0;
        using var sem = new SemaphoreSlim(6, 6);
        var slots = new TrackItem?[rawEntries.Count];

        var tasks = rawEntries.Select(async (entry, idx) =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var q = $"{entry.Title} {entry.Artist}".Trim();
                var sr = await _yt.SearchAsync(q, "songs", ct: ct);
                slots[idx] = sr.Tracks.FirstOrDefault() ?? sr.Videos.FirstOrDefault();
            }
            catch { }
            finally
            {
                sem.Release();
                int cur = Interlocked.Increment(ref done);
                onProgress?.Invoke($"Matched {cur}/{rawEntries.Count}: {entry.Title}", 0.1 + 0.9 * cur / rawEntries.Count);
            }
        });
        await Task.WhenAll(tasks);

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not { } hit) continue;
            if (rawEntries[i].DurationMs > 0)
            {
                hit.SpotifyDurationMilliseconds = rawEntries[i].DurationMs;
                // Preserve Spotify's exact source length in playlist rows as well as totals;
                // the matched YouTube upload can be a few seconds longer or shorter.
                hit.DurationText = TrackItem.FormatSeconds(rawEntries[i].DurationMs / 1000.0);
            }
            matched.Add(hit);
        }

        if (collectionOwner.StartsWith("By ", StringComparison.OrdinalIgnoreCase)) collectionOwner = collectionOwner[3..].Trim();
        _spotifyImportCache[cacheKey] = (collectionTitle, collectionOwner, matched.Select(t => t.Clone()).ToList());
        return (collectionTitle, collectionOwner, matched);
    }

    public async Task<(string Name, List<TrackItem> MatchedTracks)> ImportPlaylistFileAsync(
        string filePath,
        Action<string, double>? onProgress = null,
        CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var entries = new List<(string Title, string Artist)>();
        bool isCsv = filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

        if (isCsv && lines.Length > 1)
        {
            var header = SplitCsvLine(lines[0]);
            int titleCol = header.FindIndex(h => h.Contains("Track Name", StringComparison.OrdinalIgnoreCase) || h.Equals("Title", StringComparison.OrdinalIgnoreCase));
            int artistCol = header.FindIndex(h => h.Contains("Artist", StringComparison.OrdinalIgnoreCase));
            if (titleCol < 0) titleCol = 1;
            if (artistCol < 0) artistCol = 3;

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = SplitCsvLine(lines[i]);
                if (titleCol < cols.Count && !string.IsNullOrWhiteSpace(cols[titleCol]))
                {
                    var art = artistCol < cols.Count ? cols[artistCol].Replace(';', ' ') : "";
                    entries.Add((cols[titleCol], art));
                }
            }
        }
        else // .m3u / .m3u8 / .txt
        {
            foreach (var raw in lines)
            {
                var l = raw.Trim();
                if (string.IsNullOrEmpty(l)) continue;
                if (l.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = l.IndexOf(',');
                    if (comma > 0 && comma + 1 < l.Length)
                        entries.Add((l[(comma + 1)..].Trim(), ""));
                }
                else if (!l.StartsWith('#') && !File.Exists(l))
                {
                    entries.Add((Path.GetFileNameWithoutExtension(l), ""));
                }
            }
        }

        if (entries.Count == 0)
            throw new InvalidOperationException("No song entries found in that CSV / M3U file.");

        var slots = new TrackItem?[entries.Count];
        int done = 0;
        using var sem = new SemaphoreSlim(5, 5);
        await Task.WhenAll(entries.Select(async (entry, idx) =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var q = $"{entry.Title} {entry.Artist}".Trim();
                var sr = await _yt.SearchAsync(q, "songs", ct: ct);
                slots[idx] = sr.Tracks.FirstOrDefault() ?? sr.Videos.FirstOrDefault();
            }
            catch { }
            finally
            {
                sem.Release();
                int cur = Interlocked.Increment(ref done);
                onProgress?.Invoke($"Matched {cur}/{entries.Count}: {entry.Title}", (double)cur / entries.Count);
            }
        }));

        return (Path.GetFileNameWithoutExtension(filePath), slots.Where(x => x is not null).Select(x => x!).ToList());
    }

    private static List<string> SplitCsvLine(string line)
    {
        var cols = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                cols.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else sb.Append(c);
        }
        cols.Add(sb.ToString().Trim());
        return cols;
    }

    // --------------------------------------------------------- 4. Odesli (song.link) Universal Cross-Platform Share

    public async Task<string> GetUniversalShareLinkAsync(TrackItem track, CancellationToken ct = default)
    {
        if (track.IsLocal) return track.LocalFilePath ?? track.Title;
        var ytUrl = $"https://music.youtube.com/watch?v={track.Id}";
        try
        {
            var api = $"https://api.song.link/v1-alpha.1/links?url={Uri.EscapeDataString(ytUrl)}";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            using var res = await _http.GetAsync(api, cts.Token);
            if (res.IsSuccessStatusCode)
            {
                var j = JsonNode.Parse(await res.Content.ReadAsStringAsync(cts.Token));
                var pageUrl = j?["pageUrl"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(pageUrl)) return pageUrl;
            }
        }
        catch { }
        return ytUrl;
    }

    // --------------------------------------------------------- 5. Local Music Folder Scanner (TagLibSharp)

    private static readonly HashSet<string> SupportedAudioExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".wav", ".ogg", ".opus", ".webm", ".wma", ".aac"
    };

    public static Task<List<TrackItem>> ScanLocalFoldersAsync(IEnumerable<string> folders, string artCacheDir, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(artCacheDir);
            var list = new List<TrackItem>();
            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => SupportedAudioExts.Contains(Path.GetExtension(f)));
                }
                catch { continue; }

                foreach (var file in files)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        string title = Path.GetFileNameWithoutExtension(file);
                        string artist = "Local Artist";
                        string? album = null;
                        int durSec = 0;
                        string thumb = "";

                        try
                        {
                            using var tfile = TagLib.File.Create(file);
                            if (!string.IsNullOrWhiteSpace(tfile.Tag.Title)) title = tfile.Tag.Title;
                            if (tfile.Tag.Performers is { Length: > 0 } p && !string.IsNullOrWhiteSpace(p[0]))
                                artist = string.Join(", ", p);
                            else if (!string.IsNullOrWhiteSpace(tfile.Tag.FirstAlbumArtist))
                                artist = tfile.Tag.FirstAlbumArtist;
                            if (!string.IsNullOrWhiteSpace(tfile.Tag.Album)) album = tfile.Tag.Album;
                            durSec = (int)Math.Round(tfile.Properties.Duration.TotalSeconds);

                            if (tfile.Tag.Pictures is { Length: > 0 } pics && pics[0].Data?.Data is { Length: > 0 } imgBytes)
                            {
                                var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(file)))[..14];
                                var artPath = Path.Combine(artCacheDir, $"local_{hash}.jpg");
                                if (!File.Exists(artPath)) File.WriteAllBytes(artPath, imgBytes);
                                thumb = artPath;
                            }
                        }
                        catch { /* untagged WAV/raw audio still playable */ }

                        var id = "local:" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(file)))[..16];
                        list.Add(new TrackItem
                        {
                            Id = id,
                            Title = title,
                            Artists = new List<ArtistRef> { new() { Name = artist } },
                            Album = album is null ? null : new AlbumRef { Name = album },
                            DurationSeconds = durSec,
                            DurationText = durSec > 0 ? TrackItem.FormatSeconds(durSec) : "",
                            Thumbnail = thumb,
                            LocalFilePath = file,
                        });
                    }
                    catch { }
                }
            }
            return list.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);
    }
}

public sealed record SpotifyPublicProfile(string Id, string Name, string ImageUrl, List<SpotifyPublicPlaylist> Playlists);
public sealed record SpotifyPublicPlaylist(string Id, string Name, string ImageUrl);
