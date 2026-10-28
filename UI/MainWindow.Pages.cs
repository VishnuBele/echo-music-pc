using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Win32;
using EchoMusic.Core;

namespace EchoMusic.UI;

public partial class MainWindow
{
    private DispatcherTimer? _spotifyPlaceholderTimer;
    private Action? _spotifyPlaceholderSync;
    private string _libraryPlaylistSortKey = "date";
    private bool _libraryPlaylistSortDescending = true;
    private Image? _discordPreviewArtwork;
    private TextBlock? _discordPreviewStatus;
    private TextBlock? _discordPreviewTrack;
    private TextBlock? _discordPreviewArtists;
    private Border? _discordPreviewProgressTrack;
    private Border? _discordPreviewProgressFill;
    private TextBlock? _discordPreviewElapsed;
    private TextBlock? _discordPreviewDuration;
    private sealed record AppIconChoice(string Style, string Name, string Description);
    private static readonly AppIconChoice[] AppIconChoices =
    {
        new("WaveDefault", "Echo Music PC", "Green PC waveform · default"),
        new("MonoBlack", "Echo Music PC - Black Logo", "Black waveform on white"),
        new("MonoWhite", "Echo Music PC - White Logo", "White waveform on black"),
        new("MobileBillie", "Billie Eilish", "Official mobile artwork · suggested by Lalo Salamanca"),
        new("MobileCat", "Cat Icon", "Official mobile artwork · pink cat theme"),
        new("MobileCraft", "Craft", "Official mobile artwork · CR3TO_"),
        new("CrazyBlue", "Crazy Blue Icon", "Official mobile artwork · blue"),
        new("EchoCat", "Echo Cat", "Official mobile artwork · Alarp_Svc"),
        new("MobileLegacy", "Legacy Icon", "Official mobile artwork · monochrome"),
        new("MobileNew", "New Icon", "Official mobile artwork · standard vibrant"),
        new("MobilePookie", "Pookie Icon", "Official mobile artwork · pink"),
        new("MobileSabrina", "Sabrina Carpenter", "Official mobile artwork · suggested by notdieinganymore"),
        new("MobileSabrina2", "Sabrina Carpenter 2", "Official mobile artwork · suggested by Falvik"),
        new("MobileSadie", "Sadie Sink", "Official mobile artwork · community request"),
        new("Sky", "Sky Icon", "Official mobile artwork · sky theme"),
    };

    private string _settingsSearchQuery = "";
    private Image? _flowArtImage;
    private TextBlock? _flowTitleBlock;
    private TextBlock? _flowArtistBlock;
    private SpectrumVisualizerControl? _flowSpectrum;
    private Border? _flowLyricCard;
    private StackPanel? _flowLyricContent;

    // ========================================================= 1. MUSIC FLOW (Vertical Discovery Mode)

    internal async Task LoadMusicFlowViewAsync()
    {
        if (_audio.Queue.Count == 0)
        {
            ShowLoadingSkeleton("Starting Echo Music Flow discovery stream...");
            try
            {
                var brain = await _extras.BuildEchoBrainShelfAsync(_store);
                List<TrackItem> seeds = brain?.Tracks ?? new();
                if (seeds.Count == 0)
                {
                    var home = _cachedHome ?? await _yt.GetHomeAsync();
                    seeds = home.Shelves.SelectMany(s => s.Tracks).Take(25).ToList();
                }
                if (seeds.Count == 0)
                {
                    var sr = await _yt.SearchAsync("top global hits", "songs");
                    seeds = sr.Tracks;
                }
                if (seeds.Count > 0)
                    await _audio.PlayQueueAsync(seeds, 0, autoRadioIfSingle: true);
            }
            catch (Exception ex)
            {
                SetCenterContent(ErrorCard("Music Flow unavailable", ex.Message, () => _ = LoadMusicFlowViewAsync()));
                return;
            }
        }

        var card = new Border
        {
            Background = ResBrush("BgCardBrush"),
            BorderBrush = ResBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(26),
            Padding = new Thickness(32, 28),
            MinHeight = 560,
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // Mouse-wheel vertical swipe just like mobile TikTok/Music Flow!
        DateTime lastWheel = DateTime.MinValue;
        card.PointerWheelChanged += async (_, ev) =>
        {
            if ((DateTime.UtcNow - lastWheel).TotalMilliseconds < 450) return;
            lastWheel = DateTime.UtcNow;
            if (ev.Delta.Y < -0.2) await _audio.NextAsync();
            else if (ev.Delta.Y > 0.2) await _audio.PreviousAsync();
            ev.Handled = true;
        };

        var stack = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
        var flowHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
        flowHeader.Children.Add(new PathIcon { Data = Icons.Flow, Width = 13, Height = 13, Foreground = ResBrush("AccentBrush"), VerticalAlignment = VerticalAlignment.Center });
        flowHeader.Children.Add(new TextBlock { Text = "ECHO MUSIC FLOW  •  SCROLL OR CLICK NEXT TO SURF", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush"), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(new Border
        {
            Background = ResBrush("AccentSoftBrush"),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(14, 5),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = flowHeader,
        });

        _flowArtImage = new Image { Width = 270, Height = 270, Stretch = Stretch.UniformToFill };
        stack.Children.Add(new Border
        {
            Width = 270,
            Height = 270,
            CornerRadius = new CornerRadius(24),
            ClipToBounds = true,
            Background = ResBrush("BgSurfaceBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            BoxShadow = BoxShadows.Parse("0 20 50 0 #70000000"),
            Child = _flowArtImage,
        });

        _flowTitleBlock = new TextBlock { FontSize = 24, FontWeight = FontWeight.ExtraBold, TextAlignment = TextAlignment.Center, MaxWidth = 560, TextTrimming = TextTrimming.CharacterEllipsis };
        _flowArtistBlock = new TextBlock { FontSize = 14.5, Foreground = ResBrush("TextSecondaryBrush"), TextAlignment = TextAlignment.Center };
        stack.Children.Add(_flowTitleBlock);
        stack.Children.Add(_flowArtistBlock);

        // Upstream Echo Music Flow 2-line ExtraBold live synced lyrics
        _flowLyricLine1 = new TextBlock
        {
            Text = "",
            FontSize = 25,
            FontWeight = FontWeight.ExtraBold,
            Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseOut() },
                new DoubleTransition { Property = TextBlock.FontSizeProperty, Duration = TimeSpan.FromMilliseconds(360), Easing = new CubicEaseOut() },
            },
            Foreground = ResBrush("AccentBrush"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = 560,
            MaxWidth = 560,
        };
        _flowLyricLine2 = new TextBlock
        {
            Text = "",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseOut() },
                new DoubleTransition { Property = TextBlock.FontSizeProperty, Duration = TimeSpan.FromMilliseconds(360), Easing = new CubicEaseOut() },
            },
            Foreground = ResBrush("TextMutedBrush"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = 560,
            MaxWidth = 560,
        };
        var flowLyricContent = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Stretch };
        flowLyricContent.Children.Add(_flowLyricLine1);
        flowLyricContent.Children.Add(_flowLyricLine2);
        _flowSpectrum = new SpectrumVisualizerControl { Width = 260, Height = 38, BarCount = 30, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        flowLyricContent.Children.Add(_flowSpectrum);
        _flowLyricContent = flowLyricContent;
        _flowLyricCard = new Border
        {
            MaxWidth = 660,
            Padding = new Thickness(18, 13),
            CornerRadius = new CornerRadius(18),
            Background = ResBrush("AccentSoftBrush"),
            BorderBrush = ResBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(360), Easing = new CubicEaseOut() } },
            Child = flowLyricContent,
        };
        stack.Children.Add(_flowLyricCard);
        _flowLyricStatus = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = ResBrush("TextMutedBrush"),
            IsVisible = false,
            TextAlignment = TextAlignment.Center,
        };
        stack.Children.Add(_flowLyricStatus);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        var prevBtn = new Button { Classes = { "pill" }, Content = "↑ Previous" };
        prevBtn.Click += async (_, _) => await _audio.PreviousAsync();

        var likeBtn = new Button
        {
            Classes = { "pill" },
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { new PathIcon { Data = Icons.HeartOutline, Width = 15, Height = 15, Foreground = ResBrush("TextSecondaryBrush") }, new TextBlock { Text = "Like Song" } } },
        };
        likeBtn.Click += (_, _) =>
        {
            if (_audio.CurrentTrack is { } t)
            {
                bool liked = _store.ToggleLike(t, _yt);
                ShowToast(liked ? "Saved to Liked Songs" : "Removed from Liked Songs");
            }
        };

        var nextBtn = new Button { Classes = { "accentBtn" }, Content = "↓ Next Flow Track" };
        nextBtn.Click += async (_, _) => await _audio.NextAsync();

        var lyricsBtn = new Button { Classes = { "pill" }, Content = "♫ Lyrics" };
        lyricsBtn.Click += (_, _) => SetDockTab("lyrics", ensureOpen: true);

        var radioBtn = new Button { Classes = { "pill" }, Content = "More Like This" };
        radioBtn.Click += async (_, _) =>
        {
            if (_audio.CurrentTrack is { IsLocal: false } t)
            {
                ShowToast($"Tuning Flow toward \"{t.Title}\"...");
                var r = await _yt.GetRadioQueueAsync(t.Id);
                if (r.Count > 0) await _audio.PlayQueueAsync(r, 0, autoRadioIfSingle: false);
            }
        };

        actions.Children.Add(prevBtn);
        actions.Children.Add(likeBtn);
        actions.Children.Add(nextBtn);
        actions.Children.Add(lyricsBtn);
        actions.Children.Add(radioBtn);
        stack.Children.Add(actions);

        card.Child = stack;
        SetCenterContent(card);
        UpdateMusicFlowLyricWidth();
        RefreshMusicFlowCardUi();
    }

    internal void RefreshMusicFlowCardUi()
    {
        if (_activeNavKey != "flow") return;
        var tr = _audio.CurrentTrack;
        if (_flowTitleBlock is not null) _flowTitleBlock.Text = tr?.Title ?? "Music Flow";
        if (_flowArtistBlock is not null) _flowArtistBlock.Text = tr?.ArtistNames ?? "";
        if (_flowArtImage is not null && tr is not null)
            LoadImageInto(_flowArtImage, InnertubeParsers.UpgradeThumb(tr.Thumbnail, 544));
    }

    private void UpdateMusicFlowLyricWidth()
    {
        if (_flowLyricCard is null || _flowLyricLine1 is null || _flowLyricLine2 is null || _flowLyricContent is null) return;
        double available = CenterViewHost.Bounds.Width;
        if (available <= 1) available = 660;
        double textWidth = Math.Clamp(available - 100, 140, 560);
        _flowLyricLine1.Width = textWidth;
        _flowLyricLine2.Width = textWidth;
        _flowLyricContent.Width = textWidth;
        _flowLyricCard.Width = textWidth + 36;
    }

    // ========================================================= 2. LIBRARY VIEW

    internal void RenderLibraryView(string activeTab)
    {
        var root = new StackPanel { Spacing = 18 };
        IEnumerable<LocalPlaylist> SortedPlaylists()
        {
            IEnumerable<LocalPlaylist> ordered = _libraryPlaylistSortKey switch
            {
                "name" => _store.Playlists.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
                "updated" => _store.Playlists.OrderBy(p => p.UpdatedAt),
                "tracks" => _store.Playlists.OrderBy(p => p.Tracks.Count),
                _ => _store.Playlists.OrderBy(p => p.CreatedAt),
            };
            return _libraryPlaylistSortDescending ? ordered.Reverse() : ordered;
        }

        // 1:1 Echo Music Library Filter Chips (Matches Screenshots/library.png: Playlists, Songs, Albums, Artists, Spotify, Local!)
        var tabs = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (tid, lbl) in new[]
                 {
                     ("overview", "Playlists"),
                     ("liked", $"Songs ({_store.LikedSongs.Count})"),
                     ("albums", $"Albums ({_store.SavedAlbums.Count})"),
                     ("artists", $"Artists ({_store.FollowedArtists.Count})"),
                     ("spotify", "Spotify"),
                     ("local", "Local"),
                     ("history", "History"),
                 })
        {
            var ctid = tid;
            bool isSel = activeTab == ctid || (activeTab == "playlists" && ctid == "overview");
            var b = new Button
            {
                Classes = { isSel ? "accentBtn" : "pill" },
                Content = lbl,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 7),
                Margin = new Thickness(0, 0, 8, 8),
                FontWeight = FontWeight.SemiBold,
            };
            b.Click += (_, _) =>
            {
                if (ctid == "spotify") RenderSpotifyImportView();
                else if (ctid == "local") RenderLocalMusicView();
                else RenderLibraryView(ctid);
            };
            tabs.Children.Add(b);
        }
        root.Children.Add(tabs);

        if (activeTab == "overview" || activeTab == "playlists")
        {
            // Playlist ordering is an actual menu + direction toggle, not a decorative date badge.
            var sortRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto") };
            var sortLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            string SortLabel() => _libraryPlaylistSortKey switch
            {
                "name" => "Name",
                "updated" => "Last updated",
                "tracks" => "Track count",
                _ => "Date added",
            };
            var sortButton = new Button
            {
                Classes = { "pill" },
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(13, 7),
                Height = 36,
            };
            ToolTip.SetTip(sortButton, "Choose how playlists are sorted");
            var sortContent = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
            var sortLabel = new TextBlock { Text = SortLabel(), FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var sortChevron = new PathIcon { Data = Icons.ChevronDown, Width = 13, Height = 13, Foreground = ResBrush("TextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            sortContent.Children.Add(sortLabel);
            Grid.SetColumn(sortChevron, 1);
            sortContent.Children.Add(sortChevron);
            sortButton.Content = sortContent;
            sortButton.Click += (_, _) =>
            {
                var menu = new ContextMenu();
                foreach (var (key, label) in new[] { ("date", "Date added"), ("name", "Name"), ("updated", "Last updated"), ("tracks", "Track count") })
                {
                    string sortKey = key;
                    var item = new MenuItem { Header = _libraryPlaylistSortKey == key ? $"✓  {label}" : label };
                    item.Click += (_, _) =>
                    {
                        _libraryPlaylistSortKey = sortKey;
                        RenderLibraryView(activeTab);
                    };
                    menu.Items.Add(item);
                }
                menu.Open(sortButton);
            };
            sortLeft.Children.Add(sortButton);

            var directionButton = new Button
            {
                Classes = { "pill" },
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(18),
                Content = new TextBlock
                {
                    Text = _libraryPlaylistSortDescending ? "↓" : "↑",
                    FontSize = 15,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ToolTip.SetTip(directionButton, _libraryPlaylistSortDescending ? "Descending order; click for ascending" : "Ascending order; click for descending");
            directionButton.Click += (_, _) =>
            {
                _libraryPlaylistSortDescending = !_libraryPlaylistSortDescending;
                RenderLibraryView(activeTab);
            };
            sortLeft.Children.Add(directionButton);
            sortRow.Children.Add(sortLeft);

            var fabBtn = new Button
            {
                Classes = { "accentBtn" },
                Content = "+  New Playlist",
                Padding = new Thickness(16, 8),
            };
            fabBtn.Click += (_, _) =>
            {
                var pl = _store.CreatePlaylist($"My Playlist #{_store.Playlists.Count + 1}");
                RenderLocalPlaylistDetail(pl);
            };
            Grid.SetColumn(fabBtn, 2);
            sortRow.Children.Add(fabBtn);
            root.Children.Add(sortRow);

            // 1:1 Echo Music 2-Column Rounded Quick-Access Tile Grid (Matches Screenshots/library.png!)
            var tileGrid = new UniformGrid { Columns = 2 };
            Control MakeQuickTile(Geometry icon, string title, string sub, Action onClick)
            {
                var g = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*") };
                g.Children.Add(new PathIcon { Data = icon, Width = 18, Height = 18, Foreground = ResBrush("TextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 14, 0) });
                var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                st.Children.Add(new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeight.Bold });
                if (!string.IsNullOrEmpty(sub))
                    st.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = ResBrush("TextSecondaryBrush") });
                Grid.SetColumn(st, 1);
                g.Children.Add(st);
                var b = new Border
                {
                    Background = ResBrush("BgCardBrush"),
                    BorderBrush = ResBrush("BorderSubtleBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(16, 14),
                    Margin = new Thickness(0, 0, 10, 10),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = g,
                };
                b.Tapped += (_, _) => onClick();
                return b;
            }

            tileGrid.Children.Add(MakeQuickTile(Icons.HeartFilled, "Liked", $"{_store.LikedSongs.Count} songs", () => RenderLibraryView("liked")));
            tileGrid.Children.Add(MakeQuickTile(Icons.CheckCircle, "Downloaded", $"{_store.Downloads.Count(d => d.Status == "done")} offline", RenderDownloadsView));
            tileGrid.Children.Add(MakeQuickTile(Icons.Spotify, "Spotify Library", "Import & stream playlists", RenderSpotifyImportView));
            tileGrid.Children.Add(MakeQuickTile(Icons.Repeat, "Cached & History", $"{_store.History.Count} plays", () => RenderLibraryView("history")));
            tileGrid.Children.Add(MakeQuickTile(Icons.Stats, "My top 50", "Most played tracks", () => RenderStatsWrappedView(null)));
            tileGrid.Children.Add(MakeQuickTile(Icons.Equalizer, "Studio Equalizer", "10-band EQ & WSOLA speed", RenderEqualizerView));
            tileGrid.Children.Add(MakeQuickTile(Icons.Folder, "Local", "PC audio folders", RenderLocalMusicView));
            tileGrid.Children.Add(MakeQuickTile(Icons.Flow, "Music Flow", "Swipe discovery reel", () => _ = LoadMusicFlowViewAsync()));
            root.Children.Add(tileGrid);

            // Pinned Liked Songs + User Playlists section (v1.4.4: "Pinned Liked Songs sits at top with gradient cover")
            root.Children.Add(new TextBlock { Text = "Playlists", FontSize = 20, FontWeight = FontWeight.ExtraBold, Margin = new Thickness(0, 6, 0, 2) });
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

            // Pinned Liked Songs Card (Spotify Desktop signature purple-emerald gradient!)
            var likedCard = new Border
            {
                Width = 162,
                Background = ResBrush("BgCardBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 12, 12),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            var likedStack = new StackPanel { Spacing = 8 };
            likedStack.Children.Add(new Border
            {
                Width = 138,
                Height = 138,
                CornerRadius = new CornerRadius(6),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = new GradientStops { new GradientStop(Color.Parse("#450AF5"), 0), new GradientStop(Color.Parse("#1ED760"), 1) }
                },
                Child = new PathIcon { Data = Icons.HeartFilled, Width = 44, Height = 44, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            likedStack.Children.Add(new TextBlock { Text = "Liked Songs", FontSize = 13.5, FontWeight = FontWeight.Bold });
            likedStack.Children.Add(new TextBlock { Text = $"{_store.LikedSongs.Count} songs", FontSize = 12, Foreground = ResBrush("TextSecondaryBrush") });
            likedCard.Child = likedStack;
            likedCard.Tapped += (_, _) => RenderLibraryView("liked");
            wrap.Children.Add(likedCard);

            foreach (var pl in SortedPlaylists())
            {
                var cpl = pl;
                var tile = BuildMediaCardTile(new MediaCardItem
                {
                    Id = cpl.Id,
                    Kind = "local-playlist",
                    Title = cpl.Name,
                    Subtitle = $"{cpl.Tracks.Count} songs",
                    Thumbnail = cpl.CustomCoverPath,
                    PreviewTracks = cpl.Tracks,
                });
                tile.Margin = new Thickness(0, 0, 14, 14);
                tile.PointerPressed += (_, ev) =>
                {
                    ev.Handled = true;
                    RenderLocalPlaylistDetail(cpl);
                };
                wrap.Children.Add(tile);
            }
            root.Children.Add(wrap);
            SetCenterContent(root);
            return;
        }

        switch (activeTab)
        {
            case "liked":
                if (_store.LikedSongs.Count == 0)
                    root.Children.Add(EmptyHint("Songs you like will appear here."));
                else
                {
                    var playLiked = new Button { Classes = { "accentBtn" }, Content = "▶  Play All Liked Songs", HorizontalAlignment = HorizontalAlignment.Left };
                    playLiked.Click += async (_, _) => await _audio.PlayQueueAsync(_store.LikedSongs, 0, autoRadioIfSingle: false);
                    root.Children.Add(playLiked);
                    root.Children.Add(BuildTrackListTable(_store.LikedSongs, showIndex: true));
                }
                break;

            case "playlists":
                if (_store.Playlists.Count == 0)
                    root.Children.Add(EmptyHint("No local playlists yet. Create one or import from Spotify!"));
                else
                {
                    var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                    foreach (var pl in SortedPlaylists())
                    {
                        var cpl = pl;
                        var tile = BuildMediaCardTile(new MediaCardItem
                        {
                            Id = cpl.Id,
                            Kind = "local-playlist",
                            Title = cpl.Name,
                            Subtitle = $"{cpl.Tracks.Count} songs",
                            Thumbnail = cpl.CustomCoverPath,
                            PreviewTracks = cpl.Tracks,
                        });
                        tile.Margin = new Thickness(0, 0, 14, 14);
                        tile.PointerPressed += (_, ev) =>
                        {
                            ev.Handled = true;
                            RenderLocalPlaylistDetail(cpl);
                        };
                        wrap.Children.Add(tile);
                    }
                    root.Children.Add(wrap);
                }
                break;

            case "albums":
                if (_store.SavedAlbums.Count == 0)
                    root.Children.Add(EmptyHint("Save albums or playlists to access them quickly here."));
                else
                {
                    var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                    foreach (var c in _store.SavedAlbums)
                    {
                        var tile = BuildMediaCardTile(c);
                        tile.Margin = new Thickness(0, 0, 14, 14);
                        wrap.Children.Add(tile);
                    }
                    root.Children.Add(wrap);
                }
                break;

            case "artists":
                if (_store.FollowedArtists.Count == 0)
                    root.Children.Add(EmptyHint("Follow your favorite artists to keep them in Your Library."));
                else
                {
                    var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                    foreach (var c in _store.FollowedArtists)
                    {
                        var tile = BuildMediaCardTile(c);
                        tile.Margin = new Thickness(0, 0, 14, 14);
                        wrap.Children.Add(tile);
                    }
                    root.Children.Add(wrap);
                }
                break;

            case "history":
                var recentTracks = _store.History
                    .GroupBy(h => h.VideoId)
                    .Select(g =>
                    {
                        var f = g.First();
                        return new TrackItem
                        {
                            Id = f.VideoId,
                            Title = f.Title,
                            Artists = f.Artists,
                            Album = f.Album,
                            Thumbnail = f.Thumbnail,
                            DurationSeconds = f.DurationSeconds,
                        };
                    })
                    .Take(60)
                    .ToList();
                root.Children.Add(recentTracks.Count == 0
                    ? EmptyHint("Your listening history will build up automatically as you play music.")
                    : BuildTrackListTable(recentTracks, showIndex: false));
                break;
        }

        SetCenterContent(root);
    }

    // ========================================================= 3. OFFLINE DOWNLOADS VIEW

    internal void RenderDownloadsView()
    {
        var root = new StackPanel { Spacing = 18 };
        double mbUsed = Math.Round(_store.GetOfflineDiskUsageBytes() / (1024.0 * 1024.0), 1);

        var hdr = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        var left = new StackPanel { Spacing = 3 };
        left.Children.Add(new TextBlock { Text = "Offline Downloads", FontSize = 26, FontWeight = FontWeight.ExtraBold });
        left.Children.Add(new TextBlock { Text = $"{_store.Downloads.Count(d => d.Status == "done")} tracks ready offline  •  {mbUsed} MB on disk", FontSize = 13, Foreground = ResBrush("TextSecondaryBrush") });
        hdr.Children.Add(left);

        var doneTracks = _store.Downloads
            .Where(d => d.Status == "done")
            .Select(d => new TrackItem
            {
                Id = d.VideoId,
                Title = d.Title,
                Artists = d.Artists,
                Album = d.Album,
                Thumbnail = d.Thumbnail,
                DurationSeconds = d.DurationSeconds,
                LocalFilePath = _store.TryGetDownloadedFilePath(d.VideoId),
            })
            .ToList();

        if (doneTracks.Count > 0)
        {
            var playOffline = new Button { Classes = { "accentBtn" }, Content = "▶  Play All Offline" };
            playOffline.Click += async (_, _) => await _audio.PlayQueueAsync(doneTracks, 0, autoRadioIfSingle: false);
            Grid.SetColumn(playOffline, 1);
            hdr.Children.Add(playOffline);
        }
        root.Children.Add(hdr);

        if (_store.Downloads.Count == 0)
        {
            root.Children.Add(EmptyHint("Click the Download icon on any song or album to save lossless-container Opus audio for 100% offline listening."));
            SetCenterContent(root);
            return;
        }

        foreach (var d in _store.Downloads.ToList())
        {
            var captured = d;
            var card = new Border
            {
                Background = ResBrush("BgCardBrush"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 10),
                BorderBrush = ResBrush("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
            };
            var g = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("48,*,150,Auto,Auto") };
            var img = new Image { Width = 46, Height = 46, Stretch = Stretch.UniformToFill };
            LoadImageInto(img, captured.Thumbnail);
            g.Children.Add(new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = img });

            var meta = new StackPanel { Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
            meta.Children.Add(new TextBlock { Text = captured.Title, FontSize = 13.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            meta.Children.Add(new TextBlock
            {
                Text = string.Join(", ", captured.Artists.Select(a => a.Name)) + (string.IsNullOrEmpty(captured.QualityLabel) ? "" : $"  •  {captured.QualityLabel}"),
                FontSize = 11.5,
                Foreground = ResBrush("TextSecondaryBrush"),
            });
            if (captured.Status == "downloading")
            {
                meta.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = captured.Progress, Height = 5 });
            }
            Grid.SetColumn(meta, 1);
            g.Children.Add(meta);

            var statusLbl = new TextBlock
            {
                Text = captured.Status switch
                {
                    "done" => "Saved Offline",
                    "downloading" => $"{(int)(captured.Progress * 100)}%",
                    "error" => $"Failed: {captured.ErrorMessage}",
                    _ => captured.Status
                },
                FontSize = 12,
                Foreground = captured.Status == "done" ? ResBrush("AccentBrush") : ResBrush("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(statusLbl, 2);
            g.Children.Add(statusLbl);

            if (captured.Status == "done")
            {
                var pBtn = new Button { Classes = { "pill" }, Content = "Play", Padding = new Thickness(12, 5), Margin = new Thickness(6, 0) };
                pBtn.Click += async (_, _) =>
                {
                    var tr = doneTracks.FirstOrDefault(x => x.Id == captured.VideoId);
                    if (tr is not null) await _audio.PlaySingleAsync(tr);
                };
                Grid.SetColumn(pBtn, 3);
                g.Children.Add(pBtn);
            }

            var delBtn = new Button { Classes = { "ghost" }, Content = new PathIcon { Data = Icons.Trash, Width = 14, Height = 14 } };
            delBtn.Click += (_, _) => _store.CancelOrDeleteDownload(captured.VideoId);
            Grid.SetColumn(delBtn, 4);
            g.Children.Add(delBtn);

            card.Child = g;
            root.Children.Add(card);
        }

        SetCenterContent(root);
    }

    // ========================================================= 4. LOCAL MUSIC SCANNER VIEW

    internal void RenderLocalMusicView()
    {
        var root = new StackPanel { Spacing = 18 };
        var hdr = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto") };
        var left = new StackPanel { Spacing = 3 };
        left.Children.Add(new TextBlock { Text = "Local Music Files", FontSize = 26, FontWeight = FontWeight.ExtraBold });
        left.Children.Add(new TextBlock { Text = "Play MP3, FLAC, M4A, WAV, OGG & OPUS directly from your computer with ID3 cover art", FontSize = 13, Foreground = ResBrush("TextSecondaryBrush") });
        hdr.Children.Add(left);

        var addFolderBtn = new Button { Classes = { "accentBtn" }, Content = "+ Add Music Folder", Margin = new Thickness(0, 0, 8, 0) };
        addFolderBtn.Click += async (_, _) =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Music Folder", AllowMultiple = true });
            bool changed = false;
            foreach (var dir in picked)
            {
                var path = dir.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path) && !_store.Settings.LocalMusicFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    _store.Settings.LocalMusicFolders.Add(path);
                    changed = true;
                }
            }
            if (changed)
            {
                _store.SaveNow();
                ShowToast("Scanning local music folder...");
                var tracks = await MusicDnaAndExtras.ScanLocalFoldersAsync(_store.Settings.LocalMusicFolders, _store.ArtCacheDir);
                _store.SetLocalTracks(tracks);
                RenderLocalMusicView();
            }
        };
        Grid.SetColumn(addFolderBtn, 1);
        hdr.Children.Add(addFolderBtn);

        var rescanBtn = new Button { Classes = { "pill" }, Content = "Rescan" };
        rescanBtn.Click += async (_, _) =>
        {
            ShowToast("Rescanning folders...");
            var tracks = await MusicDnaAndExtras.ScanLocalFoldersAsync(_store.Settings.LocalMusicFolders, _store.ArtCacheDir);
            _store.SetLocalTracks(tracks);
            RenderLocalMusicView();
        };
        Grid.SetColumn(rescanBtn, 2);
        hdr.Children.Add(rescanBtn);
        root.Children.Add(hdr);

        if (_store.Settings.LocalMusicFolders.Count > 0)
        {
            var folderWrap = new WrapPanel();
            foreach (var f in _store.Settings.LocalMusicFolders.ToList())
            {
                var cf = f;
                var pill = new Border
                {
                    Background = ResBrush("BgCardBrush"),
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(12, 5),
                    Margin = new Thickness(0, 0, 8, 8),
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                sp.Children.Add(new TextBlock { Text = cf, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                var xBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(4, 1), Content = new PathIcon { Data = Icons.Close, Width = 11, Height = 11 } };
                xBtn.Click += async (_, _) =>
                {
                    _store.Settings.LocalMusicFolders.Remove(cf);
                    _store.SaveNow();
                    var tracks = await MusicDnaAndExtras.ScanLocalFoldersAsync(_store.Settings.LocalMusicFolders, _store.ArtCacheDir);
                    _store.SetLocalTracks(tracks);
                    RenderLocalMusicView();
                };
                sp.Children.Add(xBtn);
                pill.Child = sp;
                folderWrap.Children.Add(pill);
            }
            root.Children.Add(folderWrap);
        }

        if (_store.LocalTracks.Count == 0)
            root.Children.Add(EmptyHint("Add your Windows Music folder above to browse and play all your local audio files alongside streaming tracks."));
        else
            root.Children.Add(BuildTrackListTable(_store.LocalTracks, showIndex: true));

        SetCenterContent(root);
    }

    // ========================================================= 5. SPOTIFY PUBLIC IMPORTER VIEW

    private static bool IsSpotifyProfileUrl(string url) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            url ?? "",
            @"spotify\.com/(?:intl-[a-z-]+/)?user/[A-Za-z0-9]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string ExtractSpotifyCollectionId(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            url ?? "",
            @"spotify\.com/(?:intl-[a-z-]+/)?(?:playlist|album|track)/([A-Za-z0-9]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }

    internal void RenderSpotifyImportView()
    {
        var root = new StackPanel { Spacing = 18, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock { Text = "Import a Spotify Playlist (No Sign-In)", FontSize = 26, FontWeight = FontWeight.ExtraBold });
        root.Children.Add(new TextBlock
        {
            Text = "Paste one Spotify link and Echo Music will detect whether it is a public profile, playlist, album, or track. Profile links show public playlists; playlist links are imported locally. You can also import a .CSV / .M3U / .M3U8 file. No login or OAuth is required; private playlists are not supported.",
            FontSize = 13.5,
            Foreground = ResBrush("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        _spotifyPlaceholderTimer?.Stop();
        var urlBox = new TextBox
        {
            Padding = new Thickness(14, 11),
            FontSize = 14,
            Height = 44,
        };
        string[] urlExamples =
        {
            "https://open.spotify.com/playlist/your-playlist-id",
            "https://open.spotify.com/album/your-album-id",
            "https://open.spotify.com/track/your-track-id",
            "https://open.spotify.com/user/your-username",
        };
        int urlExampleIndex = 0;
        var urlPlaceholderShift = new TranslateTransform();
        var urlPlaceholder = new TextBlock
        {
            Text = urlExamples[0],
            FontSize = 13,
            Foreground = ResBrush("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(15, 0, 14, 0),
            IsHitTestVisible = false,
            RenderTransform = urlPlaceholderShift,
        };
        var urlInputGrid = new Grid { Height = 44 };
        urlInputGrid.Children.Add(urlBox);
        urlInputGrid.Children.Add(urlPlaceholder);
        bool showSpotifyExample = true;
        void SyncSpotifyPlaceholder()
        {
            showSpotifyExample = string.IsNullOrWhiteSpace(urlBox.Text) && !urlBox.IsFocused;
            urlPlaceholder.IsVisible = showSpotifyExample;
            if (showSpotifyExample && !IsResourceSaverActive) _spotifyPlaceholderTimer?.Start();
            else _spotifyPlaceholderTimer?.Stop();
        }
        urlBox.TextChanged += (_, _) => SyncSpotifyPlaceholder();
        urlBox.GotFocus += (_, _) => SyncSpotifyPlaceholder();
        urlBox.LostFocus += (_, _) => SyncSpotifyPlaceholder();
        _spotifyPlaceholderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2800) };
        _spotifyPlaceholderTimer.Tick += async (_, _) =>
        {
            if (!showSpotifyExample || !urlPlaceholder.IsVisible || !string.IsNullOrWhiteSpace(urlBox.Text)) return;
            _spotifyPlaceholderTimer.Stop();
            const int steps = 10;
            for (int i = 0; i <= steps; i++)
            {
                if (!urlPlaceholder.IsVisible || !string.IsNullOrWhiteSpace(urlBox.Text)) return;
                double t = i / (double)steps;
                urlPlaceholder.Opacity = 1 - t;
                urlPlaceholderShift.X = -14 * t;
                await Task.Delay(16);
            }
            urlExampleIndex = (urlExampleIndex + 1) % urlExamples.Length;
            urlPlaceholder.Text = urlExamples[urlExampleIndex];
            urlPlaceholderShift.X = 14;
            urlPlaceholder.Opacity = 0;
            for (int i = 0; i <= steps; i++)
            {
                if (!urlPlaceholder.IsVisible || !string.IsNullOrWhiteSpace(urlBox.Text)) return;
                double t = i / (double)steps;
                urlPlaceholder.Opacity = t;
                urlPlaceholderShift.X = 14 * (1 - t);
                await Task.Delay(16);
            }
            if (showSpotifyExample && !IsResourceSaverActive) _spotifyPlaceholderTimer.Start();
        };
        _spotifyPlaceholderSync = SyncSpotifyPlaceholder;
        var progBar = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0, Height = 8, IsVisible = false };
        var statusText = new TextBlock { FontSize = 13, Foreground = ResBrush("TextSecondaryBrush") };

        var btnRow = new WrapPanel();
        var openBtn = new Button { Classes = { "accentBtn" }, Content = "Open Spotify Link", Margin = new Thickness(0, 0, 10, 8) };
        var csvBtn = new Button { Classes = { "pill" }, Content = "Import .CSV / .M3U File (1,000+ Songs)", Margin = new Thickness(0, 0, 10, 8) };
        var profilePanel = new StackPanel { Spacing = 12, IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };

        openBtn.Click += async (_, _) =>
        {
            string spotifyUrl = urlBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(spotifyUrl)) return;
            openBtn.IsEnabled = false;
            progBar.IsVisible = true;
            progBar.Value = 0;
            profilePanel.Children.Clear();
            profilePanel.IsVisible = false;
            bool isProfile = IsSpotifyProfileUrl(spotifyUrl);
            if (!isProfile)
            {
                try
                {
                    statusText.Text = "Reading Spotify link…";
                    var (name, owner, tracks) = await _extras.ImportSpotifyUrlAsync(spotifyUrl, (msg, p) =>
                    {
                        Dispatcher.UIThread.Post(() => { statusText.Text = msg; progBar.Value = p; });
                    });
                    if (tracks.Count == 0) throw new InvalidOperationException("No playable tracks were matched from that public link.");
                    var pl = _store.CreatePlaylist(name, "", tracks);
                    pl.OwnerName = owner;
                    pl.Source = "Spotify";
                    pl.SpotifySourceId = ExtractSpotifyCollectionId(spotifyUrl);
                    _store.SaveDebounced();
                    ShowToast($"Imported \"{pl.Name}\" ({tracks.Count} tracks)!");
                    await NavigateToAsync("pl:" + pl.Id, () => { RenderLocalPlaylistDetail(pl); return Task.CompletedTask; });
                }
                catch (Exception ex) { statusText.Text = $"Spotify link error: {ex.Message}"; }
                finally { openBtn.IsEnabled = true; progBar.IsVisible = false; progBar.IsIndeterminate = false; }
                return;
            }
            try
            {
            statusText.Text = "Loading public Spotify profile…";
            progBar.IsIndeterminate = true;
            var profile = await _extras.GetPublicSpotifyProfileAsync(spotifyUrl, (msg, p) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    statusText.Text = msg;
                    if (p < 1) { progBar.IsVisible = true; progBar.IsIndeterminate = true; }
                });
            });
            progBar.IsIndeterminate = false;
            progBar.Value = 1;
            statusText.Text = $"Loaded {profile.Playlists.Count} public playlist(s). Playing does not import; use Add as playlist to save locally.";
                var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("88,*"), Margin = new Thickness(0, 4, 0, 4) };
                var avatar = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(38), ClipToBounds = true, Background = ResBrush("BgSurfaceBrush") };
                if (!string.IsNullOrWhiteSpace(profile.ImageUrl))
                {
                    var avatarImg = new Image { Width = 76, Height = 76, Stretch = Stretch.UniformToFill };
                    LoadImageInto(avatarImg, profile.ImageUrl);
                    avatar.Child = avatarImg;
                }
                header.Children.Add(avatar);
                var profileMeta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
                profileMeta.Children.Add(new TextBlock { Text = profile.Name, FontSize = 22, FontWeight = FontWeight.ExtraBold, TextTrimming = TextTrimming.CharacterEllipsis });
                profileMeta.Children.Add(new TextBlock { Text = "PUBLIC SPOTIFY PROFILE", FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush"), LetterSpacing = 0.8 });
                Grid.SetColumn(profileMeta, 1);
                header.Children.Add(profileMeta);
                profilePanel.Children.Add(header);
                var profileActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var importAll = new Button { Classes = { "accentBtn" }, Content = "Import all playlists" };
                importAll.Click += async (_, _) =>
                {
                    importAll.IsEnabled = false;
                    openBtn.IsEnabled = false;
                    profilePanel.IsEnabled = false;
                    int imported = 0, failed = 0;
                    progBar.IsVisible = true;
                    progBar.IsIndeterminate = false;
                    try
                    {
                        for (int i = 0; i < profile.Playlists.Count; i++)
                        {
                            var item = profile.Playlists[i];
                            statusText.Text = $"Importing playlist {i + 1}/{profile.Playlists.Count}: {item.Name}…";
                            progBar.Value = (double)i / Math.Max(1, profile.Playlists.Count);
                            if (_store.Playlists.Any(p => p.SpotifySourceId == item.Id)) { failed++; continue; }
                            try
                            {
                                var (_, owner, tracks) = await _extras.ImportSpotifyUrlAsync($"https://open.spotify.com/playlist/{item.Id}", (msg, p) =>
                                {
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        statusText.Text = $"Playlist {i + 1}/{profile.Playlists.Count} · {msg}";
                                        progBar.Value = ((double)i + Math.Clamp(p, 0, 1)) / Math.Max(1, profile.Playlists.Count);
                                    });
                                });
                                if (tracks.Count == 0) { failed++; continue; }
                                var pl = _store.CreatePlaylist(item.Name, "", tracks);
                                pl.OwnerName = string.IsNullOrWhiteSpace(owner) ? profile.Name : owner;
                                pl.Source = "Spotify";
                                pl.SpotifySourceId = item.Id;
                                imported++;
                            }
                            catch { failed++; }
                        }
                        _store.SaveNow();
                        statusText.Text = $"Imported {imported} of {profile.Playlists.Count} public playlists" + (failed > 0 ? $" ({failed} skipped or unavailable)." : ".");
                        ShowToast(imported > 0 ? $"Imported {imported} Spotify playlist(s)" : "No playlists were added - none had matched tracks");
                    }
                    finally { importAll.IsEnabled = true; openBtn.IsEnabled = true; profilePanel.IsEnabled = true; progBar.IsVisible = false; progBar.IsIndeterminate = false; }
                };
                profileActions.Children.Add(importAll);
                profilePanel.Children.Add(profileActions);
                var playlistList = new StackPanel { Spacing = 8 };
                foreach (var source in profile.Playlists)
                {
                    var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("56,*,Auto,Auto"), Margin = new Thickness(0, 1) };
                    var cover = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = ResBrush("BgSurfaceBrush") };
                    if (!string.IsNullOrWhiteSpace(source.ImageUrl))
                    {
                        var coverImg = new Image { Width = 52, Height = 52, Stretch = Stretch.UniformToFill };
                        LoadImageInto(coverImg, source.ImageUrl);
                        cover.Child = coverImg;
                    }
                    row.Children.Add(cover);
                    var name = new TextBlock { Text = source.Name, FontSize = 13.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 8, 0) };
                    Grid.SetColumn(name, 1); row.Children.Add(name);
                    var play = new Button { Classes = { "pill" }, Content = "Play", Padding = new Thickness(12, 6), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2) };
                    play.Click += async (_, _) =>
                    {
                        play.IsEnabled = false; progBar.IsVisible = true; progBar.Value = 0; statusText.Text = $"Loading playlist: {source.Name}…";
                        try
                        {
                            var (_, _, tracks) = await _extras.ImportSpotifyUrlAsync($"https://open.spotify.com/playlist/{source.Id}");
                            if (tracks.Count == 0) throw new InvalidOperationException("No playable tracks were matched.");
                            await _audio.PlayQueueAsync(tracks, 0);
                            statusText.Text = $"Playing {source.Name} ({tracks.Count} matched tracks). Not added to your library.";
                        }
                        catch (Exception ex) { statusText.Text = $"Could not play playlist: {ex.Message}"; }
                        finally { play.IsEnabled = true; progBar.IsVisible = false; }
                    };
                    Grid.SetColumn(play, 2); row.Children.Add(play);
                    LocalPlaylist? addedPlaylist = null;
                    var add = new Button { Classes = { "ghost" }, Content = "Add as playlist", Padding = new Thickness(10, 6), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2) };
                    ToolTip.SetTip(add, "Import and save this playlist locally; after import, click again to open it.");
                    add.Click += async (_, _) =>
                    {
                        if (addedPlaylist is not null)
                        {
                            await NavigateToAsync("pl:" + addedPlaylist.Id, () => { RenderLocalPlaylistDetail(addedPlaylist); return Task.CompletedTask; });
                            return;
                        }
                        var existingPlaylist = _store.Playlists.FirstOrDefault(p => p.SpotifySourceId == source.Id);
                        if (existingPlaylist is not null)
                        {
                            addedPlaylist = existingPlaylist;
                            add.Content = "Added";
                            statusText.Text = $"“{existingPlaylist.Name}” is already in Your Library. Click Added to open it.";
                            return;
                        }
                        add.IsEnabled = false;
                        progBar.IsVisible = true;
                        progBar.IsIndeterminate = false;
                        progBar.Value = 0;
                        statusText.Text = $"Adding playlist: {source.Name}…";
                        try
                        {
                            var (_, owner, tracks) = await _extras.ImportSpotifyUrlAsync($"https://open.spotify.com/playlist/{source.Id}", (msg, p) =>
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    statusText.Text = $"{source.Name} · {msg}";
                                    progBar.Value = Math.Clamp(p, 0, 1);
                                });
                            });
                            if (tracks.Count == 0) throw new InvalidOperationException("No playable tracks were matched; nothing was added.");
                            addedPlaylist = _store.CreatePlaylist(source.Name, "", tracks);
                            addedPlaylist.OwnerName = string.IsNullOrWhiteSpace(owner) ? profile.Name : owner;
                            addedPlaylist.Source = "Spotify";
                            addedPlaylist.SpotifySourceId = source.Id;
                            _store.SaveNow();
                            add.Content = "Added";
                            statusText.Text = $"Added “{addedPlaylist.Name}” with {tracks.Count} matched tracks. It is now in Your Library / Playlists. Click Added to open it.";
                            ShowToast($"Added {addedPlaylist.Name} ({tracks.Count} tracks) to Your Library");
                        }
                        catch (Exception ex) { statusText.Text = $"Could not add “{source.Name}”: {ex.Message}"; }
                        finally { add.IsEnabled = true; progBar.IsVisible = false; progBar.IsIndeterminate = false; }
                    };
                    Grid.SetColumn(add, 3); row.Children.Add(add);
                    playlistList.Children.Add(new Border { Background = ResBrush("BgCardBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Child = row });
                }
                if (profile.Playlists.Count == 0) playlistList.Children.Add(EmptyHint("No public playlists were found on this profile."));
                profilePanel.Children.Add(new ScrollViewer { MaxHeight = 430, Content = playlistList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                profilePanel.IsVisible = true;
            }
            catch (Exception ex) { statusText.Text = ex is TaskCanceledException ? "Spotify profile request timed out. Check the connection and try again." : $"Profile error: {ex.Message}"; }
            finally { openBtn.IsEnabled = true; progBar.IsVisible = false; progBar.IsIndeterminate = false; }
        };

        csvBtn.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Exportify .CSV or .M3U / .M3U8 Playlist",
                AllowMultiple = false,
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            csvBtn.IsEnabled = false;
            progBar.IsVisible = true;
            try
            {
                var (name, tracks) = await _extras.ImportPlaylistFileAsync(path, (msg, p) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        statusText.Text = msg;
                        progBar.Value = p;
                    });
                });
                var pl = _store.CreatePlaylist(name, $"Imported from file ({tracks.Count} tracks)", tracks);
                ShowToast($"Imported \"{pl.Name}\" ({tracks.Count} tracks)!");
                await NavigateToAsync("pl:" + pl.Id, () => { RenderLocalPlaylistDetail(pl); return Task.CompletedTask; });
            }
            catch (Exception ex)
            {
                statusText.Text = $"File import error: {ex.Message}";
            }
            finally
            {
                csvBtn.IsEnabled = true;
            }
        };

        btnRow.Children.Add(openBtn);
        btnRow.Children.Add(csvBtn);

        root.Children.Add(urlInputGrid);
        root.Children.Add(btnRow);
        root.Children.Add(progBar);
        root.Children.Add(statusText);
        root.Children.Add(profilePanel);
        SetCenterContent(root);
        _spotifyPlaceholderSync?.Invoke();
    }

    // ========================================================= 6. ECHO WRAPPED & LISTENING STATS

    internal void RenderStatsWrappedView(int? daysWindow)
    {
        var stats = _store.ComputeWrapped(daysWindow);
        var root = new StackPanel { Spacing = 22 };

        var hdr = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        hdr.Children.Add(new TextBlock { Text = "Echo Wrapped & Analytics", FontSize = 26, FontWeight = FontWeight.ExtraBold });

        var winSwitch = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (d, lbl) in new (int?, string)[] { (7, "Last 7 Days"), (30, "Last 30 Days"), (null, "All Time") })
        {
            var cd = d;
            var b = new Button { Classes = { daysWindow == cd ? "accentBtn" : "pill" }, Content = lbl, Padding = new Thickness(14, 6) };
            b.Click += (_, _) => RenderStatsWrappedView(cd);
            winSwitch.Children.Add(b);
        }
        Grid.SetColumn(winSwitch, 1);
        hdr.Children.Add(winSwitch);
        root.Children.Add(hdr);

        // Listener Personality Hero Card
        var hero = new Border
        {
            Background = ResBrush("AccentSoftBrush"),
            BorderBrush = ResBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(24, 20),
        };
        var hst = new StackPanel { Spacing = 4 };
        hst.Children.Add(new TextBlock { Text = "YOUR LISTENING ARCHETYPE", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });
        hst.Children.Add(new TextBlock { Text = stats.PersonalityArchetype, FontSize = 24, FontWeight = FontWeight.ExtraBold });
        hero.Child = hst;
        root.Children.Add(hero);

        // 4 KPI Metric Cards
        var kpis = new UniformGrid { Columns = 4 };
        foreach (var (val, label) in new[]
                 {
                     (stats.TotalMinutes.ToString("0.0"), "Minutes Listened"),
                     (stats.TotalPlays.ToString(), "Total Streams"),
                     (stats.UniqueSongs.ToString(), "Unique Tracks"),
                     (stats.UniqueArtists.ToString(), "Unique Artists"),
                 })
        {
            kpis.Children.Add(new Border
            {
                Background = ResBrush("BgCardBrush"),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(18, 16),
                Margin = new Thickness(0, 0, 12, 0),
                BorderBrush = ResBrush("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = val, FontSize = 26, FontWeight = FontWeight.ExtraBold, Foreground = ResBrush("AccentBrush") },
                        new TextBlock { Text = label, FontSize = 12, Foreground = ResBrush("TextSecondaryBrush") },
                    }
                }
            });
        }
        root.Children.Add(kpis);

        if (stats.TopTracks.Count > 0)
        {
            root.Children.Add(SectionHeading("Most Played Songs", () => _ = _audio.PlayQueueAsync(stats.TopTracks.Select(x => x.Track), 0, autoRadioIfSingle: false)));
            root.Children.Add(BuildTrackListTable(stats.TopTracks.Select(x => x.Track).ToList(), showIndex: true));
        }

        if (stats.TopArtists.Count > 0)
        {
            root.Children.Add(SectionHeading("Top Artists"));
            var wrap = new WrapPanel();
            foreach (var (artist, _, plays, mins) in stats.TopArtists)
            {
                wrap.Children.Add(new Border
                {
                    Background = ResBrush("BgCardBrush"),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(16, 12),
                    Margin = new Thickness(0, 0, 10, 10),
                    Child = new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = artist, FontSize = 14, FontWeight = FontWeight.Bold },
                            new TextBlock { Text = $"{plays} plays • {mins} min", FontSize = 11.5, Foreground = ResBrush("TextSecondaryBrush") }
                        }
                    }
                });
            }
            root.Children.Add(wrap);
        }

        SetCenterContent(root);
    }

    // ========================================================= 7. 10-BAND EQUALIZER & AMBIENT DSP STUDIO

    internal void RenderEqualizerView()
    {
        var s = _store.Settings;
        var root = new StackPanel { Spacing = 22 };
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new PathIcon { Data = Icons.Equalizer, Width = 25, Height = 25, Foreground = ResBrush("AccentBrush"), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "10-Band Studio Equalizer & Ambient DSP", FontSize = 26, FontWeight = FontWeight.ExtraBold, VerticalAlignment = VerticalAlignment.Center },
            }
        });

        // Preset pills
        var presetWrap = new WrapPanel();
        var bandSliders = new Slider[10];
        var bandLabels = new TextBlock[10];
        bool updatingSliders = false;

        foreach (var kv in EqPresets.Map)
        {
            var pName = kv.Key;
            var pVals = kv.Value;
            var b = new Button
            {
                Classes = { string.Equals(s.EqPreset, pName, StringComparison.OrdinalIgnoreCase) ? "accentBtn" : "pill" },
                Content = pName,
                Padding = new Thickness(14, 6),
                Margin = new Thickness(0, 0, 8, 8),
            };
            b.Click += (_, _) =>
            {
                s.EqPreset = pName;
                Array.Copy(pVals, s.EqBands, 10);
                _audio.ApplySettingsFromStore();
                _store.SaveDebounced();
                RenderEqualizerView();
            };
            presetWrap.Children.Add(b);
        }
        root.Children.Add(presetWrap);

        // 10 Vertical Biquad Sliders Card
        var eqCard = new Border
        {
            Background = ResBrush("BgCardBrush"),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(22, 20),
            BorderBrush = ResBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
        };
        var cols = new UniformGrid { Columns = 10, Height = 230 };
        string[] freqNames = { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };
        for (int i = 0; i < 10; i++)
        {
            int idx = i;
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8 };
            var valText = new TextBlock { Text = $"{s.EqBands[idx]:+0.0;-0.0;0} dB", FontSize = 11, Foreground = ResBrush("TextSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Center };
            var sl = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -12,
                Maximum = 12,
                Value = s.EqBands[idx],
                Height = 155,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            sl.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty && !updatingSliders)
                {
                    s.EqBands[idx] = Math.Round(sl.Value * 2) / 2.0;
                    s.EqPreset = "Custom";
                    valText.Text = $"{s.EqBands[idx]:+0.0;-0.0;0} dB";
                    _audio.ApplySettingsFromStore();
                    _store.SaveDebounced();
                }
            };
            bandSliders[idx] = sl;
            bandLabels[idx] = valText;
            col.Children.Add(valText);
            col.Children.Add(sl);
            col.Children.Add(new TextBlock { Text = freqNames[idx], FontSize = 11.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            cols.Children.Add(col);
        }
        eqCard.Child = cols;
        root.Children.Add(eqCard);

        // Ambient Mode & Playback Engine Card (Slowed + Reverb / Nightcore / Bass Shelf / Crossfade)
        var dspCard = new Border
        {
            Background = ResBrush("BgCardBrush"),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(24, 20),
            BorderBrush = ResBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
        };
        var dspStack = new StackPanel { Spacing = 16 };
        dspStack.Children.Add(new TextBlock { Text = "AMBIENT STUDIO & PLAYBACK DSP", FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });

        var quickDspRow = new WrapPanel();
        var normalModeBtn = new Button
        {
            Classes = { "pill" }, Margin = new Thickness(0, 0, 10, 8),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new PathIcon { Data = Icons.Equalizer, Width = 15, Height = 15, Foreground = ResBrush("TextSecondaryBrush") }, new TextBlock { Text = "Studio Normal (1.0x Dry)" } } },
        };
        normalModeBtn.Click += (_, _) =>
        {
            s.PlaybackSpeed = 1.0; s.PitchLockSpeed = false; s.AmbientReverb = false;
            _audio.ApplySettingsFromStore(); _store.SaveDebounced(); RenderEqualizerView();
            ShowToast("Switched to Studio Normal 1.0x");
        };
        var slowedBtn = new Button
        {
            Classes = { s.AmbientReverb && s.PlaybackSpeed < 0.95 ? "accentBtn" : "pill" },
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new PathIcon { Data = Icons.MoonSoft, Width = 15, Height = 15, Foreground = ResBrush("TextSecondaryBrush") }, new TextBlock { Text = "Slowed + Lush Reverb (0.85x)" } } },
            Margin = new Thickness(0, 0, 10, 8),
        };
        slowedBtn.Click += (_, _) =>
        {
            s.PlaybackSpeed = 0.85; s.PitchLockSpeed = false; s.AmbientReverb = true; s.ReverbMix = 0.28;
            _audio.ApplySettingsFromStore(); _store.SaveDebounced(); RenderEqualizerView();
            ShowToast("Activated Ambient Slowed + Reverb DSP!");
        };
        var nightcoreBtn = new Button
        {
            Classes = { !s.PitchLockSpeed && s.PlaybackSpeed > 1.12 ? "accentBtn" : "pill" },
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new PathIcon { Data = Icons.FastForwardSoft, Width = 15, Height = 15, Foreground = ResBrush("TextSecondaryBrush") }, new TextBlock { Text = "Nightcore Upbeat (1.20x)" } } },
            Margin = new Thickness(0, 0, 10, 8),
        };
        nightcoreBtn.Click += (_, _) =>
        {
            s.PlaybackSpeed = 1.20; s.PitchLockSpeed = false; s.AmbientReverb = false;
            _audio.ApplySettingsFromStore(); _store.SaveDebounced(); RenderEqualizerView();
            ShowToast("Activated Nightcore 1.20x!");
        };
        var fastPitchLockBtn = new Button
        {
            Classes = { s.PitchLockSpeed && s.PlaybackSpeed > 1.05 ? "accentBtn" : "pill" },
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new PathIcon { Data = Icons.Waveform, Width = 15, Height = 15, Foreground = ResBrush("TextSecondaryBrush") }, new TextBlock { Text = "1.25x Constant Pitch (WSOLA)" } } },
            Margin = new Thickness(0, 0, 10, 8),
        };
        fastPitchLockBtn.Click += (_, _) =>
        {
            s.PlaybackSpeed = 1.25; s.PitchLockSpeed = true; s.AmbientReverb = false;
            _audio.ApplySettingsFromStore(); _store.SaveDebounced(); RenderEqualizerView();
            ShowToast("Activated 1.25x Constant-Pitch WSOLA Time-Stretch!");
        };
        quickDspRow.Children.Add(normalModeBtn);
        quickDspRow.Children.Add(slowedBtn);
        quickDspRow.Children.Add(nightcoreBtn);
        quickDspRow.Children.Add(fastPitchLockBtn);
        dspStack.Children.Add(quickDspRow);

        dspStack.Children.Add(BuildToggleRow("Lock Vocal Pitch When Changing Speed (WSOLA Time-Stretcher)", "Keep original vocal pitch at 0.75x–1.60x instead of vinyl pitch-shifting", s.PitchLockSpeed, v =>
        {
            s.PitchLockSpeed = v;
            _audio.ApplySettingsFromStore();
            _store.SaveDebounced();
        }));

        dspStack.Children.Add(BuildLabeledSlider("Sub-Bass Low-Shelf Boost", $"{s.BassBoostDb:0.0} dB", 0, 12, s.BassBoostDb, v =>
        {
            s.BassBoostDb = Math.Round(v * 2) / 2.0;
            _audio.ApplySettingsFromStore();
            _store.SaveDebounced();
            return $"{s.BassBoostDb:0.0} dB";
        }));

        dspStack.Children.Add(BuildLabeledSlider("Playback Speed / Pitch", $"{s.PlaybackSpeed:0.00}x", 0.55, 1.60, s.PlaybackSpeed, v =>
        {
            s.PlaybackSpeed = Math.Round(v, 2);
            _audio.ApplySettingsFromStore();
            _store.SaveDebounced();
            return $"{s.PlaybackSpeed:0.00}x";
        }));

        dspStack.Children.Add(BuildLabeledSlider("Hall Reverb Wet Mix", s.AmbientReverb ? $"{(int)(s.ReverbMix * 100)}%" : "Off", 0, 0.55, s.AmbientReverb ? s.ReverbMix : 0, v =>
        {
            s.AmbientReverb = v > 0.02;
            s.ReverbMix = Math.Round(v, 2);
            _audio.ApplySettingsFromStore();
            _store.SaveDebounced();
            return s.AmbientReverb ? $"{(int)(s.ReverbMix * 100)}%" : "Off";
        }));

        dspStack.Children.Add(BuildLabeledSlider("Equal-Power Crossfade", s.CrossfadeSeconds == 0 ? "Off (Gapless)" : $"{s.CrossfadeSeconds} sec", 0, 12, s.CrossfadeSeconds, v =>
        {
            s.CrossfadeSeconds = (int)Math.Round(v);
            _store.SaveDebounced();
            return s.CrossfadeSeconds == 0 ? "Off (Gapless)" : $"{s.CrossfadeSeconds} sec";
        }));

        dspCard.Child = dspStack;
        root.Children.Add(dspCard);

        SetCenterContent(root);
    }

    private Control BuildLabeledSlider(string title, string initialVal, double min, double max, double cur, Func<double, string> onChange)
    {
        var g = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("210,*,80") };
        g.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center });
        var lbl = new TextBlock { Text = initialVal, FontSize = 12.5, Foreground = ResBrush("AccentBrush"), FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var sl = new Slider { Minimum = min, Maximum = max, Value = cur, Margin = new Thickness(12, 0) };
        bool init = true;
        sl.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !init)
                lbl.Text = onChange(sl.Value);
        };
        init = false;
        Grid.SetColumn(sl, 1);
        Grid.SetColumn(lbl, 2);
        g.Children.Add(sl);
        g.Children.Add(lbl);
        return g;
    }

    // ========================================================= ABOUT

    internal void RenderAboutView()
    {
        var root = new StackPanel { Spacing = 18, MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock { Text = "About Echo Music", FontSize = 28, FontWeight = FontWeight.ExtraBold });

        var hero = new Border
        {
            Background = ResBrush("BgCardBrush"),
            BorderBrush = ResBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(24, 20),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Echo Music · Native PC Edition", FontSize = 21, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = "Native Windows app built with C# and Avalonia.", FontSize = 13, Foreground = ResBrush("TextSecondaryBrush") },
                    new TextBlock { Text = $"Version {DynamicConfigService.CurrentAppVersion} · GPL-3.0", FontSize = 12, Foreground = ResBrush("TextMutedBrush") },
                }
            }
        };
        root.Children.Add(hero);

        var credits = new StackPanel { Spacing = 9 };
        credits.Children.Add(new TextBlock
        {
            Text = "PC edition by V!SHNU",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
        });
        var lineageContent = new StackPanel { Spacing = 8 };
        lineageContent.Children.Add(new TextBlock
        {
            Text = "Original Echo Music project: EchoMusicApp and its contributors. The project and this PC edition are distributed under GPL-3.0; full notices are in LICENSE.txt.",
            FontSize = 11.5,
            Foreground = ResBrush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        var sourceBtn = new Button { Classes = { "pill" }, Content = "Original project source & credits", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(11, 5), FontSize = 11.5 };
        sourceBtn.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/EchoMusicApp/Echo-Music") { UseShellExecute = true }); } catch { }
        };
        lineageContent.Children.Add(sourceBtn);
        credits.Children.Add(new Expander
        {
            Header = "Project lineage & license notices",
            Content = lineageContent,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        root.Children.Add(WrapSettingsCard(credits));

        root.Children.Add(new TextBlock
        {
            Text = "Guest Mode works without signing in. Spotify playlist import accepts a public Spotify URL or a local CSV/M3U playlist file; this desktop edition does not ask for your Spotify password or use Spotify OAuth.",
            FontSize = 12.5,
            Foreground = ResBrush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        SetCenterContent(root);
    }

    // ========================================================= 8. SETTINGS VIEW

    internal void RenderSettingsView()
    {
        var s = _store.Settings;
        var root = new StackPanel { Spacing = 22, MaxWidth = _leftSidebarMode == "Expanded" ? 840 : double.PositiveInfinity, HorizontalAlignment = _leftSidebarMode == "Expanded" ? HorizontalAlignment.Left : HorizontalAlignment.Stretch };
        root.Children.Add(new TextBlock { Text = "Settings", FontSize = 26, FontWeight = FontWeight.ExtraBold });
        var settingsSearch = new TextBox { Text = _settingsSearchQuery, Watermark = "Search settings…", Padding = new Thickness(13, 9), MinHeight = 38 };
        var noSettingsMatch = new TextBlock { Text = "No settings sections match that search.", FontSize = 13, Foreground = ResBrush("TextSecondaryBrush"), IsVisible = false, Margin = new Thickness(4, 0) };
        void ApplySettingsSearch()
        {
            string query = _settingsSearchQuery.Trim();
            int visibleSections = 0;
            foreach (var section in root.Children.OfType<Border>())
            {
                bool show = query.Length == 0 || ((section.Tag as string) ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
                section.IsVisible = show;
                if (show) visibleSections++;
            }
            noSettingsMatch.IsVisible = query.Length > 0 && visibleSections == 0;
        }
        settingsSearch.TextChanged += (_, _) => { _settingsSearchQuery = settingsSearch.Text ?? ""; ApplySettingsSearch(); };
        root.Children.Add(settingsSearch);

        if (OperatingSystem.IsWindows())
        {
            var startupCard = new StackPanel { Spacing = 12 };
            startupCard.Children.Add(new TextBlock { Text = "WINDOWS STARTUP", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });
            var startupWarningGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"), HorizontalAlignment = HorizontalAlignment.Stretch };
            var warningIcon = new PathIcon { Data = Icons.ShieldCheck, Width = 19, Height = 19, Margin = new Thickness(0, 1, 10, 0), Foreground = new SolidColorBrush(Color.Parse("#F3C64E")), VerticalAlignment = VerticalAlignment.Top };
            startupWarningGrid.Children.Add(warningIcon);
            var startupWarningText = new TextBlock { Text = "This Echo Music app is distributed unsigned, so SmartScreen or antivirus may warn or quarantine the setup file. Verify the source and SHA-256 first. If quarantined, review the vendor detection; restore it or add an exclusion only if you have verified and trust this exact file. Echo Music never disables antivirus.", FontSize = 12.5, MaxWidth = 710, HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = ResBrush("TextPrimaryBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(startupWarningText, 1);
            startupWarningGrid.Children.Add(startupWarningText);
            startupCard.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Color.FromArgb(28, 243, 198, 78)), BorderBrush = new SolidColorBrush(Color.FromArgb(112, 243, 198, 78)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Child = startupWarningGrid });
            startupCard.Children.Add(BuildToggleRow("Start Echo Music at Windows sign-in", "Creates or removes the standard per-user Startup folder shortcut. No background service or hidden scheduled task is installed.", s.RunOnStartup, enabled =>
            {
                if (!TrySetRunOnStartup(enabled, out var error))
                {
                    ShowToast(error);
                    RenderSettingsView(); // Keep the toggle in sync if Windows refused the shortcut change.
                    return;
                }
                s.RunOnStartup = enabled;
                _store.SaveNow();
                ShowToast(enabled ? "Echo Music will run when you sign in to Windows" : "Echo Music removed from Windows startup");
                RenderSettingsView();
            }));
            root.Children.Add(WrapSettingsCard(startupCard, "windows startup boot sign in launch"));
        }

        // 0. Windows Audio Output Hardware & Instant Speaker Test
        var hwCard = new StackPanel { Spacing = 14 };
        hwCard.Children.Add(new TextBlock { Text = "AUDIO OUTPUT HARDWARE & SPEAKER DIAGNOSTICS", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });
        var hwTopRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        hwTopRow.Children.Add(new TextBlock
        {
            Text = $"Active Driver: {_audio.ActiveAudioBackendLabel}  •  Master Volume: {(int)(s.Volume * 100)}%",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var testChimeBtn = new Button
        {
            Classes = { "accentBtn" },
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new PathIcon { Data = Icons.VolumeHigh, Width = 14, Height = 14, Foreground = ResBrush("OnAccentBrush") },
                    new TextBlock { Text = "Play Test Speaker Chime", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
                }
            }
        };
        testChimeBtn.Click += (_, _) =>
        {
            if (_audio.IsMuted) _audio.ToggleMute();
            if (s.Volume < 0.35) _audio.SetVolume(0.85);
            _audio.PlayTestChime();
            ShowToast($"Playing stereo test chord via {_audio.ActiveAudioBackendLabel}!");
        };
        Grid.SetColumn(testChimeBtn, 1);
        hwTopRow.Children.Add(testChimeBtn);
        hwCard.Children.Add(hwTopRow);

        if (OperatingSystem.IsWindows())
        {
            hwCard.Children.Add(BuildChoiceRow("Windows Audio Driver", new[]
            {
                ("Win32WaveOut", "Win32 WaveOut 16-bit PCM (Recommended)"),
                ("DirectSound", "DirectSound 16-bit PCM"),
                ("NAudioWaveOut", "NAudio WaveOutEvent"),
                ("WasapiPolling", "WASAPI Shared Polling"),
            }, s.AudioBackend, v =>
            {
                s.AudioBackend = v;
                _store.SaveNow();
                _audio.RestartAudioOutputBackend();
                _audio.PlayTestChime();
                RenderSettingsView();
            }));

            var devices = _audio.GetOutputDevices();
            if (devices.Count > 0)
            {
                var devOpts = devices.Select(d => (d.Id.ToString(), d.Name)).ToArray();
                hwCard.Children.Add(BuildChoiceRow("Output Speaker / Headphone Device", devOpts, s.AudioDeviceId.ToString(), v =>
                {
                    if (int.TryParse(v, out int id)) s.AudioDeviceId = id;
                    _store.SaveNow();
                    _audio.RestartAudioOutputBackend();
                    _audio.PlayTestChime();
                    RenderSettingsView();
                }));
            }
        }
        else
        {
            hwCard.Children.Add(new TextBlock
            {
                Text = "Linux audio output uses the active system audio stack (ALSA, PulseAudio, or PipeWire).",
                FontSize = 12,
                Foreground = ResBrush("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        root.Children.Add(WrapSettingsCard(hwCard, "audio output hardware speaker diagnostics driver device test volume"));

        // 1. Appearance, Official App Icons & Themes
        var themeCard = new StackPanel { Spacing = 14 };
        themeCard.Children.Add(new TextBlock { Text = "APPEARANCE, OFFICIAL APP ICONS & THEMES", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });
        var themeWrap = new WrapPanel();
        foreach (var p in ThemeManager.Palettes)
        {
            var cp = p;
            var btn = new Button
            {
                Classes = { s.Theme == cp.Id ? "accentBtn" : "pill" },
                Content = cp.DisplayName,
                Margin = new Thickness(0, 0, 8, 8),
            };
            btn.Click += (_, _) =>
            {
                s.Theme = cp.Id;
                ApplyThemeAndTypography();
                _store.SaveDebounced();
                RenderSettingsView();
            };
            themeWrap.Children.Add(btn);
        }
        themeCard.Children.Add(themeWrap);

        themeCard.Children.Add(BuildChoiceRow("Sidebar close mode", new[]
        {
            ("Partial", "Partial - expanded ↔ icons"),
            ("Stepwise", "Stepwise - expanded → icons → hidden"),
            ("Hide", "Hide - expanded ↔ hidden"),
        }, _store.Settings.SidebarCollapseBehavior, behavior =>
        {
            _store.Settings.SidebarCollapseBehavior = behavior;
            if (behavior == "Partial" && _leftSidebarMode == "Hidden") ApplyLeftSidebarMode("Compact", rerenderSettings: false);
            else if (behavior == "Hide" && _leftSidebarMode == "Compact") ApplyLeftSidebarMode("Expanded", rerenderSettings: false);
            else ApplyLeftSidebarMode(_leftSidebarMode, rerenderSettings: false);
            _store.SaveDebounced();
            RenderSettingsView();
        }));

        themeCard.Children.Add(new TextBlock { Text = "App icons", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush"), Margin = new Thickness(0, 4, 0, 0) });
        var iconRows = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 0) };
        foreach (var choice in AppIconChoices)
        {
            bool selectedIcon = string.Equals(s.AppIconStyle, choice.Style, StringComparison.OrdinalIgnoreCase);
            var option = choice;
            var iconCard = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var iconBorder = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = ResBrush("BgSurfaceBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            try
            {
                using var iconStream = AssetLoader.Open(new Uri(GetAppIconAssetUri(option.Style)));
                iconBorder.Child = new Image { Source = new Bitmap(iconStream), Stretch = Stretch.UniformToFill };
            }
            catch { }
            iconCard.Children.Add(iconBorder);
            iconCard.Children.Add(new TextBlock { Text = option.Name, FontSize = 12, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 34 });
            var optionBorder = new Border
            {
                Width = 196,
                MinHeight = 112,
                Margin = new Thickness(4),
                Padding = new Thickness(8, 7),
                CornerRadius = new CornerRadius(10),
                Background = selectedIcon ? ResBrush("AccentSoftBrush") : ResBrush("BgSurfaceBrush"),
                BorderBrush = selectedIcon ? ResBrush("AccentBrush") : ResBrush("BorderSubtleBrush"),
                BorderThickness = new Thickness(selectedIcon ? 1.5 : 1),
                Child = iconCard,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(optionBorder, option.Description);
            optionBorder.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(optionBorder).Properties.IsLeftButtonPressed) return;
                s.AppIconStyle = option.Style;
                ApplyAppIconVariant();
                _store.SaveDebounced();
                RenderSettingsView();
            };
            iconRows.Children.Add(optionBorder);
        }
        themeCard.Children.Add(iconRows);

        themeCard.Children.Add(BuildToggleRow("10-Second Seek Buttons (-10s / +10s)", "Show quick -10s and +10s skip buttons inside the bottom player bar", s.ShowTenSecSkip, v =>
        {
            s.ShowTenSecSkip = v;
            ApplyThemeAndTypography();
            _store.SaveDebounced();
        }));

        themeCard.Children.Add(BuildChoiceRow("App Typography", new[] { ("SpotifyCircular", "Default"), ("PlusJakarta", "Plus Jakarta Sans"), ("Outfit", "Outfit"), ("System", "System Segoe UI") }, s.FontChoice, v =>
        {
            s.FontChoice = v;
            ApplyThemeAndTypography();
            _store.SaveDebounced();
            RenderSettingsView();
        }));

        root.Children.Add(WrapSettingsCard(themeCard, "appearance theme app icons logo sidebar close mode run on startup startup seek buttons play button typography font"));

        // 2. Audio & Streaming
        var audioCard = new StackPanel { Spacing = 14 };
        audioCard.Children.Add(new TextBlock { Text = "STREAMING QUALITY & PLAYBACK", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });
        audioCard.Children.Add(BuildChoiceRow("Streaming Audio Quality", new[] { ("Best", "Best (160 kbps Opus)"), ("High", "High (128 kbps)"), ("Normal", "Normal"), ("DataSaver", "Data Saver (48 kbps)") }, s.AudioQuality, v =>
        {
            s.AudioQuality = v;
            _store.SaveDebounced();
            RenderSettingsView();
        }));
        audioCard.Children.Add(BuildToggleRow("Loudness Normalization", "Prevent sudden volume jumps between tracks using ReplayGain/YouTube LUFS", s.NormalizeVolume, v =>
        {
            s.NormalizeVolume = v;
            _audio.ApplySettingsFromStore();
            _store.SaveDebounced();
        }));
        audioCard.Children.Add(BuildToggleRow("Full Gapless Next-Track Preload", "On: decode the next track up to 15 seconds early for seamless transitions. Off: still warm its stream near the end to reduce next-track loading, without holding a second decoded deck.", s.GaplessPreload, v =>
        {
            s.GaplessPreload = v;
            _store.SaveDebounced();
        }));
        audioCard.Children.Add(BuildToggleRow("Echo Brain Autoplay", "Automatically continue playing smart recommendations when queue ends", s.AutoplayEchoBrain, v =>
        {
            s.AutoplayEchoBrain = v;
            _store.SaveDebounced();
        }));
        root.Children.Add(WrapSettingsCard(audioCard, "audio streaming quality loudness normalization gapless preload autoplay echo brain"));

        // Discovery exclusions: reversible per-song, album, and artist blocks follow the mobile app's content controls.
        var contentCard = new StackPanel { Spacing = 14 };
        contentCard.Children.Add(new TextBlock { Text = "CONTENT FILTERS & EXCLUSIONS", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });
        contentCard.Children.Add(BuildToggleRow("Hide Video Songs in Search & Radio", "Exclude video-type tracks from Search, Top Result, Home recommendations and radio. This does not add video playback.", s.HideVideoSongs, v =>
        {
            s.HideVideoSongs = v;
            _store.SaveDebounced();
            _audio.RemoveDiscoveryExcludedQueuedTracks();
            if (_activeNavKey == "search" && !string.IsNullOrWhiteSpace(_activeSearchQuery)) _ = ExecuteSearchViewAsync(_activeSearchQuery, _activeSearchFilter);
            else if (_activeNavKey == "home") _ = LoadHomeViewAsync(_activeMoodParams, _activeMoodBrowseId);
        }));
        var exclusionsRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        var exclusionCopy = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        exclusionCopy.Children.Add(new TextBlock { Text = "Manage excluded content", FontSize = 14, FontWeight = FontWeight.SemiBold });
        exclusionCopy.Children.Add(new TextBlock { Text = $"{_store.ContentExclusions.Count} exclusions · songs, albums, and artists can be blocked from discovery", FontSize = 12, Foreground = ResBrush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        exclusionsRow.Children.Add(exclusionCopy);
        var manageExclusions = new Button { Classes = { "pill" }, Content = "Manage exclusions", VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(13, 7) };
        manageExclusions.Click += (_, _) => OpenContentExclusionsModal();
        Grid.SetColumn(manageExclusions, 1);
        exclusionsRow.Children.Add(manageExclusions);
        contentCard.Children.Add(exclusionsRow);
        root.Children.Add(WrapSettingsCard(contentCard, "content exclusions blocked artists blocked albums blocked songs hide video songs video-only discovery search radio"));

        // 3. Lyrics Translation & AI Route + Region
        var lyrCard = new StackPanel { Spacing = 14 };
        lyrCard.Children.Add(new TextBlock { Text = "LYRICS TRANSLATION & CATALOG REGION", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });
        lyrCard.Children.Add(BuildChoiceRow("Auto-Translate Lyrics To", new[] { ("", "Off"), ("en", "English"), ("hi", "Hindi"), ("es", "Spanish"), ("ja", "Japanese"), ("ko", "Korean"), ("fr", "French") }, s.TranslateLyricsTo, v =>
        {
            s.TranslateLyricsTo = v;
            _store.SaveDebounced();
            RenderSettingsView();
        }));
        lyrCard.Children.Add(BuildChoiceRow("Catalog Country (Charts & Home)", new[] { ("IN", "India"), ("US", "United States"), ("GB", "United Kingdom"), ("JP", "Japan"), ("KR", "Korea"), ("DE", "Germany"), ("BR", "Brazil") }, s.RegionGl, v =>
        {
            s.RegionGl = v;
            _yt.RegionGl = v;
            _store.SaveDebounced();
            RenderSettingsView();
        }));
        root.Children.Add(WrapSettingsCard(lyrCard, "lyrics translation catalog region country language"));

        // 4. Integrations: SponsorBlock and Discord Rich Presence
        var intCard = new StackPanel { Spacing = 14 };
        intCard.Children.Add(new TextBlock { Text = "INTEGRATIONS: SPONSORBLOCK & DISCORD RICH PRESENCE", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });

        string sponsorMode = !s.SponsorBlockEnabled ? "Off" : s.SponsorBlockAllSegments ? "All" : "IntroOutro";
        intCard.Children.Add(BuildChoiceRow("SponsorBlock Auto-Skip", new[]
        {
            ("Off", "Off"),
            ("IntroOutro", "Intro & outro only"),
            ("All", "All submitted segment types"),
        }, sponsorMode, v =>
        {
            s.SponsorBlockEnabled = v != "Off";
            s.SponsorBlockAllSegments = v == "All";
            _audio.RefreshSponsorBlockForCurrentTrack();
            _store.SaveDebounced();
            ShowToast(v == "Off" ? "SponsorBlock auto-skip disabled" : "SponsorBlock preferences updated");
        }));

        var discordPreview = BuildDiscordActivityPreview();
        discordPreview.IsVisible = s.DiscordRpcEnabled;
        intCard.Children.Add(BuildToggleRow("Discord Rich Presence", "Share your current track and playback progress with Discord", s.DiscordRpcEnabled, v =>
        {
            s.DiscordRpcEnabled = v;
            _audio.ApplySettingsFromStore();
            _store.SaveDebounced();
            discordPreview.IsVisible = v;
        }));
        intCard.Children.Add(discordPreview);
        root.Children.Add(WrapSettingsCard(intCard, "integrations sponsorblock auto-skip intros outros segments discord rpc rich presence activity preview"));



        // 5. Desktop Integration & Backup/Restore
        var sysCard = new StackPanel { Spacing = 14 };
        sysCard.Children.Add(new TextBlock { Text = "DESKTOP INTEGRATION, UPDATES & BACKUP", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });
        if (OperatingSystem.IsWindows())
        {
            sysCard.Children.Add(BuildToggleRow("Minimize to System Tray on Close", "Keep music playing smoothly in the background when closing window", s.CloseToTray, v =>
            {
                s.CloseToTray = v;
                _store.SaveDebounced();
            }));
            sysCard.Children.Add(BuildToggleRow("Media Keys While Echo Music Is Focused", "Only route media keys to Echo Music while its window is focused; other apps keep control when you switch away", s.MediaKeysEnabled, v =>
            {
                s.MediaKeysEnabled = v;
                UpdateWindowActivityState();
                _store.SaveDebounced();
            }));
        }

        const string releaseRepository = "VishnuBele/echo-music-pc";
        var updaterLabel = new TextBlock
        {
            Text = "Check the Echo Music PC GitHub releases for updates. Downloads open in your browser and are never installed silently.",
            FontSize = 12,
            Foreground = ResBrush("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        sysCard.Children.Add(updaterLabel);
        var updateStatus = new TextBlock
        {
            Text = "Ready to check the latest public release.",
            FontSize = 11.5,
            Foreground = ResBrush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        sysCard.Children.Add(updateStatus);
        var updateButtons = new WrapPanel();
        var checkReleaseBtn = new Button { Classes = { "accentBtn" }, Content = "Check for PC update", Margin = new Thickness(0, 0, 10, 8) };
        var getReleaseBtn = new Button { Classes = { "pill" }, Content = "Open latest installer", IsVisible = false, Margin = new Thickness(0, 0, 10, 8) };
        AppReleaseInfo? latestRelease = null;
        checkReleaseBtn.Click += async (_, _) =>
        {
            checkReleaseBtn.IsEnabled = false;
            getReleaseBtn.IsVisible = false;
            updateStatus.Text = "Checking GitHub Releases…";
            try
            {
                latestRelease = await AppUpdateService.CheckLatestAsync(_yt.SharedHttp, releaseRepository);
                var current = AppUpdateService.ParseVersion(DynamicConfigService.CurrentAppVersion);
                bool newer = latestRelease.ParsedVersion is not null && current is not null && latestRelease.ParsedVersion > current;
                updateStatus.Text = newer
                    ? $"Update available: {latestRelease.Tag} (installed: v{DynamicConfigService.CurrentAppVersion})."
                    : latestRelease.ParsedVersion is null
                        ? $"Found release {latestRelease.Tag}; its version tag could not be compared automatically."
                        : $"You're up to date. Latest release: {latestRelease.Tag}.";
                getReleaseBtn.Content = latestRelease.InstallerUrl is null ? "Open release page" : newer ? $"Open installer for {latestRelease.Tag}" : "Open latest release";
                getReleaseBtn.IsVisible = true;
            }
            catch (Exception ex)
            {
                updateStatus.Text = $"Update check failed: {ex.Message}";
                getReleaseBtn.IsVisible = false;
            }
            finally { checkReleaseBtn.IsEnabled = true; }
        };
        getReleaseBtn.Click += (_, _) =>
        {
            if (latestRelease is null) return;
            string target = latestRelease.InstallerUrl ?? latestRelease.ReleaseUrl;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { updateStatus.Text = $"Couldn't open the release download: {ex.Message}"; }
        };
        updateButtons.Children.Add(checkReleaseBtn);
        updateButtons.Children.Add(getReleaseBtn);
        sysCard.Children.Add(updateButtons);

        sysCard.Children.Add(new TextBlock { Text = "Auto reduces visual work whenever another app or a game is focused. Choose a mode in this row or right-click the leaf beside PiP; hidden and minimized windows always pause UI work.", FontSize = 12, Foreground = ResBrush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        sysCard.Children.Add(BuildChoiceRow("Resource Saver", new[]
        {
            ("Auto", "Auto (reduce work in background)"),
            ("On", "On (keep saver enabled)"),
            ("Off", "Off (manual saver disabled)"),
        }, s.ResourceSaverPolicy ?? "Auto", SetResourceSaverPolicy));

        sysCard.Children.Add(new TextBlock
        {
            Text = $"Clear all local data by closing Echo Music and deleting {_store.DataDir}. This also removes downloaded tracks and sign-in state. It does not uninstall the app.",
            FontSize = 11.5,
            Foreground = ResBrush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        var backupRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var acctBtn = new Button { Classes = { "pill" }, Content = _yt.IsSignedIn ? "Google account / Guest Mode" : "Google login / Guest Mode", Margin = new Thickness(0, 0, 10, 8) };
        acctBtn.Click += (_, _) => OpenAccountSignInModal();

        var refreshCfgBtn = new Button { Classes = { "pill" }, Content = "Refresh Stream Clients & Check Updates", Margin = new Thickness(0, 0, 10, 8) };
        refreshCfgBtn.Click += async (_, _) =>
        {
            ShowToast("Refreshing YouTube stream client tokens...");
            var msg = await DynamicConfigService.CheckUpdatesAndRefreshClientsAsync(_yt.SharedHttp, _yt, _store.DataDir);
            ShowToast(msg);
        };

        var expBtn = new Button { Classes = { "pill" }, Content = "Export Full Backup (.json)", Margin = new Thickness(0, 0, 10, 8) };
        expBtn.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Echo Music Backup",
                SuggestedFileName = $"EchoMusic-Backup-{DateTime.Now:yyyyMMdd}.json",
            });
            if (file?.TryGetLocalPath() is { } p)
            {
                _store.ExportBackup(p);
                ShowToast("Exported backup successfully!");
            }
        };

        var impBtn = new Button { Classes = { "pill" }, Content = "Restore Backup (.json)", Margin = new Thickness(0, 0, 10, 8) };
        impBtn.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Echo Music Backup JSON", AllowMultiple = false });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } p)
            {
                try
                {
                    _store.ImportBackup(p);
                    ApplyThemeAndTypography();
                    ShowToast("Backup restored!");
                    RenderSettingsView();
                }
                catch (Exception ex) { ShowToast($"Restore failed: {ex.Message}"); }
            }
        };

        backupRow.Children.Add(acctBtn);
        backupRow.Children.Add(refreshCfgBtn);
        backupRow.Children.Add(expBtn);
        backupRow.Children.Add(impBtn);
        sysCard.Children.Add(backupRow);
        sysCard.Children.Add(new TextBlock
        {
            Text = $"Echo Music Native PC v{DynamicConfigService.CurrentAppVersion} · PC edition by V!SHNU · GPL-3.0  •  Data folder: {_store.DataDir}",
            FontSize = 11.5,
            Foreground = ResBrush("TextMutedBrush"),
        });
        root.Children.Add(WrapSettingsCard(sysCard, "system update backup restore storage data folder account logs about version"));
        root.Children.Add(noSettingsMatch);
        settingsSearch.Text = _settingsSearchQuery;
        ApplySettingsSearch();
        SetCenterContent(root);
    }

    private Control BuildDiscordActivityPreview()
    {
        var preview = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        preview.Children.Add(new TextBlock
        {
            Text = "Current activity",
            FontSize = 16,
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(Color.Parse("#B5BAC1")),
            Margin = new Thickness(10, 10, 10, 14),
        });

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2B2D31")),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18),
            Margin = new Thickness(10, 0, 10, 12),
            MinHeight = 222,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var content = new StackPanel { Spacing = 0 };
        _discordPreviewStatus = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#F2F3F5")),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 2, 0, 14),
        };
        content.Children.Add(_discordPreviewStatus);

        var activity = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("150,*"), VerticalAlignment = VerticalAlignment.Center };
        var artBorder = new Border
        {
            Width = 150,
            Height = 150,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.Parse("#1E1F22")),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _discordPreviewArtwork = new Image { Stretch = Stretch.UniformToFill };
        try
        {
            using var appIconStream = AssetLoader.Open(new Uri("avares://EchoMusic/Assets/echo-discord.png"));
            _discordPreviewArtwork.Source = new Bitmap(appIconStream);
        }
        catch { }
        artBorder.Child = _discordPreviewArtwork;
        activity.Children.Add(artBorder);

        var details = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        _discordPreviewTrack = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#F2F3F5")),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(0, 0, 0, 2),
        };
        _discordPreviewArtists = new TextBlock
        {
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.Parse("#F2F3F5")),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        details.Children.Add(_discordPreviewTrack);
        details.Children.Add(_discordPreviewArtists);

        var progressRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("54,*,54"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        _discordPreviewElapsed = new TextBlock
        {
            FontSize = 16,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.Parse("#F2F3F5")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _discordPreviewDuration = new TextBlock
        {
            FontSize = 16,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.Parse("#F2F3F5")),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 0, 0),
        };
        _discordPreviewProgressTrack = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(99),
            Background = new SolidColorBrush(Color.Parse("#42444A")),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
        };
        _discordPreviewProgressFill = new Border
        {
            Width = 0,
            Height = 3,
            CornerRadius = new CornerRadius(99),
            Background = new SolidColorBrush(Color.Parse("#F2F3F5")),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _discordPreviewProgressTrack.Child = _discordPreviewProgressFill;
        progressRow.Children.Add(_discordPreviewElapsed);
        Grid.SetColumn(_discordPreviewProgressTrack, 1);
        progressRow.Children.Add(_discordPreviewProgressTrack);
        Grid.SetColumn(_discordPreviewDuration, 2);
        progressRow.Children.Add(_discordPreviewDuration);
        _discordPreviewProgressTrack.SizeChanged += (_, _) => UpdateDiscordActivityPreview();
        details.Children.Add(progressRow);
        Grid.SetColumn(details, 1);
        activity.Children.Add(details);
        content.Children.Add(activity);
        card.Child = content;
        preview.Children.Add(card);
        UpdateDiscordActivityPreview();
        return preview;
    }

    private void UpdateDiscordActivityPreview()
    {
        if (_discordPreviewStatus is null) return;
        var track = _audio.CurrentTrack;
        bool enabled = _store.Settings.DiscordRpcEnabled;
        _discordPreviewStatus.Text = !enabled
            ? "Discord sharing is turned off"
            : _audio.IsPlaying ? "Listening to Echo Music"
            : track is not null ? "Paused in Echo Music" : "Nothing playing";
        _discordPreviewTrack!.Text = track?.Title ?? "Nothing playing";
        _discordPreviewArtists!.Text = track is null ? "Select a song to preview the activity" : $"by {track.ArtistNames}";
        double duration = Math.Max(0, _audio.DurationSeconds);
        double position = Math.Clamp(_audio.PositionSeconds, 0, duration > 0 ? duration : 0);
        double ratio = duration > 0.1 ? Math.Clamp(position / duration, 0, 1) : 0;
        if (_discordPreviewProgressTrack is not null && _discordPreviewProgressFill is not null)
            _discordPreviewProgressFill.Width = Math.Max(0, _discordPreviewProgressTrack.Bounds.Width * ratio);
        _discordPreviewElapsed!.Text = FormatDiscordTime(position);
        _discordPreviewDuration!.Text = FormatDiscordTime(duration);
    }

    private static string FormatDiscordTime(double seconds)
    {
        var safe = TimeSpan.FromSeconds(Math.Max(0, seconds));
        int totalMinutes = (int)Math.Floor(safe.TotalMinutes);
        return totalMinutes >= 60
            ? $"{totalMinutes / 60}:{totalMinutes % 60:D2}:{safe.Seconds:D2}"
            : $"{totalMinutes:D2}:{safe.Seconds:D2}";
    }

    private Border WrapSettingsCard(Control child, string searchTerms = "") => new()
    {
        Tag = searchTerms,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(20, 18),
        BorderThickness = new Thickness(1),
        Background = ResBrush("BgCardBrush"),
        BorderBrush = ResBrush("BorderSubtleBrush"),
        Child = child,
    };

    private Control BuildToggleRow(string title, string subtitle, bool initial, Action<bool> onChanged)
    {
        var g = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        var st = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 16, 0) };
        st.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold });
        st.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = ResBrush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        g.Children.Add(st);

        var sw = new ToggleSwitch { IsChecked = initial, OnContent = "On", OffContent = "Off", VerticalAlignment = VerticalAlignment.Center };
        sw.IsCheckedChanged += (_, _) => onChanged(sw.IsChecked == true);
        Grid.SetColumn(sw, 1);
        g.Children.Add(sw);
        return g;
    }

    private Control BuildChoiceRow(string title, (string Val, string Label)[] options, string current, Action<string> onPick)
    {
        var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,280"), Margin = new Thickness(0, 2, 0, 2) };
        var label = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        row.Children.Add(label);
        var buttonText = new TextBlock { Text = options.FirstOrDefault(x => string.Equals(x.Val, current, StringComparison.OrdinalIgnoreCase)).Label ?? current, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        var chevron = new PathIcon { Data = Icons.ChevronDown, Width = 13, Height = 13, Foreground = ResBrush("TextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        var buttonContent = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Children = { buttonText, chevron } };
        Grid.SetColumn(chevron, 1);
        var button = new Button { Classes = { "pill" }, Content = buttonContent, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8), MinHeight = 38 };
        Grid.SetColumn(button, 1);
        row.Children.Add(button);

        var popupBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(5),
            Background = ResBrush("BgCardBrush"),
            BorderBrush = ResBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 10 28 0 #50000000"),
            Opacity = 0,
            Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(130) } },
        };
        var choices = new StackPanel { Spacing = 3 };
        var scroll = new ScrollViewer { MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = choices };
        popupBorder.Child = scroll;
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Child = popupBorder };
        row.Children.Add(popup);

        void ResizePopup() => popupBorder.Width = Math.Max(220, button.Bounds.Width);
        button.SizeChanged += (_, _) => ResizePopup();
        ResizePopup();

        async Task CloseAnimatedAsync()
        {
            popupBorder.Opacity = 0;
            await Task.Delay(135);
            popup.IsOpen = false;
        }
        button.Click += (_, _) =>
        {
            if (popup.IsOpen) { _ = CloseAnimatedAsync(); return; }
            ResizePopup();
            popup.IsOpen = true;
            popupBorder.Opacity = 0;
            Dispatcher.UIThread.Post(() => popupBorder.Opacity = 1, DispatcherPriority.Render);
        };

        foreach (var (value, text) in options)
        {
            string captured = value;
            var optionButton = new Button
            {
                Classes = { string.Equals(current, value, StringComparison.OrdinalIgnoreCase) ? "accentBtn" : "navItem" },
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8),
                MinHeight = 36,
            };
            optionButton.Click += async (_, _) =>
            {
                buttonText.Text = text;
                await CloseAnimatedAsync();
                onPick(captured);
            };
            choices.Children.Add(optionButton);
        }

        return row;
    }

    private static bool TrySetRunOnStartup(bool enabled, out string error)
    {
        error = "";
        if (!OperatingSystem.IsWindows())
        {
            error = "Run on startup is only supported on Windows.";
            return false;
        }
        try
        {
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (string.IsNullOrWhiteSpace(startupFolder)) throw new InvalidOperationException("Windows did not provide this account's Startup folder.");
            Directory.CreateDirectory(startupFolder);
            string shortcutPath = Path.Combine(startupFolder, "Echo Music.lnk");

            if (enabled)
            {
                string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Could not locate Echo Music executable.");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
                object shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Could not create the Windows shortcut service.");
                object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath })!;
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { exe });
                shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exe) ?? startupFolder });
                shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "Start Echo Music when this Windows account signs in" });
                shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { exe + ",0" });
                shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
                if (!File.Exists(shortcutPath)) throw new InvalidOperationException("Windows did not save the visible Startup folder shortcut.");
            }
            else
            {
                if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                if (File.Exists(shortcutPath)) throw new InvalidOperationException("Windows did not remove the Startup folder shortcut.");
            }

            // Remove the legacy Run key value written by older builds. The current opt-in uses only
            // the ordinary, visible per-user Startup shortcut, with no service or hidden task.
            using var oldRunKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            oldRunKey?.DeleteValue("EchoMusic", throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not update Windows startup: {ex.Message}";
            return false;
        }
    }

}
