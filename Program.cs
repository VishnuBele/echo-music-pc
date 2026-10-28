using Avalonia;
using EchoMusic.Core;
using EchoMusic.UI;

namespace EchoMusic;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Win32TaskbarIconHelper.RegisterFreshAppUserModelId();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EchoMusic");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "crash.log"), $"[{DateTimeOffset.Now:O}] {e.ExceptionObject}\n\n");
            }
            catch { }
        };

        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            return RunHeadlessSelfTestAsync().GetAwaiter().GetResult();
        }

        int smokeIdx = Array.FindIndex(args, a => a.Equals("--visual-smoke", StringComparison.OrdinalIgnoreCase));
        if (smokeIdx >= 0)
        {
            App.VisualSmokeMode = true;
            App.VisualSmokeOutDir = smokeIdx + 1 < args.Length ? args[smokeIdx + 1] : "/opt/build/screenshots";
        }
        else
        {
            // Single-instance check: forward opened audio files or focus existing window
            if (SingleInstanceIpc.TryForwardArgsToRunningInstance(args))
                return 0;
            App.StartupFileArgs = args;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static async Task<int> RunHeadlessSelfTestAsync()
    {
        Console.WriteLine("=== Echo Music v2.0.0 Native Engine End-to-End Self-Test ===");

        // [0/8] Verify C1-continuous SmoothLimit has ZERO step discontinuity across [-1.5, +1.5]
        float maxStep = 0f;
        float prevY = EchoMasterMixer.SmoothLimit(-1.5f);
        for (float x = -1.5f + 0.0005f; x <= 1.5f; x += 0.0005f)
        {
            float y = EchoMasterMixer.SmoothLimit(x);
            maxStep = MathF.Max(maxStep, MathF.Abs(y - prevY));
            prevY = y;
        }
        Console.WriteLine($"[1/10] SmoothLimit waveform continuity verified: maxStep={maxStep:0.00000} (limit<=0.0010, maxOut={EchoMasterMixer.SmoothLimit(5f):0.000})");
        if (maxStep > 0.001f) return 10;

        var syntheticBlob = "[00:00.00]" + string.Join(" ", Enumerable.Repeat("sample lyric phrase", 30));
        var formattedBlob = LyricsService.ParseLrc(syntheticBlob, out _);
        bool formattedCorrectly = formattedBlob.Count > 4 && formattedBlob.All(x => x.Text.Length <= 56 && x.TimeSec is null);
        Console.WriteLine($"[lyrics-format] Oversized single-timestamp lyric paragraph split into {formattedBlob.Count} plain lines (maxLine={formattedBlob.Max(x => x.Text.Length)}; no false sync={formattedCorrectly})");
        if (!formattedCorrectly) return 11;

        var timingFixture = System.Text.Json.Nodes.JsonNode.Parse("""
        {"contents":[{"playlistPanelVideoWrapperRenderer":{"primaryRenderer":{"playlistPanelVideoRenderer":{"videoId":"playing-video"}},"counterpart":[{"counterpartRenderer":{"playlistPanelVideoRenderer":{"videoId":"source-audio"}},"segmentMap":{"segment":[{"primaryVideoStartTimeMilliseconds":"1200","counterpartVideoStartTimeMilliseconds":"500","durationMilliseconds":"800"}]}}]}}]}
        """);
        var forwardMap = InnertubeParsers.ParseCounterpartTimingMap(timingFixture, "playing-video");
        var reverseMap = InnertubeParsers.ParseCounterpartTimingMap(timingFixture, "source-audio");
        bool timingMapCorrect = forwardMap?.SourceVideoId == "source-audio"
            && forwardMap.Value.Segments.Single().SourceStartMilliseconds == 500
            && forwardMap.Value.Segments.Single().TargetStartMilliseconds == 1200
            && reverseMap?.SourceVideoId == "playing-video"
            && reverseMap.Value.Segments.Single().SourceStartMilliseconds == 1200
            && reverseMap.Value.Segments.Single().TargetStartMilliseconds == 500;
        Console.WriteLine($"[lyrics-segment-map] Forward and reversed counterpart timing maps verified={timingMapCorrect}");
        if (!timingMapCorrect) return 12;

        var endpointFixture = System.Text.Json.Nodes.JsonNode.Parse("""
        {"navigationEndpoint":{"watchEndpoint":{"videoId":"not-a-browse-id"}},"fallback":{"browseEndpoint":{"browseId":"FEmusic_new_releases_albums"}}}
        """);
        var shelfBrowseTarget = InnertubeParsers.FindBrowseEndpoint(endpointFixture);
        var watchOnlyTarget = InnertubeParsers.FindBrowseEndpoint(System.Text.Json.Nodes.JsonNode.Parse("""{"navigationEndpoint":{"watchEndpoint":{"videoId":"not-a-browse-id"}}}"""));
        bool browseTargetCorrect = shelfBrowseTarget?.Id == "FEmusic_new_releases_albums" && shelfBrowseTarget.Kind == "browse" && watchOnlyTarget is null;
        Console.WriteLine($"[browse-endpoint] ignored Play All watch endpoint and selected browse target={browseTargetCorrect}");
        if (!browseTargetCorrect) return 13;

        var store = new AppStore();
        var yt = new InnerTubeClient { RegionGl = "IN", LanguageHl = "en" };

        var vdata = await yt.EnsureVisitorDataAsync();
        Console.WriteLine($"[2/10] visitorData fetched: len={vdata?.Length ?? 0}");
        if (string.IsNullOrEmpty(vdata)) return 1;

        var home = await yt.GetHomeAsync();
        bool hasPodcastChip = home.MoodChips.Any(c => c.Title.Contains("Podcast", StringComparison.OrdinalIgnoreCase) || c.Title.Contains("Uploaded", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"[3/10] Extended PC Home shelves={home.Shelves.Count}, moodChips={home.MoodChips.Count} ({string.Join(", ", home.MoodChips.Select(c => c.Title))}), hasPodcastChip={hasPodcastChip}, hasNextContinuation={!string.IsNullOrEmpty(home.Continuation)}");
        if (home.Shelves.Count < 6 || hasPodcastChip) return 2;
        var exploreCharts = await yt.GetExploreAndChartsAsync();
        var topSongChart = exploreCharts.FirstOrDefault(s => s.Title == "Top Songs Chart");
        Console.WriteLine($"[top-100-chart] region={yt.RegionGl}, playableChartTracks={topSongChart?.Tracks.Count ?? 0}");
        if (yt.RegionGl == "IN" && (topSongChart?.Tracks.Count ?? 0) < 80) return 14;
        // Verify 'kill chori' search parity with OG Echo Music APK!
        var kcSearch = await yt.SearchAsync("kill chori", "all");
        Console.WriteLine($"[4/10] Search 'kill chori': TopResult='{kcSearch.TopResultTrack?.Title}' ({kcSearch.TopResultTrack?.Id}), AlbumArtist='{kcSearch.TopResultTrack?.AlbumArtist}', Tracks={kcSearch.Tracks.Count}, Videos={kcSearch.Videos.Count}, WebClient={YouTubeClients.WebRemix.Version}");
        if (kcSearch.TopResultTrack is null || kcSearch.Tracks.Count == 0 || !kcSearch.TopResultTrack.Title.Contains("Kill Chori", StringComparison.OrdinalIgnoreCase)
            || kcSearch.TopResultTrack.AlbumArtist != "Garena Free Fire")
            return 3;

        var paginationFirst = await yt.SearchAsync("arijit singh", "songs");
        if (!string.IsNullOrEmpty(paginationFirst.Continuation))
        {
            var paginationNext = await yt.SearchAsync("arijit singh", "songs", paginationFirst.Continuation);
            int uniquePageTracks = paginationFirst.Tracks.Concat(paginationNext.Tracks).Select(t => t.Id).Distinct(StringComparer.Ordinal).Count();
            Console.WriteLine($"[search-pagination] first={paginationFirst.Tracks.Count}, next={paginationNext.Tracks.Count}, uniqueAcrossPages={uniquePageTracks}, nextContinuation={!string.IsNullOrEmpty(paginationNext.Continuation)}");
        }
        else
        {
            Console.WriteLine($"[search-pagination] upstream returned no continuation for 'arijit singh' (first={paginationFirst.Tracks.Count}); continuation path not exercised");
        }

        var resolver = new StreamResolver(yt, () => "Best");
        var verified = await resolver.ResolveVerifiedAsync(kcSearch.TopResultTrack.Id, preferWebmOpus: true);
        var resolved = verified.Stream;
        Console.WriteLine($"[5/10] Verified 'Kill Chori' stream via {resolved.ClientName}: itag={resolved.Itag}, mime={resolved.MimeType}, bitrate={resolved.Bitrate}, len={resolved.ContentLength}");

        await using var httpStream = new BoundedRangeHttpStream(yt.SharedHttp, resolved, verified.InitialPage0);
        using var opus = new WebmOpusSampleSource(httpStream, resolved.DurationSeconds, ownsStream: false);
        var pcm = new float[48000 * 2];
        int readStart = opus.Read(pcm, 0, pcm.Length);
        float peakStart = pcm.Take(readStart).Select(MathF.Abs).DefaultIfEmpty(0).Max();
        opus.Seek(Math.Min(60.0, resolved.DurationSeconds * 0.5));
        int readMid = opus.Read(pcm, 0, pcm.Length);
        float peakMid = pcm.Take(readMid).Select(MathF.Abs).DefaultIfEmpty(0).Max();
        Console.WriteLine($"[6/10] Pure-C# WebM/Opus decoded 'Kill Chori' start={readStart} floats (peak={peakStart:0.000}), mid@60s={readMid} floats (peak={peakMid:0.000})");
        if (readStart < 4000 || readMid < 4000 || peakMid <= 0.0001f) return 5;

        using (var engine = new AudioEngine(yt, resolver, store))
        {
            store.Settings.Volume = 1.0;
            store.Settings.PlaybackSpeed = 1.0;
            store.Settings.EqPreset = "Flat";
            engine.ApplySettingsFromStore();
            await engine.PlaySingleAsync(kcSearch.TopResultTrack);
            await Task.Delay(2400);
            var spec = new float[32];
            engine.CopySpectrum32(spec);
            Console.WriteLine($"[7/10] AudioEngine live 'Kill Chori' @ 100% vol: IsPlaying={engine.IsPlaying}, Pos={engine.PositionSeconds:0.00}s, SpecMax={spec.Max():0.000}, Err='{engine.LastError}'");
            if (!engine.IsPlaying || engine.PositionSeconds < 1.2 || spec.Max() <= 0.01f) return 6;
        }

        var search2 = await yt.SearchAsync("Blinding Lights The Weeknd", "songs");
        var lyricsSvc = new LyricsService(yt);
        var lyrics = await lyricsSvc.GetLyricsAsync(search2.Tracks[0]);
        var extras = new MusicDnaAndExtras(yt);
        var dna = await extras.GetMusicDnaAsync(search2.Tracks[0]);
        Console.WriteLine($"[8/10] Lyrics provider='{lyrics.Provider}' (synced={lyrics.IsSynced}, lines={lyrics.Lines.Count}); DNA story len={dna.TrackStory.Length}");

        using var artRequest = new HttpRequestMessage(HttpMethod.Get, kcSearch.TopResultTrack.Thumbnail);
        artRequest.Headers.TryAddWithoutValidation("User-Agent", YouTubeClients.WebUa);
        artRequest.Headers.TryAddWithoutValidation("Referer", YouTubeClients.Referer);
        using var artResponse = await yt.SharedHttp.SendAsync(artRequest);
        var artBytes = artResponse.IsSuccessStatusCode ? await artResponse.Content.ReadAsByteArrayAsync() : Array.Empty<byte>();
        bool artIsJpeg = artBytes.Length > 128 && artBytes[0] == 0xFF && artBytes[1] == 0xD8 && artBytes[2] == 0xFF;
        Console.WriteLine($"[9/10] Kill Chori artwork source fetched: {artIsJpeg} ({artResponse.StatusCode}, {artBytes.Length} bytes)");
        if (!artIsJpeg) return 8;

        var kcLyrics = await lyricsSvc.GetLyricsAsync(kcSearch.TopResultTrack);
        var syntheticVideo = new TrackItem { Title = "Unrelated video title", IsVideo = true, DurationSeconds = 218 };
        var syntheticLyrics = new LyricsData { IsSynced = true, Provider = "self-test" };
        for (int i = 0; i < 34; i++)
            syntheticLyrics.Lines.Add(new LyricLine
            {
                Text = "timed line " + i, TimeSec = i * 4.8, EndSec = i * 4.8 + 3.8,
                Words = new List<LyricWord> { new() { TimeSec = i * 4.8 + 0.2, Text = "timed" } },
            });
        double syntheticFirstBefore = syntheticLyrics.Lines[0].TimeSec!.Value;
        MainWindow.ApplyConservativeVideoIntroAlignment(syntheticVideo, syntheticLyrics);
        double syntheticOffset = syntheticLyrics.AppliedTimingOffsetSeconds;
        bool inferredIntro = syntheticOffset is > 20 and < 90
            && Math.Abs(syntheticLyrics.Lines[0].TimeSec!.Value - syntheticFirstBefore - syntheticOffset) < 0.01
            && Math.Abs(syntheticLyrics.Lines[0].Words[0].TimeSec - syntheticFirstBefore - syntheticOffset - 0.2) < 0.01;
        Console.WriteLine($"[10/10] Live Kill Chori lyrics provider='{kcLyrics.Provider}' (synced={kcLyrics.IsSynced}, lines={kcLyrics.Lines.Count}); generic long-intro alignment inferred={inferredIntro} ({syntheticOffset:0.0}s, arbitrary title)");
        if (!inferredIntro) return 10;

        var sponsorHandler = new SelfTestSponsorBlockHandler();
        using var sponsorHttp = new HttpClient(sponsorHandler);
        var sponsorService = new SponsorBlockService(sponsorHttp);
        var introSegments = await sponsorService.GetSegmentsAsync("cache-category-test", "intro,outro");
        var sponsorSegments = await sponsorService.GetSegmentsAsync("cache-category-test", "sponsor");
        _ = await sponsorService.GetSegmentsAsync("cache-category-test", "outro,intro");
        if (sponsorHandler.RequestCount != 2 || introSegments.FirstOrDefault()?.Category != "intro" || sponsorSegments.FirstOrDefault()?.Category != "sponsor") return 11;
        Console.WriteLine("[extra] SponsorBlock category-specific cache verified (distinct scopes; equivalent order reuses cache).");

        static LyricsData MakeSyntheticLyrics(double shift, string provider)
        {
            var data = new LyricsData { IsSynced = true, Provider = provider };
            for (int i = 0; i < 8; i++)
                data.Lines.Add(new LyricLine
                {
                    Text = "anchor line " + i,
                    TimeSec = i * 4 + shift,
                    EndSec = i * 4 + shift + 3,
                    Words = new List<LyricWord> { new() { TimeSec = i * 4 + shift + 0.2, Text = "anchor" } },
                });
            return data;
        }
        var selectedLyrics = MakeSyntheticLyrics(0, "selftest-selected");
        var alternateLyrics = new[] { MakeSyntheticLyrics(52.0, "provider-a"), MakeSyntheticLyrics(52.2, "provider-b") };
        LyricsService.AutoAlignWithConsensus(selectedLyrics, alternateLyrics, durationSec: 180);
        if (Math.Abs((selectedLyrics.Lines[0].TimeSec ?? 0) - 52.1) > 0.2 || Math.Abs(selectedLyrics.Lines[0].Words[0].TimeSec - 52.3) > 0.2) return 12;
        Console.WriteLine($"[extra] Generic long-intro lyric consensus alignment verified (+{selectedLyrics.AppliedTimingOffsetSeconds:0.00}s; word timestamps move with the line).");
        if (kcLyrics.Lines.Count == 0)
            Console.WriteLine("[10/10] No lyric provider responded in this run; remote lyric availability is reported separately from the engine checks.");

        Console.WriteLine("ALL 10 engine and subsystem checks passed; Kill Chori lyric availability is reported separately from sync status.");
        return 0;
    }

    private sealed class SelfTestSponsorBlockHandler : HttpMessageHandler
    {
        public int RequestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref RequestCount);
            string category = request.RequestUri?.Query.Contains("sponsor", StringComparison.OrdinalIgnoreCase) == true ? "sponsor" : "intro";
            string start = category == "sponsor" ? "5.0" : "1.0";
            string end = category == "sponsor" ? "8.0" : "3.0";
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent($"[{{\"segment\":[{start},{end}],\"category\":\"{category}\"}}]")
            };
            return Task.FromResult(response);
        }
    }

}
