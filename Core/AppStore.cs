using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EchoMusic.Core;

public sealed class PersistentDatabase
{
    public AppSettings Settings { get; set; } = new();
    public List<TrackItem> LikedSongs { get; set; } = new();
    public List<MediaCardItem> SavedAlbums { get; set; } = new();
    public List<MediaCardItem> FollowedArtists { get; set; } = new();
    public List<LocalPlaylist> Playlists { get; set; } = new();
    public List<PlayHistoryEntry> History { get; set; } = new();
    public List<DownloadEntry> Downloads { get; set; } = new();
    public List<ContentExclusion> ContentExclusions { get; set; } = new();
    public Dictionary<string, double> LyricOffsets { get; set; } = new();
    public List<string> RecentSearches { get; set; } = new();
}

public sealed class WrappedStats
{
    public int TotalPlays { get; set; }
    public double TotalMinutes { get; set; }
    public int UniqueSongs { get; set; }
    public int UniqueArtists { get; set; }
    public string PersonalityArchetype { get; set; } = "Sonic Explorer";
    public List<(TrackItem Track, int Plays, double Minutes)> TopTracks { get; set; } = new();
    public List<(string Artist, string Thumbnail, int Plays, double Minutes)> TopArtists { get; set; } = new();
}

public sealed class AppStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _saveLock = new();
    private CancellationTokenSource? _debounceCts;
    private PersistentDatabase _db = new();

    public string DataDir { get; }
    public string DownloadsDir { get; }
    public string ArtCacheDir { get; }
    public string DbPath { get; }

    public AppSettings Settings => _db.Settings;
    public List<TrackItem> LikedSongs => _db.LikedSongs;
    public List<MediaCardItem> SavedAlbums => _db.SavedAlbums;
    public List<MediaCardItem> FollowedArtists => _db.FollowedArtists;
    public List<LocalPlaylist> Playlists => _db.Playlists;
    public List<PlayHistoryEntry> History => _db.History;
    public List<DownloadEntry> Downloads => _db.Downloads;
    public List<ContentExclusion> ContentExclusions => _db.ContentExclusions;
    public Dictionary<string, double> LyricOffsets => _db.LyricOffsets;
    public List<string> RecentSearches => _db.RecentSearches;

    public List<TrackItem> LocalTracks { get; private set; } = new();

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDownloads = new();
    public event Action? LibraryChanged;
    public event Action? DownloadsChanged;

    public AppStore()
    {
        var baseAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseAppData))
            baseAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        DataDir = Path.Combine(baseAppData, "EchoMusic");
        DownloadsDir = Path.Combine(DataDir, "Downloads");
        ArtCacheDir = Path.Combine(DataDir, "ArtCache");
        DbPath = Path.Combine(DataDir, "echo_state.json");

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(ArtCacheDir);
        // A crash can leave a temporary WebView2 login profile. Retire only stale profiles,
        // so an unusually long-running login in another app instance is not interrupted.
        try
        {
            var authProfiles = Path.Combine(DataDir, "AuthSessions");
            if (Directory.Exists(authProfiles))
            {
                foreach (var profile in Directory.GetDirectories(authProfiles))
                    if (Directory.GetLastWriteTimeUtc(profile) < DateTime.UtcNow.AddDays(-1))
                        try { Directory.Delete(profile, recursive: true); } catch { }
            }
        }
        catch { }

        Load();
        ReconcileDownloadsOnDisk();
    }

    private sealed record ProtectedAuthSession(string? CookieHeader, string? DataSyncId);

    private string AuthSessionPath => Path.Combine(DataDir, "auth_session.bin");

    private void Load()
    {
        string? legacyCookieHeader = null;
        string? legacyDataSyncId = null;
        try
        {
            if (File.Exists(DbPath))
            {
                var txt = File.ReadAllText(DbPath);
                TryReadLegacyAuth(txt, out legacyCookieHeader, out legacyDataSyncId);
                bool hasUiGen = txt.Contains("\"uiDesignGeneration\"", StringComparison.OrdinalIgnoreCase);
                _db = JsonSerializer.Deserialize<PersistentDatabase>(txt, JsonOpts) ?? new PersistentDatabase();
                if (!hasUiGen || _db.Settings.UiDesignGeneration < 4)
                {
                    // Migrate upgraded installs to v2.4 Spotify Green + thicker CircularGeo font + WaveDefault emerald icon!
                    if (!hasUiGen || _db.Settings.UiDesignGeneration < 3)
                    {
                        _db.Settings.Theme = "SpotifyGreen";
                        _db.Settings.PlayerStyle = "Circle";
                    }
                    _db.Settings.FontChoice = "SpotifyCircular";
                    if (string.IsNullOrEmpty(_db.Settings.AppIconStyle) || _db.Settings.AppIconStyle == "Default")
                        _db.Settings.AppIconStyle = "WaveDefault";
                    _db.Settings.UiDesignGeneration = 4;
                }
                if (_db.Settings.UiDesignGeneration < 5)
                {
                    // v2.6.1: match upstream's opt-in default and stop unexpected SponsorBlock jumps.
                    _db.Settings.SponsorBlockEnabled = false;
                    _db.Settings.UiDesignGeneration = 5;
                }
            }
        }
        catch
        {
            _db = new PersistentDatabase();
        }
        _db.ContentExclusions ??= new List<ContentExclusion>();
        // Windows media-session integration stays enabled; expose no user-facing toggle for this required default.
        _db.Settings.SmtcEnabled = true;
        if (_db.Settings.PlayerStyle == "LiquidGlass") _db.Settings.PlayerStyle = "Circle";
        if (_db.Settings.PlayerSliderStyle is not ("Bar" or "Wavy" or "Slim" or "Squiggly"))
            _db.Settings.PlayerSliderStyle = "Slim";
        string? saverPolicy = _db.Settings.ResourceSaverPolicy;
        if (saverPolicy is not ("Auto" or "On" or "Off"))
            saverPolicy = _db.Settings.ResourceSaverMode ? "On" : "Auto";
        _db.Settings.ResourceSaverPolicy = saverPolicy;
        _db.Settings.ResourceSaverMode = saverPolicy == "On";
        if (_db.Settings.EqBands is null || _db.Settings.EqBands.Length != 10)
            _db.Settings.EqBands = new double[10];

        bool migratedLegacySession = false;
        if (OperatingSystem.IsWindows() && File.Exists(AuthSessionPath))
        {
            try
            {
                var clearBytes = WindowsUserDataProtection.Unprotect(File.ReadAllBytes(AuthSessionPath));
                var session = JsonSerializer.Deserialize<ProtectedAuthSession>(clearBytes, JsonOpts);
                _db.Settings.CookieHeader = session?.CookieHeader;
                _db.Settings.DataSyncId = session?.DataSyncId;
            }
            catch
            {
                _db.Settings.CookieHeader = null;
                _db.Settings.DataSyncId = null;
                try { File.Delete(AuthSessionPath); } catch { }
            }
        }
        else if (!string.IsNullOrWhiteSpace(legacyCookieHeader))
        {
            if (OperatingSystem.IsWindows())
            {
                _db.Settings.CookieHeader = legacyCookieHeader;
                _db.Settings.DataSyncId = legacyDataSyncId;
                migratedLegacySession = true;
            }
            // On non-Windows platforms, never retain a legacy plaintext session.
        }

        // Re-serialize once to remove any legacy plaintext cookie fields from old settings files.
        if (legacyCookieHeader is not null || migratedLegacySession)
            SaveNow();
    }

    private static void TryReadLegacyAuth(string json, out string? cookieHeader, out string? dataSyncId)
    {
        cookieHeader = null;
        dataSyncId = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement settings = default;
            foreach (var property in doc.RootElement.EnumerateObject())
                if (string.Equals(property.Name, "settings", StringComparison.OrdinalIgnoreCase)) { settings = property.Value; break; }
            if (settings.ValueKind != JsonValueKind.Object) return;
            foreach (var property in settings.EnumerateObject())
            {
                if (string.Equals(property.Name, "cookieHeader", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    cookieHeader = property.Value.GetString();
                else if (string.Equals(property.Name, "dataSyncId", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    dataSyncId = property.Value.GetString();
            }
        }
        catch { }
    }

    private void SaveProtectedSession()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (string.IsNullOrWhiteSpace(_db.Settings.CookieHeader))
            {
                if (File.Exists(AuthSessionPath)) File.Delete(AuthSessionPath);
                return;
            }
            var clear = JsonSerializer.SerializeToUtf8Bytes(
                new ProtectedAuthSession(_db.Settings.CookieHeader, _db.Settings.DataSyncId), JsonOpts);
            var encrypted = WindowsUserDataProtection.Protect(clear);
            var temp = AuthSessionPath + ".tmp";
            File.WriteAllBytes(temp, encrypted);
            File.Move(temp, AuthSessionPath, overwrite: true);
        }
        catch { }
    }

    public void SaveNow()
    {
        lock (_saveLock)
        {
            try
            {
                SaveProtectedSession();
                var tmp = DbPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_db, JsonOpts));
                File.Move(tmp, DbPath, overwrite: true);
            }
            catch { }
        }
    }

    public void SaveDebounced()
    {
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cts.Token);
                SaveNow();
            }
            catch { }
        });
    }

    // Content exclusions follow Echo Music's discovery controls: a track, album, or artist can be hidden from search/home/radio, and exclusions remain reversible in Settings.
    private static string NormalizeExclusionName(string? value) =>
        string.Join(" ", (value ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ExclusionIdentity(string kind, string? id, string? name) =>
        !string.IsNullOrWhiteSpace(id) ? kind + "|id:" + id.Trim() : kind + "|name:" + NormalizeExclusionName(name);

    public bool IsContentExcluded(string kind, string? id, string? name = null)
    {
        var key = ExclusionIdentity(kind, id, name);
        if (key.EndsWith("|name:", StringComparison.Ordinal)) return false;
        return ContentExclusions.Any(x => ExclusionIdentity(x.Kind, x.Id, x.Name) == key);
    }

    public bool IsTrackExcluded(TrackItem track)
    {
        if (IsContentExcluded("track", track.Id, track.Title)) return true;
        if (track.Album is { } album && IsContentExcluded("album", album.Id, album.Name)) return true;
        return track.Artists.Any(a => IsContentExcluded("artist", a.Id, a.Name));
    }

    public bool IsMediaCardExcluded(MediaCardItem card) =>
        (card.Kind is "artist" or "album") && IsContentExcluded(card.Kind, card.Id, card.Title);

    public bool SetContentExcluded(string kind, string? id, string name, string subtitle, bool excluded)
    {
        var key = ExclusionIdentity(kind, id, name);
        var existing = ContentExclusions.FirstOrDefault(x => ExclusionIdentity(x.Kind, x.Id, x.Name) == key);
        if (excluded)
        {
            if (existing is null)
                ContentExclusions.Add(new ContentExclusion { Kind = kind, Id = id ?? "", Name = name ?? "", Subtitle = subtitle ?? "", AddedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        }
        else if (existing is not null) ContentExclusions.Remove(existing);
        SaveDebounced();
        return excluded;
    }

    public bool ToggleContentExclusion(string kind, string? id, string name, string subtitle = "") =>
        SetContentExcluded(kind, id, name, subtitle, !IsContentExcluded(kind, id, name));

    // --------------------------------------------------------- Library Mutations

    public bool IsLiked(string videoId) => LikedSongs.Any(t => t.Id == videoId);

    public bool ToggleLike(TrackItem track, InnerTubeClient? yt = null)
    {
        int idx = LikedSongs.FindIndex(t => t.Id == track.Id);
        bool nowLiked;
        if (idx >= 0)
        {
            LikedSongs.RemoveAt(idx);
            nowLiked = false;
        }
        else
        {
            var copy = track.Clone();
            copy.AddedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            LikedSongs.Insert(0, copy);
            nowLiked = true;
        }
        SaveDebounced();
        LibraryChanged?.Invoke();
        if (yt is { IsSignedIn: true } && !track.IsLocal)
            _ = Task.Run(() => yt.RateSongOnCloudAsync(track.Id, nowLiked));
        return nowLiked;
    }

    public bool IsAlbumSaved(string id) => SavedAlbums.Any(a => a.Id == id);

    public bool ToggleSaveAlbum(MediaCardItem card)
    {
        int idx = SavedAlbums.FindIndex(a => a.Id == card.Id);
        if (idx >= 0) SavedAlbums.RemoveAt(idx);
        else SavedAlbums.Insert(0, card);
        SaveDebounced();
        LibraryChanged?.Invoke();
        return idx < 0;
    }

    public bool IsArtistFollowed(string id) => FollowedArtists.Any(a => a.Id == id);

    public bool ToggleFollowArtist(MediaCardItem artist)
    {
        int idx = FollowedArtists.FindIndex(a => a.Id == artist.Id);
        if (idx >= 0) FollowedArtists.RemoveAt(idx);
        else FollowedArtists.Insert(0, artist);
        SaveDebounced();
        LibraryChanged?.Invoke();
        return idx < 0;
    }

    public LocalPlaylist CreatePlaylist(string name, string description = "", IEnumerable<TrackItem>? initialTracks = null)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pl = new LocalPlaylist
        {
            Id = "pl_" + Guid.NewGuid().ToString("N")[..10],
            Name = string.IsNullOrWhiteSpace(name) ? "My Playlist" : name.Trim(),
            Description = description,
            Tracks = initialTracks?.Select(t => t.Clone()).ToList() ?? new List<TrackItem>(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        Playlists.Insert(0, pl);
        SaveDebounced();
        LibraryChanged?.Invoke();
        return pl;
    }

    public void DeletePlaylist(string playlistId)
    {
        Playlists.RemoveAll(p => p.Id == playlistId);
        SaveDebounced();
        LibraryChanged?.Invoke();
    }

    public void RenamePlaylist(string playlistId, string newName)
    {
        var pl = Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (pl is null || string.IsNullOrWhiteSpace(newName)) return;
        pl.Name = newName.Trim();
        pl.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SaveDebounced();
        LibraryChanged?.Invoke();
    }

    public bool AddTrackToPlaylist(string playlistId, TrackItem track)
    {
        var pl = Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (pl is null) return false;
        if (pl.Tracks.Any(t => t.Id == track.Id)) return false;
        var copy = track.Clone();
        copy.AddedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        pl.Tracks.Add(copy);
        pl.UpdatedAt = copy.AddedAt;
        SaveDebounced();
        LibraryChanged?.Invoke();
        return true;
    }

    public void RemoveTrackFromPlaylist(string playlistId, string trackId)
    {
        var pl = Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (pl is null) return;
        pl.Tracks.RemoveAll(t => t.Id == trackId);
        pl.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SaveDebounced();
        LibraryChanged?.Invoke();
    }

    public void AddRecentSearch(string query)
    {
        var q = query.Trim();
        if (string.IsNullOrEmpty(q)) return;
        RecentSearches.RemoveAll(s => string.Equals(s, q, StringComparison.OrdinalIgnoreCase));
        RecentSearches.Insert(0, q);
        if (RecentSearches.Count > 16) RecentSearches.RemoveRange(16, RecentSearches.Count - 16);
        SaveDebounced();
    }

    public void SetLocalTracks(List<TrackItem> tracks)
    {
        LocalTracks = tracks;
        LibraryChanged?.Invoke();
    }

    // --------------------------------------------------------- Play History & Echo Wrapped

    public PlayHistoryEntry RecordPlay(TrackItem track, long msPlayed, bool wasSkipped)
    {
        var entry = new PlayHistoryEntry
        {
            EntryKind = "track",
            VideoId = track.Id,
            Title = track.Title,
            Artists = track.Artists.Select(a => new ArtistRef { Name = a.Name, Id = a.Id }).ToList(),
            Album = track.Album is null ? null : new AlbumRef { Name = track.Album.Name, Id = track.Album.Id },
            Thumbnail = track.Thumbnail,
            LocalFilePath = track.LocalFilePath,
            DurationSeconds = track.DurationSeconds,
            PlayedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MsPlayed = msPlayed,
            WasSkipped = wasSkipped,
        };
        History.Insert(0, entry);
        TrimHistory();
        SaveDebounced();
        LibraryChanged?.Invoke();
        return entry;
    }

    public void UpdatePlayHistory(PlayHistoryEntry entry, long msPlayed, bool wasSkipped)
    {
        if (!History.Contains(entry) || entry.EntryKind != "track") return;
        entry.MsPlayed = Math.Max(0, msPlayed);
        entry.PlayedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        entry.WasSkipped = wasSkipped;
        SaveDebounced();
        LibraryChanged?.Invoke();
    }

    public void RecordPlaylistPlay(PlaybackCollectionContext source) => RecordCollectionPlay(source);

    public void RecordCollectionPlay(PlaybackCollectionContext source)
    {
        if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.Title)) return;
        string entryKind = string.Equals(source.Kind, "album", StringComparison.OrdinalIgnoreCase) ? "album" : "playlist";
        string sourceKind = source.IsLocal ? "local" : "youtube";
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var matches = History.Where(h => h.EntryKind == entryKind
            && string.Equals(h.PlaylistId, source.Id, StringComparison.Ordinal)
            && string.Equals(h.PlaylistSource ?? "youtube", sourceKind, StringComparison.OrdinalIgnoreCase)).ToList();
        var entry = matches.FirstOrDefault();
        if (entry is null)
        {
            entry = new PlayHistoryEntry { EntryKind = entryKind, PlaylistId = source.Id, PlaylistSource = sourceKind };
        }
        else
        {
            foreach (var duplicate in matches) History.Remove(duplicate);
        }
        entry.PlaylistId = source.Id;
        entry.PlaylistSource = sourceKind;
        entry.Title = source.Title;
        entry.Thumbnail = source.Thumbnail;
        entry.PlayedAt = now;
        History.Insert(0, entry);
        TrimHistory();
        SaveDebounced();
        LibraryChanged?.Invoke();
    }

    private void TrimHistory()
    {
        if (History.Count > 5000)
            History.RemoveRange(5000, History.Count - 5000);
    }

    public WrappedStats ComputeWrapped(int? daysWindow = null)
    {
        long cutoff = daysWindow is int d
            ? DateTimeOffset.UtcNow.AddDays(-d).ToUnixTimeMilliseconds()
            : 0;
        var items = History.Where(h => h.EntryKind == "track" && h.PlayedAt >= cutoff).ToList();

        var stats = new WrappedStats
        {
            TotalPlays = items.Count,
            TotalMinutes = Math.Round(items.Sum(i => i.MsPlayed) / 60_000.0, 1),
            UniqueSongs = items.Select(i => i.VideoId).Distinct().Count(),
            UniqueArtists = items.SelectMany(i => i.Artists.Select(a => a.Name)).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        };

        stats.TopTracks = items
            .GroupBy(i => i.VideoId)
            .Select(g =>
            {
                var f = g.First();
                var tr = new TrackItem
                {
                    Id = f.VideoId,
                    Title = f.Title,
                    Artists = f.Artists,
                    Album = f.Album,
                    Thumbnail = f.Thumbnail,
                    DurationSeconds = f.DurationSeconds,
                };
                return (tr, g.Count(), Math.Round(g.Sum(x => x.MsPlayed) / 60_000.0, 1));
            })
            .OrderByDescending(x => x.Item2)
            .ThenByDescending(x => x.Item3)
            .Take(10)
            .ToList();

        stats.TopArtists = items
            .Where(i => i.Artists.Count > 0)
            .GroupBy(i => i.Artists[0].Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.First().Thumbnail, g.Count(), Math.Round(g.Sum(x => x.MsPlayed) / 60_000.0, 1)))
            .OrderByDescending(x => x.Item3)
            .Take(8)
            .ToList();

        double repeatRatio = stats.UniqueSongs > 0 ? (double)stats.TotalPlays / stats.UniqueSongs : 1.0;
        stats.PersonalityArchetype = stats.TotalPlays switch
        {
            0 => "Fresh Listener - Play songs to unlock your Echo Wrapped!",
            _ when repeatRatio >= 3.2 => "Obsessive Replay Loyalist",
            _ when stats.UniqueArtists >= 25 => "Genre-Hopping Curator",
            _ when items.Count(i => DateTimeOffset.FromUnixTimeMilliseconds(i.PlayedAt).LocalDateTime.Hour is >= 22 or <= 4) > items.Count * 0.45 => "Midnight Audio Philosopher",
            _ => "Sonic Explorer"
        };

        return stats;
    }

    // --------------------------------------------------------- Backup & Restore

    public void ExportBackup(string filePath)
    {
        File.WriteAllText(filePath, JsonSerializer.Serialize(_db, JsonOpts));
    }

    public void ImportBackup(string filePath)
    {
        var txt = File.ReadAllText(filePath);
        var imported = JsonSerializer.Deserialize<PersistentDatabase>(txt, JsonOpts)
                       ?? throw new InvalidOperationException("Invalid Echo Music backup file.");
        _db = imported;
        ReconcileDownloadsOnDisk();
        SaveNow();
        LibraryChanged?.Invoke();
        DownloadsChanged?.Invoke();
    }

    // --------------------------------------------------------- Offline Download Manager

    private void ReconcileDownloadsOnDisk()
    {
        foreach (var d in Downloads)
        {
            if (d.Status is "downloading" or "queued")
                d.Status = "error";
            if (d.Status == "done")
            {
                var p = Path.Combine(DownloadsDir, d.FileName);
                if (!File.Exists(p)) d.Status = "error";
            }
        }
    }

    public string? TryGetDownloadedFilePath(string videoId)
    {
        var d = Downloads.FirstOrDefault(x => x.VideoId == videoId && x.Status == "done");
        if (d is null || string.IsNullOrEmpty(d.FileName)) return null;
        var full = Path.Combine(DownloadsDir, d.FileName);
        return File.Exists(full) ? full : null;
    }

    public bool IsDownloaded(string videoId) => TryGetDownloadedFilePath(videoId) is not null;

    public long GetOfflineDiskUsageBytes()
    {
        try
        {
            return Directory.EnumerateFiles(DownloadsDir).Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    public void EnqueueDownload(TrackItem track, StreamResolver resolver, HttpClient http)
    {
        if (track.IsLocal) return;
        if (_activeDownloads.ContainsKey(track.Id) || IsDownloaded(track.Id)) return;

        Downloads.RemoveAll(d => d.VideoId == track.Id);
        var entry = new DownloadEntry
        {
            VideoId = track.Id,
            Title = track.Title,
            Artists = track.Artists.Select(a => new ArtistRef { Name = a.Name, Id = a.Id }).ToList(),
            Album = track.Album is null ? null : new AlbumRef { Name = track.Album.Name, Id = track.Album.Id },
            Thumbnail = track.Thumbnail,
            DurationSeconds = track.DurationSeconds,
            Status = "downloading",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        Downloads.Insert(0, entry);
        DownloadsChanged?.Invoke();

        var cts = new CancellationTokenSource();
        _activeDownloads[track.Id] = cts;

        _ = Task.Run(async () =>
        {
            string tempFile = "";
            try
            {
                var resolved = await resolver.ResolveAsync(track.Id, preferWebmOpus: true, forceRefresh: false, cts.Token);
                var ext = resolved.MimeType.Contains("webm", StringComparison.OrdinalIgnoreCase) ? ".webm" : ".m4a";
                var safeName = string.Concat($"{track.Title} - {track.ArtistNames}".Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                if (safeName.Length > 80) safeName = safeName[..80].Trim();
                var finalName = $"{safeName} [{track.Id}]{ext}";
                var finalPath = Path.Combine(DownloadsDir, finalName);
                tempFile = finalPath + ".part";

                entry.FileName = finalName;
                entry.TotalBytes = resolved.ContentLength;
                entry.QualityLabel = $"{resolved.Codec.ToUpperInvariant()} {(resolved.Bitrate / 1000)} kbps";
                DownloadsChanged?.Invoke();

                const long chunkSize = 1536 * 1024;
                long offset = 0;
                long total = resolved.ContentLength > 0 ? resolved.ContentLength : long.MaxValue;

                await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    while (offset < total)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        long end = total < long.MaxValue ? Math.Min(total - 1, offset + chunkSize - 1) : offset + chunkSize - 1;
                        using var req = new HttpRequestMessage(HttpMethod.Get, resolved.Url);
                        req.Headers.TryAddWithoutValidation("User-Agent", resolved.UserAgent);
                        if (!string.IsNullOrWhiteSpace(resolved.MediaOrigin))
                        {
                            req.Headers.TryAddWithoutValidation("Origin", resolved.MediaOrigin);
                            req.Headers.TryAddWithoutValidation("Referer", resolved.MediaOrigin.TrimEnd('/') + "/");
                        }
                        req.Headers.Range = new RangeHeaderValue(offset, end);

                        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                        if (res.StatusCode != HttpStatusCode.PartialContent && !res.IsSuccessStatusCode)
                            throw new HttpRequestException($"HTTP {(int)res.StatusCode}");

                        if (res.Content.Headers.ContentRange?.Length is long crLen && crLen > 0)
                        {
                            total = crLen;
                            entry.TotalBytes = crLen;
                        }

                        var bytes = await res.Content.ReadAsByteArrayAsync(cts.Token);
                        if (bytes.Length == 0) break;
                        await fs.WriteAsync(bytes, cts.Token);
                        offset += bytes.Length;
                        entry.ReceivedBytes = offset;
                        entry.Progress = total < long.MaxValue && total > 0 ? Math.Clamp((double)offset / total, 0, 1) : 0.5;
                        DownloadsChanged?.Invoke();

                        if (total == long.MaxValue && bytes.Length < chunkSize)
                            break;
                    }
                }

                File.Move(tempFile, finalPath, overwrite: true);
                entry.Status = "done";
                entry.Progress = 1.0;
                SaveDebounced();
                DownloadsChanged?.Invoke();
            }
            catch (OperationCanceledException)
            {
                entry.Status = "cancelled";
                if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile)) { try { File.Delete(tempFile); } catch { } }
                DownloadsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                entry.Status = "error";
                entry.ErrorMessage = ex.Message;
                if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile)) { try { File.Delete(tempFile); } catch { } }
                SaveDebounced();
                DownloadsChanged?.Invoke();
            }
            finally
            {
                _activeDownloads.TryRemove(track.Id, out _);
            }
        });
    }

    public void CancelOrDeleteDownload(string videoId)
    {
        if (_activeDownloads.TryRemove(videoId, out var cts))
            cts.Cancel();

        var existing = Downloads.FirstOrDefault(d => d.VideoId == videoId);
        if (existing is not null && !string.IsNullOrEmpty(existing.FileName))
        {
            var path = Path.Combine(DownloadsDir, existing.FileName);
            if (File.Exists(path)) { try { File.Delete(path); } catch { } }
        }
        Downloads.RemoveAll(d => d.VideoId == videoId);
        SaveDebounced();
        DownloadsChanged?.Invoke();
    }
}
