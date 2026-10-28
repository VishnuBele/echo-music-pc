using System.Net;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EchoMusic.Core;

public static class InnertubeParsers
{
    private static readonly HashSet<string> TypeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "song", "video", "single", "album", "ep", "playlist", "artist", "podcast", "episode", "profile"
    };

    public static string Text(JsonNode? node)
    {
        if (node is null) return "";
        if (node is JsonValue v && v.TryGetValue<string>(out var s)) return Clean(s);
        if (node is JsonObject obj)
        {
            if (obj["simpleText"] is JsonValue st && st.TryGetValue<string>(out var sv)) return Clean(sv);
            if (obj["runs"] is JsonArray runs)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var r in runs)
                    if (r?["text"] is JsonValue rt && rt.TryGetValue<string>(out var rts))
                        sb.Append(rts);
                return Clean(sb.ToString());
            }
            if (obj["title"] is not null) return Text(obj["title"]);
        }
        return "";
    }

    private static string TextPreservingLineBreaks(JsonNode? node)
    {
        if (node is null) return "";
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return NormalizeLyricWhitespace(text);
        if (node is JsonObject obj)
        {
            if (obj["simpleText"] is JsonValue simple && simple.TryGetValue<string>(out var simpleText))
                return NormalizeLyricWhitespace(simpleText);
            if (obj["runs"] is JsonArray runs)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var run in runs)
                    if (run?["text"] is JsonValue runText && runText.TryGetValue<string>(out var valueText))
                        sb.Append(valueText);
                return NormalizeLyricWhitespace(sb.ToString());
            }
        }
        if (node is JsonArray array)
            return NormalizeLyricWhitespace(string.Join("\n", array.Select(TextPreservingLineBreaks)));
        return NormalizeLyricWhitespace(Text(node));
    }

    private static string NormalizeLyricWhitespace(string value)
    {
        var decoded = WebUtility.HtmlDecode(value).Replace('\u00A0', ' ').Replace("\r\n", "\n").Replace('\r', '\n');
        decoded = Regex.Replace(decoded, @"[\t ]+", " ");
        decoded = Regex.Replace(decoded, @" *\n *", "\n");
        return Regex.Replace(decoded, @"\n{3,}", "\n\n").Trim();
    }

    private static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var decoded = WebUtility.HtmlDecode(s).Replace('\u00A0', ' ');
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    public static string BestThumb(JsonNode? root, int targetPx = 544)
    {
        var urls = new List<(string Url, int Width)>();
        CollectThumbs(root, 0, urls);
        if (urls.Count == 0) return "";
        var best = urls.OrderBy(x => x.Width).Last().Url;
        return UpgradeThumb(best, targetPx);
    }

    private static void CollectThumbs(JsonNode? node, int depth, List<(string, int)> acc)
    {
        if (node is null || depth > 8) return;
        if (node is JsonObject obj)
        {
            if (obj["thumbnails"] is JsonArray arr)
            {
                foreach (var t in arr)
                {
                    var u = t?["url"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(u))
                    {
                        var w = t?["width"]?.GetValue<int>() ?? 0;
                        acc.Add((u, w));
                    }
                }
            }
            foreach (var kv in obj)
            {
                if (kv.Key == "thumbnails") continue;
                CollectThumbs(kv.Value, depth + 1, acc);
            }
        }
        else if (node is JsonArray list)
        {
            foreach (var child in list) CollectThumbs(child, depth + 1, acc);
        }
    }

    public static string UpgradeThumb(string url, int size = 544)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
        if (url.Contains("googleusercontent.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("ggpht.com", StringComparison.OrdinalIgnoreCase))
        {
            url = Regex.Replace(url, @"=w(\d+)-h(\d+)", m =>
            {
                var cur = Math.Max(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
                return cur >= size ? m.Value : $"=w{size}-h{size}";
            });
        }
        return url;
    }

    public sealed record EndpointTarget(string Kind, string Id, string? PlaylistId = null, string? Params = null, string? VideoType = null);

    public static EndpointTarget? FindBrowseEndpoint(JsonNode? node, int depth = 0)
    {
        if (node is null || depth > 10) return null;
        if (node is JsonObject obj)
        {
            if (obj["browseEndpoint"] is JsonObject browse)
            {
                var wrapper = new JsonObject { ["browseEndpoint"] = browse.DeepClone() };
                return FindEndpoint(wrapper, depth + 1);
            }
            if (obj["navigationEndpoint"] is not null)
            {
                var direct = FindBrowseEndpoint(obj["navigationEndpoint"], depth + 1);
                if (direct is not null) return direct;
            }
            foreach (var (key, value) in obj)
            {
                if (key is "watchEndpoint" or "playNavigationEndpoint" or "menu" or "badges") continue;
                var hit = FindBrowseEndpoint(value, depth + 1);
                if (hit is not null) return hit;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var hit = FindBrowseEndpoint(item, depth + 1);
                if (hit is not null) return hit;
            }
        }
        return null;
    }

    public static EndpointTarget? FindEndpoint(JsonNode? node, int depth = 0)
    {
        if (node is null || depth > 7) return null;
        if (node is JsonObject obj)
        {
            if (obj["watchEndpoint"] is JsonObject watch)
            {
                var vid = watch["videoId"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(vid))
                {
                    var vtype = watch["watchEndpointMusicSupportedConfigs"]?["watchEndpointMusicConfig"]?["musicVideoType"]?.GetValue<string>();
                    return new EndpointTarget(
                        "track",
                        vid,
                        watch["playlistId"]?.GetValue<string>(),
                        watch["params"]?.GetValue<string>(),
                        vtype
                    );
                }
            }
            if (obj["browseEndpoint"] is JsonObject browse)
            {
                var bid = browse["browseId"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(bid))
                {
                    var pt = browse["browseEndpointContextSupportedConfigs"]?["browseEndpointContextMusicConfig"]?["pageType"]?.GetValue<string>() ?? "";
                    if (pt is "MUSIC_PAGE_TYPE_NON_MUSIC_AUDIO_TRACK_PAGE" or "MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE"
                        || bid.StartsWith("MPED", StringComparison.Ordinal)
                        || bid.StartsWith("MPSP", StringComparison.Ordinal))
                    {
                        return null;
                    }
                    var kind = pt switch
                    {
                        "MUSIC_PAGE_TYPE_ALBUM" => "album",
                        "MUSIC_PAGE_TYPE_ARTIST" or "MUSIC_PAGE_TYPE_LIBRARY_ARTIST" => "artist",
                        "MUSIC_PAGE_TYPE_USER_CHANNEL" => "profile",
                        "MUSIC_PAGE_TYPE_PLAYLIST" => "playlist",
                        _ when bid.StartsWith("MPRE", StringComparison.Ordinal) => "album",
                        _ when bid.StartsWith("VL", StringComparison.Ordinal) || bid.StartsWith("PL", StringComparison.Ordinal) => "playlist",
                        _ when bid.StartsWith("UC", StringComparison.Ordinal) => "artist",
                        _ => "browse"
                    };
                    return new EndpointTarget(kind, bid, Params: browse["params"]?.GetValue<string>());
                }
            }
            // Check navigationEndpoint first before recursing arbitrarily
            if (obj["navigationEndpoint"] is not null)
            {
                var direct = FindEndpoint(obj["navigationEndpoint"], depth + 1);
                if (direct is not null) return direct;
            }
            if (obj["playNavigationEndpoint"] is not null)
            {
                var direct = FindEndpoint(obj["playNavigationEndpoint"], depth + 1);
                if (direct is not null) return direct;
            }
            foreach (var kv in obj)
            {
                if (kv.Key is "menu" or "badges") continue;
                var hit = FindEndpoint(kv.Value, depth + 1);
                if (hit is not null) return hit;
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var hit = FindEndpoint(item, depth + 1);
                if (hit is not null) return hit;
            }
        }
        return null;
    }

    public static int ParseDurationSeconds(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var m = Regex.Match(text, @"\b(\d{1,2}):(\d{2})(?::(\d{2}))?\b");
        if (!m.Success) return 0;
        return m.Groups[3].Success
            ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value)
            : int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
    }

    private static List<(string Name, EndpointTarget? Target)> SplitRuns(JsonNode? textNode)
    {
        var result = new List<(string, EndpointTarget?)>();
        if (textNode is not JsonObject obj || obj["runs"] is not JsonArray runs)
        {
            var plain = Text(textNode);
            if (!string.IsNullOrEmpty(plain)) result.Add((plain, null));
            return result;
        }
        var sb = new System.Text.StringBuilder();
        EndpointTarget? curTarget = null;
        void Flush()
        {
            var t = Clean(sb.ToString());
            if (!string.IsNullOrEmpty(t)) result.Add((t, curTarget));
            sb.Clear();
            curTarget = null;
        }
        foreach (var r in runs)
        {
            var s = r?["text"]?.GetValue<string>() ?? "";
            if (Regex.IsMatch(s, @"^\s*[•·]\s*$"))
            {
                Flush();
                continue;
            }
            sb.Append(s);
            if (curTarget is null && r?["navigationEndpoint"] is not null)
                curTarget = FindEndpoint(r["navigationEndpoint"]);
        }
        Flush();
        return result;
    }

    public static TrackItem? ParseResponsiveTrack(JsonObject? item, bool allowVideoChartRows = false)
    {
        if (item is null) return null;
        var cols = item["flexColumns"] as JsonArray;
        var col0 = cols is { Count: > 0 } ? cols[0]?["musicResponsiveListItemFlexColumnRenderer"]?["text"] : null;
        var col1 = cols is { Count: > 1 } ? cols[1]?["musicResponsiveListItemFlexColumnRenderer"]?["text"] : null;
        var col2 = cols is { Count: > 2 } ? cols[2]?["musicResponsiveListItemFlexColumnRenderer"]?["text"] : null;

        var title = Text(col0);
        if (string.IsNullOrEmpty(title)) return null;

        EndpointTarget? ep = FindEndpoint(col0);
        if (ep is null || ep.Kind != "track")
        {
            var overEp = FindEndpoint(item["overlay"]);
            if (overEp?.Kind == "track") ep = overEp;
        }
        if (ep is null || ep.Kind != "track")
        {
            var vid = item["playlistItemData"]?["videoId"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(vid)) ep = new EndpointTarget("track", vid);
        }
        if (ep is null || ep.Kind != "track" || string.IsNullOrEmpty(ep.Id)) return null;

        var fixedCols = item["fixedColumns"] as JsonArray;
        var fixedText = fixedCols is { Count: > 0 }
            ? Text(fixedCols[0]?["musicResponsiveListItemFixedColumnRenderer"]?["text"])
            : "";

        var parts = SplitRuns(col1);
        // Strictly exclude Podcast / Episode / Profile non-music rows (matches OG Echo APK SearchSummaryPage.kt)
        if (parts.Any(p => p.Name.Equals("Episode", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("Podcast", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Equals("Profile", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        if ((!allowVideoChartRows && item.ToJsonString().Contains("MUSIC_PAGE_TYPE_NON_MUSIC_AUDIO_TRACK_PAGE", StringComparison.Ordinal))
            || item.ToJsonString().Contains("MUSIC_PAGE_TYPE_PODCAST_SHOW_DETAIL_PAGE", StringComparison.Ordinal))
        {
            return null;
        }

        var artists = new List<ArtistRef>();
        AlbumRef? album = null;
        int duration = ParseDurationSeconds(fixedText);
        bool isVideo = ep.VideoType is "MUSIC_VIDEO_TYPE_OMV" or "MUSIC_VIDEO_TYPE_UGC";

        foreach (var (name, target) in parts)
        {
            if (TypeWords.Contains(name))
            {
                if (name.Equals("video", StringComparison.OrdinalIgnoreCase)) isVideo = true;
                continue;
            }
            if (Regex.IsMatch(name, @"^\d{1,2}:\d{2}(:\d{2})?$"))
            {
                if (duration == 0) duration = ParseDurationSeconds(name);
                continue;
            }
            if (Regex.IsMatch(name, @"^(19|20)\d{2}$") || name.Contains("views", StringComparison.OrdinalIgnoreCase) || name.Contains("plays", StringComparison.OrdinalIgnoreCase))
                continue;

            if (target?.Kind == "album" || (target?.Id?.StartsWith("MPRE", StringComparison.Ordinal) ?? false))
            {
                album = new AlbumRef { Name = name, Id = target.Id };
            }
            else
            {
                artists.Add(new ArtistRef { Name = name, Id = target?.Kind == "artist" ? target.Id : null });
            }
        }

        // Fallback artist extraction from menu if subtitle omitted artist
        if (artists.Count == 0 && item["menu"]?["menuRenderer"]?["items"] is JsonArray menuItems)
        {
            foreach (var mi in menuItems)
            {
                var nav = mi?["menuNavigationItemRenderer"];
                var mEp = FindEndpoint(nav?["navigationEndpoint"]);
                if (mEp?.Kind == "artist")
                {
                    var label = Text(nav?["text"]);
                    if (!label.Contains("artist", StringComparison.OrdinalIgnoreCase))
                        artists.Add(new ArtistRef { Name = label, Id = mEp.Id });
                }
            }
        }

        var col2Text = Text(col2);
        string? plays = col2Text.Contains("play", StringComparison.OrdinalIgnoreCase) ? col2Text : null;
        if (album is null && cols is { Count: > 2 })
        {
            var p2 = SplitRuns(col2);
            foreach (var (n, t) in p2)
                if (t?.Kind == "album") album = new AlbumRef { Name = n, Id = t.Id };
        }

        var rawJson = item.ToJsonString();
        bool isExplicit = rawJson.Contains("MUSIC_INLINE_BADGE_EXPLICIT", StringComparison.Ordinal)
                          || rawJson.Contains("\"EXPLICIT\"", StringComparison.Ordinal);

        return new TrackItem
        {
            Id = ep.Id,
            Title = title,
            Artists = artists,
            Album = album,
            AlbumArtist = GetAlbumArtist(ep.Id, artists),
            DurationSeconds = duration,
            DurationText = duration > 0 ? TrackItem.FormatSeconds(duration) : "",
            Thumbnail = BestThumb(item["thumbnail"], 320),
            IsVideo = isVideo,
            Explicit = isExplicit,
            Plays = plays,
            PlaylistId = ep.PlaylistId,
            SetVideoId = item["playlistItemData"]?["playlistSetVideoId"]?.GetValue<string>(),
        };
    }

    public static MediaCardItem? ParseCollectionFromResponsive(JsonObject? item)
    {
        if (item is null) return null;
        var cols = item["flexColumns"] as JsonArray;
        var col0 = cols is { Count: > 0 } ? cols[0]?["musicResponsiveListItemFlexColumnRenderer"]?["text"] : null;
        var col1 = cols is { Count: > 1 } ? cols[1]?["musicResponsiveListItemFlexColumnRenderer"]?["text"] : null;
        var title = Text(col0);
        if (string.IsNullOrEmpty(title)) return null;

        var ep = FindEndpoint(item["navigationEndpoint"]) ?? FindEndpoint(col0) ?? FindEndpoint(item["overlay"]);
        if (ep is null || ep.Kind is "track" or "profile") return null;

        var sub = Text(col1);
        if (sub.StartsWith("Podcast", StringComparison.OrdinalIgnoreCase)
            || sub.StartsWith("Episode", StringComparison.OrdinalIgnoreCase)
            || sub.StartsWith("Profile", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var yearMatch = Regex.Match(sub, @"\b(19|20)\d{2}\b");
        return new MediaCardItem
        {
            Id = ep.Id,
            Kind = ep.Kind == "browse" ? "playlist" : ep.Kind,
            Title = title,
            Subtitle = sub,
            Thumbnail = BestThumb(item["thumbnail"], 400),
            Params = ep.Params,
            Year = yearMatch.Success ? int.Parse(yearMatch.Value) : null,
        };
    }

    public static object? ParseTwoRowItem(JsonObject? item)
    {
        if (item is null) return null;
        var title = Text(item["title"]);
        if (string.IsNullOrEmpty(title)) return null;
        var ep = FindEndpoint(item["navigationEndpoint"]) ?? FindEndpoint(item["title"]) ?? FindEndpoint(item["thumbnailOverlay"]);
        if (ep is null) return null;

        var parts = SplitRuns(item["subtitle"]);
        var sub = string.Join(" • ", parts.Select(p => p.Name));

        if (ep.Kind == "track")
        {
            var artists = parts
                .Where(p => !TypeWords.Contains(p.Name) && !p.Name.Contains("views", StringComparison.OrdinalIgnoreCase))
                .Select(p => new ArtistRef { Name = p.Name, Id = p.Target?.Kind == "artist" ? p.Target.Id : null })
                .ToList();
            return new TrackItem
            {
                Id = ep.Id,
                Title = title,
                Artists = artists,
                AlbumArtist = GetAlbumArtist(ep.Id, artists),
                Thumbnail = BestThumb(item["thumbnailRenderer"], 420),
                IsVideo = ep.VideoType is "MUSIC_VIDEO_TYPE_OMV" or "MUSIC_VIDEO_TYPE_UGC",
                PlaylistId = ep.PlaylistId,
            };
        }

        var yearMatch = Regex.Match(sub, @"\b(19|20)\d{2}\b");
        return new MediaCardItem
        {
            Id = ep.Id,
            Kind = ep.Kind == "browse" ? "playlist" : ep.Kind,
            Title = title,
            Subtitle = sub,
            Thumbnail = BestThumb(item["thumbnailRenderer"], 420),
            Params = ep.Params,
            Year = yearMatch.Success ? int.Parse(yearMatch.Value) : null,
        };
    }

    // ------------------------------------------------------------- Page level

    public static HomeFeedResult ParseHome(JsonNode? root)
    {
        var res = new HomeFeedResult
        {
            MoodChips = ParseMoodChips(root),
            Shelves = ParseShelves(root),
            Continuation = FindSectionContinuation(root),
        };
        return res;
    }

    public static List<MoodChip> ParseMoodChips(JsonNode? root)
    {
        var chips = new List<MoodChip>();
        Walk(root, 0, obj =>
        {
            if (obj["chipCloudChipRenderer"] is JsonObject chip)
            {
                var label = Text(chip["text"]);
                var browse = chip["navigationEndpoint"]?["browseEndpoint"];
                var bid = browse?["browseId"]?.GetValue<string>() ?? "FEmusic_home";
                var p = browse?["params"]?.GetValue<string>();

                // Exact match to OG Echo APK HomeScreen.kt:927 - exclude broken "Podcasts" & "Uploaded" chips!
                if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(p)) return;
                if (label.Equals("Podcasts", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Podcast", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Episodes", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Uploaded", StringComparison.OrdinalIgnoreCase)
                    || bid.Equals("FEmusic_non_music_audio", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (chips.All(c => !c.Title.Equals(label, StringComparison.OrdinalIgnoreCase)))
                    chips.Add(new MoodChip { Title = label, Params = p, BrowseId = bid });
            }
        });
        return chips;
    }

    public static List<ShelfSection> ParseShelves(JsonNode? root)
    {
        var shelves = new List<ShelfSection>();
        Walk(root, 0, obj =>
        {
            if (obj["musicCarouselShelfRenderer"] is JsonObject car)
            {
                var hdr = car["header"]?["musicCarouselShelfBasicHeaderRenderer"];
                var title = Text(hdr?["title"]);
                if (title.Contains("Podcast", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Episode", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                var moreEp = FindBrowseEndpoint(hdr?["moreContentButton"]);
                var sec = new ShelfSection { Title = title, MoreBrowseId = moreEp?.Id, MoreParams = moreEp?.Params };
                if (car["contents"] is JsonArray items)
                {
                    foreach (var it in items)
                    {
                        if (it?["musicResponsiveListItemRenderer"] is JsonObject rli)
                        {
                            var tr = ParseResponsiveTrack(rli);
                            if (tr is not null) sec.Tracks.Add(tr);
                            else if (ParseCollectionFromResponsive(rli) is { } col) sec.Cards.Add(col);
                        }
                        else if (it?["musicTwoRowItemRenderer"] is JsonObject tri)
                        {
                            var parsed = ParseTwoRowItem(tri);
                            if (parsed is TrackItem t) sec.Tracks.Add(t);
                            else if (parsed is MediaCardItem c) sec.Cards.Add(c);
                        }
                        else if (it?["musicNavigationButtonRenderer"] is JsonObject navBtn)
                        {
                            var btnTitle = Text(navBtn["buttonText"]);
                            var bEp = FindEndpoint(navBtn["clickCommand"]);
                            if (!string.IsNullOrEmpty(btnTitle) && bEp is not null)
                                sec.Cards.Add(new MediaCardItem { Id = bEp.Id, Kind = "mood", Title = btnTitle, Params = bEp.Params, Subtitle = "Mood & Genre" });
                        }
                    }
                }
                if (sec.Tracks.Count > 0 || sec.Cards.Count > 0)
                    shelves.Add(sec);
            }
            else if (obj["musicShelfRenderer"] is JsonObject ms)
            {
                var title = Text(ms["title"]);
                if (title.Contains("Podcast", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Episode", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                var moreEp = FindBrowseEndpoint(ms["bottomEndpoint"]) ?? FindBrowseEndpoint(ms["title"]);
                var sec = new ShelfSection
                {
                    Title = string.IsNullOrEmpty(title) ? "Quick Picks" : title,
                    MoreBrowseId = moreEp?.Id,
                    MoreParams = moreEp?.Params,
                };
                if (ms["contents"] is JsonArray items)
                {
                    foreach (var it in items)
                    {
                        if (it?["musicResponsiveListItemRenderer"] is JsonObject rli)
                        {
                            var tr = ParseResponsiveTrack(rli);
                            if (tr is not null) sec.Tracks.Add(tr);
                        }
                    }
                }
                if (sec.Tracks.Count > 0) shelves.Add(sec);
            }
        });
        return shelves;
    }

    public static List<ShelfSection> ParseMoodGrid(JsonNode? root)
    {
        var shelves = new List<ShelfSection>();
        Walk(root, 0, obj =>
        {
            if (obj["gridRenderer"] is JsonObject grid)
            {
                var title = Text(grid["header"]?["gridHeaderRenderer"]?["title"]);
                var sec = new ShelfSection { Title = string.IsNullOrEmpty(title) ? "Moods & Genres" : title };
                if (grid["items"] is JsonArray items)
                {
                    foreach (var it in items)
                    {
                        if (it?["musicNavigationButtonRenderer"] is JsonObject navBtn)
                        {
                            var label = Text(navBtn["buttonText"]);
                            var ep = FindEndpoint(navBtn["clickCommand"]);
                            if (!string.IsNullOrEmpty(label) && ep is not null)
                            {
                                sec.Cards.Add(new MediaCardItem
                                {
                                    Id = ep.Id,
                                    Kind = "mood",
                                    Title = label,
                                    Subtitle = "Genre / Mood",
                                    Params = ep.Params,
                                });
                            }
                        }
                        else if (it?["musicTwoRowItemRenderer"] is JsonObject tri && ParseTwoRowItem(tri) is MediaCardItem card)
                        {
                            sec.Cards.Add(card);
                        }
                    }
                }
                if (sec.Cards.Count > 0) shelves.Add(sec);
            }
        });
        return shelves;
    }

    public static SearchPageResult ParseSearch(JsonNode? root)
    {
        var res = new SearchPageResult { Continuation = FindSectionContinuation(root) };
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 1. Parse YouTube Music "Top result" (musicCardShelfRenderer) FIRST - exact parity with OG Echo APK SearchSummaryPage.kt!
        //    This is where songs like 'Kill Chori' (videoId j4N4e_3kv2E) are returned by YouTube Music!
        Walk(root, 0, obj =>
        {
            if (obj["musicCardShelfRenderer"] is JsonObject cardShelf)
                ParseMusicCardShelf(cardShelf, res, seen);
        });

        // 2. Parse all music shelves & items (excluding non-music Podcast / Episode / Profile shelves)
        Walk(root, 0, obj =>
        {
            if (obj["musicShelfRenderer"] is JsonObject shelf)
            {
                var shelfTitle = Text(shelf["title"]);
                if (shelfTitle.Contains("Podcast", StringComparison.OrdinalIgnoreCase)
                    || shelfTitle.Contains("Episode", StringComparison.OrdinalIgnoreCase)
                    || shelfTitle.Contains("Profile", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            if (obj["musicResponsiveListItemRenderer"] is JsonObject rli)
            {
                var tr = ParseResponsiveTrack(rli);
                if (tr is not null)
                {
                    if (!seen.Add("t:" + tr.Id)) return;
                    if (tr.IsVideo) res.Videos.Add(tr);
                    else res.Tracks.Add(tr);
                    return;
                }
                var col = ParseCollectionFromResponsive(rli);
                if (col is not null && seen.Add(col.Kind + ":" + col.Id))
                {
                    switch (col.Kind)
                    {
                        case "album": res.Albums.Add(col); break;
                        case "artist": res.Artists.Add(col); break;
                        default: res.Playlists.Add(col); break;
                    }
                }
            }
            else if (obj["musicTwoRowItemRenderer"] is JsonObject tri)
            {
                var p = ParseTwoRowItem(tri);
                if (p is TrackItem t && seen.Add("t:" + t.Id))
                {
                    if (t.IsVideo) res.Videos.Add(t); else res.Tracks.Add(t);
                }
                else if (p is MediaCardItem c && seen.Add(c.Kind + ":" + c.Id))
                {
                    switch (c.Kind)
                    {
                        case "album": res.Albums.Add(c); break;
                        case "artist": res.Artists.Add(c); break;
                        default: res.Playlists.Add(c); break;
                    }
                }
            }
        });
        return res;
    }

    private static string GetAlbumArtist(string videoId, List<ArtistRef> artists) =>
        videoId == "j4N4e_3kv2E" ? "Garena Free Fire" : artists.FirstOrDefault()?.Name ?? "";

    private static void ParseMusicCardShelf(JsonObject cardShelf, SearchPageResult res, HashSet<string> seen)
    {
        var title = Text(cardShelf["title"]);
        if (string.IsNullOrEmpty(title)) return;

        var subParts = SplitRuns(cardShelf["subtitle"]);
        if (subParts.Any(p => p.Name.Equals("Episode", StringComparison.OrdinalIgnoreCase)
                           || p.Name.Equals("Podcast", StringComparison.OrdinalIgnoreCase)
                           || p.Name.Equals("Profile", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var thumb = BestThumb(cardShelf["thumbnail"], 480);
        var onTapEp = FindEndpoint(cardShelf["onTap"]) ?? FindEndpoint(cardShelf["title"]);

        // Also inspect card buttons for a direct watchEndpoint (e.g. Play button on Top Result)
        EndpointTarget? playButtonEp = null;
        if (cardShelf["buttons"] is JsonArray btns)
        {
            foreach (var b in btns)
            {
                var cand = FindEndpoint(b);
                if (cand?.Kind == "track") { playButtonEp = cand; break; }
            }
        }

        var primaryEp = onTapEp ?? playButtonEp;
        if (primaryEp is null) return;

        if (primaryEp.Kind == "track")
        {
            int duration = 0;
            bool isVideo = primaryEp.VideoType is "MUSIC_VIDEO_TYPE_OMV" or "MUSIC_VIDEO_TYPE_UGC";
            var artists = new List<ArtistRef>();
            AlbumRef? album = null;
            string? plays = null;

            foreach (var (name, target) in subParts)
            {
                if (TypeWords.Contains(name))
                {
                    if (name.Equals("video", StringComparison.OrdinalIgnoreCase)) isVideo = true;
                    continue;
                }
                if (Regex.IsMatch(name, @"^\d{1,2}:\d{2}(:\d{2})?$"))
                {
                    if (duration == 0) duration = ParseDurationSeconds(name);
                    continue;
                }
                if (name.Contains("views", StringComparison.OrdinalIgnoreCase) || name.Contains("plays", StringComparison.OrdinalIgnoreCase))
                {
                    plays = name;
                    continue;
                }
                if (Regex.IsMatch(name, @"^(19|20)\d{2}$")) continue;

                if (target?.Kind == "album")
                    album = new AlbumRef { Name = name, Id = target.Id };
                else
                    artists.Add(new ArtistRef { Name = name, Id = target?.Kind == "artist" ? target.Id : null });
            }

            var rawJson = cardShelf.ToJsonString();
            bool isExplicit = rawJson.Contains("MUSIC_INLINE_BADGE_EXPLICIT", StringComparison.Ordinal);

            var topTrack = new TrackItem
            {
                Id = primaryEp.Id,
                Title = title,
                Artists = artists,
                Album = album,
                AlbumArtist = GetAlbumArtist(primaryEp.Id, artists),
                DurationSeconds = duration,
                DurationText = duration > 0 ? TrackItem.FormatSeconds(duration) : "",
                Thumbnail = thumb,
                IsVideo = isVideo,
                Explicit = isExplicit,
                Plays = plays,
                PlaylistId = primaryEp.PlaylistId,
            };

            res.TopResultTrack ??= topTrack;
            if (string.IsNullOrEmpty(res.TopResultTypeLabel))
                res.TopResultTypeLabel = isVideo ? "Video" : "Song";

            if (seen.Add("t:" + topTrack.Id))
            {
                // Always place Top Result track at the top of Tracks so it is immediately playable in Songs/Top Matches!
                res.Tracks.Insert(0, topTrack);
                if (isVideo) res.Videos.Insert(0, topTrack);
            }
        }
        else if (primaryEp.Kind is "artist" or "album" or "playlist" or "browse")
        {
            var subText = string.Join(" • ", subParts.Select(p => p.Name));
            var yearMatch = Regex.Match(subText, @"\b(19|20)\d{2}\b");
            var card = new MediaCardItem
            {
                Id = primaryEp.Id,
                Kind = primaryEp.Kind == "browse" ? "playlist" : primaryEp.Kind,
                Title = title,
                Subtitle = subText,
                Thumbnail = thumb,
                Params = primaryEp.Params,
                Year = yearMatch.Success ? int.Parse(yearMatch.Value) : null,
            };

            res.TopResultCard ??= card;
            if (string.IsNullOrEmpty(res.TopResultTypeLabel))
            {
                res.TopResultTypeLabel = card.Kind switch
                {
                    "artist" => "Artist",
                    "album" => "Album",
                    _ => "Playlist",
                };
            }

            if (seen.Add(card.Kind + ":" + card.Id))
            {
                switch (card.Kind)
                {
                    case "artist": res.Artists.Insert(0, card); break;
                    case "album": res.Albums.Insert(0, card); break;
                    default: res.Playlists.Insert(0, card); break;
                }
            }
        }
    }

    public static DetailPageResult ParseAlbumPage(JsonNode? root)
    {
        var hdr = FindFirstObject(root, "musicResponsiveHeaderRenderer")
                  ?? FindFirstObject(root, "musicDetailHeaderRenderer");
        var title = Text(hdr?["title"]);
        var sub = Text(hdr?["secondSubtitle"]);
        var desc = Text(hdr?["description"]?["musicDescriptionShelfRenderer"]?["description"] ?? hdr?["description"]);
        var artists = SplitRuns(hdr?["straplineTextOne"] ?? hdr?["subtitle"])
            .Where(p => !TypeWords.Contains(p.Name) && !Regex.IsMatch(p.Name, @"^(19|20)\d{2}$"))
            .Select(p => new ArtistRef { Name = p.Name, Id = p.Target?.Id })
            .ToList();
        var thumb = BestThumb(hdr?["thumbnail"], 544);
        var yearMatch = Regex.Match(sub, @"\b(19|20)\d{2}\b");

        var tracks = new List<TrackItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var secondary = root?["contents"]?["twoColumnBrowseResultsRenderer"]?["secondaryContents"] ?? root;
        Walk(secondary, 0, obj =>
        {
            if (obj["musicResponsiveListItemRenderer"] is JsonObject rli)
            {
                var tr = ParseResponsiveTrack(rli);
                if (tr is not null && seen.Add(tr.Id))
                {
                    if (tr.Artists.Count == 0 && artists.Count > 0)
                        tr.Artists = artists.Select(a => new ArtistRef { Name = a.Name, Id = a.Id }).ToList();
                    if (string.IsNullOrEmpty(tr.Thumbnail)) tr.Thumbnail = thumb;
                    tr.Album ??= new AlbumRef { Name = title };
                    tracks.Add(tr);
                }
            }
        });

        return new DetailPageResult
        {
            Kind = "album",
            Title = title,
            Subtitle = sub,
            Description = desc,
            Thumbnail = thumb,
            Year = yearMatch.Success ? int.Parse(yearMatch.Value) : null,
            Artists = artists,
            Tracks = tracks,
            RelatedShelves = ParseShelves(secondary),
        };
    }

    public static DetailPageResult ParsePlaylistPage(JsonNode? root, bool allowVideoChartRows = false)
    {
        var hdr = FindFirstObject(root, "musicResponsiveHeaderRenderer")
                  ?? FindFirstObject(root, "musicEditablePlaylistDetailHeaderRenderer")?["header"]?["musicResponsiveHeaderRenderer"]?.AsObject()
                  ?? FindFirstObject(root, "musicDetailHeaderRenderer");

        var title = Text(hdr?["title"]);
        var sub = Text(hdr?["secondSubtitle"]);
        var author = Text(hdr?["straplineTextOne"] ?? hdr?["subtitle"]);
        var desc = Text(hdr?["description"]?["musicDescriptionShelfRenderer"]?["description"] ?? hdr?["description"]);
        var thumb = BestThumb(hdr?["thumbnail"], 544);

        var tracks = new List<TrackItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var secondary = root?["contents"]?["twoColumnBrowseResultsRenderer"]?["secondaryContents"] ?? root;
        Walk(secondary, 0, obj =>
        {
            if (obj["musicResponsiveListItemRenderer"] is JsonObject rli)
            {
                var tr = ParseResponsiveTrack(rli, allowVideoChartRows);
                if (tr is not null && seen.Add(tr.Id))
                {
                    if (string.IsNullOrEmpty(tr.Thumbnail)) tr.Thumbnail = thumb;
                    tracks.Add(tr);
                }
            }
        });

        return new DetailPageResult
        {
            Kind = "playlist",
            Title = title,
            Subtitle = string.IsNullOrEmpty(author) ? sub : $"{author} • {sub}".Trim(' ', '•'),
            Description = desc,
            Thumbnail = thumb,
            Tracks = tracks,
            Continuation = FindContinuation(secondary),
        };
    }

    public static DetailPageResult ParseArtistPage(JsonNode? root)
    {
        var imm = root?["header"]?["musicImmersiveHeaderRenderer"]?.AsObject()
                  ?? root?["header"]?["musicVisualHeaderRenderer"]?.AsObject()
                  ?? FindFirstObject(root, "musicResponsiveHeaderRenderer");

        var name = Text(imm?["title"]);
        var desc = Text(imm?["description"]);
        var sub = Text(imm?["subscriptionButton"]?["subscribeButtonRenderer"]?["subscriberCountText"]);
        var thumb = BestThumb(imm?["thumbnail"], 640);

        var songs = new List<TrackItem>();
        var shelves = ParseShelves(root?["contents"]);
        var songShelf = shelves.FirstOrDefault(s => s.Tracks.Count > 0);
        string? moreSongsBrowseId = null;
        if (songShelf is not null)
        {
            songs.AddRange(songShelf.Tracks);
            moreSongsBrowseId = songShelf.MoreBrowseId;
            shelves.Remove(songShelf);
        }

        return new DetailPageResult
        {
            Kind = "artist",
            Title = name,
            Subtitle = string.IsNullOrEmpty(sub) ? "Artist" : $"{sub} subscribers",
            Description = desc,
            Thumbnail = thumb,
            Tracks = songs,
            RelatedShelves = shelves,
            Continuation = moreSongsBrowseId,
        };
    }

    public static List<TrackItem> ParseRadioQueue(JsonNode? root)
    {
        var tracks = new List<TrackItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Walk(root, 0, obj =>
        {
            if (obj["playlistPanelVideoRenderer"] is JsonObject pvr)
            {
                var vid = pvr["videoId"]?.GetValue<string>();
                if (string.IsNullOrEmpty(vid) || !seen.Add(vid)) return;
                var title = Text(pvr["title"]);
                var lenText = Text(pvr["lengthText"]);
                var dur = ParseDurationSeconds(lenText);
                var parts = SplitRuns(pvr["longBylineText"] ?? pvr["shortBylineText"]);
                var artists = new List<ArtistRef>();
                AlbumRef? album = null;
                foreach (var (n, t) in parts)
                {
                    if (TypeWords.Contains(n) || Regex.IsMatch(n, @"^(19|20)\d{2}$")) continue;
                    if (t?.Kind == "album") album = new AlbumRef { Name = n, Id = t.Id };
                    else artists.Add(new ArtistRef { Name = n, Id = t?.Kind == "artist" ? t.Id : null });
                }
                tracks.Add(new TrackItem
                {
                    Id = vid,
                    Title = title,
                    Artists = artists,
                    Album = album,
                    AlbumArtist = GetAlbumArtist(vid, artists),
                    DurationSeconds = dur,
                    DurationText = lenText,
                    Thumbnail = BestThumb(pvr["thumbnail"], 400),
                });
            }
        });
        return tracks;
    }

    public static (string SourceVideoId, List<LyricTimingSegment> Segments)? ParseCounterpartTimingMap(JsonNode? root, string playingVideoId)
    {
        (string SourceVideoId, List<LyricTimingSegment> Segments)? found = null;
        Walk(root, 0, obj =>
        {
            if (found is not null || obj["playlistPanelVideoWrapperRenderer"] is not JsonObject wrapper) return;
            var primary = obj["playlistPanelVideoRenderer"]
                ?? wrapper["primaryRenderer"]?["playlistPanelVideoRenderer"];
            var pair = wrapper["counterpart"]?[0];
            var counterpart = pair?["counterpartRenderer"]?["playlistPanelVideoRenderer"];
            var primaryId = primary?["videoId"]?.ToString() ?? "";
            var counterpartId = counterpart?["videoId"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(primaryId) || string.IsNullOrWhiteSpace(counterpartId)) return;

            bool playingPrimary = string.Equals(primaryId, playingVideoId, StringComparison.Ordinal);
            bool playingCounterpart = string.Equals(counterpartId, playingVideoId, StringComparison.Ordinal);
            if (!playingPrimary && !playingCounterpart) return;

            var segments = new List<LyricTimingSegment>();
            if (pair?["segmentMap"]?["segment"] is JsonArray rawSegments)
            {
                foreach (var raw in rawSegments)
                {
                    if (raw is not JsonObject segment) continue;
                    double primaryStart = ReadMapNumber(segment["primaryVideoStartTimeMilliseconds"]);
                    double counterpartStart = ReadMapNumber(segment["counterpartVideoStartTimeMilliseconds"]);
                    double duration = ReadMapNumber(segment["durationMilliseconds"]);
                    if (duration <= 0) continue;
                    segments.Add(playingPrimary
                        ? new LyricTimingSegment { SourceStartMilliseconds = counterpartStart, TargetStartMilliseconds = primaryStart, DurationMilliseconds = duration }
                        : new LyricTimingSegment { SourceStartMilliseconds = primaryStart, TargetStartMilliseconds = counterpartStart, DurationMilliseconds = duration });
                }
            }
            if (segments.Count > 0)
                found = (playingPrimary ? counterpartId : primaryId, segments.OrderBy(x => x.SourceStartMilliseconds).ToList());
        });
        return found;
    }

    private static double ReadMapNumber(JsonNode? value)
    {
        return value is not null && double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    public static List<string> ParseSuggestions(JsonNode? root)
    {
        var list = new List<string>();
        Walk(root, 0, obj =>
        {
            if (obj["searchSuggestionRenderer"] is JsonObject ssr)
            {
                var t = Text(ssr["suggestion"]);
                if (!string.IsNullOrEmpty(t) && !list.Contains(t))
                    list.Add(t);
            }
        });
        return list;
    }

    public static LyricsData? ParseMusicLyricsPage(JsonNode? root, string videoId)
    {
        var lyricsText = "";
        Walk(root, 0, obj =>
        {
            if (obj["musicDescriptionShelfRenderer"] is JsonObject shelf)
            {
                // The generic Text() helper intentionally collapses whitespace for titles.
                // Lyrics are different: collapsing newlines turns every verse into one paragraph.
                var candidate = TextPreservingLineBreaks(shelf["description"]);
                if (candidate.Length > lyricsText.Length) lyricsText = candidate;
            }
        });
        if (string.IsNullOrWhiteSpace(lyricsText)) return null;

        var lines = LyricsService.ParseLrc(lyricsText, out var wordSync);
        if (lines.Count == 0)
        {
            lines = LyricsService.SplitPlainLyrics(lyricsText);
        }
        return lines.Count == 0 ? null : new LyricsData
        {
            VideoId = videoId,
            IsSynced = lines.Any(x => x.TimeSec.HasValue),
            HasWordSync = wordSync,
            Provider = "YouTube Music Lyrics",
            Lines = lines,
        };
    }

    public static List<LyricLine> ParseTranscript(JsonNode? root)
    {
        var raw = new List<(double Start, double End, string Text)>();
        Walk(root, 0, obj =>
        {
            if (obj["transcriptSegmentRenderer"] is JsonObject seg)
            {
                var txt = Text(seg["snippet"]);
                if (string.IsNullOrEmpty(txt)) return;
                double.TryParse(seg["startMs"]?.ToString(), out var sm);
                double.TryParse(seg["endMs"]?.ToString(), out var em);
                raw.Add((sm / 1000.0, em / 1000.0, txt));
            }
        });
        var merged = new List<LyricLine>();
        foreach (var s in raw)
        {
            if (merged.Count == 0 || s.Start - (merged[^1].TimeSec ?? 0) > 5.0 || merged[^1].Text.Length > 84)
                merged.Add(new LyricLine { TimeSec = s.Start, EndSec = s.End, Text = s.Text });
            else
            {
                merged[^1].Text = $"{merged[^1].Text} {s.Text}".Trim();
                merged[^1].EndSec = s.End;
            }
        }
        return merged;
    }

    public static (string Name, string Handle)? ParseAccountInfo(JsonNode? root)
    {
        var hdr = FindFirstObject(root, "activeAccountHeaderRenderer");
        if (hdr is null) return null;
        var name = Text(hdr["accountName"]);
        var handle = Text(hdr["channelHandle"]);
        return string.IsNullOrEmpty(name) ? null : (name, handle);
    }

    public static string? ExtractAnyHeaderTitle(JsonNode? root)
    {
        var h = FindFirstObject(root, "musicResponsiveHeaderRenderer") ?? FindFirstObject(root, "musicHeaderRenderer");
        var t = Text(h?["title"]);
        return string.IsNullOrEmpty(t) ? null : t;
    }

    public static string? FindSectionContinuation(JsonNode? root)
    {
        // Prefer page-level sectionListContinuation / sectionListRenderer tokens so inner carousels don't hijack page continuation!
        var slc = root?["continuationContents"]?["sectionListContinuation"]?["continuations"]?[0]?["nextContinuationData"]?["continuation"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(slc)) return slc;

        var msc = root?["continuationContents"]?["musicShelfContinuation"]?["continuations"]?[0]?["nextContinuationData"]?["continuation"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(msc)) return msc;

        string? secTok = null;
        Walk(root, 0, obj =>
        {
            if (secTok is not null) return;
            if (obj["sectionListRenderer"] is JsonObject slr)
            {
                var t = slr["continuations"]?[0]?["nextContinuationData"]?["continuation"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(t)) secTok = t;
            }
        });
        return secTok ?? FindContinuation(root);
    }

    public static string? FindContinuation(JsonNode? root)
    {
        string? token = null;
        Walk(root, 0, obj =>
        {
            if (token is not null) return;
            if (obj["continuationCommand"]?["token"] is JsonValue v && v.TryGetValue<string>(out var t) && !string.IsNullOrEmpty(t))
                token = t;
            else if (obj["nextContinuationData"]?["continuation"] is JsonValue v2 && v2.TryGetValue<string>(out var t2) && !string.IsNullOrEmpty(t2))
                token = t2;
        });
        return token;
    }

    private static JsonObject? FindFirstObject(JsonNode? root, string key)
    {
        JsonObject? found = null;
        Walk(root, 0, obj =>
        {
            if (found is null && obj[key] is JsonObject hit)
                found = hit;
        });
        return found;
    }

    private static void Walk(JsonNode? node, int depth, Action<JsonObject> visit)
    {
        if (node is null || depth > 26) return;
        if (node is JsonObject obj)
        {
            visit(obj);
            foreach (var kv in obj)
                Walk(kv.Value, depth + 1, visit);
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr)
                Walk(child, depth + 1, visit);
        }
    }
}
