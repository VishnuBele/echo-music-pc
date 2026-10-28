using System.Text.Json.Serialization;

namespace EchoMusic.Core;

public sealed class ArtistRef
{
    public string Name { get; set; } = "";
    public string? Id { get; set; }
}

public sealed class AlbumRef
{
    public string Name { get; set; } = "";
    public string? Id { get; set; }
}

public sealed class TrackItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<ArtistRef> Artists { get; set; } = new();
    public AlbumRef? Album { get; set; }
    public string AlbumArtist { get; set; } = "";
    public int DurationSeconds { get; set; }
    public long SpotifyDurationMilliseconds { get; set; }
    public string DurationText { get; set; } = "";
    public string Thumbnail { get; set; } = "";
    public bool IsVideo { get; set; }
    public bool Explicit { get; set; }
    public string? Plays { get; set; }
    public string? PlaylistId { get; set; }
    public string? SetVideoId { get; set; }
    public string? LocalFilePath { get; set; }
    public long AddedAt { get; set; }

    [JsonIgnore]
    public string ArtistNames =>
        Artists.Count > 0
            ? string.Join(", ", Artists.Where(a => !string.IsNullOrWhiteSpace(a.Name)).Select(a => a.Name))
            : "Unknown Artist";

    [JsonIgnore]
    public string FormattedDuration =>
        !string.IsNullOrWhiteSpace(DurationText)
            ? DurationText
            : DurationSeconds > 0
                ? FormatSeconds(DurationSeconds)
                : "";

    [JsonIgnore]
    public double PlaylistDurationSeconds => SpotifyDurationMilliseconds > 0 ? SpotifyDurationMilliseconds / 1000.0 : DurationSeconds;

    [JsonIgnore]
    public bool IsLocal => !string.IsNullOrEmpty(LocalFilePath);

    public static string FormatSeconds(double sec)
    {
        if (double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0) sec = 0;
        var total = (int)Math.Round(sec);
        var h = total / 3600;
        var m = (total % 3600) / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m}:{s:D2}";
    }

    public TrackItem Clone() => new()
    {
        Id = Id,
        Title = Title,
        Artists = Artists.Select(a => new ArtistRef { Name = a.Name, Id = a.Id }).ToList(),
        Album = Album is null ? null : new AlbumRef { Name = Album.Name, Id = Album.Id },
        AlbumArtist = AlbumArtist,
        DurationSeconds = DurationSeconds,
        SpotifyDurationMilliseconds = SpotifyDurationMilliseconds,
        DurationText = DurationText,
        Thumbnail = Thumbnail,
        IsVideo = IsVideo,
        Explicit = Explicit,
        Plays = Plays,
        PlaylistId = PlaylistId,
        SetVideoId = SetVideoId,
        LocalFilePath = LocalFilePath,
        AddedAt = AddedAt,
    };
}

public sealed class MediaCardItem
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "album"; // album | playlist | artist | podcast | mood | local-playlist
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Thumbnail { get; set; } = "";
    [JsonIgnore]
    public List<TrackItem> PreviewTracks { get; set; } = new();
    public string? Params { get; set; }
    public int? Year { get; set; }
    public List<ArtistRef> Artists { get; set; } = new();
}

public sealed class MoodChip
{
    public string Title { get; set; } = "";
    public string Params { get; set; } = "";
    public string BrowseId { get; set; } = "FEmusic_home";
}

public sealed class ShelfSection
{
    public string Title { get; set; } = "";
    public List<TrackItem> Tracks { get; set; } = new();
    public List<MediaCardItem> Cards { get; set; } = new();
    public string? MoreBrowseId { get; set; }
    public string? MoreParams { get; set; }
}

public sealed class HomeFeedResult
{
    public List<MoodChip> MoodChips { get; set; } = new();
    public List<ShelfSection> Shelves { get; set; } = new();
    public string? Continuation { get; set; }
}

public sealed class SearchPageResult
{
    public TrackItem? TopResultTrack { get; set; }
    public MediaCardItem? TopResultCard { get; set; }
    public string TopResultTypeLabel { get; set; } = "";
    public List<TrackItem> Tracks { get; set; } = new();
    public List<TrackItem> Videos { get; set; } = new();
    public List<MediaCardItem> Albums { get; set; } = new();
    public List<MediaCardItem> Artists { get; set; } = new();
    public List<MediaCardItem> Playlists { get; set; } = new();
    public string? Continuation { get; set; }
}

public sealed class DetailPageResult
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "album"; // album | playlist | artist | local-playlist
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Description { get; set; } = "";
    public string Thumbnail { get; set; } = "";
    public int? Year { get; set; }
    public List<ArtistRef> Artists { get; set; } = new();
    public List<TrackItem> Tracks { get; set; } = new();
    public List<ShelfSection> RelatedShelves { get; set; } = new();
    public string? Continuation { get; set; }
    public bool IsLocalPlaylist { get; set; }
}

public sealed class ResolvedStream
{
    public string VideoId { get; set; } = "";
    public string Url { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string Codec { get; set; } = "";
    public int Itag { get; set; }
    public int Bitrate { get; set; }
    public long ContentLength { get; set; }
    public int DurationSeconds { get; set; }
    public double LoudnessDb { get; set; }
    public string ClientName { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
}

public sealed class LyricWord
{
    public double TimeSec { get; set; }
    public string Text { get; set; } = "";
}

public sealed class LyricLine
{
    public double? TimeSec { get; set; }
    public double? EndSec { get; set; }
    public string Text { get; set; } = "";
    public string? TranslatedText { get; set; }
    public string? RomanizedText { get; set; }
    public List<LyricWord> Words { get; set; } = new();
}

public sealed class LyricTimingSegment
{
    public double SourceStartMilliseconds { get; set; }
    public double TargetStartMilliseconds { get; set; }
    public double DurationMilliseconds { get; set; }
}

public sealed class LyricsData
{
    public string VideoId { get; set; } = "";
    public string TimingSourceVideoId { get; set; } = "";
    public List<LyricTimingSegment> TimingMap { get; set; } = new();
    public bool IsSynced { get; set; }
    public bool HasWordSync { get; set; }
    public bool IsInstrumental { get; set; }
    public double AppliedTimingOffsetSeconds { get; set; }
    public string Provider { get; set; } = "";
    public List<LyricLine> Lines { get; set; } = new();
}

public sealed class DnaInfo
{
    public string VideoId { get; set; } = "";
    public string TrackHeadline { get; set; } = "";
    public string TrackStory { get; set; } = "";
    public string ArtistBio { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}

public sealed class LocalPlaylist
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "New Playlist";
    public string Description { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Source { get; set; } = "Local";
    public string SpotifySourceId { get; set; } = "";
    public string CustomCoverPath { get; set; } = "";
    public List<TrackItem> Tracks { get; set; } = new();
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}

public sealed class ContentExclusion
{
    public string Kind { get; set; } = "track"; // track | album | artist
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public long AddedAt { get; set; }
}

public sealed class PlayHistoryEntry
{
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public List<ArtistRef> Artists { get; set; } = new();
    public AlbumRef? Album { get; set; }
    public string Thumbnail { get; set; } = "";
    public int DurationSeconds { get; set; }
    public long PlayedAt { get; set; }
    public long MsPlayed { get; set; }
    public bool WasSkipped { get; set; }
}

public sealed class DownloadEntry
{
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public List<ArtistRef> Artists { get; set; } = new();
    public AlbumRef? Album { get; set; }
    public string Thumbnail { get; set; } = "";
    public int DurationSeconds { get; set; }
    public string Status { get; set; } = "queued"; // queued | downloading | done | error | cancelled
    public double Progress { get; set; }
    public long TotalBytes { get; set; }
    public long ReceivedBytes { get; set; }
    public string FileName { get; set; } = "";
    public string QualityLabel { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public long CreatedAt { get; set; }
}

public sealed class AppSettings
{
    public int UiDesignGeneration { get; set; } = 5;
    public string Theme { get; set; } = "SpotifyGreen"; // SpotifyGreen | EchoDark | EchoLight | AmoledBlack | AppleVelvet | NothingMono | PookiePink | RosePetalBloom | LateNight
    public string PlayerStyle { get; set; } = "Circle"; // Circle (Spotify Desktop) | Scallop (Echo Expressive)
    public string AppIconStyle { get; set; } = "WaveDefault"; // WaveDefault is the V!SHNU PC emerald waveform
    public string LeftSidebarMode { get; set; } = "Expanded"; // current visual state: Expanded | Compact | Hidden
    public string SidebarCollapseBehavior { get; set; } = "Partial"; // Partial | Stepwise | Hide
    public bool ShowTenSecSkip { get; set; } = false;
    public string AudioBackend { get; set; } = "Win32WaveOut"; // Win32WaveOut | DirectSound | NAudioWaveOut | WasapiPolling
    public int AudioDeviceId { get; set; } = -1; // -1 = Windows Default Sound Mapper
    public string FontChoice { get; set; } = "SpotifyCircular"; // SpotifyCircular | PlusJakarta | Outfit | Inter | System
    public string UiDensity { get; set; } = "Comfortable"; // Comfortable | Compact
    public string AudioQuality { get; set; } = "Best"; // DataSaver | Normal | High | Best
    public double Volume { get; set; } = 0.85;
    public bool NormalizeVolume { get; set; } = true;
    public int CrossfadeSeconds { get; set; } = 0;
    public bool GaplessPreload { get; set; } = true;
    public bool AutoplayEchoBrain { get; set; } = true;
    public bool RunOnStartup { get; set; } = false;
    public bool PauseOnMute { get; set; } = true;
    public bool HideVideoSongs { get; set; } = false;
    public bool HideShorts { get; set; } = true;
    public string EqPreset { get; set; } = "Flat";
    public double[] EqBands { get; set; } = new double[10]; // 31..16k dB
    public double BassBoostDb { get; set; } = 0.0;
    public double PlaybackSpeed { get; set; } = 1.0;
    public bool PitchLockSpeed { get; set; } = false; // false = Vinyl (Slowed+Reverb/Nightcore), true = WSOLA Constant-Pitch Time-Stretch
    public bool AmbientReverb { get; set; } = false;
    public double ReverbMix { get; set; } = 0.24;
    public bool SponsorBlockEnabled { get; set; } = false;
    public string SponsorBlockCategories { get; set; } = "intro,outro"; // retained for migrations
    public bool SponsorBlockAllSegments { get; set; } = false;
    public bool DiscordRpcEnabled { get; set; } = true;
    public bool SmtcEnabled { get; set; } = true;
    public string TranslateLyricsTo { get; set; } = ""; // "", "en", "hi", "es", "ja", "ko", "fr", "de"
    public string CustomAiEndpoint { get; set; } = "";
    public string CustomAiApiKey { get; set; } = "";
    public string CustomAiModel { get; set; } = "";
    public string RegionGl { get; set; } = "IN";
    public string LanguageHl { get; set; } = "en";
    public bool CloseToTray { get; set; } = true;
    public bool MediaKeysEnabled { get; set; } = true;
    // New three-state policy. Null/empty allows old bool-only settings to migrate cleanly.
    public string? ResourceSaverPolicy { get; set; }
    // Legacy JSON field retained only to read v2.0.1 preferences; synced to Policy during load/save.
    public bool ResourceSaverMode { get; set; } = false;
    public string UpdateRepository { get; set; } = ""; // retained for settings-file migration; the release checker uses the fixed public PC repository
    public List<string> LocalMusicFolders { get; set; } = new();
    [JsonIgnore] public string? CookieHeader { get; set; }
    [JsonIgnore] public string? DataSyncId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountHandle { get; set; }
}
