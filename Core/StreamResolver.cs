using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EchoMusic.Core;

public sealed class ResolvedStreamResult
{
    public ResolvedStream Stream { get; init; } = new();
    public byte[] InitialPage0 { get; init; } = Array.Empty<byte>();
}

public sealed class StreamResolver
{
    // Mirror the Android playback order: full-file clients first, preview-only profiles last.
    private static ClientSpec[] GetCascade() => new[]
    {
        YouTubeClients.VisionOs,
        YouTubeClients.AndroidVr,
        YouTubeClients.TvHtml5,
        YouTubeClients.AndroidVrLegacy,
        YouTubeClients.IpadOs,
        YouTubeClients.Ios,
    };

    private static readonly string[] PipedInstances =
    {
        "https://pipedapi.kavin.rocks",
        "https://pipedapi.tokhmi.xyz",
        "https://pipedapi.moomoo.me",
    };

    private readonly InnerTubeClient _yt;
    private readonly Func<string> _qualitySetting;
    private readonly ConcurrentDictionary<string, ResolvedStream> _cache = new(StringComparer.Ordinal);

    public StreamResolver(InnerTubeClient yt, Func<string> qualitySetting)
    {
        _yt = yt;
        _qualitySetting = qualitySetting;
    }

    public Task<ResolvedStreamResult> ResolveVerifiedAsync(
        string videoId,
        bool preferWebmOpus = true,
        bool forceRefresh = false,
        CancellationToken ct = default)
        => ResolveVerifiedCoreAsync(videoId, preferWebmOpus, forceRefresh, ct,
            retryAfterGuestRefresh: true, visitorAlreadyRefreshed: false);

    private async Task<ResolvedStreamResult> ResolveVerifiedCoreAsync(
        string videoId,
        bool preferWebmOpus,
        bool forceRefresh,
        CancellationToken ct,
        bool retryAfterGuestRefresh,
        bool visitorAlreadyRefreshed)
    {
        var cacheKey = $"{videoId}:{(preferWebmOpus ? "opus" : "mp4")}:{_qualitySetting()}";
        if (!forceRefresh && _cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(3))
        {
            try
            {
                var (p0, total) = await BoundedRangeHttpStream.FetchPageStaticAsync(_yt.SharedHttp, cached.Url, cached.UserAgent, 0, cached.ContentLength, ct, cached.MediaOrigin);
                if (p0.Length > 256)
                {
                    if (total > cached.ContentLength) cached.ContentLength = total;
                    return new ResolvedStreamResult { Stream = cached, InitialPage0 = p0 };
                }
            }
            catch
            {
                _cache.TryRemove(cacheKey, out _);
            }
        }

        if (!visitorAlreadyRefreshed)
            await _yt.EnsureVisitorDataAsync(forceRefresh, ct);

        Exception? lastErr = null;
        string? guestChallengeMessage = null;
        var profileOutcomes = new List<(string Client, string Status, string Reason)>();
        var cascade = GetCascade();

        for (int idx = 0; idx < cascade.Length; idx++)
        {
            var client = cascade[idx];
            bool isPreviewCappedClient = client.Name is "IOS" or "IPADOS"
                || (client.Name == "ANDROID_VR" && client.Version == YouTubeClients.AndroidVrLegacy.Version);
            try
            {
                var root = await _yt.GetPlayerRawAsync(videoId, client, ct);
                var status = root?["playabilityStatus"]?["status"]?.GetValue<string>();
                if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    var reason = root?["playabilityStatus"]?["reason"]?.GetValue<string>() ?? status ?? "UNPLAYABLE";
                    profileOutcomes.Add((client.Name, status ?? "UNKNOWN", reason));
                    lastErr = new InvalidOperationException($"{client.Name}: {reason}");
                    if (IsGuestVerificationChallenge(reason))
                        guestChallengeMessage ??= lastErr.Message;
                    continue;
                }

                var adaptive = root?["streamingData"]?["adaptiveFormats"] as JsonArray;
                if (adaptive is null || adaptive.Count == 0) continue;

                // Try preferred codec first (e.g. WebM/Opus), then fallback codec (e.g. MP4/AAC on Windows)
                foreach (var chosen in RankCandidateFormats(adaptive, _qualitySetting(), preferWebmOpus))
                {
                    var url = chosen["url"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(url)) continue;

                    var mime = chosen["mimeType"]?.GetValue<string>() ?? "audio/webm";
                    // On non-Windows only WebM/Opus is decoded in pure C#
                    if (!OperatingSystem.IsWindows() && !mime.Contains("webm", StringComparison.OrdinalIgnoreCase))
                        continue;

                    long.TryParse(chosen["contentLength"]?.GetValue<string>(), out var clen);
                    string mediaUa = ResolveMediaUserAgent(url, client.UserAgent);

                    // ACTUALLY FETCH FIRST 512 KiB CHUNK NOW to prove CDN serves 206 Partial Content!
                    byte[] page0;
                    long verifiedLen;
                    try
                    {
                        (page0, verifiedLen) = await BoundedRangeHttpStream.FetchPageStaticAsync(_yt.SharedHttp, url, mediaUa, 0, clen, ct, client.Origin);
                        if (page0.Length < 256) continue;

                        // Mirror YTPlayerUtils.validateStatus: for whole-file clients, verify final byte isn't 1MiB-capped
                        if (!isPreviewCappedClient && verifiedLen > 1_200_000)
                        {
                            bool tailOk = await ProbeTailByteAsync(_yt.SharedHttp, url, mediaUa, verifiedLen, ct, client.Origin);
                            if (!tailOk)
                            {
                                lastErr = new InvalidOperationException($"{client.Name} failed tail-byte probe (preview-capped)");
                                continue;
                            }
                        }
                    }
                    catch (Exception probeEx)
                    {
                        lastErr = probeEx;
                        continue;
                    }

                    var codecMatch = Regex.Match(mime, @"codecs=""([^""]+)""");
                    int.TryParse(root?["videoDetails"]?["lengthSeconds"]?.GetValue<string>(), out var durSec);
                    var loudness = root?["playerConfig"]?["audioConfig"]?["loudnessDb"]?.GetValue<double>() ?? 0.0;
                    var expMatch = Regex.Match(url, @"[?&]expire=(\d+)");
                    var expires = expMatch.Success
                        ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(expMatch.Groups[1].Value))
                        : DateTimeOffset.UtcNow.AddHours(4);

                    var resolved = new ResolvedStream
                    {
                        VideoId = videoId,
                        Url = url,
                        MimeType = mime,
                        Codec = codecMatch.Success ? codecMatch.Groups[1].Value : "",
                        Itag = chosen["itag"]?.GetValue<int>() ?? 0,
                        Bitrate = chosen["bitrate"]?.GetValue<int>() ?? 0,
                        ContentLength = verifiedLen > 0 ? verifiedLen : clen,
                        DurationSeconds = durSec,
                        LoudnessDb = loudness,
                        ClientName = client.Name,
                        UserAgent = mediaUa,
                        MediaOrigin = client.Origin,
                        ExpiresAt = expires,
                        Title = root?["videoDetails"]?["title"]?.GetValue<string>(),
                        Author = root?["videoDetails"]?["author"]?.GetValue<string>(),
                    };

                    _cache[cacheKey] = resolved;
                    return new ResolvedStreamResult { Stream = resolved, InitialPage0 = page0 };
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastErr = ex;
                if (IsGuestVerificationChallenge(ex.Message))
                    guestChallengeMessage ??= ex.Message;
            }
        }

        // Emergency Piped API Fallback (exact mirror of YTPlayerUtils.kt:515)
        foreach (var pipedHost in PipedInstances)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(6));
                using var res = await _yt.SharedHttp.GetAsync($"{pipedHost}/streams/{Uri.EscapeDataString(videoId)}", cts.Token);
                if (!res.IsSuccessStatusCode) continue;
                var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(cts.Token));
                var streams = json?["audioStreams"] as JsonArray;
                if (streams is null || streams.Count == 0) continue;

                var best = RankCandidateFormats(streams, _qualitySetting(), preferWebmOpus)
                    .FirstOrDefault(s =>
                    {
                        var mime = s["mimeType"]?.GetValue<string>() ?? "";
                        return mime.Contains("webm", StringComparison.OrdinalIgnoreCase) || OperatingSystem.IsWindows();
                    });

                if (best is not null)
                {
                    var u = best["url"]!.GetValue<string>();
                    var mime = best["mimeType"]?.GetValue<string>() ?? "audio/webm";
                    var (p0, vlen) = await BoundedRangeHttpStream.FetchPageStaticAsync(_yt.SharedHttp, u, YouTubeClients.WebUa, 0, 0, ct);
                    if (p0.Length > 256)
                    {
                        var rs = new ResolvedStream
                        {
                            VideoId = videoId,
                            Url = u,
                            MimeType = mime,
                            Codec = best["codec"]?.GetValue<string>() ?? "opus",
                            Bitrate = best["bitrate"]?.GetValue<int>() ?? 128000,
                            ContentLength = vlen,
                            DurationSeconds = json?["duration"]?.GetValue<int>() ?? 0,
                            ClientName = "PIPED",
                            UserAgent = YouTubeClients.WebUa,
                            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                        };
                        return new ResolvedStreamResult { Stream = rs, InitialPage0 = p0 };
                    }
                }
            }
            catch { }
        }

        if (retryAfterGuestRefresh && !_yt.IsSignedIn && guestChallengeMessage is not null)
        {
            // Guest visitor IDs can become throttled independently of the app/account. Refresh one
            // time and rerun the client cascade; never turn a YouTube challenge into a fake Echo
            // sign-in requirement or loop indefinitely.
            string? previousVisitor = _yt.CurrentVisitorData;
            string? refreshedVisitor = await _yt.EnsureVisitorDataAsync(force: true, ct: ct);
            if (!ct.IsCancellationRequested
                && !string.IsNullOrWhiteSpace(refreshedVisitor)
                && !string.Equals(previousVisitor, refreshedVisitor, StringComparison.Ordinal))
            {
                return await ResolveVerifiedCoreAsync(videoId, preferWebmOpus, forceRefresh: true, ct: ct,
                    retryAfterGuestRefresh: false, visitorAlreadyRefreshed: true);
            }
        }

        if (!_yt.IsSignedIn && guestChallengeMessage is not null)
        {
            throw new InvalidOperationException(
                "YouTube temporarily blocked anonymous playback for this track. Echo does not require sign-in; please try again later or on another network.",
                new InvalidOperationException(guestChallengeMessage, lastErr));
        }

        bool unavailableAcrossProfiles = profileOutcomes.Count == cascade.Length
            && profileOutcomes.All(x => x.Status.Equals("UNPLAYABLE", StringComparison.OrdinalIgnoreCase)
                && x.Reason.Contains("video is not available", StringComparison.OrdinalIgnoreCase));
        if (unavailableAcrossProfiles)
        {
            var clients = string.Join(", ", profileOutcomes.Select(x => x.Client).Distinct(StringComparer.OrdinalIgnoreCase));
            throw new InvalidOperationException(
                $"YouTube reports this video as unavailable on every guest playback profile tried ({clients}) for the current region/session. This is not a bot-verification challenge; the video may be removed, region/rights restricted, or otherwise unavailable. Changing request headers cannot override that.",
                lastErr);
        }

        throw lastErr ?? new InvalidOperationException($"Could not resolve playable stream for {videoId}.");
    }

    private static bool IsGuestVerificationChallenge(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        return message.Contains("sign in to confirm", StringComparison.OrdinalIgnoreCase)
            || message.Contains("confirm you're not a bot", StringComparison.OrdinalIgnoreCase)
            || message.Contains("automated queries", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ResolvedStream> ResolveAsync(string videoId, bool preferWebmOpus = true, bool forceRefresh = false, CancellationToken ct = default)
        => (await ResolveVerifiedAsync(videoId, preferWebmOpus, forceRefresh, ct)).Stream;

    private static async Task<bool> ProbeTailByteAsync(HttpClient http, string url, string ua, long totalLen, CancellationToken ct, string? mediaOrigin = null)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", ua);
            if (!string.IsNullOrWhiteSpace(mediaOrigin))
            {
                req.Headers.TryAddWithoutValidation("Origin", mediaOrigin);
                req.Headers.TryAddWithoutValidation("Referer", mediaOrigin.TrimEnd('/') + "/");
            }
            req.Headers.Range = new RangeHeaderValue(totalLen - 2, totalLen - 1);
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return res.StatusCode == HttpStatusCode.PartialContent || res.IsSuccessStatusCode;
        }
        catch
        {
            return true; // IO timeout on probe -> accept optimistically like YTPlayerUtils.kt:414
        }
    }

    private static string ResolveMediaUserAgent(string streamUrl, string fallbackUa)
    {
        var m = Regex.Match(streamUrl, @"[?&]c=([^&]+)");
        if (!m.Success) return fallbackUa;
        var c = Uri.UnescapeDataString(m.Groups[1].Value).ToUpperInvariant();
        var versionMatch = Regex.Match(streamUrl, @"[?&]cver=([^&]+)");
        string version = versionMatch.Success ? Uri.UnescapeDataString(versionMatch.Groups[1].Value) : "";
        return c switch
        {
            "ANDROID_VR" when version == YouTubeClients.AndroidVrLegacy.Version => YouTubeClients.AndroidVrLegacy.UserAgent,
            "ANDROID_VR" => YouTubeClients.AndroidVr.UserAgent,
            "TVHTML5" => YouTubeClients.TvHtml5.UserAgent,
            "IPADOS" => YouTubeClients.IpadOs.UserAgent,
            "IOS" => YouTubeClients.Ios.UserAgent,
            "VISIONOS" => fallbackUa,
            _ => fallbackUa,
        };
    }

    private static List<JsonObject> RankCandidateFormats(JsonArray formats, string quality, bool preferWebmOpus)
    {
        int ceiling = quality switch
        {
            "DataSaver" => 72_000,
            "Normal" => 142_000,
            "High" => 190_000,
            _ => int.MaxValue,
        };

        var candidates = new List<JsonObject>();
        foreach (var node in formats)
        {
            if (node is not JsonObject f) continue;
            var mime = f["mimeType"]?.GetValue<string>() ?? "";
            if (!mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(f["url"]?.GetValue<string>())) continue;
            candidates.Add(f);
        }

        // Pick the highest-rate playable stream at or below the requested ceiling. If YouTube
        // does not provide a stream below that ceiling, choose the closest available rate instead
        // of accidentally jumping to the highest rate in Data Saver mode. Codec efficiency only
        // breaks equal-bitrate ties; Opus is not inherently a lower-quality format.
        var withinCeiling = candidates
            .Where(f => (f["bitrate"]?.GetValue<int>() ?? 0) <= ceiling)
            .ToList();
        var pool = withinCeiling.Count > 0 ? withinCeiling : candidates;
        bool chooseLowest = quality == "DataSaver" && withinCeiling.Count == 0;
        return pool
            .OrderBy(f => chooseLowest ? (f["bitrate"]?.GetValue<int>() ?? 0) : -(f["bitrate"]?.GetValue<int>() ?? 0))
            .ThenByDescending(f =>
            {
                var m = f["mimeType"]?.GetValue<string>() ?? "";
                bool isOpus = m.Contains("webm", StringComparison.OrdinalIgnoreCase) && m.Contains("opus", StringComparison.OrdinalIgnoreCase);
                return preferWebmOpus == isOpus ? 1 : 0;
            })
            .ToList();
    }
}

/// <summary>
/// Seekable, read-ahead buffered HTTP byte-range stream using 512 KiB chunks (exact PlayerClient.kt NARROW_RANGE_BYTES).
/// Never uses open-ended ranges; prefetches 2 chunks ahead so background decoder never stalls.
/// </summary>
public sealed class BoundedRangeHttpStream : Stream
{
    public const int PageSize = 512 * 1024; // 512 KiB - matches upstream PlayerClient.NARROW_RANGE_BYTES
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _userAgent;
    private readonly string? _mediaOrigin;
    private readonly Dictionary<int, byte[]> _pages = new();
    private readonly object _sync = new();
    private long _length;
    private long _position;
    private readonly HashSet<int> _inflightPages = new();

    public BoundedRangeHttpStream(HttpClient http, ResolvedStream resolved, byte[] initialPage0)
    {
        _http = http;
        _url = resolved.Url;
        _userAgent = resolved.UserAgent;
        _mediaOrigin = resolved.MediaOrigin;
        _length = resolved.ContentLength > 0 ? resolved.ContentLength : initialPage0.Length;
        _pages[0] = initialPage0;
        TriggerPrefetch(1);
        if (!ResourceSaverState.IsActive) TriggerPrefetch(2);
    }

    public static async Task<BoundedRangeHttpStream> OpenAsync(HttpClient http, ResolvedStream resolved, CancellationToken ct = default)
    {
        var (data, total) = await FetchPageStaticAsync(http, resolved.Url, resolved.UserAgent, 0, resolved.ContentLength, ct, resolved.MediaOrigin);
        if (total > resolved.ContentLength) resolved.ContentLength = total;
        return new BoundedRangeHttpStream(http, resolved, data);
    }

    public static async Task<(byte[] Data, long Total)> FetchPageStaticAsync(
        HttpClient http, string url, string ua, int pageIndex, long knownLength, CancellationToken ct, string? mediaOrigin = null)
    {
        long start = (long)pageIndex * PageSize;
        long end = knownLength > 0 ? Math.Min(knownLength - 1, start + PageSize - 1) : start + PageSize - 1;
        if (knownLength > 0 && start >= knownLength)
            return (Array.Empty<byte>(), knownLength);

        Exception? lastEx = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", ua);
                if (!string.IsNullOrWhiteSpace(mediaOrigin))
                {
                    req.Headers.TryAddWithoutValidation("Origin", mediaOrigin);
                    req.Headers.TryAddWithoutValidation("Referer", mediaOrigin.TrimEnd('/') + "/");
                }
                req.Headers.Range = new RangeHeaderValue(start, end);

                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (res.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    return (Array.Empty<byte>(), start);
                if (res.StatusCode != HttpStatusCode.PartialContent && !res.IsSuccessStatusCode)
                    throw new HttpRequestException($"Stream range HTTP {(int)res.StatusCode}");

                long total = knownLength;
                if (res.Content.Headers.ContentRange?.Length is long crLen && crLen > 0)
                    total = crLen;
                else if (total <= 0 && res.Content.Headers.ContentLength is long cl)
                    total = start + cl;

                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                return (bytes, total);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                if (attempt < 2) await Task.Delay(250 * (attempt + 1), ct);
            }
        }
        throw lastEx ?? new IOException("Failed reading stream range.");
    }

    private byte[] GetOrFetchPage(int pageIndex)
    {
        lock (_sync)
        {
            if (_pages.TryGetValue(pageIndex, out var hit))
            {
                TriggerPrefetch(pageIndex + 1);
                if (!ResourceSaverState.IsActive) TriggerPrefetch(pageIndex + 2);
                return hit;
            }
        }

        var (data, total) = FetchPageStaticAsync(_http, _url, _userAgent, pageIndex, _length, CancellationToken.None, _mediaOrigin)
            .GetAwaiter().GetResult();
        lock (_sync)
        {
            if (total > _length) _length = total;
            int pageCapacity = ResourceSaverState.IsActive ? 6 : 36; // ~3 MiB vs ~18 MiB per active track
            while (_pages.Count >= pageCapacity)
            {
                var furthest = _pages.Keys.Where(k => k != pageIndex).OrderByDescending(k => Math.Abs(k - pageIndex)).FirstOrDefault(-1);
                if (furthest < 0) break;
                _pages.Remove(furthest);
            }
            _pages[pageIndex] = data;
            TriggerPrefetch(pageIndex + 1);
            if (!ResourceSaverState.IsActive) TriggerPrefetch(pageIndex + 2);
            return data;
        }
    }

    private void TriggerPrefetch(int nextPage)
    {
        long nextStart = (long)nextPage * PageSize;
        if (_length > 0 && nextStart >= _length) return;
        lock (_sync)
        {
            if (_pages.ContainsKey(nextPage) || !_inflightPages.Add(nextPage))
                return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var (d, t) = await FetchPageStaticAsync(_http, _url, _userAgent, nextPage, _length, CancellationToken.None, _mediaOrigin);
                lock (_sync)
                {
                    if (t > _length) _length = t;
                    _pages[nextPage] = d;
                }
            }
            catch { }
            finally
            {
                lock (_sync) _inflightPages.Remove(nextPage);
            }
        });
    }

    public double BufferedFraction
    {
        get
        {
            lock (_sync)
            {
                if (_length <= 0) return 0;
                int maxPage = _pages.Keys.DefaultIfEmpty(0).Max();
                long bytes = Math.Min(_length, (long)(maxPage + 1) * PageSize);
                return Math.Clamp((double)bytes / _length, 0, 1);
            }
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, _length);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length || count <= 0) return 0;
        int totalRead = 0;
        while (count > 0 && _position < _length)
        {
            int pageIdx = (int)(_position / PageSize);
            int pageOff = (int)(_position % PageSize);
            byte[] page;
            try
            {
                page = GetOrFetchPage(pageIdx);
            }
            catch
            {
                break; // never crash real-time decoder
            }
            if (page.Length == 0 || pageOff >= page.Length) break;
            int avail = Math.Min(count, page.Length - pageOff);
            Buffer.BlockCopy(page, pageOff, buffer, offset, avail);
            _position += avail;
            offset += avail;
            count -= avail;
            totalRead += avail;
            if (avail == 0) break;
        }
        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => _position
        };
        _position = Math.Clamp(_position, 0, _length);
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
