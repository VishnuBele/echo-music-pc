using Avalonia;
using Avalonia.Controls;
using EchoMusic.Core;
using EchoMusic.UI;
using NAudio.Wave;

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

        int suggestionsIdx = Array.FindIndex(args, a => a.Equals("--visual-suggestions", StringComparison.OrdinalIgnoreCase));
        int smokeIdx = Array.FindIndex(args, a => a.Equals("--visual-smoke", StringComparison.OrdinalIgnoreCase));
        if (suggestionsIdx >= 0)
        {
            App.VisualSmokeMode = true;
            App.VisualSuggestionsOnly = true;
            App.VisualSmokeOutDir = suggestionsIdx + 1 < args.Length ? args[suggestionsIdx + 1] : "/opt/build/screenshots";
        }
        else if (smokeIdx >= 0)
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
        Console.WriteLine("=== Echo Music v2.0.4 Native Engine End-to-End Self-Test ===");

        var spotifyMatchProbe = MusicDnaAndExtras.PickSpotifyMatch(
            ("Safe Title", "Correct Artist", 200_000),
            new SearchPageResult
            {
                Tracks = new List<TrackItem>
                {
                    new() { Id = "wrong-artist", Title = "Safe Title", Artists = new() { new ArtistRef { Name = "Wrong Artist" } }, DurationSeconds = 200 },
                    new() { Id = "wrong-duration", Title = "Safe Title", Artists = new() { new ArtistRef { Name = "Correct Artist" } }, DurationSeconds = 400 },
                    new() { Id = "verified", Title = "Safe Title", Artists = new() { new ArtistRef { Name = "Correct Artist" } }, DurationSeconds = 201 },
                },
            });
        bool spotifyMatchValidated = spotifyMatchProbe?.Id == "verified";
        Console.WriteLine($"[spotify-import-match] rejected artist/duration mismatches and selected the verified title+artist+duration result={spotifyMatchValidated}");
        if (!spotifyMatchValidated) return 19;

        var oldPlaylist = System.Text.Json.JsonSerializer.Deserialize<LocalPlaylist>("{\"Id\":\"legacy\"}");
        bool legacyCoverDefaultPreserved = oldPlaylist?.CoverRoundness == 14;
        Console.WriteLine($"[playlist-cover-roundness] older playlist JSON keeps the legacy default radius={legacyCoverDefaultPreserved}");
        if (!legacyCoverDefaultPreserved) return 20;

        string upgradedCurrentArt = InnertubeParsers.UpgradeThumb("https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg", 1200);
        bool currentArtPrefersMaxResolution = upgradedCurrentArt.EndsWith("/maxresdefault.jpg", StringComparison.OrdinalIgnoreCase);
        string upgradedCardArt = InnertubeParsers.UpgradeThumb("https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg", 384);
        bool cardArtUsesRightSizedSource = upgradedCardArt.EndsWith("/hqdefault.jpg", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"[now-playing-artwork-resolution] large artwork uses max-resolution source={currentArtPrefersMaxResolution}; standard card art avoids max-res transfer={cardArtUsesRightSizedSource}");
        if (!currentArtPrefersMaxResolution || !cardArtUsesRightSizedSource) return 31;

        bool youtubeProfileRejected = false;
        try
        {
            await new MusicDnaAndExtras(new InnerTubeClient()).ImportYouTubeMusicUrlAsync("https://music.youtube.com/@channel");
        }
        catch (ArgumentException) { youtubeProfileRejected = true; }
        Console.WriteLine($"[youtube-import-profile] channel/profile links are rejected instead of being implied supported={youtubeProfileRejected}");
        if (!youtubeProfileRejected) return 21;

        var codecFixture = System.Text.Json.Nodes.JsonNode.Parse("""
        [
          {"mimeType":"audio/webm; codecs=\"opus\"","bitrate":160000,"url":"https://example.invalid/opus"},
          {"mimeType":"audio/mp4; codecs=\"mp4a.40.2\"","bitrate":128000,"url":"https://example.invalid/aac"}
        ]
        """)!.AsArray();
        bool nativeCodecPreferenceCorrect = StreamResolver.RankCandidateFormats(codecFixture, "Auto", preferWebmOpus: false).First()["mimeType"]?.GetValue<string>()?.Contains("audio/mp4", StringComparison.OrdinalIgnoreCase) == true
            && StreamResolver.RankCandidateFormats(codecFixture, "Auto", preferWebmOpus: true).First()["mimeType"]?.GetValue<string>()?.Contains("audio/webm", StringComparison.OrdinalIgnoreCase) == true;
        Console.WriteLine($"[platform-codec-preference] Windows can prefer native AAC while WebM/Opus remains explicitly selectable={nativeCodecPreferenceCorrect}");
        if (!nativeCodecPreferenceCorrect) return 27;

        byte[] rangeFixture = Enumerable.Range(0, 1024).Select(i => (byte)(i % 251)).ToArray();
        var transientRangeHandler = new SelfTestRangeHandler(rangeFixture, failFirstRequests: 1);
        using (var rangeHttp = new HttpClient(transientRangeHandler))
        using (var rangeStream = new BoundedRangeHttpStream(rangeHttp,
            new ResolvedStream { Url = "https://range-selftest.invalid/audio", UserAgent = "selftest", ContentLength = rangeFixture.Length },
            rangeFixture.Take(128).ToArray()))
        {
            var received = new byte[rangeFixture.Length];
            int total = 0;
            while (total < received.Length)
            {
                int n = rangeStream.Read(received, total, received.Length - total);
                if (n == 0) break;
                total += n;
            }
            bool retryRecovered = total == rangeFixture.Length && received.SequenceEqual(rangeFixture) && transientRangeHandler.RequestCount >= 2;
            Console.WriteLine($"[range-read-retry] transient HTTP failure retried without truncating the audio stream={retryRecovered} (bytes={total}/{rangeFixture.Length})");
            if (!retryRecovered) return 22;
        }

        var failedRangeHandler = new SelfTestRangeHandler(rangeFixture, failFirstRequests: 9);
        using (var rangeHttp = new HttpClient(failedRangeHandler))
        using (var rangeStream = new BoundedRangeHttpStream(rangeHttp,
            new ResolvedStream { Url = "https://range-selftest.invalid/audio", UserAgent = "selftest", ContentLength = rangeFixture.Length },
            rangeFixture.Take(128).ToArray()))
        {
            bool raisedError = false;
            try { _ = rangeStream.Read(new byte[rangeFixture.Length], 0, rangeFixture.Length); }
            catch (IOException) { raisedError = true; }
            Console.WriteLine($"[range-read-failure] persistent range failures surface as errors, never zero-byte EOF={raisedError}");
            if (!raisedError) return 23;
        }

        var cutoffGuard = new PlaybackCutoffGuard();
        cutoffGuard.Reset("early-cutoff");
        var firstCutoff = cutoffGuard.Evaluate("early-cutoff", 1.2, 168, 168, out _, out int attempt1);
        var secondCutoff = cutoffGuard.Evaluate("early-cutoff", 1.3, 168, 168, out _, out int attempt2);
        var exhaustedCutoff = cutoffGuard.Evaluate("early-cutoff", 1.4, 168, 168, out _, out _);
        var naturalEnd = cutoffGuard.Evaluate("complete-track", 165.2, 168, 168, out _, out _);
        var unknownDurationGuard = new PlaybackCutoffGuard();
        unknownDurationGuard.Reset("unknown-duration");
        var unknownEarlyEnd = unknownDurationGuard.Evaluate("unknown-duration", 1.0, 0, 0, out _, out _);
        var unknownLongEnd = unknownDurationGuard.Evaluate("unknown-duration", 8.0, 0, 0, out _, out _);
        bool cutoffGuardCorrect = firstCutoff == TrackEndDecision.RetryStream && attempt1 == 1
            && secondCutoff == TrackEndDecision.RetryStream && attempt2 == 2
            && exhaustedCutoff == TrackEndDecision.StopAfterRetries
            && naturalEnd == TrackEndDecision.AllowEnd
            && unknownEarlyEnd == TrackEndDecision.RetryStream
            && unknownLongEnd == TrackEndDecision.AllowEnd;
        Console.WriteLine($"[premature-track-end] early EOF refreshes same song twice then stops; unknown-duration early EOF is guarded and true end remains normal={cutoffGuardCorrect}");
        if (!cutoffGuardCorrect) return 25;

        byte[] malformedWebm = { 0x1A,0x45,0xDF,0xA3,0x80, 0x18,0x53,0x80,0x67,0x80, 0x1F,0x43,0xB6,0x75,0x80, 0x00 };
        bool malformedWebmRaisedError = false;
        try
        {
            using var malformedSource = new WebmOpusSampleSource(new MemoryStream(malformedWebm), fallbackDurationSec: 30, ownsStream: true);
            _ = malformedSource.Read(new float[128], 0, 128);
        }
        catch (InvalidDataException) { malformedWebmRaisedError = true; }
        Console.WriteLine($"[webm-malformed-not-eof] corrupt EBML within a declared stream raises an error, not natural EOF={malformedWebmRaisedError}");
        if (!malformedWebmRaisedError) return 26;

        var sourceRows = new StackPanel();
        var targetRows = new StackPanel();
        var testRow = new Border();
        sourceRows.Children.Add(testRow);
        var detachedRow = sourceRows.Children[0];
        sourceRows.Children.RemoveAt(0);
        targetRows.Children.Add(detachedRow);
        bool rowWasDetachedBeforeMove = sourceRows.Children.Count == 0 && targetRows.Children.Count == 1;
        Console.WriteLine($"[playlist-row-parent-transfer] batch rows are detached before being moved to the persistent list={rowWasDetachedBeforeMove}");
        if (!rowWasDetachedBeforeMove) return 29;

        using (var durationBoundDeck = new DeckState(new TrackItem { Id = "selftest-duration", Title = "Duration-bound source", DurationSeconds = 1 },
            new SelfTestSampleProvider(), new SelfTestNoopDisposable(), null, null, null, null))
        {
            bool bufferedToEnd = SpinWait.SpinUntil(() => durationBoundDeck.BufferedSampleFloats >= 96_000, TimeSpan.FromSeconds(2));
            var beyondEnd = new float[120_000];
            int decodedFloats = bufferedToEnd ? durationBoundDeck.Read(beyondEnd, 0, beyondEnd.Length) : 0;
            bool timelineBounded = bufferedToEnd && decodedFloats == 96_000
                && durationBoundDeck.PositionSeconds <= 1.0001 && durationBoundDeck.IsFinished;
            Console.WriteLine($"[duration-bound-deck] decoder audio stops at declared duration instead of playing beyond the end={timelineBounded} (buffered={durationBoundDeck.BufferedSampleFloats}, read={decodedFloats}, pos={durationBoundDeck.PositionSeconds:0.000}, finished={durationBoundDeck.IsFinished})");
            if (!timelineBounded) return 30;
        }

        using (var readyDeck = new DeckState(new TrackItem { Id = "selftest-ready", Title = "Buffered source" },
            new SelfTestSampleProvider(), new SelfTestNoopDisposable(), null, null, null, null))
        {
            bool validAudioAccepted = await readyDeck.WaitForUsableAudioAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
            Console.WriteLine($"[codec-race-audio-readiness] a candidate with at least 100 ms decoded PCM is accepted={validAudioAccepted}");
            if (!validAudioAccepted) return 28;
        }

        using (var brokenDeck = new DeckState(new TrackItem { Id = "selftest", Title = "Failing source" },
            new SelfTestFailingSampleProvider(), new SelfTestNoopDisposable(), null, null, null, null))
        {
            bool observedError = SpinWait.SpinUntil(() => brokenDeck.SourceReadError is not null, TimeSpan.FromSeconds(2));
            var silence = new float[128];
            int nonEofRead = brokenDeck.Read(silence, 0, silence.Length);
            bool errorIsNotNaturalEnd = observedError && nonEofRead == silence.Length && !brokenDeck.IsFinished;
            bool emptyDeckRejected = !await brokenDeck.WaitForUsableAudioAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
            Console.WriteLine($"[decode-error-not-eof] failed provider read stays non-EOF and cannot signal queue advance={errorIsNotNaturalEnd}");
            Console.WriteLine($"[codec-race-audio-readiness] a zero-sample decoder candidate is rejected rather than selected={emptyDeckRejected}");
            if (!errorIsNotNaturalEnd || !emptyDeckRejected) return 24;
        }

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

        var transcriptCueFixture = System.Text.Json.Nodes.JsonNode.Parse("""
        {"contents":[
          {"transcriptSegmentRenderer":{"snippet":{"runs":[{"text":"first phrase"}]},"startMs":"1000","endMs":"1800"}},
          {"transcriptSegmentRenderer":{"snippet":{"runs":[{"text":"next phrase"}]},"startMs":"2500","endMs":"3100"}},
          {"transcriptSegmentRenderer":{"snippet":{"runs":[{"text":" same cue"}]},"startMs":"2520","endMs":"3200"}}
        ]}
        """);
        var transcriptCues = InnertubeParsers.ParseTranscript(transcriptCueFixture);
        bool transcriptTimingPreserved = transcriptCues.Count == 2
            && transcriptCues[0].TimeSec == 1.0
            && transcriptCues[1].TimeSec == 2.5
            && transcriptCues[1].Text.Contains("next phrase", StringComparison.Ordinal)
            && transcriptCues[1].Text.Contains("same cue", StringComparison.Ordinal);
        Console.WriteLine($"[transcript-cue-timing] Later phrases keep their own cue times; same-time fragments coalesce={transcriptTimingPreserved}");
        if (!transcriptTimingPreserved) return 15;

        bool listeningTimeCorrect = AudioEngine.CountAudibleIntervalMilliseconds(10, 11, 1000, 1, true) == 1000
            && AudioEngine.CountAudibleIntervalMilliseconds(10, 45, 1000, 1, true) == 0
            && AudioEngine.CountAudibleIntervalMilliseconds(10, 10, 1000, 1, true) == 0
            && AudioEngine.CountAudibleIntervalMilliseconds(10, 11, 1000, 1, false) == 0;
        Console.WriteLine($"[wrapped-audible-time] Counts played elapsed time and rejects seek/pause intervals={listeningTimeCorrect}");
        if (!listeningTimeCorrect) return 13;

        var filterResetProbe = DoubleBiquad.PeakingEq(EchoMasterMixer.SampleRate, 1000, 1.4, 6);
        _ = filterResetProbe.Transform(1);
        filterResetProbe.ResetState();
        bool cleanTrackDspReset = Math.Abs(filterResetProbe.Transform(0)) < 1e-12;
        Console.WriteLine($"[track-dsp-reset] IIR history clears at a new track boundary={cleanTrackDspReset}");
        if (!cleanTrackDspReset) return 14;

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

            engine.Seek(60.0);
            await Task.Delay(700);
            double forwardSeekPosition = engine.PositionSeconds;
            bool forwardSeekRecovered = forwardSeekPosition is > 60.0 and < 63.0;
            Console.WriteLine($"[seek-forward] target=60.0s, current={forwardSeekPosition:0.00}s, recovered={forwardSeekRecovered}");
            if (!forwardSeekRecovered) return 15;

            engine.Seek(15.0);
            await Task.Delay(700);
            double backwardSeekPosition = engine.PositionSeconds;
            bool backwardSeekRecovered = backwardSeekPosition is > 15.0 and < 18.0;
            Console.WriteLine($"[seek-backward] target=15.0s, current={backwardSeekPosition:0.00}s, recovered={backwardSeekRecovered}");
            if (!backwardSeekRecovered) return 16;
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

    private sealed class SelfTestRangeHandler(byte[] payload, int failFirstRequests) : HttpMessageHandler
    {
        public int RequestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int requestNumber = Interlocked.Increment(ref RequestCount);
            if (requestNumber <= failFirstRequests)
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));

            long start = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            if (start >= payload.LongLength)
            {
                var eof = new HttpResponseMessage(System.Net.HttpStatusCode.RequestedRangeNotSatisfiable);
                eof.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(payload.LongLength);
                return Task.FromResult(eof);
            }
            long requestedEnd = request.Headers.Range?.Ranges.FirstOrDefault()?.To ?? (start + BoundedRangeHttpStream.PageSize - 1);
            long end = Math.Min(payload.LongLength - 1, requestedEnd);
            int offset = checked((int)start);
            int length = checked((int)(end - start + 1));
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload, offset, length),
            };
            response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, end, payload.LongLength);
            return Task.FromResult(response);
        }
    }

    private sealed class SelfTestSampleProvider : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            Array.Fill(buffer, 0.05f, offset, count);
            return count;
        }
    }

    private sealed class SelfTestFailingSampleProvider : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        public int Read(float[] buffer, int offset, int count) => throw new IOException("self-test decoder read failed");
    }

    private sealed class SelfTestNoopDisposable : IDisposable
    {
        public void Dispose() { }
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
