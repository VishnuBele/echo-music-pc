using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EchoMusic.Core;

public sealed class LyricsService
{
    private readonly InnerTubeClient _yt;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, LyricsData> _cache = new(StringComparer.Ordinal);

    public LyricsService(InnerTubeClient yt)
    {
        _yt = yt;
        _http = yt.SharedHttp;
    }

    public async Task<LyricsData> GetLyricsAsync(TrackItem track, CancellationToken ct = default, bool forceRefresh = false)
    {
        if (forceRefresh) _cache.TryRemove(track.Id, out _);
        else if (_cache.TryGetValue(track.Id, out var hit))
            return hit;

        var cleanTitle = CleanTitle(track.Title);
        var creditMatch = Regex.Match(track.Title, @"(?:song|music|vocals?)\s+by\s+([^|]+)", RegexOptions.IgnoreCase);
        var bestArtist = creditMatch.Success
            ? creditMatch.Groups[1].Value
            : track.Artists.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Name))?.Name
              ?? track.AlbumArtist;
        var cleanArtist = CleanArtist(bestArtist ?? "");
        var titleVariants = GetTitleVariants(track.Title);

        LyricsData? providerPlain = null;
        // Probe independent video-ID and metadata providers together. Untimed text is retained,
        // but is never allowed to short-circuit a timed source that may still be responding.
        if (!track.IsLocal)
        {
            using var providerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pending = new List<Task<LyricsData?>>
            {
                TrySimpMusicAsync(track.Id, cleanTitle, track.DurationSeconds, providerCts.Token),
                TryYouLyPlusAsync(cleanTitle, cleanArtist, track.Album?.Name, track.DurationSeconds, track.Id, providerCts.Token),
                TryNativeLyricsAsync(track.Id, providerCts.Token),
                TryYouTubeTranscriptAsync(track.Id, providerCts.Token),
                TryUnisonAsync(cleanTitle, cleanArtist, track.Album?.Name, track.DurationSeconds, track.Id, providerCts.Token),
                TryPaxSenixAsync(cleanTitle, cleanArtist, track.DurationSeconds, track.Album?.Name, providerCts.Token),
                TryBetterLyricsAsync(track.Title, CleanArtist(bestArtist ?? ""), track.DurationSeconds, track.Album?.Name, track.Id, providerCts.Token),
                TryBetterLyricsAsync(cleanTitle, cleanArtist, track.DurationSeconds, track.Album?.Name, track.Id, providerCts.Token),
            };
            var candidates = new List<LyricsData>();
            var deadline = Task.Delay(TimeSpan.FromSeconds(13), ct);
            Task stopAfterFirstSynced = deadline;
            bool sawUsableSynced = false;
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending.Cast<Task>().Append(stopAfterFirstSynced));
                if (finished == stopAfterFirstSynced) break;
                var providerTask = (Task<LyricsData?>)finished;
                pending.Remove(providerTask);
                LyricsData? candidate = null;
                try { candidate = await providerTask; } catch { }
                if (candidate is null || candidate.Lines.Count == 0) continue;
                if (string.IsNullOrWhiteSpace(candidate.TimingSourceVideoId))
                    candidate.TimingSourceVideoId = string.IsNullOrWhiteSpace(candidate.VideoId) ? track.Id : candidate.VideoId;
                candidate.VideoId = track.Id;
                if (candidate.IsInstrumental || IsUsableSynced(candidate, track.DurationSeconds) || !candidate.IsSynced)
                {
                    candidates.Add(candidate);
                    if (candidate.IsSynced && IsUsableSynced(candidate, track.DurationSeconds) && !sawUsableSynced)
                    {
                        sawUsableSynced = true;
                        // Give concurrently-started providers a short chance to return a better match.
                        stopAfterFirstSynced = Task.Delay(TimeSpan.FromMilliseconds(2200), ct);
                    }
                }
            }
            providerCts.Cancel();
            ct.ThrowIfCancellationRequested();

            var syncedCandidates = candidates.Where(x => x.IsSynced && IsUsableSynced(x, track.DurationSeconds)).ToList();
            if (syncedCandidates.Count > 0)
            {
                var best = syncedCandidates.OrderByDescending(x => ScoreCandidate(x, track.DurationSeconds)).First();
                AutoAlignWithConsensus(best, syncedCandidates.Where(x => !ReferenceEquals(x, best)), track.DurationSeconds);
                await ApplyCounterpartTimingMapAsync(track, best, ct);
                _cache[track.Id] = best;
                return best;
            }
            providerPlain = candidates
                .Where(x => !x.IsSynced && !x.IsInstrumental && x.Lines.Count > 0)
                .OrderByDescending(x => x.Lines.Count)
                .FirstOrDefault();
            var instrumental = candidates.FirstOrDefault(x => x.IsInstrumental);
            if (instrumental is not null && providerPlain is null)
            {
                instrumental.VideoId = track.Id;
                _cache[track.Id] = instrumental;
                return instrumental;
            }
        }

        // Metadata lookup includes title-only and artist/title queries; title-only is important for
        // YouTube uploads whose credit/artist fields differ from catalog metadata (e.g. Kill Chori).
        var lrclib = await TryLrclibAsync(cleanTitle, cleanArtist, track.Album?.Name, track.DurationSeconds, ct);
        if (lrclib is { IsSynced: true } && !IsUsableSynced(lrclib, track.DurationSeconds)) lrclib.IsSynced = false;
        if (lrclib is { IsSynced: true } || lrclib is { IsInstrumental: true })
        {
            lrclib.VideoId = track.Id;
            _cache[track.Id] = lrclib;
            return lrclib;
        }

        // KuGou is another line-timed fallback; try title variants if the first metadata query misses.
        LyricsData? kugou = null;
        foreach (var titleVariant in titleVariants.Take(3))
        {
            kugou = await TryKuGouAsync(titleVariant, cleanArtist, track.DurationSeconds, ct);
            if (kugou is { IsSynced: true } && IsUsableSynced(kugou, track.DurationSeconds)) break;
            kugou = null;
            if (!string.IsNullOrWhiteSpace(cleanArtist))
            {
                kugou = await TryKuGouAsync(titleVariant, "", track.DurationSeconds, ct);
                if (kugou is { IsSynced: true } && IsUsableSynced(kugou, track.DurationSeconds)) break;
                kugou = null;
            }
        }
        if (kugou is { IsSynced: true } && IsUsableSynced(kugou, track.DurationSeconds))
        {
            kugou.VideoId = track.Id;
            _cache[track.Id] = kugou;
            return kugou;
        }

        // If the current queue item is an uploaded music video, its ID may have no lyric record.
        // Search for a matching canonical song by cleaned title/credit and ask both ID providers.
        if (!track.IsLocal)
        {
            var alternate = await TrySearchAlternateLyricsAsync(cleanTitle, cleanArtist, track.DurationSeconds, track.Id, ct);
            if (alternate is { IsSynced: true } && IsUsableSynced(alternate, track.DurationSeconds))
            {
                if (string.IsNullOrWhiteSpace(alternate.TimingSourceVideoId))
                    alternate.TimingSourceVideoId = string.IsNullOrWhiteSpace(alternate.VideoId) ? track.Id : alternate.VideoId;
                await ApplyCounterpartTimingMapAsync(track, alternate, ct);
                alternate.VideoId = track.Id;
                _cache[track.Id] = alternate;
                return alternate;
            }
            if (alternate is { Lines.Count: > 0 })
            {
                if (string.IsNullOrWhiteSpace(alternate.TimingSourceVideoId))
                    alternate.TimingSourceVideoId = string.IsNullOrWhiteSpace(alternate.VideoId) ? track.Id : alternate.VideoId;
                providerPlain ??= alternate;
            }
        }

        LyricsData? nativePlain = providerPlain is { Provider: var providerName } && providerName.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase)
            ? providerPlain
            : null;

        // Prefer timed video captions over a plain lyric paragraph when a track has no synced source.
        // 5. YouTube auto-transcript fallback
        if (!track.IsLocal)
        {
            try
            {
                using var transcriptTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                transcriptTimeout.CancelAfter(TimeSpan.FromSeconds(6));
                var trLines = await _yt.GetTranscriptAsync(track.Id, transcriptTimeout.Token);
                if (trLines.Count > 0)
                {
                    var tr = new LyricsData
                    {
                        VideoId = track.Id,
                        IsSynced = true,
                        Provider = "YouTube Transcript",
                        Lines = trLines,
                    };
                    _cache[track.Id] = tr;
                    return tr;
                }
            }
            catch { }
        }

        var plainFallback = new[] { providerPlain, nativePlain, lrclib, kugou }
            .Where(x => x is { IsSynced: false, Lines.Count: > 0 })
            .OrderByDescending(x => x!.Lines.Count)
            .FirstOrDefault();
        if (plainFallback is not null)
        {
            plainFallback.VideoId = track.Id;
            _cache[track.Id] = plainFallback;
            return plainFallback;
        }
        return new LyricsData { VideoId = track.Id, Provider = "None" };
    }

    private async Task ApplyCounterpartTimingMapAsync(TrackItem track, LyricsData lyrics, CancellationToken ct)
    {
        var sourceId = string.IsNullOrWhiteSpace(lyrics.TimingSourceVideoId) ? lyrics.VideoId : lyrics.TimingSourceVideoId;
        if (!track.IsVideo || string.IsNullOrWhiteSpace(sourceId) || sourceId == track.Id
            || !lyrics.Lines.Any(line => line.TimeSec.HasValue)) return;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var map = await _yt.GetCounterpartTimingMapAsync(track.Id, track.PlaylistId, timeout.Token);
            if (map is null || map.Value.SourceVideoId != sourceId || map.Value.Segments.Count == 0) return;

            foreach (var line in lyrics.Lines)
            {
                if (line.TimeSec is not double time) continue;
                double shiftSeconds = GetSegmentTimeShiftMilliseconds(map.Value.Segments, time * 1000.0) / 1000.0;
                line.TimeSec = Math.Max(0, time + shiftSeconds);
                if (line.EndSec is double end) line.EndSec = Math.Max(0, end + shiftSeconds);
                foreach (var word in line.Words)
                    word.TimeSec = Math.Max(0, word.TimeSec + shiftSeconds);
            }
            lyrics.TimingMap = map.Value.Segments;
            lyrics.TimingSourceVideoId = sourceId;
            lyrics.Provider += " · counterpart-aligned";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }
    }

    private static double GetSegmentTimeShiftMilliseconds(IReadOnlyList<LyricTimingSegment> segments, double timeMs)
    {
        double shift = 0;
        foreach (var segment in segments)
        {
            if (timeMs < segment.SourceStartMilliseconds) break;
            shift = segment.TargetStartMilliseconds - segment.SourceStartMilliseconds;
            if (timeMs <= segment.SourceStartMilliseconds + segment.DurationMilliseconds) break;
        }
        return shift;
    }

    private async Task<LyricsData?> TryYouTubeTranscriptAsync(string videoId, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            var lines = await _yt.GetTranscriptAsync(videoId, timeout.Token);
            return lines.Count == 0 ? null : new LyricsData
            {
                VideoId = videoId,
                IsSynced = true,
                Provider = "YouTube Transcript",
                Lines = lines,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private async Task<LyricsData?> TryNativeLyricsAsync(string videoId, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            return await _yt.GetMusicLyricsAsync(videoId, timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private async Task<LyricsData?> TrySearchAlternateLyricsAsync(string title, string artist, int durationSec, string currentVideoId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            var query = $"{title} {artist}".Trim();
            var pages = await Task.WhenAll(
                _yt.SearchAsync(query, "songs", ct: timeout.Token),
                _yt.SearchAsync(query, "videos", ct: timeout.Token));
            var alternatives = pages.SelectMany(page => page.Tracks.Concat(page.Videos))
                .DistinctBy(x => x.Id)
                .Where(x => !x.IsLocal && !string.IsNullOrWhiteSpace(x.Id) && x.Id != currentVideoId && TitlesAreCompatible(title, x.Title))
                .Where(x => durationSec <= 0 || x.DurationSeconds <= 0 || Math.Abs(x.DurationSeconds - durationSec) <= 18)
                .OrderBy(x => durationSec > 0 && x.DurationSeconds > 0 ? Math.Abs(x.DurationSeconds - durationSec) : 0)
                .Take(3)
                .ToList();
            if (alternatives.Count == 0) return null;

            var pending = new List<Task<LyricsData?>>();
            foreach (var item in alternatives)
            {
                var altTitle = CleanTitle(item.Title);
                var altArtist = CleanArtist(item.ArtistNames);
                pending.Add(TrySimpMusicAsync(item.Id, altTitle, item.DurationSeconds, timeout.Token));
                pending.Add(TryNativeLyricsAsync(item.Id, timeout.Token));
                pending.Add(TryYouTubeTranscriptAsync(item.Id, timeout.Token));
                if (!string.IsNullOrWhiteSpace(altArtist))
                    pending.Add(TryUnisonAsync(altTitle, altArtist, item.Album?.Name, item.DurationSeconds, item.Id, timeout.Token));
            }
            var candidates = new List<LyricsData>();
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending);
                var task = (Task<LyricsData?>)finished;
                pending.Remove(task);
                LyricsData? data = null;
                try { data = await task; } catch { }
                if (data is null || data.Lines.Count == 0) continue;
                data.VideoId = alternatives.FirstOrDefault(x => x.Id == data.VideoId)?.Id ?? data.VideoId;
                if (data.IsSynced && IsUsableSynced(data, durationSec))
                {
                    timeout.Cancel();
                    return data;
                }
                if (!data.IsSynced) candidates.Add(data);
            }
            return candidates.OrderByDescending(x => ScoreCandidate(x, durationSec)).FirstOrDefault();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private async Task<LyricsData?> TryUnisonAsync(string title, string artist, string? album, int durationSec, string videoId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            foreach (var url in new[]
            {
                $"https://unison.boidu.dev/lyrics?v={Uri.EscapeDataString(videoId)}",
                $"https://unison.boidu.dev/lyrics?song={Uri.EscapeDataString(title)}&artist={Uri.EscapeDataString(artist)}"
                    + (string.IsNullOrWhiteSpace(album) ? "" : $"&album={Uri.EscapeDataString(album)}")
                    + (durationSec > 0 ? $"&duration={durationSec}" : ""),
            })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "EchoMusicLyrics/2.0.0");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var response = await _http.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode) continue;
                var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                if (root?["success"]?.GetValue<bool>() != true) continue;
                var raw = root["data"]?["lyrics"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var lines = ParseLrc(raw, out var wordSync);
                if (lines.Count == 0)
                    lines = SplitPlainLyrics(raw);
                return new LyricsData
                {
                    VideoId = videoId,
                    IsSynced = lines.Any(x => x.TimeSec.HasValue),
                    HasWordSync = wordSync,
                    Provider = wordSync ? "Unison Word-Sync" : "Unison",
                    Lines = lines,
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }
        return null;
    }

    private static bool IsUsableSynced(LyricsData data, int durationSec)
    {
        if (!data.IsSynced || data.Lines.Count < 5) return false;
        var timed = data.Lines.Where(x => x.TimeSec.HasValue).ToList();
        if (timed.Count < 5) return false;
        if (durationSec > 0 && timed.Count(x => x.TimeSec is double t && t < durationSec + 20) < Math.Min(5, timed.Count)) return false;
        double previous = -1;
        foreach (var line in timed)
        {
            var time = line.TimeSec!.Value;
            if (time < 0 || time < previous) return false;
            previous = time;
        }
        return data.Lines.Count <= 1200;
    }

    private static int ScoreCandidate(LyricsData data, int durationSec)
    {
        var timed = data.Lines.Where(x => x.TimeSec.HasValue).ToList();
        int score = Math.Min(timed.Count, 100) / 2 + Math.Min(data.Lines.Count, 160) / 8;
        if (data.HasWordSync) score += 30;
        if (data.Provider.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase)) score += 12;
        if (data.Provider.Contains("SimpMusic", StringComparison.OrdinalIgnoreCase)) score += 4;
        if (durationSec > 0 && timed.Count > 0 && timed[^1].TimeSec <= durationSec + 12) score += 8;
        return score;
    }

    internal static void AutoAlignWithConsensus(LyricsData selected, IEnumerable<LyricsData> alternatives, int durationSec)
    {
        // Estimate one constant time shift per independent provider, then shift only when
        // matched lyric anchors agree. This handles ordinary small timing drift and long
        // missing/extra intros without blindly offsetting every song.
        var providerOffsets = new List<(string Provider, double Offset)>();
        foreach (var alternative in alternatives)
        {
            var selectedLines = selected.Lines
                .Where(x => x.TimeSec.HasValue)
                .GroupBy(x => NormalizeLyricText(x.Text))
                .Where(g => g.Key.Length > 0)
                .ToDictionary(g => g.Key, g => g.Select(x => x.TimeSec!.Value).ToList());
            var altLines = alternative.Lines
                .Where(x => x.TimeSec.HasValue)
                .GroupBy(x => NormalizeLyricText(x.Text))
                .Where(g => g.Key.Length > 0)
                .ToDictionary(g => g.Key, g => g.Select(x => x.TimeSec!.Value).ToList());
            var deltas = new List<double>();
            foreach (var (text, altTimes) in altLines)
            {
                if (!selectedLines.TryGetValue(text, out var selectedTimes)) continue;
                int n = Math.Min(altTimes.Count, selectedTimes.Count);
                for (int i = 0; i < n; i++) deltas.Add(altTimes[i] - selectedTimes[i]);
            }
            if (deltas.Count < 5) continue;
            double providerMedian = Median(deltas);
            double providerMad = Median(deltas.Select(x => Math.Abs(x - providerMedian)).ToList());
            if (providerMad <= 0.45)
            {
                string providerName = alternative.Provider.Split('·')[0].Trim();
                if (!providerOffsets.Any(x => string.Equals(x.Provider, providerName, StringComparison.OrdinalIgnoreCase)))
                    providerOffsets.Add((providerName, providerMedian));
            }
        }

        if (providerOffsets.Count == 0) return;
        var offsets = providerOffsets.Select(x => x.Offset).ToList();
        double median = Median(offsets);
        double mad = Median(offsets.Select(x => Math.Abs(x - median)).ToList());
        if (mad > 0.45 || Math.Abs(median) < 0.12) return;
        // A single alternate source can safely correct small drift; large intro-length shifts
        // require two independent providers to agree, avoiding a bad source moving the whole song.
        if (Math.Abs(median) > 2.5 && providerOffsets.Count < 2) return;
        double maxOffset = durationSec > 0 ? Math.Min(90, durationSec * 0.35) : 90;
        if (Math.Abs(median) > maxOffset) return;
        var shifted = selected.Lines.Where(x => x.TimeSec.HasValue).Select(x => x.TimeSec!.Value + median).ToList();
        if (shifted.Count < 5 || shifted.Any(x => x < -5 || (durationSec > 0 && x > durationSec + 20))) return;

        foreach (var line in selected.Lines)
        {
            if (line.TimeSec is double time) line.TimeSec = Math.Max(0, time + median);
            if (line.EndSec is double end) line.EndSec = Math.Max(0, end + median);
            foreach (var word in line.Words)
                word.TimeSec = Math.Max(0, word.TimeSec + median);
        }
        selected.AppliedTimingOffsetSeconds += median;
        selected.Provider += $" · consensus aligned {median:+0.00;-0.00}s";
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 0 ? (values[mid - 1] + values[mid]) / 2 : values[mid];
    }

    private static string NormalizeLyricText(string text) =>
        Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static bool TitlesAreCompatible(string expected, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(expected)) return true;
        var a = NormalizeLyricText(CleanTitle(expected)).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var b = NormalizeLyricText(CleanTitle(candidate)).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (a.Count == 0 || b.Count == 0) return true;
        if (string.Join(' ', a).Contains(string.Join(' ', b), StringComparison.Ordinal) ||
            string.Join(' ', b).Contains(string.Join(' ', a), StringComparison.Ordinal)) return true;
        return a.Intersect(b).Count() / (double)Math.Min(a.Count, b.Count) >= 0.5;
    }

    private async Task<LyricsData?> TrySimpMusicAsync(string videoId, string expectedTitle, int durationSec, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(videoId)) return null;
        foreach (var baseUrl in new[]
        {
            "https://api-lyrics.simpmusic.org/v1/",
            "https://vivi-yt-music-server.onrender.com/v1/",
        })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + Uri.EscapeDataString(videoId));
                request.Headers.TryAddWithoutValidation("User-Agent", "SimpMusicLyrics/1.0");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var response = await _http.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode) continue;

                var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                if (root?["success"]?.GetValue<bool>() != true || root["data"] is not JsonArray rows)
                    continue;
                var candidates = rows.OfType<JsonObject>().ToList();
                if (candidates.Count == 0) continue;
                if (durationSec > 0)
                {
                    var withDuration = candidates.Where(x => (x["durationSeconds"]?.GetValue<double>() ?? 0) > 0).ToList();
                    if (withDuration.Count > 0)
                    {
                        candidates = withDuration
                            .Where(x => Math.Abs((x["durationSeconds"]?.GetValue<double>() ?? 0) - durationSec) <= 15)
                            .ToList();
                        if (candidates.Count == 0) continue;
                    }
                }

                foreach (var item in candidates)
                {
                    var candidateTitle = item["title"]?.ToString() ?? item["trackName"]?.ToString() ?? "";
                    if (!TitlesAreCompatible(expectedTitle, candidateTitle)) continue;
                    var synced = item["syncedLyrics"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(synced))
                    {
                        var lines = ParseLrc(synced, out var wordSync);
                        if (lines.Count > 0)
                            return new LyricsData
                            {
                                VideoId = videoId,
                                IsSynced = true,
                                HasWordSync = wordSync,
                                Provider = wordSync ? "SimpMusic Word-Sync" : "SimpMusic Synced",
                                Lines = lines,
                            };
                    }
                    var plain = item["plainLyrics"]?.GetValue<string>() ?? item["plainLyric"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(plain))
                    {
                        var parsed = ParseLrc(plain, out var wordSync);
                        var lines = parsed.Count > 0 ? parsed : SplitPlainLyrics(Regex.Replace(plain, @"^\s*\[[^\]]+\]\s*", "", RegexOptions.Multiline));
                        if (lines.Count > 0)
                            return new LyricsData
                            {
                                VideoId = videoId,
                                IsSynced = parsed.Count > 0,
                                HasWordSync = wordSync,
                                Provider = parsed.Count > 0 ? "SimpMusic Synced" : "SimpMusic Plain",
                                Lines = lines,
                            };
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* try next provider */ }
        }
        return null;
    }

    private static readonly string[] YouLyPlusServers =
    {
        "https://lyricsplus.prjktla.my.id",
        "https://lyricsplus.atomix.one",
        "https://lyricsplus.binimum.org",
        "https://lyricsplus.prjktla.workers.dev",
        "https://lyricsplus-seven.vercel.app",
        "https://lyrics-plus-backend.vercel.app",
    };

    private async Task<LyricsData?> TryYouLyPlusAsync(string title, string artist, string? album, int durationSec, string videoId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        race.CancelAfter(TimeSpan.FromSeconds(6));
        var tasks = YouLyPlusServers.Select(server => FetchYouLyPlusAsync(server, title, artist, album, durationSec, videoId, race.Token)).ToList();
        LyricsData? plain = null;
        try
        {
            while (tasks.Count > 0)
            {
                var task = await Task.WhenAny(tasks);
                tasks.Remove(task);
                LyricsData? lyrics = null;
                try { lyrics = await task; } catch { }
                if (lyrics is { IsSynced: true } && IsUsableSynced(lyrics, durationSec)) { race.Cancel(); return lyrics; }
                if (lyrics is { Lines.Count: > 0 } && (plain is null || lyrics.Lines.Count > plain.Lines.Count)) plain = lyrics;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }
        return plain;
    }

    private async Task<LyricsData?> FetchYouLyPlusAsync(string server, string title, string artist, string? album, int durationSec, string videoId, CancellationToken ct)
    {
        try
        {
            string query = $"title={Uri.EscapeDataString(title)}&artist={Uri.EscapeDataString(artist)}&duration={Math.Max(0, durationSec)}"
                + (string.IsNullOrWhiteSpace(album) ? "" : $"&album={Uri.EscapeDataString(album)}")
                + (string.IsNullOrWhiteSpace(videoId) ? "" : $"&id={Uri.EscapeDataString(videoId)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, server.TrimEnd('/') + "/v2/lyrics/get?" + query);
            request.Headers.TryAddWithoutValidation("User-Agent", "EchoMusicLyrics/2.6");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            if (root is null) return null;
            var synced = root["syncedLyrics"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(synced))
            {
                var lines = ParseLrc(synced, out var wordSync);
                if (lines.Count > 0)
                    return new LyricsData { VideoId = videoId, IsSynced = true, HasWordSync = wordSync, Provider = wordSync ? "YouLyPlus Word-Sync" : "YouLyPlus Synced", Lines = lines };
            }
            var plain = root["plainLyrics"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(plain))
            {
                var lines = ParseLrc(plain, out var wordSync);
                if (lines.Count == 0)
                    lines = SplitPlainLyrics(plain);
                if (lines.Count > 0)
                    return new LyricsData { VideoId = videoId, IsSynced = lines.Any(x => x.TimeSec.HasValue), HasWordSync = wordSync, Provider = "YouLyPlus Plain", Lines = lines };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }
        return null;
    }

    private async Task<LyricsData?> TryBetterLyricsAsync(string title, string artist, int durationSec, string? album, string videoId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            var query = $"s={Uri.EscapeDataString(title)}&a={Uri.EscapeDataString(artist)}"
                + (durationSec > 0 ? $"&d={durationSec}" : "")
                + (string.IsNullOrWhiteSpace(album) ? "" : $"&al={Uri.EscapeDataString(album)}");
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://lyrics-api.boidu.dev/getLyrics?" + query);
            req.Headers.TryAddWithoutValidation("User-Agent", "EchoMusicLyrics/2.0.0");
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var res = await _http.SendAsync(req, timeout.Token);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadAsStringAsync(timeout.Token);
            var json = JsonNode.Parse(body);
            var ttml = json?["ttml"]?.GetValue<string>() ?? json?["data"]?["ttml"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(ttml))
            {
                var parsed = ParseTtmlLyrics(ttml, "BetterLyrics TTML");
                if (parsed is { Lines.Count: > 0 }) { parsed.VideoId = videoId; return parsed; }
            }
            var synced = json?["syncedLyrics"]?.GetValue<string>() ?? json?["elrc"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(synced))
            {
                var lines = ParseLrc(synced, out bool wordSync);
                if (lines.Count > 0) return new LyricsData { VideoId = videoId, IsSynced = lines.Count(x => x.TimeSec.HasValue) >= 5, HasWordSync = wordSync, Provider = "BetterLyrics", Lines = lines };
            }
            var plain = json?["plainLyrics"]?.GetValue<string>() ?? json?["plain"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(plain)) return new LyricsData { VideoId = videoId, IsSynced = false, Provider = "BetterLyrics Plain", Lines = SplitPlainLyrics(plain) };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }
        return null;
    }

    private async Task<LyricsData?> TryPaxSenixAsync(string title, string artist, int durationSec, string? album, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(7));
        try
        {
            JsonArray? results = null;
            var queries = new[] { $"{title} {artist}".Trim(), title, string.IsNullOrWhiteSpace(album) ? "" : $"{title} {artist} {album}".Trim() }
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var query in queries)
            {
                using var search = new HttpRequestMessage(HttpMethod.Get, "https://lyrics.paxsenix.org/apple-music/search?q=" + Uri.EscapeDataString(query));
                search.Headers.TryAddWithoutValidation("User-Agent", "echomusic/2.0.0");
                search.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var response = await _http.SendAsync(search, timeout.Token);
                if (!response.IsSuccessStatusCode) continue;
                var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                results = root as JsonArray ?? root?["results"] as JsonArray ?? root?["data"] as JsonArray;
                if (results is { Count: > 0 }) break;
            }
            if (results is null || results.Count == 0) return null;

            var candidates = results.Where(x => x is not null).Select(x =>
            {
                var item = x!;
                string display = item["displayName"]?.GetValue<string>() ?? item["title"]?.GetValue<string>() ?? item["name"]?.GetValue<string>() ?? "";
                string artistName = item["displayArtist"]?.GetValue<string>() ?? item["artistName"]?.GetValue<string>() ?? item["artist"]?.GetValue<string>() ?? "";
                string id = item["id"]?.ToString() ?? item["trackId"]?.ToString() ?? "";
                double duration = item["duration"]?.GetValue<double>() ?? item["durationMs"]?.GetValue<double>() ?? 0;
                if (duration > 10000) duration /= 1000.0;
                double score = 0;
                var candidateTitle = CleanTitle(display);
                if (string.Equals(candidateTitle, title, StringComparison.OrdinalIgnoreCase)) score += 50;
                else if (candidateTitle.Contains(title, StringComparison.OrdinalIgnoreCase) || title.Contains(candidateTitle, StringComparison.OrdinalIgnoreCase)) score += 25;
                else score -= 30;
                if (ArtistsMatch(artistName, artist)) score += 25;
                if (durationSec > 0 && duration > 0) score += Math.Max(-20, 20 - Math.Abs(duration - durationSec) * 1.5);
                return (Id: id, Score: score);
            }).Where(x => !string.IsNullOrWhiteSpace(x.Id) && x.Score > 0).OrderByDescending(x => x.Score).Take(5).ToList();

            LyricsData? plainFallback = null;
            foreach (var candidate in candidates)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://lyrics.paxsenix.org/apple-music/lyrics?id=" + Uri.EscapeDataString(candidate.Id));
                request.Headers.TryAddWithoutValidation("User-Agent", "echomusic/2.0.0");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var response = await _http.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode) continue;
                var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                if (root is null) continue;
                var ttml = root["ttmlContent"]?.GetValue<string>() ?? root["ttml"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(ttml))
                {
                    var timed = ParseTtmlLyrics(ttml, "PaxSenix TTML");
                    if (timed is { Lines.Count: > 0 }) { timed.VideoId = ""; return timed; }
                }
                foreach (var key in new[] { "elrcMultiPerson", "elrc" })
                {
                    var lrc = root[key]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(lrc)) continue;
                    var lines = ParseLrc(lrc, out bool wordSync);
                    if (lines.Count == 0) continue;
                    var data = new LyricsData { IsSynced = lines.Count(x => x.TimeSec.HasValue) >= 5, HasWordSync = wordSync, Provider = "PaxSenix", Lines = lines };
                    if (data.IsSynced) return data;
                    plainFallback ??= data;
                }
                var plain = root["plain"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(plain)) plainFallback ??= new LyricsData { Provider = "PaxSenix Plain", Lines = SplitPlainLyrics(plain) };
                if (root["content"] is JsonArray content)
                {
                    var lines = new List<LyricLine>();
                    foreach (var line in content)
                    {
                        if (line is null) continue;
                        var textParts = line["text"] as JsonArray;
                        string text = textParts is null ? line["text"]?.ToString() ?? "" : string.Join(" ", textParts.Select(w => w?["text"]?.ToString() ?? ""));
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        double ms = line["timestamp"]?.GetValue<double>() ?? 0;
                        lines.Add(new LyricLine { TimeSec = ms > 0 ? ms / 1000.0 : null, Text = text.Trim() });
                    }
                    if (lines.Count > 0)
                    {
                        var data = new LyricsData { IsSynced = lines.Count(x => x.TimeSec.HasValue) >= 5, Provider = "PaxSenix", Lines = lines };
                        if (data.IsSynced) return data;
                        plainFallback ??= data;
                    }
                }
            }
            return plainFallback;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static LyricsData? ParseTtmlLyrics(string ttml, string provider)
    {
        try
        {
            var doc = XDocument.Parse(ttml, LoadOptions.PreserveWhitespace);
            var lines = new List<LyricLine>();
            foreach (var p in doc.Descendants().Where(x => x.Name.LocalName == "p"))
            {
                var text = Regex.Replace(string.Concat(p.DescendantNodes().OfType<XText>().Select(x => x.Value)), @"\\s+", " ").Trim();
                if (text.Length == 0) continue;
                double? begin = ParseTtmlTime((string?)p.Attribute("begin"));
                if (!begin.HasValue) continue;
                var words = new List<LyricWord>();
                foreach (var span in p.Descendants().Where(x => x.Name.LocalName == "span"))
                {
                    var wordText = Regex.Replace(string.Concat(span.DescendantNodes().OfType<XText>().Select(x => x.Value)), @"\\s+", " ").Trim();
                    var wordTime = ParseTtmlTime((string?)span.Attribute("begin"));
                    if (wordText.Length > 0 && wordTime.HasValue) words.Add(new LyricWord { TimeSec = wordTime.Value, Text = wordText });
                }
                lines.Add(new LyricLine { TimeSec = begin, EndSec = ParseTtmlTime((string?)p.Attribute("end")), Text = text, Words = words });
            }
            lines.Sort((a,b) => Nullable.Compare(a.TimeSec,b.TimeSec));
            for (int i=0;i<lines.Count-1;i++) if (!lines[i].EndSec.HasValue) lines[i].EndSec=lines[i+1].TimeSec;
            if (lines.Count == 0) return null;
            return new LyricsData { IsSynced = lines.Count(x => x.TimeSec.HasValue) >= 5, HasWordSync = lines.Count(x => x.Words.Count > 1) > 0, Provider = provider, Lines = lines };
        }
        catch { return null; }
    }

    private static double? ParseTtmlTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase) && double.TryParse(value[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)) return seconds;
        if (TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var ts)) return ts.TotalSeconds;
        return null;
    }

    private async Task<LyricsData?> TryLrclibAsync(string title, string artist, string? album, int durationSec, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        LyricsData? plainFallback = null;

        // Exact catalog lookup is fast when YouTube metadata lines up; an untimed exact match is
        // held as a fallback while search still looks for a better timestamped record.
        if (!string.IsNullOrWhiteSpace(artist) && durationSec > 0)
        {
            try
            {
                var qs = $"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}&duration={durationSec}"
                         + (string.IsNullOrWhiteSpace(album) ? "" : $"&album_name={Uri.EscapeDataString(album)}");
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://lrclib.net/api/get?{qs}");
                req.Headers.TryAddWithoutValidation("User-Agent", "EchoMusicDesktop/2.0.0 (https://github.com/EchoMusicApp/Echo-Music)");
                using var res = await _http.SendAsync(req, timeout.Token);
                if (res.IsSuccessStatusCode)
                {
                    var parsed = ParseLrclibRecord(JsonNode.Parse(await res.Content.ReadAsStringAsync(timeout.Token)));
                    if (parsed is { IsSynced: true } || parsed is { IsInstrumental: true }) return parsed;
                    plainFallback = parsed;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        var queries = new[] { title, string.IsNullOrWhiteSpace(artist) ? title : $"{title} {artist}" }
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        JsonNode? best = null;
        double bestScore = double.NegativeInfinity;
        bool bestTitleMatched = false;
        bool bestHasSynced = false;
        foreach (var query in queries)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://lrclib.net/api/search?q={Uri.EscapeDataString(query)}");
                req.Headers.TryAddWithoutValidation("User-Agent", "EchoMusicDesktop/2.0.0 (https://github.com/EchoMusicApp/Echo-Music)");
                using var res = await _http.SendAsync(req, timeout.Token);
                if (!res.IsSuccessStatusCode) continue;
                var arr = JsonNode.Parse(await res.Content.ReadAsStringAsync(timeout.Token)) as JsonArray;
                if (arr is null) continue;
                foreach (var item in arr)
                {
                    if (item is null) continue;
                    var (score, titleMatched, hasSynced) = ScoreLrclibRecord(item, title, artist, durationSec);
                    if (titleMatched && score > bestScore)
                    {
                        bestScore = score;
                        best = item;
                        bestTitleMatched = titleMatched;
                        bestHasSynced = hasSynced;
                    }
                }
                // A good exact, duration-compatible timed match is enough; otherwise search the
                // second query as well, especially when title-only search returned plain lyrics.
                if (bestTitleMatched && bestHasSynced && bestScore >= 45) break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
        double minScore = bestHasSynced ? 28 : 15;
        if (bestTitleMatched && bestScore >= minScore)
        {
            var parsed = ParseLrclibRecord(best);
            if (parsed is { IsSynced: true } || parsed is { IsInstrumental: true }) return parsed;
            plainFallback ??= parsed;
        }
        return plainFallback;
    }

    private static (double Score, bool TitleMatched, bool HasSynced) ScoreLrclibRecord(JsonNode item, string title, string artist, int durationSec)
    {
        var synced = item["syncedLyrics"]?.GetValue<string>();
        var plain = item["plainLyrics"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(synced) && string.IsNullOrWhiteSpace(plain)) return (double.NegativeInfinity, false, false);
        double score = !string.IsNullOrWhiteSpace(synced) ? 25 : 6;
        var recordTitle = CleanTitle(item["trackName"]?.GetValue<string>() ?? "");
        bool titleMatched = string.Equals(recordTitle, title, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(recordTitle) && (recordTitle.Contains(title, StringComparison.OrdinalIgnoreCase) || title.Contains(recordTitle, StringComparison.OrdinalIgnoreCase)));
        if (string.Equals(recordTitle, title, StringComparison.OrdinalIgnoreCase)) score += 20;
        else if (titleMatched) score += 10;
        else score -= 35;
        var recordArtist = item["artistName"]?.GetValue<string>() ?? "";
        if (!string.IsNullOrWhiteSpace(artist))
            score += ArtistsMatch(recordArtist, artist) ? 18 : -20;
        if (durationSec > 0 && item["duration"]?.GetValue<double>() is double d)
        {
            double diff = Math.Abs(d - durationSec);
            if (diff <= 3) score += 14;
            else if (diff <= 10) score += 5;
            else if (diff > 30) score -= 20;
        }
        return (score, titleMatched, !string.IsNullOrWhiteSpace(synced));
    }

    private static bool ArtistsMatch(string providerArtist, string expectedArtist)
    {
        static string N(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        var p = N(providerArtist); var e = N(expectedArtist);
        if (p.Length == 0 || e.Length == 0) return false;
        return p.Contains(e, StringComparison.Ordinal) || e.Contains(p, StringComparison.Ordinal)
            || e.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length > 3).Any(p.Contains);
    }

    private static LyricsData? ParseLrclibRecord(JsonNode? rec)
    {
        if (rec is null) return null;
        if (rec["instrumental"]?.GetValue<bool>() == true)
        {
            return new LyricsData
            {
                IsInstrumental = true,
                Provider = "LRCLIB",
                Lines = new List<LyricLine> { new() { Text = "♪ Instrumental ♪" } }
            };
        }
        var synced = rec["syncedLyrics"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(synced))
        {
            var lines = ParseLrc(synced, out bool wordSync);
            if (lines.Count > 0)
            {
                return new LyricsData
                {
                    IsSynced = true,
                    HasWordSync = wordSync,
                    Provider = wordSync ? "LRCLIB Word-Sync" : "LRCLIB Synced",
                    Lines = lines,
                };
            }
        }
        var plain = rec["plainLyrics"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(plain))
        {
            var lines = SplitPlainLyrics(plain);
            if (lines.Count > 0)
                return new LyricsData { IsSynced = false, Provider = "LRCLIB Plain", Lines = lines };
        }
        return null;
    }

    private async Task<LyricsData?> TryKuGouAsync(string title, string artist, int durationSec, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var kw = Uri.EscapeDataString($"{artist} - {title}".Trim(' ', '-'));
            var durMs = durationSec > 0 ? durationSec * 1000 : 200_000;
            var searchUrl = $"https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&keyword={kw}&duration={durMs}&hash=";
            using var sRes = await _http.GetAsync(searchUrl, timeout.Token);
            if (!sRes.IsSuccessStatusCode) return null;
            var sJson = JsonNode.Parse(await sRes.Content.ReadAsStringAsync(timeout.Token));
            var cand = (sJson?["candidates"] as JsonArray)?.FirstOrDefault();
            var id = cand?["id"]?.ToString();
            var accesskey = cand?["accesskey"]?.GetValue<string>();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(accesskey)) return null;

            var dlUrl = $"https://lyrics.kugou.com/download?ver=1&client=pc&id={Uri.EscapeDataString(id)}&accesskey={Uri.EscapeDataString(accesskey)}&fmt=lrc&charset=utf8";
            using var dRes = await _http.GetAsync(dlUrl, timeout.Token);
            if (!dRes.IsSuccessStatusCode) return null;
            var dJson = JsonNode.Parse(await dRes.Content.ReadAsStringAsync(timeout.Token));
            var b64 = dJson?["content"]?.GetValue<string>();
            if (string.IsNullOrEmpty(b64)) return null;

            var lrcText = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var lines = ParseLrc(lrcText, out bool wordSync);
            if (lines.Count == 0) return null;
            return new LyricsData
            {
                IsSynced = true,
                HasWordSync = wordSync,
                Provider = "KuGou Synced",
                Lines = lines,
            };
        }
        catch
        {
            return null;
        }
    }

    public static List<LyricLine> SplitPlainLyrics(string raw, int maxCharsPerLine = 56)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<LyricLine>();
        var text = WebUtility.HtmlDecode(raw)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\u00A0', ' ');
        text = Regex.Replace(text, @"(?i)<br\s*/?>|</p>\s*<p[^>]*>", "\n");
        text = Regex.Replace(text, @"[\t ]+", " ");
        var result = new List<LyricLine>();
        foreach (var source in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new StringBuilder();
            foreach (var word in words)
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > maxCharsPerLine)
                {
                    result.Add(new LyricLine { Text = line.ToString() });
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            if (line.Length > 0) result.Add(new LyricLine { Text = line.ToString() });
        }
        return result;
    }

    public static List<LyricLine> ParseLrc(string raw, out bool hasWordSync)
    {
        hasWordSync = false;
        var lines = new List<LyricLine>();
        var tagRx = new Regex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]");
        var wordRx = new Regex(@"<(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?>([^<]*)");

        foreach (var rawLine in raw.Split('\n'))
        {
            var matches = tagRx.Matches(rawLine);
            if (matches.Count == 0) continue;
            var body = tagRx.Replace(rawLine, "").Trim();

            var words = new List<LyricWord>();
            var wm = wordRx.Matches(body);
            if (wm.Count >= 2)
            {
                hasWordSync = true;
                var cleanSb = new StringBuilder();
                foreach (Match w in wm)
                {
                    double wt = ParseTagTime(w.Groups[1].Value, w.Groups[2].Value, w.Groups[3].Value);
                    var wtxt = w.Groups[4].Value;
                    if (!string.IsNullOrEmpty(wtxt))
                    {
                        words.Add(new LyricWord { TimeSec = wt, Text = wtxt });
                        cleanSb.Append(wtxt);
                    }
                }
                body = cleanSb.ToString().Trim();
            }
            else
            {
                body = Regex.Replace(body, @"<\d{1,3}:\d{2}(?:[.:]\d{1,3})?>", "").Trim();
            }

            foreach (Match m in matches)
            {
                double t = ParseTagTime(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
                lines.Add(new LyricLine
                {
                    TimeSec = t,
                    Text = string.IsNullOrEmpty(body) ? "♪" : body,
                    Words = words,
                });
            }
        }

        lines.Sort((a, b) => Nullable.Compare(a.TimeSec, b.TimeSec));

        // Some providers mark an entire lyric dump with only one or two timestamps.
        // That is not line-level sync: render it as readable plain lyric lines instead
        // of presenting the full song as one giant karaoke row or claiming false sync.
        var combinedText = string.Join(" ", lines.Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!hasWordSync && lines.Count <= 3 && combinedText.Length > 180)
        {
            hasWordSync = false;
            return SplitPlainLyrics(combinedText, maxCharsPerLine: 56);
        }

        for (int i = 0; i < lines.Count - 1; i++)
            lines[i].EndSec = lines[i + 1].TimeSec;
        return lines;
    }

    private static double ParseTagTime(string min, string sec, string frac)
    {
        int m = int.Parse(min);
        int s = int.Parse(sec);
        double f = string.IsNullOrEmpty(frac) ? 0 : int.Parse(frac.PadRight(3, '0')[..3]) / 1000.0;
        return m * 60 + s + f;
    }

    public async Task TranslateLyricsAsync(LyricsData data, string targetLang, AppSettings settings, CancellationToken ct = default)
    {
        if (data.Lines.Count == 0 || string.IsNullOrWhiteSpace(targetLang)) return;

        // 1. Custom OpenAI-compatible AI Route if configured by user in Settings (Echo Music AI Lyric Translation!)
        if (!string.IsNullOrWhiteSpace(settings.CustomAiEndpoint) && !string.IsNullOrWhiteSpace(settings.CustomAiApiKey))
        {
            try
            {
                var numbered = string.Join("\n", data.Lines.Select((l, idx) => $"{idx}: {l.Text}"));
                var payload = new JsonObject
                {
                    ["model"] = string.IsNullOrWhiteSpace(settings.CustomAiModel) ? "gpt-4o-mini" : settings.CustomAiModel,
                    ["messages"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["role"] = "system",
                            ["content"] = $"Translate each numbered song lyric line faithfully into {targetLang}. Avoid long dash punctuation and use commas or periods instead. Reply ONLY with 'index: translated line' per line."
                        },
                        new JsonObject { ["role"] = "user", ["content"] = numbered }
                    },
                    ["temperature"] = 0.2
                };
                using var req = new HttpRequestMessage(HttpMethod.Post, settings.CustomAiEndpoint.TrimEnd('/') + "/chat/completions")
                {
                    Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {settings.CustomAiApiKey}");
                using var res = await _http.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    var j = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
                    var content = j?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
                    foreach (var line in content.Split('\n'))
                    {
                        var colon = line.IndexOf(':');
                        if (colon > 0 && int.TryParse(line[..colon].Trim(), out int idx) && idx >= 0 && idx < data.Lines.Count)
                            data.Lines[idx].TranslatedText = line[(colon + 1)..].Trim().Replace('\u2014', ',');
                    }
                    return;
                }
            }
            catch { /* fall through to built-in translator */ }
        }

        // 2. Built-in instant zero-key translator via Google Translate GTX batch
        try
        {
            const int batchSize = 25;
            for (int start = 0; start < data.Lines.Count; start += batchSize)
            {
                var slice = data.Lines.Skip(start).Take(batchSize).ToList();
                var joined = string.Join("\n", slice.Select(l => l.Text == "♪" ? "..." : l.Text));
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLang)}&dt=t&q={Uri.EscapeDataString(joined)}";
                using var res = await _http.GetAsync(url, ct);
                if (!res.IsSuccessStatusCode) break;
                var root = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
                var sb = new StringBuilder();
                if (root?[0] is JsonArray segs)
                {
                    foreach (var seg in segs)
                        if (seg?[0]?.GetValue<string>() is string piece)
                            sb.Append(piece);
                }
                var translatedLines = sb.ToString().Split('\n');
                for (int i = 0; i < slice.Count && i < translatedLines.Length; i++)
                {
                    var tr = translatedLines[i].Trim();
                    if (!string.IsNullOrEmpty(tr) && tr != "..." && !string.Equals(tr, slice[i].Text, StringComparison.OrdinalIgnoreCase))
                        slice[i].TranslatedText = tr;
                }
            }
        }
        catch { }
    }

    private static string CleanTitle(string s)
    {
        // YouTube uploads commonly append credits/channel branding after the real title.
        // Keeping that suffix made metadata lyric providers search for a different song.
        var r = Regex.Replace(s, @"\s*[|–-]\s*(?:song|music|vocals?)\s+by\b.*$", "", RegexOptions.IgnoreCase);
        r = Regex.Replace(r, @"\s*[\(\[]\s*(?:official|lyric|audio|video|visualizer|hd|4k|remaster|mv|live|prod\.)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
        r = Regex.Replace(r, @"\s*-\s*(?:remastered|single version|radio edit).*$", "", RegexOptions.IgnoreCase);
        // Keep the real title as the primary search query, but strip contributor tails like
        // "Kill Chori ft. ..."; full/raw variants are still attempted by providers that need them.
        r = Regex.Replace(r, @"\s+(?:ft|feat|featuring)\.?\s+.*$", "", RegexOptions.IgnoreCase);
        return Regex.Replace(r, @"\s+", " ").Trim();
    }

    private static List<string> GetTitleVariants(string raw)
    {
        var cleaned = CleanTitle(raw);
        var withoutYoutubeCredits = Regex.Replace(raw, @"\s*[|–-]\s*(?:song|music|vocals?)\s+by\b.*$", "", RegexOptions.IgnoreCase);
        withoutYoutubeCredits = Regex.Replace(withoutYoutubeCredits, @"\s*[\(\[]\s*(?:official|lyric|audio|video|visualizer|hd|4k|remaster|mv|live|prod\.)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
        withoutYoutubeCredits = Regex.Replace(withoutYoutubeCredits, @"\s+", " ").Trim();
        var withoutFeat = Regex.Replace(withoutYoutubeCredits, @"\s+(?:ft|feat|featuring)\.?\s+.*$", "", RegexOptions.IgnoreCase);
        return new[] { cleaned, withoutYoutubeCredits, CleanTitle(withoutFeat) }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CleanArtist(string s)
    {
        var r = Regex.Replace(s, @"\s*-\s*Topic$", "", RegexOptions.IgnoreCase);
        r = Regex.Split(r, @"\s*(?:,|&|feat\.?|ft\.?|×)\s*", RegexOptions.IgnoreCase)[0];
        return r.Trim();
    }
}
