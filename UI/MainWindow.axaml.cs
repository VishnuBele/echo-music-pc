using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EchoMusic.Core;
using AnyAscii;
using System.Text.RegularExpressions;

namespace EchoMusic.UI;

public partial class MainWindow : Window
{
    internal readonly AppStore _store;
    internal readonly InnerTubeClient _yt;
    internal readonly StreamResolver _resolver;
    internal readonly AudioEngine _audio;
    internal readonly LyricsService _lyricsSvc;
    internal readonly MusicDnaAndExtras _extras;

    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _visualMotionTimer;
    private DispatcherTimer? _searchSuggestFadeTimer;
    private readonly float[] _fftScratch = new float[32];
    private SpectrumVisualizerControl? _dockCurrentArtSpectrum;
    private Border? _dockCurrentArtSpectrumPlate;
    private readonly List<(WeakReference<Border> Overlay, WeakReference<SpectrumVisualizerControl> Visualizer, string TrackId)> _trackArtSpectra = new();
    private readonly HashSet<DispatcherTimer> _lyricsShimmerTimers = new();
    private string? _bufferMonitorTrackId;
    private double _bufferMonitorPosition;
    private DateTime _bufferMonitorAdvancedUtc = DateTime.UtcNow;
    private bool _streamBuffering;

    // Navigation history
    private readonly List<Func<Task>> _navHistory = new();
    private int _navCursor = -1;
    private bool _suppressNavPush;
    private string _activeNavKey = "home";
    private string _currentLibraryTab = "overview";

    // Right panel state ("now" | "queue" | "lyrics" | "dna")
    private string _dockTab = "now";
    private bool _dockOpen = true;
    private bool _leftSidebarOpen = true;
    private string _leftSidebarMode = "Expanded";
    private readonly Dictionary<TextBlock, bool> _sidebarTextOriginalVisibility = new();
    private string? _sidebarPlaylistFingerprint;
    private DispatcherTimer? _leftSidebarAnimation;
    private bool _sidebarCompactAnimationPending;
    private bool _sidebarContentOffsetAnimating;
    private double _sidebarContentOffsetProgress;
    private double _sidebarContentOffsetFrom;
    private double _sidebarContentOffsetTo;
    private DispatcherTimer? _rightSidebarAnimation;
    private sealed class SmoothScrollState
    {
        public double TargetX;
        public double TargetY;
        public double VelocityX;
        public double VelocityY;
    }
    private readonly Dictionary<ScrollViewer, SmoothScrollState> _smoothScrollStates = new();
    private bool _animationFramePending;
    private bool _hasLastMotionFrame;
    private TimeSpan _lastMotionFrameTime;
    private TimeSpan _lastMotionUpdateTime;
    private ContextMenu? _openTrackContextMenu;
    private sealed class ImageShimmerState
    {
        public required Border Frame { get; init; }
        public required LinearGradientBrush Brush { get; init; }
        public required GradientStop A { get; init; }
        public required GradientStop B { get; init; }
        public required GradientStop C { get; init; }
        public required DateTime StartedUtc { get; init; }
    }
    private readonly Dictionary<Image, ImageShimmerState> _imageShimmers = new();
    private DispatcherTimer? _imageShimmerTimer;
    private ScrollViewer? _dockLyricsScroller;
    private StackPanel? _historyTrackListStack;
    private readonly Dictionary<string, Control> _historyTrackRows = new(StringComparer.Ordinal);
    private StackPanel? _dockNowStack;
    private bool _dockDnaCardAdded;

    // Search debounce
    private CancellationTokenSource? _suggestCts;
    private string _activeSearchQuery = "";
    private string _activeSearchFilter = "all";
    private readonly List<Button> _searchSuggestionButtons = new();
    private readonly List<string> _searchSuggestionTexts = new();
    private int _searchSuggestionIndex = -1;
    private CancellationTokenSource? _lyricsFetchCts;

    // Home & Charts state
    private HomeFeedResult? _cachedHome;
    private List<ShelfSection>? _homeMoodGenresCache;
    private string? _activeMoodParams;
    private string? _activeMoodBrowseId;
    private string? _homeContinuation;
    private StackPanel? _homeShelvesContainer;
    private Button? _homeLoadMoreBtn;
    private bool _loadingMoreHome;
    private readonly HashSet<string> _homeSeenTitles = new(StringComparer.OrdinalIgnoreCase);
    private int _heroCarouselIndex;
    private DispatcherTimer? _heroAutoTimer;
    private int _chartPageIndex;
    private int _homeLoadGeneration;

    // Lyrics, DNA & Related state
    private LyricsData? _currentLyrics;
    private DnaInfo? _currentDna;
    private List<TrackItem> _relatedTracks = new();
    private int _lastActiveLyricIdx = -1;
    private bool _lyricsShowRomanized;
    private string? _autoLyricsRetryTrackId;
    private readonly List<TextBlock> _dockLyricBlocks = new();
    private readonly List<TextBlock> _fsLyricBlocks = new();
    private readonly List<List<Run>?> _dockLyricWordRuns = new();
    private readonly List<List<Run>?> _fsLyricWordRuns = new();
    private readonly List<Run> _flowLyricWordRuns = new();
    private int _flowWordSyncLine = -1;
    private string _flowDisplayedLyricLine1 = "";
    private string _flowDisplayedLyricLine2 = "";
    internal TextBlock? _flowLyricLine1;
    internal TextBlock? _flowLyricLine2;
    internal TextBlock? _flowLyricStatus;

    // Slider guard
    private bool _seekingByCode;
    private bool _volByCode;

    // Mini mode saved dimensions
    private bool _isMiniMode;
    private double _savedWidth = 1280;
    private double _savedHeight = 760;
    private CancellationTokenSource? _toastCts;
    private readonly TranslateTransform _toastTransform = new();

    public bool AllowTrueExit { get; set; }

    public MainWindow() : this(new AppStore()) { }

    public MainWindow(AppStore store)
    {
        _store = store;
        _yt = new InnerTubeClient
        {
            RegionGl = _store.Settings.RegionGl,
            LanguageHl = _store.Settings.LanguageHl,
        };
        if (!string.IsNullOrEmpty(_store.Settings.CookieHeader))
            _yt.SetCookies(_store.Settings.CookieHeader, _store.Settings.DataSyncId);

        _resolver = new StreamResolver(_yt, () => _store.Settings.AudioQuality);
        _audio = new AudioEngine(_yt, _resolver, _store);
        _lyricsSvc = new LyricsService(_yt);
        _extras = new MusicDnaAndExtras(_yt);

        InitializeComponent();
        ImageCache.SetResourceSaver(IsResourceSaverActive);
        ScrollingTitleTextBlock.AnimationRequested += EnsureAnimationFramePump;
        SearchSuggestFlyout.PlacementTarget = GlobalSearchBox;
        SearchSuggestFlyout.HorizontalOffset = 0;
        SearchSuggestFlyout.VerticalOffset = 4;
        _visualMotionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnVisualMotionTick);
        _visualMotionTimer.Interval = AnimationFrameInterval;
        ToastBanner.RenderTransform = _toastTransform;
        CenterViewHost.SizeChanged += (_, _) => UpdateMusicFlowLyricWidth();

        ApplyThemeAndTypography();
        _leftSidebarMode = NormalizeSidebarMode(_store.Settings.LeftSidebarMode);
        _store.Settings.LeftSidebarMode = _leftSidebarMode;
        ApplyLeftSidebarMode(_leftSidebarMode, animate: false, rerenderSettings: false);
        ApplyAppIconVariant();
        WireCoreEvents();
        FsLyricsScroller.SizeChanged += (_, _) =>
        {
            double width = FsLyricsScroller.Bounds.Width;
            if (width > 0) FsLyricsStack.Width = width;
        };

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty || e.Property == OffScreenMarginProperty)
                ApplySafeWindowFrameMargin();
            if (e.Property == WindowStateProperty)
                UpdateWindowActivityState();
        };

        Activated += (_, _) =>
        {
            UpdateWindowActivityState();
            // Tray restoration reuses this Window instead of raising Opened again. Re-apply the
            // persisted sidebar mode after the visual tree is active so playlist labels cannot leak
            // into the compact layout on show/restore.
            if (_leftSidebarAnimation is null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsVisible && _leftSidebarAnimation is null)
                        ApplyLeftSidebarMode(_leftSidebarMode, animate: false, rerenderSettings: false);
                }, DispatcherPriority.Loaded);
            }
        };
        Deactivated += (_, _) => UpdateWindowActivityState();

        _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Render, OnUiFrameTick);
        _uiTimer.Start();

        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
    }

    public AudioEngine Engine => _audio;
    public AppStore Store => _store;

    private void FitWindowToWorkingAreaOnStartup()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;
            double scale = RenderScaling > 0.1 ? RenderScaling : (screen.Scaling > 0.1 ? screen.Scaling : 1.0);
            var wa = screen.WorkingArea;
            double maxDipW = wa.Width / scale;
            double maxDipH = wa.Height / scale;

            if (WindowState == WindowState.Normal && !_isMiniMode)
            {
                double targetW = Math.Clamp(Math.Min(1280, maxDipW * 0.93), 820, Math.Max(820, maxDipW - 20));
                double targetH = Math.Clamp(Math.Min(760, maxDipH * 0.91), 540, Math.Max(540, maxDipH - 20));
                Width = targetW;
                Height = targetH;
                _savedWidth = targetW;
                _savedHeight = targetH;

                int pxW = (int)(targetW * scale);
                int pxH = (int)(targetH * scale);
                int posX = wa.X + Math.Max(0, (wa.Width - pxW) / 2);
                int posY = wa.Y + Math.Max(0, (wa.Height - pxH) / 2);
                Position = new PixelPoint(posX, posY);
            }
            ApplySafeWindowFrameMargin();
        }
        catch { }
    }

    private void ApplySafeWindowFrameMargin()
    {
        if (WindowState == WindowState.Maximized)
        {
            var osm = OffScreenMargin;
            double padL = Math.Max(osm.Left, OperatingSystem.IsWindows() ? 8 : 0);
            double padT = Math.Max(osm.Top, OperatingSystem.IsWindows() ? 8 : 0);
            double padR = Math.Max(osm.Right, OperatingSystem.IsWindows() ? 8 : 0);
            double padB = Math.Max(osm.Bottom, OperatingSystem.IsWindows() ? 8 : 0);
            RootLayerGrid.Margin = new Thickness(padL, padT, padR, padB);
        }
        else
        {
            RootLayerGrid.Margin = new Thickness(0);
        }
    }

    internal static string GetAppIconAssetUri(string? style) => (style ?? "WaveDefault") switch
    {
        "MobileBillie" => "avares://EchoMusic/Assets/MobileIcons/billie-eilish.png",
        "MobileCat" => "avares://EchoMusic/Assets/MobileIcons/cat.png",
        "MobileCraft" => "avares://EchoMusic/Assets/MobileIcons/craft.png",
        "CrazyBlue" => "avares://EchoMusic/Assets/MobileIcons/crazy-blue.png",
        "EchoCat" => "avares://EchoMusic/Assets/MobileIcons/echo-cat.png",
        "MobileLegacy" => "avares://EchoMusic/Assets/MobileIcons/legacy.png",
        "MobileNew" => "avares://EchoMusic/Assets/MobileIcons/new.png",
        "MobilePookie" => "avares://EchoMusic/Assets/MobileIcons/pookie.png",
        "MobileSabrina" => "avares://EchoMusic/Assets/MobileIcons/sabrina-1.png",
        "MobileSabrina2" => "avares://EchoMusic/Assets/MobileIcons/sabrina-2.png",
        "MobileSadie" => "avares://EchoMusic/Assets/MobileIcons/sadie.png",
        "Sky" => "avares://EchoMusic/Assets/MobileIcons/sky.png",
        "MonoWhite" => "avares://EchoMusic/Assets/MobileIcons/echo-white.png",
        "MonoBlack" => "avares://EchoMusic/Assets/MobileIcons/echo-black.png",
        "Eko" => "avares://EchoMusic/Assets/icon-eko.png",
        _ => "avares://EchoMusic/Assets/echo-logo.png",
    };

    internal void ApplyAppIconVariant()
    {
        string asset = GetAppIconAssetUri(_store.Settings.AppIconStyle);
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(asset));
            var bmp = new Bitmap(stream);
            TitleWaveLogo.Source = bmp;
            Icon = new WindowIcon(bmp);
            if (Application.Current is App app) app.UpdateTrayIcon(_store.Settings.AppIconStyle);
            if (TryGetPlatformHandle()?.Handle is IntPtr hwnd && hwnd != IntPtr.Zero)
                Win32TaskbarIconHelper.EnforceNativeWindowIcon(this, _store.Settings.AppIconStyle);
        }
        catch { }
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        FitWindowToWorkingAreaOnStartup();
        if (OperatingSystem.IsWindows())
        {
            // Migrate the previous Run-key registration to a normal, visible Startup shortcut.
            if (!TrySetRunOnStartup(_store.Settings.RunOnStartup, out var startupError) && _store.Settings.RunOnStartup)
                ShowToast(startupError);
        }

        if (TryGetPlatformHandle()?.Handle is IntPtr hwnd && hwnd != IntPtr.Zero)
        {
            Win32TaskbarIconHelper.SetWindowAppUserModelId(hwnd);
            Win32TaskbarIconHelper.SetInstalledShortcutsAppUserModelId();
            Win32TaskbarIconHelper.EnforceNativeWindowIcon(this, _store.Settings.AppIconStyle);

            var pal = ThemeManager.GetPalette(_store.Settings.Theme);
            WindowsIntegration.ApplyDarkTitleBar(hwnd, pal.IsDark);
            UpdateWindowActivityState();
            Win32Properties.AddWndProcHookCallback(this, (IntPtr _, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (_store.Settings.MediaKeysEnabled && IsActive && WindowsIntegration.HandleWin32MediaMessage(msg, wParam, lParam, _audio))
                    handled = true;
                return IntPtr.Zero;
            });
        }

        RefreshSidebarBadgesAndPlaylists();
        ApplyLeftSidebarMode(_leftSidebarMode, animate: false, rerenderSettings: false);
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible || _leftSidebarAnimation is not null) return;
            _leftSidebarMode = NormalizeSidebarMode(_store.Settings.LeftSidebarMode);
            ApplyLeftSidebarMode(_leftSidebarMode, animate: false, rerenderSettings: false);
        }, DispatcherPriority.Background);
        RefreshAccountBadge();
        RenderRightDock();
        UpdateNowPlayingUi();

        if (_store.Settings.LocalMusicFolders.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                var scanned = await MusicDnaAndExtras.ScanLocalFoldersAsync(_store.Settings.LocalMusicFolders, _store.ArtCacheDir);
                Dispatcher.UIThread.Post(() => _store.SetLocalTracks(scanned));
            });
        }

        await NavigateToAsync("home", () => LoadHomeViewAsync(null));
        if (!App.VisualSmokeMode) _ = CheckForStartupUpdateAsync();
    }

    private async Task CheckForStartupUpdateAsync()
    {
        try
        {
            await Task.Delay(1200); // Let the first screen settle before any update prompt.
            var release = await AppUpdateService.CheckLatestAsync(_yt.SharedHttp, "VishnuBele/echo-music-pc");
            var current = AppUpdateService.ParseVersion(DynamicConfigService.CurrentAppVersion);
            if (release.ParsedVersion is null || current is null || release.ParsedVersion <= current)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!ModalBackdrop.IsVisible) ShowStartupUpdateModal(release);
            });
        }
        catch
        {
            // Startup update checks are intentionally silent when offline or when the release service is unavailable.
        }
    }

    private void ShowStartupUpdateModal(AppReleaseInfo release)
    {
        var stack = new StackPanel { Spacing = 14, MaxWidth = 520 };
        stack.Children.Add(new TextBlock
        {
            Text = $"Echo Music {release.Tag} is available",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"You are using v{DynamicConfigService.CurrentAppVersion}. Open the release page to review the changes and download the update. Nothing will be downloaded or installed automatically.",
            FontSize = 13,
            Foreground = ResBrush("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(release.Notes))
        {
            var notes = new TextBlock
            {
                Text = release.Notes.Trim(),
                FontSize = 12,
                Foreground = ResBrush("TextMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
            };
            stack.Children.Add(new ScrollViewer { Content = notes, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var later = new Button { Classes = { "pill" }, Content = "Later" };
        later.Click += (_, _) => ModalBackdrop.IsVisible = false;
        var open = new Button { Classes = { "accentBtn" }, Content = "View release" };
        open.Click += (_, _) =>
        {
            ModalBackdrop.IsVisible = false;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(release.ReleaseUrl) { UseShellExecute = true }); }
            catch (Exception ex) { ShowToast($"Could not open the update link: {ex.Message}"); }
        };
        actions.Children.Add(later);
        actions.Children.Add(open);
        stack.Children.Add(actions);
        ModalCardHost.Child = stack;
        ModalBackdrop.IsVisible = true;
    }

    private string ResourceSaverPolicy
    {
        get
        {
            var policy = _store.Settings.ResourceSaverPolicy;
            return policy is "On" or "Off" or "Auto" ? policy : "Auto";
        }
    }

    private bool IsResourceSaverActive => ResourceSaverPolicy == "On"
        || (ResourceSaverPolicy == "Auto" && (!IsActive || !IsVisible || WindowState == WindowState.Minimized));

    private string CurrentPerformanceMode => _store.Settings.PerformanceMode is "Low" or "Auto" or "High"
        ? _store.Settings.PerformanceMode
        : _store.Settings.AnimationFrameRate <= 30 ? "Low"
        : _store.Settings.AnimationFrameRate >= 90 ? "High" : "Auto";

    // These are update caps, not promised monitor refresh rates. High uses each compositor animation tick.
    private int ConfiguredAnimationRate => CurrentPerformanceMode switch { "Low" => 30, "High" => 120, _ => 60 };

    private TimeSpan AnimationFrameInterval => TimeSpan.FromSeconds(1.0 / (IsResourceSaverActive ? 30 : ConfiguredAnimationRate));

    private TimeSpan DesiredUiInterval => (!IsVisible || WindowState == WindowState.Minimized)
        ? TimeSpan.FromMilliseconds(500)
        : (_audio.IsPlaying || _audio.IsLoading || _streamBuffering)
            ? AnimationFrameInterval
            : IsResourceSaverActive
                ? TimeSpan.FromMilliseconds(500)
                : TimeSpan.FromMilliseconds(250);

    private void UpdateWindowActivityState()
    {
        bool unavailable = !IsVisible || WindowState == WindowState.Minimized;
        ImageCache.SetResourceSaver(IsResourceSaverActive);
        ScrollingTitleTextBlock.SetPerformanceMode(CurrentPerformanceMode, IsResourceSaverActive);
        var animationInterval = AnimationFrameInterval;
        _visualMotionTimer.Interval = animationInterval;
        if (IsResourceSaverActive)
            foreach (var image in _imageShimmers.Keys.ToArray()) StopImageLoadingShimmer(image);
        foreach (var timer in _lyricsShimmerTimers)
        {
            timer.Interval = animationInterval;
            if (unavailable) timer.Stop();
            else if (!timer.IsEnabled) timer.Start();
        }
        if (_imageShimmers.Count > 0)
        {
            EnsureImageShimmerTimer();
            _imageShimmerTimer!.Interval = animationInterval;
            if (unavailable) _imageShimmerTimer.Stop();
            else if (!_imageShimmerTimer.IsEnabled) _imageShimmerTimer.Start();
        }
        else _imageShimmerTimer?.Stop();

        if (unavailable)
        {
            // Keep low-rate audio maintenance alive in the tray (sleep timers, history, crossfade/next-track),
            // while stopping all UI animation and high-frequency render work.
            _uiTimer.Interval = DesiredUiInterval;
            _uiTimer.Start();
            _visualMotionTimer.Stop();
            _heroAutoTimer?.Stop();
            _smoothScrollStates.Clear();
            _hasLastMotionFrame = false;
            if (_leftSidebarAnimation is not null)
            {
                _leftSidebarAnimation.Stop();
                _leftSidebarAnimation = null;
                _sidebarCompactAnimationPending = false;
                _sidebarContentOffsetAnimating = false;
                _sidebarContentOffsetProgress = _sidebarContentOffsetTo;
                WorkspaceColumnsGrid.ColumnDefinitions[0].Width = new GridLength(_leftSidebarMode switch { "Compact" => 76, "Hidden" => 0, _ => 244 });
                UpdateSidebarCompactVisuals();
            }
            if (_rightSidebarAnimation is not null)
            {
                _rightSidebarAnimation.Stop();
                _rightSidebarAnimation = null;
                WorkspaceColumnsGrid.ColumnDefinitions[2].Width = new GridLength(_dockOpen ? 312 : 0);
                if (!_dockOpen) RightDockBorder.IsVisible = false;
            }
        }
        else
        {
            _uiTimer.Interval = DesiredUiInterval;
            _uiTimer.Start();
            if (IsResourceSaverActive) _heroAutoTimer?.Stop();
            else if (_activeNavKey == "home") _heroAutoTimer?.Start();
        }
        _spotifyPlaceholderSync?.Invoke();
        UpdateResourceSaverButtonVisual();
    }

    private void SetPerformanceMode(string mode)
    {
        if (mode is not ("Low" or "Auto" or "High")) mode = "Auto";
        _store.Settings.PerformanceMode = mode;
        _store.Settings.AnimationFrameRate = mode switch { "Low" => 30, "High" => 120, _ => 60 };
        _store.SaveDebounced();
        UpdateWindowActivityState();
        if (_activeNavKey == "settings") RenderSettingsView();
    }

    private void CycleResourceSaverPolicy()
    {
        string next = ResourceSaverPolicy switch { "Auto" => "On", "On" => "Off", _ => "Auto" };
        SetResourceSaverPolicy(next);
    }

    private void SetResourceSaverPolicy(string policy)
    {
        if (policy is not ("Auto" or "On" or "Off")) policy = "Auto";
        _store.Settings.ResourceSaverPolicy = policy;
        _store.Settings.ResourceSaverMode = policy == "On";
        _store.SaveDebounced();
        BuildResourceSaverContextMenu();
        UpdateWindowActivityState();
        if (_activeNavKey == "settings") RenderSettingsView();
        ShowToast(policy == "On" ? "Resource Saver is on" : policy == "Off" ? "Resource Saver is off" : "Resource Saver is automatic");
    }

    private void UpdateResourceSaverButtonVisual()
    {
        if (ResourceSaverIcon is null) return;
        bool backgroundAuto = ResourceSaverPolicy == "Auto" && (!IsActive || WindowState == WindowState.Minimized || !IsVisible);
        ResourceSaverIcon.Foreground = ResBrush(IsResourceSaverActive ? "AccentBrush" : "TextMutedBrush");
        ToolTip.SetTip(ResourceSaverBtn, ResourceSaverPolicy switch
        {
            "On" => "Resource Saver: On. Click cycles modes; right-click to choose.",
            "Off" => "Resource Saver: Off. Click cycles modes; right-click to choose.",
            _ when backgroundAuto => "Resource Saver: Auto active in the background. Click cycles modes; right-click to choose.",
            _ => "Resource Saver: Auto. Click cycles modes; right-click to choose.",
        });
    }

    private void BuildResourceSaverContextMenu()
    {
        var menu = new ContextMenu();
        foreach (var (policy, label) in new[] { ("Auto", "Resource Saver: Auto"), ("On", "Resource Saver: On"), ("Off", "Resource Saver: Off") })
        {
            var item = new MenuItem { Header = ResourceSaverPolicy == policy ? label + " (current)" : label };
            item.Click += (_, _) => SetResourceSaverPolicy(policy);
            menu.Items.Add(item);
        }
        ResourceSaverBtn.ContextMenu = menu;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!AllowTrueExit && _store.Settings.CloseToTray && OperatingSystem.IsWindows())
        {
            e.Cancel = true;
            Hide();
            UpdateWindowActivityState();
            return;
        }
        _uiTimer.Stop();
        _visualMotionTimer.Stop();
        _leftSidebarAnimation?.Stop();
        _rightSidebarAnimation?.Stop();
        _smoothScrollStates.Clear();
        ScrollingTitleTextBlock.AnimationRequested -= EnsureAnimationFramePump;
        foreach (var image in _imageShimmers.Keys.ToArray()) StopImageLoadingShimmer(image);
        foreach (var timer in _lyricsShimmerTimers) timer.Stop();
        _lyricsShimmerTimers.Clear();
        _store.SaveNow();
        _audio.Dispose();
    }

    internal void ApplyThemeAndTypography()
    {
        var pal = ThemeManager.GetPalette(_store.Settings.Theme);
        RequestedThemeVariant = pal.IsDark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = RequestedThemeVariant;

        if (Application.Current is not null)
            ThemeManager.ApplyToResources(Application.Current.Resources, pal);
        ThemeManager.ApplyToResources(Resources, pal);
        // Fluent Slider templates size/color their Thumb through these dynamic resources.
        // Override them at window scope so template pseudo-states cannot revert to black/default size.
        var sliderThumbBrush = new SolidColorBrush(pal.Accent);
        Resources["SliderHorizontalThumbWidth"] = 8d;
        Resources["SliderHorizontalThumbHeight"] = 8d;
        Resources["SliderThumbCornerRadius"] = new CornerRadius(4);
        Resources["SliderVerticalThumbWidth"] = 8d;
        Resources["SliderVerticalThumbHeight"] = 8d;
        Resources["SliderThumbBackground"] = sliderThumbBrush;
        Resources["SliderThumbBorderBrush"] = sliderThumbBrush;
        Resources["SliderThumbBorderBrushPointerOver"] = sliderThumbBrush;
        Resources["SliderThumbBorderBrushPressed"] = sliderThumbBrush;
        Resources["SliderThumbBackgroundPointerOver"] = sliderThumbBrush;
        Resources["SliderThumbBackgroundPressed"] = sliderThumbBrush;
        Resources["SliderThumbBackgroundDisabled"] = sliderThumbBrush;
        UpdateImageShimmerColors(pal.Accent);

        FontFamily = ThemeManager.ResolveFont(_store.Settings.FontChoice);
        var activeAccent = pal.Accent;
        if (Resources.TryGetResource("AccentBrush", null, out var ab) && ab is SolidColorBrush scb)
            activeAccent = scb.Color;

        // Crisp Spotify-style solid white circle play button on dark themes, dark charcoal on light themes
        var btnFill = pal.IsDark ? Color.Parse("#FFFFFF") : Color.Parse("#181818");
        var btnGlyph = pal.IsDark ? Color.Parse("#000000") : Color.Parse("#FFFFFF");
        bool useWavy = _store.Settings.WavyPlayPauseButton;
        bool useScallop = !useWavy && string.Equals(_store.Settings.PlayerStyle, "Scallop", StringComparison.OrdinalIgnoreCase);

        PlayPauseScallop.FillColor = btnFill;
        PlayPauseScallop.GlyphColor = btnGlyph;
        PlayPauseScallop.UseScallopShape = useScallop;
        PlayPauseScallop.UseWavyShape = useWavy;
        PlayPauseScallop.InvalidateVisual();

        FsScallop.FillColor = pal.IsDark ? Color.Parse("#FFFFFF") : Color.Parse("#181818");
        FsScallop.GlyphColor = pal.IsDark ? Color.Parse("#000000") : Color.Parse("#FFFFFF");
        FsScallop.UseScallopShape = useScallop;
        FsScallop.UseWavyShape = useWavy;
        FsScallop.InvalidateVisual();

        MiniScallop.FillColor = btnFill;
        MiniScallop.GlyphColor = btnGlyph;
        MiniScallop.UseScallopShape = useScallop;
        MiniScallop.UseWavyShape = useWavy;
        MiniScallop.InvalidateVisual();

        string seekStyle = _store.Settings.PlayerSliderStyle;
        SeekSlider.SliderStyle = seekStyle;
        FsSeekSlider.SliderStyle = seekStyle;
        MiniSeekSlider.SliderStyle = seekStyle;
        EnsureVisualMotionTimerRunning();

        SkipBack10Btn.IsVisible = _store.Settings.ShowTenSecSkip;
        SkipFwd10Btn.IsVisible = _store.Settings.ShowTenSecSkip;

        MiniBarSpectrum.BarColor = activeAccent;
        FsSpectrum.BarColor = activeAccent;
        FsArtSpectrum.BarColor = activeAccent;

        AmbientBackdropGlow.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(pal.IsDark ? (byte)78 : (byte)42, activeAccent.R, activeAccent.G, activeAccent.B), 0),
                new GradientStop(Colors.Transparent, 1),
            }
        };

        if (_flowLyricLine1 is not null) _flowLyricLine1.Foreground = ResBrush("AccentBrush");
        if (_flowLyricLine2 is not null) _flowLyricLine2.Foreground = ResBrush("TextMutedBrush");
        if (_flowLyricCard is not null)
        {
            _flowLyricCard.Background = ResBrush("AccentSoftBrush");
            _flowLyricCard.BorderBrush = ResBrush("BorderSubtleBrush");
        }
        if (_currentLyrics is not null)
        {
            _lastActiveLyricIdx = -1;
            SyncActiveLyricHighlight(_audio.PositionSeconds);
            if (FullscreenOverlay.IsVisible) PopulateFullscreenLyrics();
        }
        if (_dockOpen) RenderRightDock();
        if (_activeNavKey != "settings" && _navCursor >= 0 && _navCursor < _navHistory.Count)
        {
            string refreshKey = _activeNavKey;
            var refreshAction = _navHistory[_navCursor];
            var savedOffset = CenterScrollViewer.Offset;
            Dispatcher.UIThread.Post(async () =>
            {
                if (_activeNavKey != refreshKey) return;
                try
                {
                    await refreshAction();
                    if (_activeNavKey == refreshKey) CenterScrollViewer.Offset = savedOffset;
                }
                catch { }
            }, DispatcherPriority.Background);
        }
        if (TryGetPlatformHandle()?.Handle is IntPtr hwnd && hwnd != IntPtr.Zero)
            WindowsIntegration.ApplyDarkTitleBar(hwnd, pal.IsDark);
    }

    private void WireCoreEvents()
    {
        // App-themed titlebar drag; the adjacent custom vector controls handle minimize/maximize/close.
        TitleBarDragRegion.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2) ToggleMaximize();
                else BeginMoveDrag(e);
            }
        };
        MiniDragHeader.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        BackNavBtn.Click += async (_, _) => await GoBackAsync();
        ForwardNavBtn.Click += async (_, _) => await GoForwardAsync();
        CenterScrollViewer.ScrollChanged += OnCenterScrollChanged;
        AddHandler(InputElement.PointerWheelChangedEvent, OnSmoothPointerWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerPressedEvent, OnMouseNavigationButtons, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Sidebar navigation
        NavHomeBtn.Click += async (_, _) => await NavigateToAsync("home", () => LoadHomeViewAsync(null));
        NavExploreBtn.Click += async (_, _) => await NavigateToAsync("explore", LoadExploreViewAsync);
        NavFlowBtn.Click += async (_, _) => await NavigateToAsync("flow", LoadMusicFlowViewAsync);
        NavLibraryBtn.Click += async (_, _) => await NavigateToAsync("library", () => { RenderLibraryView("overview"); return Task.CompletedTask; });
        NavLikedBtn.Click += async (_, _) => await NavigateToAsync("liked", () => { RenderLibraryView("liked"); return Task.CompletedTask; });
        NavDownloadsBtn.Click += async (_, _) => await NavigateToAsync("downloads", () => { RenderDownloadsView(); return Task.CompletedTask; });
        NavSpotifyBtn.Click += async (_, _) => await NavigateToAsync("spotify", () => { RenderSpotifyImportView(); return Task.CompletedTask; });
        NavEqBtn.Click += async (_, _) => await NavigateToAsync("eq", () => { RenderEqualizerView(); return Task.CompletedTask; });
        NavLocalBtn.Click += async (_, _) => await NavigateToAsync("local", () => { RenderLocalMusicView(); return Task.CompletedTask; });
        NavStatsBtn.Click += async (_, _) => await NavigateToAsync("stats", () => { RenderStatsWrappedView(null); return Task.CompletedTask; });
        NavSettingsBtn.Click += async (_, _) => await NavigateToAsync("settings", () => { RenderSettingsView(); return Task.CompletedTask; });
        NavAboutBtn.Click += async (_, _) => await NavigateToAsync("about", () => { RenderAboutView(); return Task.CompletedTask; });

        CreatePlaylistSideBtn.Click += (_, _) => OpenNewPlaylistModal();
        CommandPaletteOpenBtn.Click += (_, _) => OpenCommandPaletteModal();
        SleepTimerTopBtn.Click += (_, _) => OpenSleepTimerModal();
        AccountTopBtn.Click += (_, _) => OpenAccountSignInModal();
        ToggleQueueTopBtn.Click += (_, _) => ToggleRightDock(!_dockOpen);
        ToggleLeftSidebarBtn.Click += (_, _) => ToggleLeftSidebar();
        MiniModeBtn.Click += (_, _) => ToggleMiniPlayerMode();
        MinimizeWindowBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeWindowBtn.Click += (_, _) => ToggleMaximize();
        CloseWindowBtn.Click += (_, _) => Close();
        ResourceSaverBtn.Click += (_, _) => CycleResourceSaverPolicy();
        BuildResourceSaverContextMenu();
        ExitMiniBtn.Click += (_, _) => ToggleMiniPlayerMode();

        // Optional 10-second seek buttons
        SkipBack10Btn.Click += (_, _) => _audio.Seek(Math.Max(0, _audio.PositionSeconds - 10));
        SkipFwd10Btn.Click += (_, _) => _audio.Seek(_audio.PositionSeconds + 10);

        FsLikeBtn.Click += (_, _) =>
        {
            if (_audio.CurrentTrack is { } tr)
            {
                bool liked = _store.ToggleLike(tr, _yt);
                ShowToast(liked ? "Added to Liked Songs" : "Removed from Liked Songs");
                UpdateNowPlayingUi();
            }
        };
        FsDownloadBtn.Click += (_, _) =>
        {
            if (_audio.CurrentTrack is { IsLocal: false } tr)
            {
                if (_store.IsDownloaded(tr.Id)) ShowToast("Already downloaded offline");
                else { _store.EnqueueDownload(tr, _resolver, _yt.SharedHttp); ShowToast($"Downloading \"{tr.Title}\"..."); }
            }
        };

        // Search input + Clear button + Live suggestions
        GlobalSearchBox.TextChanged += OnSearchBoxTextChanged;
        ClearSearchBtn.Click += async (_, _) =>
        {
            GlobalSearchBox.Text = "";
            ClearSearchBtn.IsVisible = false;
            SetSearchSuggestionsVisible(false);
            if (_activeNavKey == "search") await GoBackAsync();
            BackNavBtn.Focus();
        };
        GlobalSearchBox.KeyDown += async (_, e) =>
        {
            if (SearchSuggestPopup.IsVisible && (e.Key is Key.Down or Key.Up) && _searchSuggestionButtons.Count > 0)
            {
                MoveSearchSuggestionSelection(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter)
            {
                var q = SearchSuggestPopup.IsVisible && _searchSuggestionIndex >= 0 && _searchSuggestionIndex < _searchSuggestionTexts.Count
                    ? _searchSuggestionTexts[_searchSuggestionIndex]
                    : GlobalSearchBox.Text?.Trim() ?? "";
                SetSearchSuggestionsVisible(false);
                if (!string.IsNullOrEmpty(q)) await CommitSearchAsync(q);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SetSearchSuggestionsVisible(false);
                _searchSuggestionIndex = -1;
                e.Handled = true;
            }
        };
        GlobalSearchBox.LostFocus += async (_, _) =>
        {
            await Task.Delay(120);
            SetSearchSuggestionsVisible(false);
            _searchSuggestionIndex = -1;
        };
        PointerPressed += (_, e) =>
        {
            if (!SearchSuggestPopup.IsVisible) return;
            Control? source = e.Source as Control;
            bool withinSearch = false;
            while (source is not null)
            {
                if (ReferenceEquals(source, GlobalSearchBox) || ReferenceEquals(source, SearchSuggestPopup)) { withinSearch = true; break; }
                source = source.Parent as Control;
            }
            if (!withinSearch) { SetSearchSuggestionsVisible(false); _searchSuggestionIndex = -1; }
        };

        // Right panel tabs follow the Spotify layout. Lyrics stay on the player-bar button.
        DockTabNowBtn.Click += (_, _) => SetDockTab("now", ensureOpen: true);
        DockTabQueueBtn.Click += (_, _) => SetDockTab("queue", ensureOpen: true);
        DockTabDnaBtn.Click += (_, _) => SetDockTab("dna", ensureOpen: true);
        CloseRightDockBtn.Click += (_, _) => ToggleRightDock(false);

        ToggleLyricsBarBtn.Click += (_, _) => ToggleOrSwitchDock("lyrics");
        StartRadioBarBtn.Click += async (_, _) =>
        {
            if (_audio.CurrentTrack is { IsLocal: false } tr)
            {
                ShowToast($"Starting Song Radio from \"{tr.Title}\"...");
                var r = await _yt.GetRadioQueueAsync(tr.Id);
                r = r.Where(x => (!_store.Settings.HideVideoSongs || !x.IsVideo) && !_store.IsTrackExcluded(x)).ToList();
                if (r.Count > 0) await _audio.PlayQueueAsync(r, 0, autoRadioIfSingle: false);
            }
            else
            {
                ToggleOrSwitchDock("dna");
            }
        };

        // Transport controls
        PlayPauseBtn.Click += async (_, _) => await HandlePlayPauseActionAsync();
        FsPlayPauseBtn.Click += async (_, _) => await HandlePlayPauseActionAsync();
        MiniPlayBtn.Click += async (_, _) => await HandlePlayPauseActionAsync();

        PrevBtn.Click += async (_, _) => await _audio.PreviousAsync();
        FsPrevBtn.Click += async (_, _) => await _audio.PreviousAsync();
        MiniPrevBtn.Click += async (_, _) => await _audio.PreviousAsync();

        NextBtn.Click += async (_, _) => await _audio.NextAsync();
        FsNextBtn.Click += async (_, _) => await _audio.NextAsync();
        MiniNextBtn.Click += async (_, _) => await _audio.NextAsync();

        ShuffleBtn.Click += (_, _) => _audio.ToggleShuffle();
        RepeatBtn.Click += (_, _) => _audio.CycleRepeat();
        MuteBtn.Click += (_, _) => _audio.ToggleMute();

        NowMoreBtn.Click += (_, ev) =>
        {
            ev.Handled = true;
            if (_audio.CurrentTrack is { } playing) OpenTrackContextMenu(playing, null, NowMoreBtn, PlacementMode.Bottom);
        };

        NowLikeBtn.Click += (_, _) =>
        {
            if (_audio.CurrentTrack is { } tr)
            {
                bool liked = _store.ToggleLike(tr, _yt);
                ShowToast(liked ? "Added to Liked Songs" : "Removed from Liked Songs");
                UpdateNowPlayingUi();
                if (_dockOpen && _dockTab == "now") RenderRightDock();
            }
        };

        NowDownloadBtn.Click += (_, _) =>
        {
            if (_audio.CurrentTrack is { IsLocal: false } tr)
            {
                if (_store.IsDownloaded(tr.Id))
                    ShowToast("Already downloaded for offline playback");
                else
                {
                    _store.EnqueueDownload(tr, _resolver, _yt.SharedHttp);
                    ShowToast($"Downloading \"{tr.Title}\"...");
                }
            }
        };

        NowPlayingArtistBtn.Click += async (_, _) =>
        {
            var a = _audio.CurrentTrack?.Artists.FirstOrDefault();
            if (!string.IsNullOrEmpty(a?.Id))
                await NavigateToAsync("artist:" + a.Id, () => LoadArtistDetailAsync(a.Id!));
            else if (!string.IsNullOrEmpty(a?.Name))
                await NavigateToAsync("search", () => ExecuteSearchViewAsync(a.Name, "artists"));
        };

        NowArtExpandBtn.Click += (_, _) => ToggleFullscreenOverlay(true);
        FullscreenPlayerBtn.Click += (_, _) => ToggleFullscreenOverlay(!FullscreenOverlay.IsVisible);
        FsLyricsRefreshBtn.Click += async (_, _) => await ForceRefreshLyricsAsync();
        FsLyricsEnglishBtn.Click += async (_, _) => await TranslateCurrentLyricsToEnglishAsync();
        FsLyricsLatinBtn.Click += (_, _) => ToggleLatinLyrics();
        CloseFullscreenBtn.Click += (_, _) => ToggleFullscreenOverlay(false);

        ModalBackdrop.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, ModalBackdrop))
                ModalBackdrop.IsVisible = false;
        };

        // Seek & Volume sliders (throttled so dragging never thrashes the Opus decoder)
        DateTime lastUserSeekUtc = DateTime.MinValue;
        void HandleUserSliderSeek(double pctVal, bool force = false)
        {
            if (_seekingByCode || _audio.DurationSeconds <= 1) return;
            var now = DateTime.UtcNow;
            if (!force && (now - lastUserSeekUtc).TotalMilliseconds < 160) return;
            lastUserSeekUtc = now;
            _audio.Seek(pctVal / 100.0 * _audio.DurationSeconds);
            if (force) RefreshLyricsAfterSeek();
        }

        SeekSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == SeekProgressControl.ValueProperty) HandleUserSliderSeek(SeekSlider.Value);
        };
        SeekSlider.SeekCommitted += (_, _) => HandleUserSliderSeek(SeekSlider.Value, force: true);
        FsSeekSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == SeekProgressControl.ValueProperty) HandleUserSliderSeek(FsSeekSlider.Value);
        };
        FsSeekSlider.SeekCommitted += (_, _) => HandleUserSliderSeek(FsSeekSlider.Value, force: true);
        MiniSeekSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == SeekProgressControl.ValueProperty) HandleUserSliderSeek(MiniSeekSlider.Value);
        };
        MiniSeekSlider.SeekCommitted += (_, _) => HandleUserSliderSeek(MiniSeekSlider.Value, force: true);

        VolumeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == SeekProgressControl.ValueProperty && !_volByCode)
                _audio.SetVolume(VolumeSlider.Value);
        };

        // Engine & Store callbacks
        _audio.StateChanged += () => Dispatcher.UIThread.Post(UpdateNowPlayingUi);
        _audio.TrackChanged += tr => Dispatcher.UIThread.Post(() => OnTrackChangedUi(tr));
        _audio.ToastRequested += msg => Dispatcher.UIThread.Post(() => ShowToast(msg));

        _store.LibraryChanged += () => Dispatcher.UIThread.Post(() =>
        {
            RefreshSidebarBadgesAndPlaylists();
            UpdateNowPlayingUi();
            if (_activeNavKey == "library" && _currentLibraryTab == "history")
                UpdateHistoryTrackRowsInPlace();
        });
        _store.DownloadsChanged += () => Dispatcher.UIThread.Post(() =>
        {
            DownloadsCountBadge.Text = _store.Downloads.Count(d => d.Status == "done").ToString();
            UpdateNowPlayingUi();
            if (_activeNavKey == "downloads") RenderDownloadsView();
        });

        KeyDown += OnGlobalWindowKeyDown;
    }

    private async void OnGlobalWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ModalBackdrop.IsVisible) { ModalBackdrop.IsVisible = false; e.Handled = true; return; }
            if (FullscreenOverlay.IsVisible) { ToggleFullscreenOverlay(false); e.Handled = true; return; }
            if (SearchSuggestPopup.IsVisible)
            {
                SetSearchSuggestionsVisible(false);
                _searchSuggestionIndex = -1;
                e.Handled = true;
                return;
            }
            if (GlobalSearchBox.IsFocused || _activeNavKey == "search")
            {
                GlobalSearchBox.Text = "";
                ClearSearchBtn.IsVisible = false;
                SetSearchSuggestionsVisible(false);
                _searchSuggestionIndex = -1;
                e.Handled = true;
                return;
            }
        }

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (ctrl && e.Key == Key.K) { OpenCommandPaletteModal(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.F) { GlobalSearchBox.Focus(); GlobalSearchBox.SelectAll(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Q) { ToggleRightDock(!_dockOpen); e.Handled = true; return; }
        if (ctrl && e.Key == Key.OemComma) { await NavigateToAsync("settings", () => { RenderSettingsView(); return Task.CompletedTask; }); e.Handled = true; return; }
        if (ctrl && e.Key == Key.M) { ToggleMiniPlayerMode(); e.Handled = true; return; }
        if (e.Key == Key.F11) { ToggleFullscreenOverlay(!FullscreenOverlay.IsVisible); e.Handled = true; return; }
        if (alt && e.Key == Key.Left) { await GoBackAsync(); e.Handled = true; return; }
        if (alt && e.Key == Key.Right) { await GoForwardAsync(); e.Handled = true; return; }

        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;

        switch (e.Key)
        {
            case Key.Space:
                await HandlePlayPauseActionAsync();
                e.Handled = true;
                break;
            case Key.F:
                ToggleFullscreenOverlay(!FullscreenOverlay.IsVisible);
                e.Handled = true;
                break;
            case Key.Right when ctrl:
                await _audio.NextAsync();
                e.Handled = true;
                break;
            case Key.Left when ctrl:
                await _audio.PreviousAsync();
                e.Handled = true;
                break;
            case Key.Right:
                _audio.Seek(_audio.PositionSeconds + 5);
                e.Handled = true;
                break;
            case Key.Left:
                _audio.Seek(Math.Max(0, _audio.PositionSeconds - 5));
                e.Handled = true;
                break;
            case Key.M:
                _audio.ToggleMute();
                e.Handled = true;
                break;
            case Key.S:
                _audio.ToggleShuffle();
                e.Handled = true;
                break;
            case Key.R:
                _audio.CycleRepeat();
                e.Handled = true;
                break;
            case Key.L:
                if (_audio.CurrentTrack is { } tr)
                {
                    bool liked = _store.ToggleLike(tr, _yt);
                    ShowToast(liked ? "Liked song" : "Unliked song");
                    UpdateNowPlayingUi();
                }
                e.Handled = true;
                break;
        }
    }

    private void EnsureVisualMotionTimerRunning()
    {
        _visualMotionTimer.Interval = AnimationFrameInterval;
        if (IsVisible && !_visualMotionTimer.IsEnabled)
            _visualMotionTimer.Start();
    }

    private void OnVisualMotionTick(object? sender, EventArgs e)
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            _visualMotionTimer.Stop();
            return;
        }

        var now = DateTime.UtcNow;
        bool active = PlayPauseScallop.AdvanceAnimation(now)
            | FsScallop.AdvanceAnimation(now)
            | MiniScallop.AdvanceAnimation(now);
        bool animatedSlider = (_store.Settings.PlayerSliderStyle is "Wavy" or "Squiggly")
            && (_audio.IsPlaying || _audio.IsLoading || _streamBuffering);
        if (animatedSlider || _audio.IsLoading || _streamBuffering)
        {
            SeekSlider.InvalidateVisual();
            FsSeekSlider.InvalidateVisual();
            MiniSeekSlider.InvalidateVisual();
            active = true;
        }
        if (!active) _visualMotionTimer.Stop();
    }

    // --------------------------------------------------------- Adaptive playback and UI-maintenance tick

    private void OnUiFrameTick(object? sender, EventArgs e)
    {
        _audio.TickMaintenance();
        if (!IsVisible || WindowState == WindowState.Minimized) return;
        var desiredInterval = DesiredUiInterval;
        if (_uiTimer.Interval != desiredInterval) _uiTimer.Interval = desiredInterval;

        bool loadingTrack = _audio.IsLoading;
        double pos = loadingTrack ? 0 : _audio.PositionSeconds;
        double dur = _audio.DurationSeconds;
        string? playingId = _audio.CurrentTrack?.Id;
        DateTime tickUtc = DateTime.UtcNow;
        if (loadingTrack || !_audio.IsPlaying || _audio.CurrentTrack is not { IsLocal: false } || playingId != _bufferMonitorTrackId)
        {
            _bufferMonitorTrackId = playingId;
            _bufferMonitorPosition = pos;
            _bufferMonitorAdvancedUtc = tickUtc;
            _streamBuffering = false;
        }
        else if (pos < _bufferMonitorPosition - 0.35)
        {
            // A backward seek is a timeline discontinuity, not a network stall. Reset the
            // baseline so the old, larger timestamp cannot keep the spinner alive until caught up.
            _bufferMonitorPosition = pos;
            _bufferMonitorAdvancedUtc = tickUtc;
            _streamBuffering = false;
        }
        else if (pos > _bufferMonitorPosition + 0.025)
        {
            _bufferMonitorPosition = pos;
            _bufferMonitorAdvancedUtc = tickUtc;
            _streamBuffering = false;
        }
        else
        {
            // Distinguish a user pause from an actual stream stall. Only show loading while the
            // transport is still playing and the network-backed timeline has stopped advancing.
            _streamBuffering = dur <= 0 || pos < dur - 0.75
                ? (tickUtc - _bufferMonitorAdvancedUtc).TotalMilliseconds >= 550
                : false;
        }

        _seekingByCode = true;
        double pct = !loadingTrack && dur > 0.5 ? Math.Clamp(pos / dur * 100.0, 0, 100) : 0;
        SeekSlider.Value = pct;
        FsSeekSlider.Value = pct;
        MiniSeekSlider.Value = pct;
        _seekingByCode = false;
        SeekSlider.IsPlaying = FsSeekSlider.IsPlaying = MiniSeekSlider.IsPlaying = _audio.IsPlaying;
        PlayPauseScallop.IsPlaying = FsScallop.IsPlaying = MiniScallop.IsPlaying = _audio.IsPlaying;
        PlayPauseScallop.IsLoading = FsScallop.IsLoading = MiniScallop.IsLoading = loadingTrack;

        bool showStreamLoading = loadingTrack || _streamBuffering;
        SeekSlider.IsBuffering = FsSeekSlider.IsBuffering = MiniSeekSlider.IsBuffering = showStreamLoading;
        if (showStreamLoading || _audio.IsPlaying) EnsureVisualMotionTimerRunning();
        ToolTip.SetTip(SeekSlider, _streamBuffering ? "Waiting for stream data" : (loadingTrack ? "Loading track" : "Seek"));
        ToolTip.SetTip(FsSeekSlider, _streamBuffering ? "Waiting for stream data" : (loadingTrack ? "Loading track" : "Seek"));
        ToolTip.SetTip(MiniSeekSlider, _streamBuffering ? "Waiting for stream data" : (loadingTrack ? "Loading track" : "Seek"));
        SeekSlider.IsEnabled = !loadingTrack;
        FsSeekSlider.IsEnabled = !loadingTrack;
        MiniSeekSlider.IsEnabled = !loadingTrack;
        PosTimeText.Text = loadingTrack ? "…" : TrackItem.FormatSeconds(pos);
        DurTimeText.Text = TrackItem.FormatSeconds(dur);
        FsPosText.Text = PosTimeText.Text;
        FsDurText.Text = DurTimeText.Text;
        UpdateDiscordActivityPreview();

        if (_audio.SleepRemaining is { } rem)
        {
            SleepTimerTopLabel.IsVisible = true;
            SleepTimerTopLabel.Text = $"{(int)rem.TotalMinutes}:{rem.Seconds:D2}";
        }
        else
        {
            SleepTimerTopLabel.IsVisible = false;
        }

        bool spectrumNeedsDecay = !MiniBarSpectrum.IsSettled
            || (_flowSpectrum is { IsVisible: true, IsSettled: false })
            || (_dockOpen && _dockTab == "now" && _dockCurrentArtSpectrum is { IsVisible: true, IsSettled: false })
            || (FullscreenOverlay.IsVisible && (!FsSpectrum.IsSettled || !FsArtSpectrum.IsSettled));
        if (_audio.IsPlaying || spectrumNeedsDecay)
        {
            _audio.CopySpectrum32(_fftScratch);
            if (MiniBarSpectrum.IsVisible) MiniBarSpectrum.UpdateBands(_fftScratch, _audio.IsPlaying);
            if (_flowSpectrum is { IsVisible: true } flowSpectrum) flowSpectrum.UpdateBands(_fftScratch, _audio.IsPlaying);
            if (_dockOpen && _dockTab == "now" && _dockCurrentArtSpectrum is { IsVisible: true } dockSpectrum)
            {
                dockSpectrum.UpdateBands(_fftScratch, _audio.IsPlaying);
                if (_dockCurrentArtSpectrumPlate is not null)
                    _dockCurrentArtSpectrumPlate.IsVisible = _audio.IsPlaying;
            }
            if (FullscreenOverlay.IsVisible)
            {
                if (FsSpectrum.IsVisible || !FsSpectrum.IsSettled) FsSpectrum.UpdateBands(_fftScratch, _audio.IsPlaying);
                if (FsArtSpectrum.IsVisible || !FsArtSpectrum.IsSettled) FsArtSpectrum.UpdateBands(_fftScratch, _audio.IsPlaying);
                if (_audio.CurrentTrack is not null) FsArtSpectrumPlate.IsVisible = _audio.IsPlaying;
            }
        }
        string? activeTrackId = _audio.CurrentTrack?.Id;
        for (int i = _trackArtSpectra.Count - 1; i >= 0; i--)
        {
            var entry = _trackArtSpectra[i];
            if (!entry.Overlay.TryGetTarget(out var overlay)
                || !entry.Visualizer.TryGetTarget(out var visualizer)
                || overlay.GetVisualRoot() is null)
            {
                _trackArtSpectra.RemoveAt(i);
                continue;
            }
            bool show = _audio.IsPlaying && string.Equals(entry.TrackId, activeTrackId, StringComparison.Ordinal);
            if (overlay.IsVisible != show) overlay.IsVisible = show;
            if (show) visualizer.UpdateBands(_fftScratch, true);
        }

        SyncActiveLyricHighlight(pos);
    }

    private void SyncActiveLyricHighlight(double rawPosSec)
    {
        if (_audio.IsLoading)
        {
            SetMusicFlowLyrics("Waiting for audio…", "");
            if (_flowLyricStatus is not null) _flowLyricStatus.IsVisible = false;
            return;
        }
        var data = _currentLyrics;
        if (data is null || data.Lines.Count == 0)
        {
            SetMusicFlowLyrics(data is null ? "Finding lyrics…" : "Lyrics not found yet", "");
            if (_flowLyricStatus is not null) _flowLyricStatus.IsVisible = false;
            return;
        }
        if (!data.IsSynced)
        {
            SetMusicFlowLyrics(GetDisplayLyricText(data.Lines[0]), data.Lines.Count > 1 ? GetDisplayLyricText(data.Lines[1]) : "");
            if (_flowLyricStatus is not null)
            {
                _flowLyricStatus.Text = "Plain lyrics · not time-synced";
                _flowLyricStatus.IsVisible = true;
            }
            return;
        }
        if (_flowLyricStatus is not null) _flowLyricStatus.IsVisible = false;

        double offset = 0;
        if (_audio.CurrentTrack is { } tr) _store.LyricOffsets.TryGetValue(tr.Id, out offset);
        double adj = rawPosSec + offset;

        // Binary-search the absolute player clock on every tick. Seeking, resuming, and skipping
        // jump directly to the correct row; brief per-line transitions only run when the active row changes.
        int lo = 0, hi = data.Lines.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (data.Lines[mid].TimeSec is double ts && ts <= adj) lo = mid + 1;
            else hi = mid;
        }
        int active = lo - 1;
        string flowLine1 = active >= 0 ? GetDisplayLyricText(data.Lines[active]) : "";
        string flowLine2 = active + 1 < data.Lines.Count ? GetDisplayLyricText(data.Lines[active + 1]) : "";
        SetMusicFlowLyrics(flowLine1, flowLine2);
        UpdateWordSyncAnimations(data, active, adj);

        if (active == _lastActiveLyricIdx) return;
        _lastActiveLyricIdx = active;
        var palette = ThemeManager.GetPalette(_store.Settings.Theme);
        var dockMuted = new SolidColorBrush(palette.TextMuted);
        var fsDim = new SolidColorBrush(Color.Parse("#D8FFFFFF"));
        var fsNext = new SolidColorBrush(Color.Parse("#F2FFFFFF"));

        // Reapply distance-based dimming when the active row moves; this is event-driven, not a per-frame pulse.
        for (int i = 0; i < _dockLyricBlocks.Count; i++) ApplyDockLyricStyle(i, active, dockMuted);
        for (int i = 0; i < _fsLyricBlocks.Count; i++) ApplyFullscreenLyricStyle(i, active, fsDim, fsNext);

        if (active >= 0 && active < _dockLyricBlocks.Count) KeepLyricInComfortBand(_dockLyricBlocks[active], _dockLyricsScroller);
        if (active >= 0 && active < _fsLyricBlocks.Count && FullscreenOverlay.IsVisible) KeepLyricInComfortBand(_fsLyricBlocks[active], FsLyricsScroller);
    }

    private void RefreshLyricsAfterSeek()
    {
        _lastActiveLyricIdx = -1;
        if (!_audio.IsLoading) SyncActiveLyricHighlight(_audio.PositionSeconds);
    }

    private string GetDisplayLyricText(LyricLine line)
    {
        if (_lyricsShowRomanized)
        {
            line.RomanizedText ??= string.IsNullOrWhiteSpace(line.Text) ? line.Text : line.Text.Transliterate();
            return line.RomanizedText;
        }
        return string.IsNullOrWhiteSpace(line.TranslatedText) ? line.Text : line.TranslatedText;
    }

    private List<Run>? BuildWordSyncRuns(TextBlock block, LyricLine line)
    {
        if (_currentLyrics?.HasWordSync != true || _lyricsShowRomanized || !string.IsNullOrWhiteSpace(line.TranslatedText) || line.Words.Count < 2)
            return null;
        var inlines = block.Inlines;
        if (inlines is null) return null;
        inlines.Clear();
        block.Text = string.Empty;
        var runs = new List<Run>(line.Words.Count);
        foreach (var word in line.Words)
        {
            var run = new Run(word.Text);
            runs.Add(run);
            inlines.Add(run);
        }
        return runs;
    }

    private void ApplyWordSyncRuns(List<Run>? runs, IReadOnlyList<LyricWord> words, TextBlock block, double position, bool lineIsActive)
    {
        if (runs is null || words.Count == 0) return;
        int wordIndex = -1;
        if (lineIsActive)
            for (int i = 0; i < words.Count; i++)
            {
                if (words[i].TimeSec > position) break;
                wordIndex = i;
            }
        var baseBrush = block.Foreground ?? Brushes.White;
        for (int i = 0; i < runs.Count; i++)
        {
            runs[i].Foreground = !lineIsActive ? baseBrush
                : i < wordIndex ? ResBrush("AccentBrush")
                : i == wordIndex ? Brushes.White
                : ResBrush("TextMutedBrush");
        }
    }

    private int _lastWordSyncActiveLine = -1;

    private void UpdateWordSyncAnimations(LyricsData data, int active, double position)
    {
        if (_lastWordSyncActiveLine != active)
        {
            int previous = _lastWordSyncActiveLine;
            if (previous >= 0 && previous < data.Lines.Count)
            {
                if (previous < _dockLyricWordRuns.Count) ApplyWordSyncRuns(_dockLyricWordRuns[previous], data.Lines[previous].Words, _dockLyricBlocks[previous], position, false);
                if (previous < _fsLyricWordRuns.Count) ApplyWordSyncRuns(_fsLyricWordRuns[previous], data.Lines[previous].Words, _fsLyricBlocks[previous], position, false);
            }
            _lastWordSyncActiveLine = active;
        }
        if (active >= 0 && active < data.Lines.Count)
        {
            if (active < _dockLyricWordRuns.Count) ApplyWordSyncRuns(_dockLyricWordRuns[active], data.Lines[active].Words, _dockLyricBlocks[active], position, true);
            if (active < _fsLyricWordRuns.Count) ApplyWordSyncRuns(_fsLyricWordRuns[active], data.Lines[active].Words, _fsLyricBlocks[active], position, true);
        }

        if (active >= 0 && active < data.Lines.Count && _flowLyricLine1 is not null)
        {
            var line = data.Lines[active];
            bool hasWordTiming = _currentLyrics?.HasWordSync == true && !_lyricsShowRomanized && string.IsNullOrWhiteSpace(line.TranslatedText) && line.Words.Count >= 2;
            if (hasWordTiming)
            {
                if (_flowWordSyncLine != active)
                {
                    var runs = BuildWordSyncRuns(_flowLyricLine1, line);
                    _flowLyricWordRuns.Clear();
                    if (runs is not null) _flowLyricWordRuns.AddRange(runs);
                    _flowWordSyncLine = active;
                }
                ApplyWordSyncRuns(_flowLyricWordRuns, line.Words, _flowLyricLine1, position, true);
                return;
            }
        }
        if (_flowWordSyncLine >= 0 && _flowLyricLine1 is not null)
        {
            _flowLyricLine1.Inlines?.Clear();
            _flowLyricLine1.Text = active >= 0 && active < data.Lines.Count ? GetDisplayLyricText(data.Lines[active]) : "";
            _flowLyricWordRuns.Clear();
            _flowWordSyncLine = -1;
        }
    }

    private void SetMusicFlowLyrics(string first, string second)
    {
        if (_flowLyricLine1 is not null && _flowDisplayedLyricLine1 != first)
        {
            _flowDisplayedLyricLine1 = first;
            _flowLyricLine1.Opacity = 1;
            _flowLyricLine1.FontSize = 25;
            _flowLyricLine1.Inlines?.Clear();
            _flowLyricLine1.Text = first;
        }
        if (_flowLyricLine2 is not null && _flowDisplayedLyricLine2 != second)
        {
            _flowDisplayedLyricLine2 = second;
            _flowLyricLine2.Opacity = 0.82;
            _flowLyricLine2.FontSize = 16;
            _flowLyricLine2.Text = second;
        }
    }

    private void ApplyDockLyricStyle(int index, int active, IBrush muted)
    {
        if (index < 0 || index >= _dockLyricBlocks.Count) return;
        var block = _dockLyricBlocks[index];
        int distance = active < 0 ? int.MaxValue : Math.Abs(index - active);
        bool current = distance == 0;
        var palette = ThemeManager.GetPalette(_store.Settings.Theme);
        block.Foreground = current ? new SolidColorBrush(palette.TextPrimary) : muted;
        block.Background = Brushes.Transparent;
        block.FontWeight = current ? FontWeight.ExtraBold : FontWeight.Medium;
        block.FontSize = current ? 18 : distance == 1 ? 14.5 : 13.5;
        block.Effect = null;
        block.Opacity = current ? 1.0 : distance == 1 ? 0.82 : distance == 2 ? 0.58 : distance == 3 ? 0.40 : 0.28;
    }

    private void ApplyFullscreenLyricStyle(int index, int active, IBrush dim, IBrush next)
    {
        if (index < 0 || index >= _fsLyricBlocks.Count) return;
        var block = _fsLyricBlocks[index];
        int distance = active < 0 ? int.MaxValue : Math.Abs(index - active);
        bool current = distance == 0;
        var palette = ThemeManager.GetPalette(_store.Settings.Theme);
        var accent = palette.Accent;
        block.Foreground = current ? Brushes.White : distance == 1 ? next : dim;
        block.Background = Brushes.Transparent;
        block.FontWeight = current ? FontWeight.ExtraBold : FontWeight.Bold;
        block.FontSize = current ? 32 : distance == 1 ? 22 : distance == 2 ? 19 : 17;
        block.Effect = current
            ? new DropShadowEffect { Color = Color.FromArgb(145, accent.R, accent.G, accent.B), BlurRadius = 22, OffsetX = 0, OffsetY = 0, Opacity = 0.82 }
            : null;
        block.Opacity = current ? 1.0 : distance == 1 ? 0.78 : distance == 2 ? 0.55 : distance == 3 ? 0.36 : 0.22;
    }

    private async void OnTrackChangedUi(TrackItem? track)
    {
        _lyricsFetchCts?.Cancel();
        _lyricsFetchCts?.Dispose();
        _lyricsFetchCts = track is null ? null : new CancellationTokenSource();
        _currentLyrics = null;
        _currentDna = null;
        _relatedTracks = new();
        _lastActiveLyricIdx = -1;
        _lastWordSyncActiveLine = -1;
        _flowWordSyncLine = -1;
        _flowLyricWordRuns.Clear();
        _autoLyricsRetryTrackId = null;
        SetMusicFlowLyrics("Finding lyrics…", "");
        UpdateNowPlayingUi();
        if (_dockOpen) RenderRightDock();

        if (track is null)
        {
            SetMusicFlowLyrics("", "");
            NowPlayingArtImg.Source = null;
            UpdateDiscordActivityPreview();
            FsArtImg.Source = null;
            FsArtSpectrumPlate.IsVisible = false;
            FsBackdropImg.Source = null;
            MiniArtImg.Source = null;
            return;
        }

        // Start art and lyrics together so one slow thumbnail request can't delay lyric retrieval.
        var lyricsTask = _lyricsFetchCts is { } pendingLyrics
            ? FetchAndApplyLyricsAsync(track, pendingLyrics.Token)
            : Task.CompletedTask;
        var bmp = await ImageCache.LoadAsync(InnertubeParsers.UpgradeThumb(track.Thumbnail, 544), _yt.SharedHttp, _store.ArtCacheDir);
        if (_audio.CurrentTrack?.Id == track.Id)
        {
            NowPlayingArtImg.Source = bmp;
            FsArtImg.Source = bmp;
            FsBackdropImg.Source = bmp;
            MiniArtImg.Source = bmp;
            if (_activeNavKey == "flow") RefreshMusicFlowCardUi();
        }

        await lyricsTask;

        // Load Music DNA + Related tracks in parallel
        var dnaTask = _extras.GetMusicDnaAsync(track);
        var relTask = track.IsLocal
            ? Task.FromResult(new List<TrackItem>())
            : _yt.GetRadioQueueAsync(track.Id);
        try { await Task.WhenAll(dnaTask, relTask); } catch { }

        if (_audio.CurrentTrack?.Id == track.Id)
        {
            _currentDna = dnaTask.IsCompletedSuccessfully ? dnaTask.Result : null;
            _relatedTracks = relTask.IsCompletedSuccessfully
                ? relTask.Result.Where(x => x.Id != track.Id).Take(12).ToList()
                : new List<TrackItem>();
            if (_dockOpen && _dockTab == "dna")
                RenderRightDock();
            else if (_dockOpen && _dockTab == "now" && _currentDna is { } dna
                && _dockNowStack is { } nowStack && !_dockDnaCardAdded)
            {
                nowStack.Children.Add(SectionCard("About the track", dna.TrackStory));
                _dockDnaCardAdded = true;
            }
        }
    }

    private async Task TranslateCurrentLyricsToEnglishAsync()
    {
        if (_currentLyrics is not { Lines.Count: > 0 } lyrics)
        {
            ShowToast("Load lyrics before translating");
            return;
        }
        _lyricsShowRomanized = false;
        foreach (var line in lyrics.Lines) line.TranslatedText = null;
        ShowToast("Translating lyrics to English…");
        await _lyricsSvc.TranslateLyricsAsync(lyrics, "en", _store.Settings);
        PopulateFullscreenLyrics();
        if (_dockOpen && _dockTab == "lyrics") RenderRightDock();
    }

    private void ToggleLatinLyrics()
    {
        _lyricsShowRomanized = !_lyricsShowRomanized;
        PopulateFullscreenLyrics();
        if (_dockOpen && _dockTab == "lyrics") RenderRightDock();
        ShowToast(_lyricsShowRomanized ? "Latin transliteration on" : "Original lyric script restored");
    }

    private async Task ForceRefreshLyricsAsync()
    {
        if (_audio.CurrentTrack is not { } track)
        {
            ShowToast("Play a track before refreshing lyrics");
            return;
        }
        _lyricsFetchCts?.Cancel();
        _lyricsFetchCts?.Dispose();
        _lyricsFetchCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        if (FullscreenOverlay.IsVisible)
        {
            FsLyricsStack.Children.Clear();
            FsLyricsStack.Children.Add(BuildLyricsLoadingSkeleton(fullscreen: true));
        }
        ShowToast("Refreshing lyrics…");
        await FetchAndApplyLyricsAsync(track, _lyricsFetchCts.Token, forceRefresh: true);
    }

    private async Task FetchAndApplyLyricsAsync(TrackItem track, CancellationToken ct, bool forceRefresh = false)
    {
        LyricsData lyrics;
        try { lyrics = await _lyricsSvc.GetLyricsAsync(track, ct, forceRefresh); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch { lyrics = new LyricsData { VideoId = track.Id, Provider = "None" }; }
        ApplyConservativeVideoIntroAlignment(track, lyrics);
        if (!string.IsNullOrEmpty(_store.Settings.TranslateLyricsTo) && lyrics.Lines.Count > 0)
        {
            try { await _lyricsSvc.TranslateLyricsAsync(lyrics, _store.Settings.TranslateLyricsTo, _store.Settings, ct); }
            catch { }
        }
        if (ct.IsCancellationRequested || _audio.CurrentTrack?.Id != track.Id) return;
        _currentLyrics = lyrics;
        _lyricsShowRomanized = false;
        _lastActiveLyricIdx = -1;
        _lastWordSyncActiveLine = -1;
        _flowWordSyncLine = -1;
        _flowLyricWordRuns.Clear();
        PopulateFullscreenLyrics();
        if (_dockOpen && (_dockTab == "lyrics" || _dockTab == "now")) RenderRightDock();
        if (!track.IsLocal && !lyrics.IsInstrumental && (lyrics.Lines.Count == 0 || !lyrics.IsSynced) && _autoLyricsRetryTrackId != track.Id)
        {
            _autoLyricsRetryTrackId = track.Id;
            _ = RetryMissingLyricsQuietlyAsync(track, ct);
        }
    }

    internal static void ApplyConservativeVideoIntroAlignment(TrackItem track, LyricsData lyrics)
    {
        // Infer a long spoken/video intro only when the timed lyrics otherwise cover nearly a
        // complete song. This avoids title-specific constants and leaves ordinary audio untouched.
        if (!track.IsVideo || !lyrics.IsSynced || lyrics.Lines.Count < 12 || track.DurationSeconds < 90
            || Math.Abs(lyrics.AppliedTimingOffsetSeconds) > 0.05 || lyrics.TimingMap.Count > 0) return;

        var timed = lyrics.Lines.Where(line => line.TimeSec.HasValue)
            .Select(line => line.TimeSec!.Value).Where(time => double.IsFinite(time))
            .OrderBy(time => time).ToArray();
        if (timed.Length < 12 || timed[0] >= 24) return;

        double[] tailGaps = timed.Zip(timed.Skip(1), (a, b) => b - a)
            .Where(gap => gap >= 0.2 && gap <= 15).TakeLast(6).OrderBy(gap => gap).ToArray();
        double typicalTail = tailGaps.Length == 0 ? 3.5 : tailGaps[tailGaps.Length / 2];
        typicalTail = Math.Clamp(typicalTail, 1.25, 5.5);
        double coveredEnd = timed[^1] + typicalTail;
        double apparentIntro = track.DurationSeconds - coveredEnd;
        double coverage = coveredEnd / track.DurationSeconds;

        // Require a material gap and a lyric span consistent with an almost full-length song.
        // Short/incomplete lyric excerpts and providers already timed to the full video are left alone.
        if (apparentIntro < 20 || apparentIntro > 90 || coverage < 0.62 || coverage > 0.94
            || timed[^1] - timed[0] < track.DurationSeconds * 0.58) return;

        double correction = apparentIntro;
        foreach (var line in lyrics.Lines)
        {
            if (line.TimeSec.HasValue) line.TimeSec += correction;
            if (line.EndSec.HasValue) line.EndSec += correction;
            foreach (var word in line.Words) word.TimeSec += correction;
        }
        lyrics.AppliedTimingOffsetSeconds += correction;
    }

    private async Task RetryMissingLyricsQuietlyAsync(TrackItem track, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.5), ct);
            if (!ct.IsCancellationRequested && _audio.CurrentTrack?.Id == track.Id)
                await FetchAndApplyLyricsAsync(track, ct, forceRefresh: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task RetryLyricsForCurrentTrackAsync()
    {
        if (_audio.CurrentTrack is not { } track) return;
        _lyricsFetchCts?.Cancel();
        _lyricsFetchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _lyricsFetchCts = cts;
        _currentLyrics = null;
        _lastActiveLyricIdx = -1;
        _lastWordSyncActiveLine = -1;
        _flowWordSyncLine = -1;
        _flowLyricWordRuns.Clear();
        PopulateFullscreenLyrics();
        if (_dockOpen && _dockTab == "lyrics") RenderRightDock();
        ShowToast($"Retrying lyrics for {track.Title}…");
        await FetchAndApplyLyricsAsync(track, cts.Token, forceRefresh: true);
    }

    private void UpdateNowPlayingUi()
    {
        var tr = _audio.CurrentTrack;
        var pal = ThemeManager.GetPalette(_store.Settings.Theme);
        var accent = pal.Accent;

        UpdateDiscordActivityPreview();
        NowPlayingTitleText.Text = tr?.Title ?? "Nothing playing";
        NowPlayingArtistText.Text = _audio.IsLoading
            ? "Buffering stream..."
            : (_audio.LastError is { Length: > 0 } err ? $"Stream error: {err}" : (tr?.ArtistNames ?? "Pick something to start"));
        FsTitleText.Text = tr?.Title ?? "Nothing playing";
        FsArtistText.Text = tr?.ArtistNames ?? "";
        FsHeaderSubText.Text = tr?.Album?.Name ?? tr?.ArtistNames ?? "Echo Music";
        MiniTitleText.Text = tr?.Title ?? "Echo Music";
        MiniArtistText.Text = tr?.ArtistNames ?? "Compact Mode";
        Title = tr is null ? "Echo Music" : $"{tr.Title} • {tr.ArtistNames} - Echo Music";

        PlayPauseScallop.IsPlaying = _audio.IsPlaying;
        PlayPauseScallop.IsLoading = _audio.IsLoading;
        PlayPauseScallop.InvalidateVisual();

        FsScallop.IsPlaying = _audio.IsPlaying;
        FsScallop.IsLoading = _audio.IsLoading;
        FsScallop.InvalidateVisual();

        MiniScallop.IsPlaying = _audio.IsPlaying;
        MiniScallop.IsLoading = _audio.IsLoading;
        EnsureVisualMotionTimerRunning();

        if (_store.Settings.ShowStreamQualityLabel && _audio.CurrentStreamInfo is { } si)
        {
            StreamQualityPill.IsVisible = true;
            var codecShort = si.Codec.Contains("opus", StringComparison.OrdinalIgnoreCase) ? "OPUS" : "AAC";
            StreamQualityText.Text = $"{codecShort} {Math.Max(48, si.Bitrate / 1000)}K";
        }
        else if (_store.Settings.ShowStreamQualityLabel && tr?.IsLocal == true)
        {
            StreamQualityPill.IsVisible = true;
            StreamQualityText.Text = Path.GetExtension(tr.LocalFilePath ?? "").TrimStart('.').ToUpperInvariant();
        }
        else if (_store.Settings.ShowStreamQualityLabel && tr is not null && _store.IsDownloaded(tr.Id))
        {
            StreamQualityPill.IsVisible = true;
            StreamQualityText.Text = "OFFLINE";
        }
        else
        {
            StreamQualityPill.IsVisible = false;
        }

        bool liked = tr is not null && _store.IsLiked(tr.Id);
        NowLikeIcon.Data = liked ? Icons.HeartFilled : Icons.HeartOutline;
        NowLikeIcon.Foreground = liked ? new SolidColorBrush(accent) : new SolidColorBrush(pal.TextSecondary);
        FsLikeIcon.Data = liked ? Icons.HeartFilled : Icons.HeartOutline;
        FsLikeIcon.Foreground = liked ? new SolidColorBrush(accent) : Brushes.White;

        bool dl = tr is not null && _store.IsDownloaded(tr.Id);
        NowDownloadIcon.Data = dl ? Icons.CheckCircle : Icons.Download;
        NowDownloadIcon.Foreground = dl ? new SolidColorBrush(accent) : new SolidColorBrush(pal.TextSecondary);

        ShuffleIcon.Foreground = _audio.ShuffleEnabled ? new SolidColorBrush(accent) : new SolidColorBrush(pal.TextSecondary);
        RepeatIcon.Data = _audio.Repeat == RepeatMode.One ? Icons.RepeatOne : Icons.Repeat;
        RepeatIcon.Foreground = _audio.Repeat != RepeatMode.Off ? new SolidColorBrush(accent) : new SolidColorBrush(pal.TextSecondary);

        VolumeIcon.Data = _audio.IsMuted || _store.Settings.Volume <= 0.005 ? Icons.VolumeMute : Icons.VolumeHigh;
        _volByCode = true;
        VolumeSlider.Value = _store.Settings.Volume;
        _volByCode = false;

        if (_dockOpen && _dockTab == "queue")
            RenderRightDock();
    }

    internal async Task HandlePlayPauseActionAsync()
    {
        if (_audio.CurrentTrack is not null)
        {
            _audio.TogglePlayPause();
            return;
        }

        var seeds = _cachedHome?.Shelves.SelectMany(s => s.Tracks).Take(20).ToList();
        if (seeds is null || seeds.Count == 0)
            seeds = _store.LikedSongs.Take(20).ToList();
        if (seeds.Count == 0)
        {
            ShowToast("Loading top songs to start playback...");
            try
            {
                var sr = await _yt.SearchAsync("top hits", "songs");
                seeds = sr.Tracks.Take(20).ToList();
            }
            catch { }
        }
        if (seeds.Count > 0)
            await _audio.PlayQueueAsync(seeds, 0, autoRadioIfSingle: true);
    }

    // --------------------------------------------------------- Navigation Engine

    internal async Task NavigateToAsync(string navKey, Func<Task> renderAction)
    {
        if (navKey != "spotify") _spotifyPlaceholderTimer?.Stop();
        if (navKey != "home") _heroAutoTimer?.Stop();
        _activeNavKey = navKey;
        UpdateActiveSidebarHighlight(navKey);
        StopSmoothScroll(CenterScrollViewer);
        CenterScrollViewer.Offset = new Vector(0, 0);

        if (!_suppressNavPush)
        {
            if (_navCursor < _navHistory.Count - 1)
                _navHistory.RemoveRange(_navCursor + 1, _navHistory.Count - (_navCursor + 1));
            var capturedKey = navKey;
            _navHistory.Add(async () =>
            {
                _activeNavKey = capturedKey;
                UpdateActiveSidebarHighlight(capturedKey);
                await renderAction();
            });
            _navCursor = _navHistory.Count - 1;
        }
        BackNavBtn.IsEnabled = _navCursor > 0;
        ForwardNavBtn.IsEnabled = _navCursor < _navHistory.Count - 1;

        await renderAction();
    }

    private async Task GoBackAsync()
    {
        if (_navCursor <= 0) return;
        _navCursor--;
        _suppressNavPush = true;
        try { await _navHistory[_navCursor](); }
        finally
        {
            _suppressNavPush = false;
            BackNavBtn.IsEnabled = _navCursor > 0;
            ForwardNavBtn.IsEnabled = _navCursor < _navHistory.Count - 1;
        }
    }

    private async Task GoForwardAsync()
    {
        if (_navCursor >= _navHistory.Count - 1) return;
        _navCursor++;
        _suppressNavPush = true;
        try { await _navHistory[_navCursor](); }
        finally
        {
            _suppressNavPush = false;
            BackNavBtn.IsEnabled = _navCursor > 0;
            ForwardNavBtn.IsEnabled = _navHistory.Count - 1 > _navCursor;
        }
    }

    private void UpdateActiveSidebarHighlight(string key)
    {
        var map = new (string Key, Button Btn)[]
        {
            ("home", NavHomeBtn), ("explore", NavExploreBtn), ("flow", NavFlowBtn), ("eq", NavEqBtn),
            ("library", NavLibraryBtn), ("liked", NavLikedBtn), ("downloads", NavDownloadsBtn),
            ("local", NavLocalBtn), ("spotify", NavSpotifyBtn), ("stats", NavStatsBtn), ("about", NavAboutBtn), ("settings", NavSettingsBtn),
        };
        foreach (var (k, btn) in map)
        {
            bool active = key == k || (k == "explore" && key == "search");
            if (active) btn.Classes.Add("navActive");
            else btn.Classes.Remove("navActive");
        }
    }

    internal void RefreshSidebarBadgesAndPlaylists()
    {
        LikedCountBadge.Text = _store.LikedSongs.Count.ToString();
        DownloadsCountBadge.Text = _store.Downloads.Count(d => d.Status == "done").ToString();
        string playlistFingerprint = string.Join("\u001f", _store.Playlists.Select(pl => string.Join("\u001e",
            pl.Id, pl.Name, pl.CustomCoverPath ?? "", pl.Tracks.Count.ToString(),
            string.Join(",", pl.Tracks.Take(4).Select(track => track.Id + ":" + track.Thumbnail)))));
        if (string.Equals(_sidebarPlaylistFingerprint, playlistFingerprint, StringComparison.Ordinal)) return;
        _sidebarPlaylistFingerprint = playlistFingerprint;

        foreach (var oldLabel in SidebarPlaylistsPanel.GetVisualDescendants().OfType<TextBlock>())
            _sidebarTextOriginalVisibility.Remove(oldLabel);
        SidebarPlaylistsPanel.Children.Clear();
        bool compactSidebar = _leftSidebarMode == "Compact";
        foreach (var pl in _store.Playlists)
        {
            var captured = pl;
            var btn = new Button
            {
                Classes = { "navItem" },
                Padding = new Thickness(8, 4),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("40,*,Auto") };
            var playlistCover = BuildLocalPlaylistCover(captured, 40);
            playlistCover.Margin = new Thickness(-2, 0, 0, 0);
            row.Children.Add(playlistCover);
            var label = new TextBlock { Text = captured.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0), IsVisible = !compactSidebar };
            _sidebarTextOriginalVisibility[label] = true;
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var cnt = new TextBlock { Text = captured.Tracks.Count.ToString(), FontSize = 10.5, Foreground = ResBrush("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), IsVisible = !compactSidebar };
            _sidebarTextOriginalVisibility[cnt] = true;
            Grid.SetColumn(cnt, 2); row.Children.Add(cnt);
            btn.Content = row;
            btn.Click += async (_, _) => await NavigateToAsync("pl:" + captured.Id, () => { RenderLocalPlaylistDetail(captured); return Task.CompletedTask; });
            SidebarPlaylistsPanel.Children.Add(btn);
            if (compactSidebar)
            {
                row.Width = 40;
                row.HorizontalAlignment = HorizontalAlignment.Left;
                row.ColumnDefinitions = ColumnDefinitions.Parse("Auto,0,0");
                for (int childIndex = 0; childIndex < row.Children.Count; childIndex++)
                {
                    var child = row.Children[childIndex];
                    Grid.SetColumn(child, 0);
                    child.HorizontalAlignment = HorizontalAlignment.Center;
                    if (child is Border cover) cover.Margin = new Thickness(0);
                }
            }
        }
        UpdateSidebarCompactVisuals();
        if (compactSidebar)
            Dispatcher.UIThread.Post(UpdateSidebarCompactVisuals, DispatcherPriority.Loaded);
    }

    internal void RefreshAccountBadge()
    {
        AccountTopLabel.Text = !string.IsNullOrWhiteSpace(_store.Settings.AccountName)
            ? _store.Settings.AccountName!
            : (_yt.IsSignedIn ? "Signed In" : "Guest");
    }

    // --------------------------------------------------------- Window Modes & Right Panel

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void ToggleMiniPlayerMode()
    {
        _isMiniMode = !_isMiniMode;
        if (_isMiniMode)
        {
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            _savedWidth = Width;
            _savedHeight = Height;
            DesktopShellGrid.IsVisible = false;
            FullscreenOverlay.IsVisible = false;
            MiniPlayerOverlay.IsVisible = true;
            Topmost = true;
            var miniScreen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            double miniScale = RenderScaling > 0.1 ? RenderScaling : miniScreen is not null && miniScreen.Scaling > 0.1 ? miniScreen.Scaling : 1.0;
            double maxMiniWidth = miniScreen is null ? 600 : Math.Max(430, miniScreen.WorkingArea.Width / miniScale - 24);
            Width = Math.Min(600, maxMiniWidth);
            Height = 150;
        }
        else
        {
            MiniPlayerOverlay.IsVisible = false;
            DesktopShellGrid.IsVisible = true;
            Topmost = false;
            FitWindowToWorkingAreaOnStartup();
        }
    }

    private static int _activeLyricsShimmerOwner;

    private Control BuildLyricsLoadingSkeleton(bool fullscreen = false)
    {
        var root = new StackPanel
        {
            Spacing = fullscreen ? 20 : 12,
            Margin = new Thickness(fullscreen ? 20 : 8),
            MaxWidth = fullscreen ? 620 : 280,
            HorizontalAlignment = fullscreen ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
        };
        root.Children.Add(new TextBlock
        {
            Text = "LOOKING UP LYRICS",
            FontSize = fullscreen ? 13 : 10.5,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#9AFFFFFF")),
            Margin = new Thickness(0, 0, 0, 4),
        });

        var shimmerStops = new List<(GradientStop A, GradientStop B, GradientStop C)>();
        double[] widths = fullscreen ? new[] { 560.0, 495.0, 525.0 } : new[] { 250.0, 215.0, 235.0 };
        foreach (var width in widths)
        {
            var a = new GradientStop(Color.Parse("#16FFFFFF"), 0.30);
            var b = new GradientStop(Color.Parse("#48FFFFFF"), 0.42);
            var c = new GradientStop(Color.Parse("#16FFFFFF"), 0.54);
            var gradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.Parse("#0EFFFFFF"), 0), a, b, c,
                    new GradientStop(Color.Parse("#0EFFFFFF"), 1),
                },
            };
            root.Children.Add(new Border
            {
                Width = width,
                MaxWidth = double.PositiveInfinity,
                Height = fullscreen ? 11 : 8,
                CornerRadius = new CornerRadius(6),
                Background = gradient,
                Opacity = 0.64,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            shimmerStops.Add((a, b, c));
        }

        double phase = -0.2;
        bool ownsShimmer = false;
        var shimmerTimer = new DispatcherTimer(AnimationFrameInterval, DispatcherPriority.Render, (_, _) =>
        {
            phase += AnimationFrameInterval.TotalSeconds * 0.6875;
            if (phase > 1.35) phase = -0.2;
            for (int i = 0; i < shimmerStops.Count; i++)
            {
                double center = Math.Clamp(phase + i * 0.16, -0.16, 1.16);
                shimmerStops[i].A.Offset = Math.Clamp(center - 0.12, 0, 1);
                shimmerStops[i].B.Offset = Math.Clamp(center, 0, 1);
                shimmerStops[i].C.Offset = Math.Clamp(center + 0.12, 0, 1);
            }
        });
        _lyricsShimmerTimers.Add(shimmerTimer);
        root.AttachedToVisualTree += (_, _) =>
        {
            if (Interlocked.CompareExchange(ref _activeLyricsShimmerOwner, 1, 0) == 0)
            {
                ownsShimmer = true;
                shimmerTimer.Start();
            }
        };
        root.DetachedFromVisualTree += (_, _) =>
        {
            shimmerTimer.Stop();
            _lyricsShimmerTimers.Remove(shimmerTimer);
            if (ownsShimmer) { ownsShimmer = false; Interlocked.Exchange(ref _activeLyricsShimmerOwner, 0); }
        };
        return root;
    }

    private void ToggleFullscreenOverlay(bool show)
    {
        FullscreenOverlay.IsVisible = show;
        if (show) PopulateFullscreenLyrics();
    }

    private void PopulateFullscreenLyrics()
    {
        FsLyricsStack.Children.Clear();
        _fsLyricBlocks.Clear();
        _fsLyricWordRuns.Clear();
        if (_audio.CurrentTrack is null)
        {
            FsLyricsStack.Children.Add(new TextBlock
            {
                Text = "Play a song to view lyrics",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#80FFFFFF")),
            });
            return;
        }
        if (_currentLyrics is null)
        {
            FsLyricsStack.Children.Add(BuildLyricsLoadingSkeleton(fullscreen: true));
            return;
        }
        if (_currentLyrics.Lines.Count == 0)
        {
            FsLyricsStack.Children.Add(new TextBlock
            {
                Text = "No lyrics found yet. Use the refresh button above to try again.",
                FontSize = 18,
                FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(Color.Parse("#B0FFFFFF")),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }
        if (!_currentLyrics.IsSynced)
        {
            FsLyricsStack.Children.Add(new TextBlock
            {
                Text = "Plain lyrics · not time-synced",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#C0FFFFFF")),
                Margin = new Thickness(0, 0, 0, 4),
            });
        }
        foreach (var line in _currentLyrics.Lines)
        {
            var captured = line;
            var tb = new TextBlock
            {
                Text = GetDisplayLyricText(line),
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#D8FFFFFF")),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(8, 4),
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseOut() },
                    new DoubleTransition { Property = TextBlock.FontSizeProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseOut() },
                },
                TextWrapping = TextWrapping.Wrap,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            tb.PointerPressed += (_, _) =>
            {
                if (captured.TimeSec is double ts) SeekToLyricTime(ts);
            };
            _fsLyricBlocks.Add(tb);
            _fsLyricWordRuns.Add(BuildWordSyncRuns(tb, line));
            FsLyricsStack.Children.Add(tb);
        }
        _lastActiveLyricIdx = -1;
    }

    private void OnMouseNavigationButtons(object? sender, PointerPressedEventArgs e)
    {
        var update = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (update == PointerUpdateKind.XButton1Pressed && _navCursor > 0)
        {
            e.Handled = true;
            _ = GoBackAsync();
        }
        else if (update == PointerUpdateKind.XButton2Pressed && _navCursor < _navHistory.Count - 1)
        {
            e.Handled = true;
            _ = GoForwardAsync();
        }
    }

    private void OnSmoothPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        Control? current = e.Source as Control;

        for (var probe = current; probe is not null; probe = probe.Parent as Control)
        {
            if (probe is ScrollBar) return;
            if (probe is SeekProgressControl progressControl)
            {
                if (progressControl == VolumeSlider && Math.Abs(e.Delta.Y) >= 0.01)
                {
                    double volumeStep = Math.Max(0.02, (progressControl.Maximum - progressControl.Minimum) * 0.05);
                    progressControl.Value = Math.Clamp(progressControl.Value + Math.Sign(e.Delta.Y) * volumeStep, progressControl.Minimum, progressControl.Maximum);
                    e.Handled = true;
                }
                return;
            }
            if (probe is not Slider slider) continue;
            if (Math.Abs(e.Delta.Y) < 0.01) return;
            double range = Math.Max(0.01, slider.Maximum - slider.Minimum);
            double step = range * 0.035;
            slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta.Y) * step, slider.Minimum, slider.Maximum);
            e.Handled = true;
            return;
        }

        double wheel = NormalizeWheelDelta(e.Delta.Y);
        if (Math.Abs(wheel) < 0.0001) return;
        ScrollViewer? viewer = null;
        double maxY = 0;
        double currentY = 0;
        for (var probe = current; probe is not null; probe = probe.Parent as Control)
        {
            if (probe is not ScrollViewer candidate) continue;
            double candidateMax = Math.Max(0, candidate.Extent.Height - candidate.Viewport.Height);
            if (candidateMax <= 1) continue;
            double candidateY = _smoothScrollStates.TryGetValue(candidate, out var existing)
                ? existing.TargetY : candidate.Offset.Y;
            bool canMove = wheel > 0 ? candidateY > 0.25 : candidateY < candidateMax - 0.25;
            if (!canMove) continue; // let nested scrolls hand off naturally at their edges
            viewer = candidate;
            maxY = candidateMax;
            currentY = candidateY;
            break;
        }
        if (viewer is null) return;

        // Keep the wheel's distance; small touchpad deltas stay fractional and are not discarded.
        double pixelsPerUnit = Math.Clamp(viewer.Viewport.Height * 0.115, 72, 100);
        double targetY = Math.Clamp(currentY - wheel * pixelsPerUnit, 0, maxY);
        if (Math.Abs(targetY - currentY) < 0.01) return;
        e.Handled = true;
        QueueSmoothScrollTarget(viewer, viewer.Offset.X, targetY);
    }

    private static double NormalizeWheelDelta(double raw)
    {
        if (!double.IsFinite(raw)) return 0;
        // Avalonia normally supplies notch units; normalize drivers that report Windows' 120-unit ticks.
        return Math.Abs(raw) > 4 ? raw / 120.0 : raw;
    }

    private void StopSmoothScroll(ScrollViewer viewer) => _smoothScrollStates.Remove(viewer);

    private void SmoothScrollTo(ScrollViewer viewer, Vector target)
    {
        var maxX = Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width);
        var maxY = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        QueueSmoothScrollTarget(viewer, Math.Clamp(target.X, 0, maxX), Math.Clamp(target.Y, 0, maxY));
    }

    private void QueueSmoothScrollTarget(ScrollViewer viewer, double targetX, double targetY)
    {
        var maxX = Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width);
        var maxY = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        targetX = Math.Clamp(targetX, 0, maxX);
        targetY = Math.Clamp(targetY, 0, maxY);
        if (!_smoothScrollStates.TryGetValue(viewer, out var state))
        {
            state = new SmoothScrollState { TargetX = viewer.Offset.X, TargetY = viewer.Offset.Y };
            _smoothScrollStates[viewer] = state;
        }
        state.TargetX = targetX;
        state.TargetY = targetY;
        EnsureAnimationFramePump();
    }

    private void EnsureAnimationFramePump()
    {
        if (_animationFramePending || !IsVisible || WindowState == WindowState.Minimized) return;
        _animationFramePending = true;
        RequestAnimationFrame(AdvanceAnimationFrame);
    }

    private void AdvanceAnimationFrame(TimeSpan frameTime)
    {
        _animationFramePending = false;
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            _hasLastMotionFrame = false;
            return;
        }

        double elapsed = _hasLastMotionFrame ? (frameTime - _lastMotionFrameTime).TotalSeconds : 1.0 / 60.0;
        _lastMotionFrameTime = frameTime;
        _hasLastMotionFrame = true;
        int cap = IsResourceSaverActive ? 30 : CurrentPerformanceMode switch { "Low" => 30, "Auto" => 60, _ => 0 };
        if (cap > 0 && _lastMotionUpdateTime != TimeSpan.Zero
            && (frameTime - _lastMotionUpdateTime).TotalSeconds < 1.0 / cap)
        {
            EnsureAnimationFramePump();
            return;
        }
        _lastMotionUpdateTime = frameTime;
        elapsed = Math.Clamp(elapsed, 0.001, 0.05);
        AdvanceSmoothScrolls(elapsed);
        ScrollingTitleTextBlock.AdvanceMarquees(elapsed, DateTime.UtcNow);

        if (_smoothScrollStates.Count > 0 || ScrollingTitleTextBlock.HasActiveMarquees)
            EnsureAnimationFramePump();
    }

    private void AdvanceSmoothScrolls(double elapsedSeconds)
    {
        const double omega = 19.0; // critically damped spring; independent of wheel-event cadence
        foreach (var pair in _smoothScrollStates.ToArray())
        {
            var viewer = pair.Key;
            var state = pair.Value;
            if (!viewer.IsVisible || viewer.GetVisualRoot() is null)
            {
                _smoothScrollStates.Remove(viewer);
                continue;
            }

            var offset = viewer.Offset;
            double dx = state.TargetX - offset.X;
            double dy = state.TargetY - offset.Y;
            state.VelocityX += (omega * omega * dx - 2 * omega * state.VelocityX) * elapsedSeconds;
            state.VelocityY += (omega * omega * dy - 2 * omega * state.VelocityY) * elapsedSeconds;
            double x = offset.X + state.VelocityX * elapsedSeconds;
            double y = offset.Y + state.VelocityY * elapsedSeconds;
            double maxX = Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width);
            double maxY = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
            x = Math.Clamp(x, 0, maxX);
            y = Math.Clamp(y, 0, maxY);
            if (x <= 0 || x >= maxX) state.VelocityX = 0;
            if (y <= 0 || y >= maxY) state.VelocityY = 0;

            if (Math.Abs(state.TargetX - x) < 0.45 && Math.Abs(state.TargetY - y) < 0.45
                && Math.Abs(state.VelocityX) < 3 && Math.Abs(state.VelocityY) < 3)
            {
                viewer.Offset = new Vector(state.TargetX, state.TargetY);
                _smoothScrollStates.Remove(viewer);
            }
            else viewer.Offset = new Vector(x, y);
        }
    }

    private void KeepLyricInComfortBand(TextBlock line, ScrollViewer? viewer)
    {
        if (viewer is null || viewer.Viewport.Height <= 1) return;
        var origin = line.TranslatePoint(new Point(0, 0), viewer);
        if (origin is null) return;
        double y = origin.Value.Y;
        double height = line.Bounds.Height;
        double topBand = viewer.Viewport.Height * 0.28;
        double bottomBand = viewer.Viewport.Height * 0.62;
        if (y >= topBand && y + height <= bottomBand) return;

        double anchor = viewer.Viewport.Height * 0.40;
        double target = viewer.Offset.Y + y - anchor;
        SmoothScrollTo(viewer, new Vector(viewer.Offset.X, target));
    }

    private static string NormalizeSidebarMode(string? mode) => (mode ?? "").Trim().ToLowerInvariant() switch
    {
        "compact" or "icons-only" or "iconsonly" or "icon only" => "Compact",
        "hidden" => "Hidden",
        _ => "Expanded",
    };

    private void UpdateSidebarCompactVisuals()
    {
        bool compact = _leftSidebarMode == "Compact";
        foreach (var text in LeftSidebarBorder.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!_sidebarTextOriginalVisibility.ContainsKey(text))
                _sidebarTextOriginalVisibility[text] = text.IsVisible;
            text.IsVisible = compact ? false : _sidebarTextOriginalVisibility[text];
        }

        foreach (var button in LeftSidebarBorder.GetVisualDescendants().OfType<Button>())
        {
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (button.Content is StackPanel row)
                row.HorizontalAlignment = HorizontalAlignment.Left;

            if (button.Content is not Grid grid) continue;
            bool playlistOrLiked = button == NavLikedBtn || SidebarPlaylistsPanel.Children.Contains(button);
            bool downloads = button == NavDownloadsBtn;
            if (compact && (playlistOrLiked || downloads))
            {
                // Use a fixed 40-DIP artwork cell; a parallel fast translation eases each icon into its true center.
                grid.Width = playlistOrLiked ? 40 : double.NaN;
                grid.HorizontalAlignment = HorizontalAlignment.Left;
                grid.ColumnDefinitions = ColumnDefinitions.Parse("Auto,0,0");
                foreach (var child in grid.Children)
                {
                    Grid.SetColumn(child, 0);
                    child.HorizontalAlignment = playlistOrLiked ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                    if (child is Border cover) cover.Margin = new Thickness(0);
                    if (child is PathIcon icon) icon.Margin = new Thickness(0);
                }
            }
            else if (!compact && (playlistOrLiked || downloads))
            {
                grid.Width = double.NaN;
                grid.HorizontalAlignment = HorizontalAlignment.Stretch;
                grid.ColumnDefinitions = ColumnDefinitions.Parse(downloads ? "Auto,*,Auto" : "40,*,Auto");
                int restoredColumn = 0;
                foreach (var child in grid.Children)
                {
                    Grid.SetColumn(child, restoredColumn++);
                    child.HorizontalAlignment = HorizontalAlignment.Left;
                    if (child is Border cover) cover.Margin = playlistOrLiked ? new Thickness(-2, 0, 0, 0) : new Thickness(0);
                    if (child is PathIcon icon) icon.Margin = downloads ? new Thickness(0, 0, 12, 0) : new Thickness(0);
                }
            }
        }

        PlaylistSectionHeaderGrid.Margin = compact ? new Thickness(0, 0, 0, 4) : new Thickness(12, 0, 4, 4);
        PlaylistSectionHeaderGrid.ColumnDefinitions = ColumnDefinitions.Parse("*,Auto");
        bool centerCreateButton = compact && !_sidebarCompactAnimationPending;
        Grid.SetColumn(CreatePlaylistSideBtn, centerCreateButton ? 0 : 1);
        Grid.SetColumnSpan(CreatePlaylistSideBtn, centerCreateButton ? 2 : 1);
        CreatePlaylistSideBtn.HorizontalAlignment = centerCreateButton ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        CreatePlaylistSideBtn.Width = centerCreateButton ? 40 : double.NaN;
        CreatePlaylistSideBtn.Height = centerCreateButton ? 40 : double.NaN;
        CreatePlaylistSideBtn.Padding = centerCreateButton ? new Thickness(0) : new Thickness(5, 3);

        ApplySidebarContentOffset(_sidebarContentOffsetAnimating ? _sidebarContentOffsetProgress : (compact ? 1 : 0));
    }

    private double GetCompactContentOffset(Button button)
    {
        const double compactButtonWidth = 48; // 76-wide column minus sidebar margin and padding.
        double contentWidth;
        if (button == NavLikedBtn || SidebarPlaylistsPanel.Children.Contains(button))
        {
            contentWidth = 40;
        }
        else if (button.Content is StackPanel row && row.Children.OfType<PathIcon>().FirstOrDefault() is { } icon)
        {
            contentWidth = double.IsFinite(icon.Width) && icon.Width > 0 ? icon.Width : icon.Bounds.Width;
        }
        else if (button.Content is Grid grid && grid.Children.OfType<PathIcon>().FirstOrDefault() is { } gridIcon)
        {
            contentWidth = double.IsFinite(gridIcon.Width) && gridIcon.Width > 0 ? gridIcon.Width : gridIcon.Bounds.Width;
        }
        else return 0;

        double innerWidth = Math.Max(0, compactButtonWidth - button.Padding.Left - button.Padding.Right);
        return (innerWidth - contentWidth) / 2.0;
    }

    private void ApplySidebarContentOffset(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        foreach (var button in LeftSidebarBorder.GetVisualDescendants().OfType<Button>())
        {
            if (button.Content is not Control content) continue;
            var transform = content.RenderTransform as TranslateTransform;
            if (transform is null)
            {
                transform = new TranslateTransform();
                content.RenderTransform = transform;
            }
            transform.X = GetCompactContentOffset(button) * progress;
        }
    }

    internal void ToggleLeftSidebar()
    {
        var behavior = _store.Settings.SidebarCollapseBehavior;
        var next = behavior switch
        {
            "Stepwise" => _leftSidebarMode switch { "Expanded" => "Compact", "Compact" => "Hidden", _ => "Expanded" },
            "Hide" => _leftSidebarMode == "Hidden" ? "Expanded" : "Hidden",
            _ => _leftSidebarMode == "Compact" ? "Expanded" : "Compact", // Partial: icons-only, then expand
        };
        ApplyLeftSidebarMode(next);
        _store.SaveDebounced();
    }

    internal void ApplyLeftSidebarMode(string? mode = null, bool animate = true, bool rerenderSettings = true)
    {
        double currentContentProgress = _sidebarContentOffsetAnimating
            ? _sidebarContentOffsetProgress
            : (_leftSidebarMode == "Compact" ? 1 : 0);
        _leftSidebarMode = NormalizeSidebarMode(mode ?? _store.Settings.LeftSidebarMode);
        _store.Settings.LeftSidebarMode = _leftSidebarMode;
        _leftSidebarOpen = _leftSidebarMode != "Hidden";
        bool compact = _leftSidebarMode == "Compact";
        double target = _leftSidebarMode switch { "Compact" => 76, "Hidden" => 0, _ => 244 };
        LeftSidebarBorder.IsHitTestVisible = _leftSidebarOpen;
        LeftSidebarBorder.Padding = new Thickness(10, 12);
        LeftSidebarBorder.Margin = _leftSidebarMode == "Hidden" ? new Thickness(0) : new Thickness(0, 0, 8, 0);
        _sidebarCompactAnimationPending = animate && compact;
        _sidebarContentOffsetFrom = currentContentProgress;
        _sidebarContentOffsetTo = compact ? 1 : 0;
        _sidebarContentOffsetAnimating = animate && Math.Abs(_sidebarContentOffsetTo - _sidebarContentOffsetFrom) > 0.001;
        _sidebarContentOffsetProgress = _sidebarContentOffsetAnimating ? _sidebarContentOffsetFrom : _sidebarContentOffsetTo;
        UpdateSidebarCompactVisuals();
        string behaviorTip = _store.Settings.SidebarCollapseBehavior switch
        {
            "Stepwise" when compact => "Compact sidebar (click to hide)",
            "Stepwise" when _leftSidebarMode == "Hidden" => "Sidebar hidden (click to expand)",
            "Hide" when _leftSidebarMode == "Hidden" => "Sidebar hidden (click to expand)",
            "Hide" => "Expanded sidebar (click to hide)",
            _ when compact => "Icons-only sidebar (click to expand)",
            _ => "Expanded sidebar (click to show icons-only)",
        };
        ToolTip.SetTip(ToggleLeftSidebarBtn, behaviorTip);
        if (ToggleLeftSidebarBtn.Content is PathIcon path)
            path.Data = _leftSidebarMode == "Expanded" ? Icons.ChevronLeft : Icons.ChevronRight;
        if (animate) AnimateWorkspaceColumn(0, target, leftSidebar: true);
        else WorkspaceColumnsGrid.ColumnDefinitions[0].Width = new GridLength(target);
        if (rerenderSettings && _activeNavKey == "settings") RenderSettingsView();
    }

    internal void ToggleRightDock(bool open)
    {
        _dockOpen = open;
        UpdateLyricsDockButtonState();
        RightDockBorder.IsHitTestVisible = open;
        if (open)
        {
            RightDockBorder.IsVisible = true;
            RenderRightDock();
        }
        AnimateWorkspaceColumn(2, open ? 312 : 0, leftSidebar: false);
    }

    private void AnimateWorkspaceColumn(int index, double target, bool leftSidebar)
    {
        var previous = leftSidebar ? _leftSidebarAnimation : _rightSidebarAnimation;
        previous?.Stop();
        var column = WorkspaceColumnsGrid.ColumnDefinitions[index];
        double start = column.Width.Value;
        if (Math.Abs(start - target) < 0.5)
        {
            column.Width = new GridLength(target);
            if (leftSidebar)
            {
                _sidebarCompactAnimationPending = false;
                _sidebarContentOffsetAnimating = false;
                _sidebarContentOffsetProgress = _sidebarContentOffsetTo;
                UpdateSidebarCompactVisuals();
            }
            return;
        }

        DateTime started = DateTime.UtcNow;
        DispatcherTimer? timer = null;
        timer = new DispatcherTimer(AnimationFrameInterval, DispatcherPriority.Render, (_, _) =>
        {
            var current = leftSidebar ? _leftSidebarAnimation : _rightSidebarAnimation;
            if (!ReferenceEquals(current, timer))
            {
                timer!.Stop();
                return;
            }
            double progress = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / 190.0, 0, 1);
            double eased = 1 - Math.Pow(1 - progress, 3);
            column.Width = new GridLength(start + (target - start) * eased);
            if (leftSidebar && _sidebarContentOffsetAnimating)
            {
                _sidebarContentOffsetProgress = _sidebarContentOffsetFrom + (_sidebarContentOffsetTo - _sidebarContentOffsetFrom) * eased;
                ApplySidebarContentOffset(_sidebarContentOffsetProgress);
            }
            if (progress < 1) return;

            column.Width = new GridLength(target);
            timer!.Stop();
            if (leftSidebar)
            {
                _leftSidebarAnimation = null;
                _sidebarCompactAnimationPending = false;
                _sidebarContentOffsetAnimating = false;
                _sidebarContentOffsetProgress = _sidebarContentOffsetTo;
                UpdateSidebarCompactVisuals();
            }
            else
            {
                _rightSidebarAnimation = null;
                if (!_dockOpen) RightDockBorder.IsVisible = false;
            }
        });
        if (leftSidebar) _leftSidebarAnimation = timer;
        else _rightSidebarAnimation = timer;
        timer.Start();
    }

    private void ToggleOrSwitchDock(string tab)
    {
        if (_dockOpen && _dockTab == tab) ToggleRightDock(false);
        else SetDockTab(tab, ensureOpen: true);
    }

    internal void SetDockTab(string tab, bool ensureOpen)
    {
        _dockTab = tab;
        if (ensureOpen && !_dockOpen) ToggleRightDock(true);
        else RenderRightDock();
        UpdateLyricsDockButtonState();
    }

    private void UpdateLyricsDockButtonState()
    {
        if (ToggleLyricsBarBtn.Content is PathIcon icon)
            icon.Foreground = _dockOpen && _dockTab == "lyrics" ? ResBrush("AccentBrush") : ResBrush("TextSecondaryBrush");
    }

    private Control BuildQueueSourceCard(PlaybackCollectionContext source)
    {
        var card = new Border
        {
            Background = ResBrush("AccentSoftBrush"),
            BorderBrush = ResBrush("AccentBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 9),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("3,10,52,*") };
        row.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = ResBrush("AccentBrush") });
        Control cover;
        var local = source.IsLocal ? _store.Playlists.FirstOrDefault(p => p.Id == source.Id) : null;
        if (local is not null)
            cover = BuildLocalPlaylistCover(local, 48);
        else
        {
            var image = new Image { Width = 48, Height = 48, Stretch = Stretch.UniformToFill };
            LoadImageInto(image, source.Thumbnail, BuildGeneratedMediaCover(
                new MediaCardItem { Id = source.Id, Kind = "playlist", Title = source.Title }, 48));
            cover = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = image };
        }
        Grid.SetColumn(cover, 2);
        row.Children.Add(cover);
        var meta = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        var sourceLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        sourceLabel.Children.Add(new PathIcon { Data = Icons.Library, Width = 11, Height = 11, Foreground = ResBrush("AccentBrush") });
        bool albumSource = string.Equals(source.Kind, "album", StringComparison.OrdinalIgnoreCase);
        sourceLabel.Children.Add(new TextBlock { Text = albumSource ? "ALBUM SOURCE" : "PLAYLIST SOURCE", FontSize = 9, FontWeight = FontWeight.ExtraBold, Foreground = ResBrush("AccentBrush"), LetterSpacing = 0.65 });
        meta.Children.Add(sourceLabel);
        meta.Children.Add(new TextBlock { Text = source.Title, FontSize = 12.5, FontWeight = FontWeight.Bold });
        meta.Children.Add(new TextBlock { Text = source.IsLocal ? "Local playlist" : albumSource ? "YouTube Music album" : "YouTube Music playlist", FontSize = 10.5, Foreground = ResBrush("TextSecondaryBrush") });
        Grid.SetColumn(meta, 3);
        row.Children.Add(meta);
        card.Child = row;
        ToolTip.SetTip(card, albumSource ? "Album source for the queue — open this album in the main view" : "Playlist source for the queue — open this playlist in the main view");
        card.PointerPressed += async (_, ev) =>
        {
            if (!ev.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            ev.Handled = true;
            await NavigateToAsync((albumSource ? "album:" : "playlist:") + source.Id, () =>
            {
                if (source.IsLocal)
                {
                    var playlist = _store.Playlists.FirstOrDefault(p => p.Id == source.Id);
                    if (playlist is not null) RenderLocalPlaylistDetail(playlist);
                    else SetCenterContent(ErrorCard("Playlist no longer exists", "This local playlist was deleted.", () => RenderLibraryView("playlists")));
                    return Task.CompletedTask;
                }
                return albumSource ? LoadAlbumDetailAsync(source.Id) : LoadPlaylistDetailAsync(source.Id);
            });
        };
        return card;
    }

    private void RenderRightDock()
    {
        var tabPairs = new (string Id, Button Btn)[]
        {
            ("now", DockTabNowBtn),
            ("queue", DockTabQueueBtn),
            ("dna", DockTabDnaBtn),
        };
        foreach (var (id, btn) in tabPairs)
        {
            if (_dockTab == id) btn.Classes.Add("rpanelTabActive");
            else btn.Classes.Remove("rpanelTabActive");
        }

        RightDockContentHost.Children.Clear();
        _dockCurrentArtSpectrum = null;
        _dockCurrentArtSpectrumPlate = null;
        _dockLyricBlocks.Clear();
        _dockLyricWordRuns.Clear();
        _dockLyricsScroller = null;
        _dockNowStack = null;
        _dockDnaCardAdded = false;

        if (_dockTab == "now")
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Spacing = 14, Margin = new Thickness(0, 2, 2, 16) };
            _dockNowStack = stack;

            var tr = _audio.CurrentTrack ?? _cachedHome?.Shelves.SelectMany(s => s.Tracks).FirstOrDefault();
            if (tr is null)
            {
                var emptyCard = new Border
                {
                    Background = ResBrush("BgCardBrush"),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(18, 28),
                };
                var es = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
                es.Children.Add(new EchoWaveLogoControl { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center });
                es.Children.Add(new TextBlock { Text = "Ready to Play", FontSize = 16, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
                es.Children.Add(new TextBlock { Text = "Choose any song on Home or Search to see live artwork, synced lyrics, and queue here.", FontSize = 12.5, Foreground = ResBrush("TextSecondaryBrush"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
                emptyCard.Child = es;
                stack.Children.Add(emptyCard);
            }
            else
            {
                bool isLiveCurrent = _audio.CurrentTrack is not null;
                stack.Children.Add(new TextBlock
                {
                    Text = isLiveCurrent ? (tr.Album?.Name ?? "Now Playing") : "Suggested for You",
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    Foreground = ResBrush("TextSecondaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

                var artImg = new Image { Width = 280, Height = 280, Stretch = Stretch.UniformToFill };
                LoadImageInto(artImg, InnertubeParsers.UpgradeThumb(tr.Thumbnail, 544), isLiveCurrent
                    ? BuildGeneratedMediaCover(new MediaCardItem { Id = tr.Id, Kind = "playlist", Title = tr.Title, Subtitle = tr.ArtistNames }, 268)
                    : null);
                var artSpectrum = new SpectrumVisualizerControl { Width = 38, Height = 21, BarCount = 7, BarColor = ThemeManager.GetPalette(_store.Settings.Theme).Accent };
                var artSpectrumPlate = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(12),
                    Padding = new Thickness(5, 3),
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(Color.FromArgb(136, 0, 0, 0)),
                    IsVisible = isLiveCurrent && _audio.IsPlaying,
                    IsHitTestVisible = false,
                    Child = artSpectrum,
                };
                var artGrid = new Grid { ClipToBounds = true };
                if (isLiveCurrent && string.IsNullOrWhiteSpace(tr.Thumbnail))
                    artGrid.Children.Add(BuildGeneratedMediaCover(new MediaCardItem { Id = tr.Id, Kind = "playlist", Title = tr.Title, Subtitle = tr.ArtistNames }, 268));
                else
                    artGrid.Children.Add(artImg);
                artGrid.Children.Add(artSpectrumPlate);
                if (isLiveCurrent)
                {
                    _dockCurrentArtSpectrum = artSpectrum;
                    _dockCurrentArtSpectrumPlate = artSpectrumPlate;
                }
                var artCard = new Border
                {
                    Height = 268,
                    CornerRadius = new CornerRadius(10),
                    ClipToBounds = true,
                    Background = ResBrush("BgCardBrush"),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = artGrid,
                };
                artCard.PointerPressed += async (_, _) =>
                {
                    if (!isLiveCurrent) await _audio.PlayQueueAsync(new List<TrackItem> { tr }, 0, autoRadioIfSingle: true);
                    else ToggleFullscreenOverlay(true);
                };
                stack.Children.Add(artCard);

                var metaRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
                var metaText = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 8, 0), ClipToBounds = true };
                metaText.Children.Add(new ScrollingTitleTextBlock { Text = tr.Title, FontSize = 17, FontWeight = FontWeight.Bold });
                metaText.Children.Add(new TextBlock { Text = tr.ArtistNames, FontSize = 13, Foreground = ResBrush("TextSecondaryBrush") });
                if (!string.IsNullOrWhiteSpace(tr.AlbumArtist) && !string.Equals(tr.AlbumArtist, tr.ArtistNames, StringComparison.OrdinalIgnoreCase))
                    metaText.Children.Add(new TextBlock { Text = $"Album artist · {tr.AlbumArtist}", FontSize = 11.5, Foreground = ResBrush("TextMutedBrush") });
                metaRow.Children.Add(metaText);

                var quickPlayOrLike = new Button
                {
                    Classes = { isLiveCurrent ? "ghost" : "accentBtn" },
                    Padding = isLiveCurrent ? new Thickness(7) : new Thickness(14, 6),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (isLiveCurrent)
                {
                    quickPlayOrLike.Content = new PathIcon
                    {
                        Data = _store.IsLiked(tr.Id) ? Icons.HeartFilled : Icons.HeartOutline,
                        Width = 17,
                        Height = 17,
                        Foreground = _store.IsLiked(tr.Id) ? ResBrush("AccentBrush") : ResBrush("TextSecondaryBrush"),
                    };
                    quickPlayOrLike.Click += (_, _) =>
                    {
                        _store.ToggleLike(tr, _yt);
                        UpdateNowPlayingUi();
                        RenderRightDock();
                    };
                }
                else
                {
                    quickPlayOrLike.Content = "Play";
                    quickPlayOrLike.Click += async (_, _) => await _audio.PlayQueueAsync(new List<TrackItem> { tr }, 0, autoRadioIfSingle: true);
                }
                Grid.SetColumn(quickPlayOrLike, 1);
                metaRow.Children.Add(quickPlayOrLike);
                stack.Children.Add(metaRow);

                // Next in Queue Card (Spotify Desktop right-panel signature!)
                if (_audio.Queue.Count > _audio.CurrentIndex + 1 && _audio.CurrentIndex >= 0)
                {
                    var nextTr = _audio.Queue[_audio.CurrentIndex + 1];
                    var nextBox = new Border
                    {
                        Background = ResBrush("BgCardBrush"),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(12, 10),
                        Cursor = new Cursor(StandardCursorType.Hand),
                    };
                    var ns = new StackPanel { Spacing = 8 };
                    var nh = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
                    nh.Children.Add(new TextBlock { Text = "Next in queue", FontSize = 12.5, FontWeight = FontWeight.Bold });
                    var openQ = new Button { Classes = { "ghost" }, Padding = new Thickness(4, 1), Content = "Open queue", FontSize = 11.5, Foreground = ResBrush("AccentBrush") };
                    openQ.Click += (_, ev) => { ev.Handled = true; SetDockTab("queue", true); };
                    Grid.SetColumn(openQ, 1);
                    nh.Children.Add(openQ);
                    ns.Children.Add(nh);

                    var nr = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("40,*,Auto") };
                    var nimg = new Image { Width = 40, Height = 40, Stretch = Stretch.UniformToFill };
                    LoadImageInto(nimg, nextTr.Thumbnail);
                    nr.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = ResBrush("BgSurfaceBrush"), Child = nimg });
                    var nm = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
                    nm.Children.Add(new ScrollingTitleTextBlock { Text = nextTr.Title, FontSize = 13, FontWeight = FontWeight.SemiBold });
                    nm.Children.Add(new TextBlock { Text = nextTr.ArtistNames, FontSize = 11.5, Foreground = ResBrush("TextSecondaryBrush") });
                    Grid.SetColumn(nm, 1);
                    nr.Children.Add(nm);
                    ns.Children.Add(nr);
                    nextBox.Child = ns;
                    nextBox.PointerPressed += async (_, _) => await _audio.JumpToQueueIndexAsync(_audio.CurrentIndex + 1);
                    stack.Children.Add(nextBox);
                }

                // Credits / Music DNA Card
                if (_currentDna is not null)
                {
                    stack.Children.Add(SectionCard("About the track", _currentDna.TrackStory));
                    _dockDnaCardAdded = true;
                }
            }

            sv.Content = stack;
            RightDockContentHost.Children.Add(sv);
        }
        else if (_dockTab == "queue")
        {
            var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*") };
            var hdr = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto"), Margin = new Thickness(0, 0, 0, 8) };
            hdr.Children.Add(new TextBlock
            {
                Text = $"Queue ({Math.Max(0, _audio.Queue.Count - Math.Max(0, _audio.CurrentIndex))})",
                FontSize = 12.5,
                FontWeight = FontWeight.Bold,
                Foreground = ResBrush("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var saveQBtn = new Button { Classes = { "ghost" }, Content = "Save", FontSize = 11.5, Padding = new Thickness(8, 4) };
            saveQBtn.Click += (_, _) =>
            {
                if (_audio.Queue.Count == 0) return;
                var pl = _store.CreatePlaylist($"Queue Mix {DateTime.Now:MMM d}", "Saved from Play Queue", _audio.Queue);
                ShowToast($"Saved \"{pl.Name}\" ({pl.Tracks.Count} songs)");
            };
            Grid.SetColumn(saveQBtn, 1);
            hdr.Children.Add(saveQBtn);

            var clearBtn = new Button { Classes = { "ghost" }, Content = "Clear", FontSize = 11.5, Padding = new Thickness(8, 4) };
            clearBtn.Click += (_, _) => _audio.ClearUpcomingQueue();
            Grid.SetColumn(clearBtn, 2);
            hdr.Children.Add(clearBtn);
            root.Children.Add(hdr);

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Spacing = 4 };
            if (_audio.QueueSource is { } queueSource)
                stack.Children.Add(BuildQueueSourceCard(queueSource));
            for (int i = 0; i < _audio.Queue.Count; i++)
            {
                int idx = i;
                var tr = _audio.Queue[i];
                bool isCur = i == _audio.CurrentIndex;
                var row = new Border
                {
                    Background = isCur ? ResBrush("AccentSoftBrush") : ResBrush("BgCardBrush"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 7),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                var g = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("22,*,Auto") };
                g.Children.Add(new TextBlock
                {
                    Text = isCur ? "▶" : (idx + 1).ToString(),
                    FontSize = 11.5,
                    FontWeight = isCur ? FontWeight.Bold : FontWeight.Normal,
                    Foreground = isCur ? ResBrush("AccentBrush") : ResBrush("TextMutedBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var meta = new StackPanel { Spacing = 1, ClipToBounds = true, Margin = new Thickness(0, 0, 6, 0) };
                meta.Children.Add(new ScrollingTitleTextBlock { Text = tr.Title, FontSize = 12.5, FontWeight = isCur ? FontWeight.Bold : FontWeight.SemiBold, Foreground = isCur ? ResBrush("AccentBrush") : ResBrush("TextPrimaryBrush") });
                meta.Children.Add(new TextBlock { Text = tr.ArtistNames, FontSize = 11, Foreground = ResBrush("TextSecondaryBrush") });
                Grid.SetColumn(meta, 1);
                g.Children.Add(meta);

                var rmBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(5, 3), Content = new PathIcon { Data = Icons.Close, Width = 11, Height = 11 } };
                rmBtn.Click += (_, ev) => { ev.Handled = true; _audio.RemoveFromQueueAt(idx); };
                Grid.SetColumn(rmBtn, 2);
                g.Children.Add(rmBtn);

                row.Child = g;
                row.PointerPressed += async (_, _) => await _audio.JumpToQueueIndexAsync(idx);
                stack.Children.Add(row);
            }
            if (_audio.Queue.Count == 0)
                stack.Children.Add(EmptyHint("Queue is empty - click any song to start playing."));
            sv.Content = stack;
            Grid.SetRow(sv, 1);
            root.Children.Add(sv);
            RightDockContentHost.Children.Add(root);
        }
        else if (_dockTab == "lyrics")
        {
            var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*") };
            var toolRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 0, 0, 8) };
            toolRow.Children.Add(new TextBlock
            {
                Text = _currentLyrics?.Provider is { Length: > 0 } p && p != "None" ? p : "Lyrics",
                FontSize = 11.5,
                FontWeight = FontWeight.Bold,
                Foreground = ResBrush("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var minusBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(6, 3), Content = "-0.25s", FontSize = 10.5 };
            minusBtn.Click += (_, _) => AdjustLyricOffset(-0.25);
            Grid.SetColumn(minusBtn, 1);
            toolRow.Children.Add(minusBtn);

            var plusBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(6, 3), Content = "+0.25s", FontSize = 10.5 };
            plusBtn.Click += (_, _) => AdjustLyricOffset(+0.25);
            Grid.SetColumn(plusBtn, 2);
            toolRow.Children.Add(plusBtn);

            var retryLyricsBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(7, 3), Content = "↻", FontSize = 14 };
            ToolTip.SetTip(retryLyricsBtn, "Retry lyrics lookup");
            retryLyricsBtn.Click += async (_, _) => await RetryLyricsForCurrentTrackAsync();
            Grid.SetColumn(retryLyricsBtn, 3);
            toolRow.Children.Add(retryLyricsBtn);

            var transBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(7, 3) };
            ToolTip.SetTip(transBtn, "Translate Lyrics");
            transBtn.Content = new PathIcon { Data = Icons.Translate, Width = 13, Height = 13 };
            transBtn.Click += async (_, _) =>
            {
                if (_currentLyrics is null || _currentLyrics.Lines.Count == 0) return;
                _lyricsShowRomanized = false;
                if (_currentLyrics.Lines.Any(l => !string.IsNullOrWhiteSpace(l.TranslatedText)))
                {
                    foreach (var line in _currentLyrics.Lines) line.TranslatedText = null;
                    ShowToast("Original lyrics restored");
                }
                else
                {
                    var lang = string.IsNullOrEmpty(_store.Settings.TranslateLyricsTo) ? "en" : _store.Settings.TranslateLyricsTo;
                    ShowToast($"Translating lyrics to {lang.ToUpperInvariant()}...");
                    await _lyricsSvc.TranslateLyricsAsync(_currentLyrics, lang, _store.Settings);
                }
                PopulateFullscreenLyrics(); RenderRightDock();
            };
            Grid.SetColumn(transBtn, 4); toolRow.Children.Add(transBtn);
            var latinBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(6, 3), Content = "Aa", FontSize = 11, FontWeight = FontWeight.Bold };
            ToolTip.SetTip(latinBtn, "Toggle original script / broad Latin transliteration (offline)");
            latinBtn.Click += (_, _) =>
            {
                _lyricsShowRomanized = !_lyricsShowRomanized;
                PopulateFullscreenLyrics(); RenderRightDock();
                ShowToast(_lyricsShowRomanized ? "Latin transliteration on" : "Original lyric script restored");
            };
            Grid.SetColumn(latinBtn, 5); toolRow.Children.Add(latinBtn);
            root.Children.Add(toolRow);

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _dockLyricsScroller = sv;
            var stack = new StackPanel { Spacing = 11, Margin = new Thickness(2, 6, 6, 36) };
            if (_audio.CurrentTrack is null)
                stack.Children.Add(EmptyHint("Play a track to see real-time synced lyrics."));
            else if (_currentLyrics is null)
                stack.Children.Add(BuildLyricsLoadingSkeleton());
            else if (_currentLyrics.Lines.Count == 0)
                stack.Children.Add(EmptyHint("No lyrics yet. Tap ↻ to retry providers or wait for the next provider response."));
            else
            {
                if (!_currentLyrics.IsSynced)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = "Plain lyrics · no timestamps, so active-line sync is unavailable. Retrying timed sources once…",
                        FontSize = 10.5,
                        Foreground = ResBrush("TextMutedBrush"),
                        TextWrapping = TextWrapping.Wrap,
                    });
                }
                foreach (var line in _currentLyrics.Lines)
                {
                    var captured = line;
                    var tb = new TextBlock
                    {
                        Text = GetDisplayLyricText(line),
                        FontSize = 13.5,
                        FontWeight = FontWeight.Medium,
                        Foreground = _currentLyrics.IsSynced ? ResBrush("TextMutedBrush") : ResBrush("TextPrimaryBrush"),
                        Background = Brushes.Transparent,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        TextWrapping = TextWrapping.Wrap,
                        Transitions = new Transitions
                        {
                            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseOut() },
                            new DoubleTransition { Property = TextBlock.FontSizeProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseOut() },
                        },
                        Cursor = _currentLyrics.IsSynced ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
                    };
                    if (_currentLyrics.IsSynced)
                    {
                        tb.PointerPressed += (_, _) =>
                        {
                            if (captured.TimeSec is double ts) SeekToLyricTime(ts);
                        };
                    }
                    _dockLyricBlocks.Add(tb);
                    _dockLyricWordRuns.Add(BuildWordSyncRuns(tb, line));
                    stack.Children.Add(tb);
                }
                _lastActiveLyricIdx = -1;
            }
            sv.Content = stack;
            Grid.SetRow(sv, 1);
            root.Children.Add(sv);
            RightDockContentHost.Children.Add(root);
        }
        else // Related tracks + Music DNA
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Spacing = 12, Margin = new Thickness(0, 2, 2, 16) };
            if (_audio.CurrentTrack is null)
            {
                stack.Children.Add(EmptyHint("Play any song to explore related tracks and song backstory."));
            }
            else
            {
                if (_relatedTracks.Count > 0)
                {
                    stack.Children.Add(new TextBlock { Text = "RELATED TRACKS", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush"), LetterSpacing = 0.6 });
                    foreach (var rt in _relatedTracks.Take(8))
                    {
                        var captured = rt;
                        var rCard = new Border
                        {
                            Background = ResBrush("BgCardBrush"),
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(8, 7),
                            Cursor = new Cursor(StandardCursorType.Hand),
                        };
                        var rg = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("38,*,Auto") };
                        var rimg = new Image { Width = 38, Height = 38, Stretch = Stretch.UniformToFill };
                        LoadImageInto(rimg, captured.Thumbnail);
                        rg.Children.Add(new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(5), ClipToBounds = true, Background = ResBrush("BgSurfaceBrush"), Child = rimg });
                        var rm = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
                        rm.Children.Add(new ScrollingTitleTextBlock { Text = captured.Title, FontSize = 12.5, FontWeight = FontWeight.SemiBold });
                        rm.Children.Add(new TextBlock { Text = captured.ArtistNames, FontSize = 11, Foreground = ResBrush("TextSecondaryBrush") });
                        Grid.SetColumn(rm, 1);
                        rg.Children.Add(rm);
                        rCard.Child = rg;
                        rCard.PointerPressed += async (_, _) => await _audio.PlayQueueAsync(_relatedTracks, Math.Max(0, _relatedTracks.IndexOf(captured)), autoRadioIfSingle: false);
                        stack.Children.Add(rCard);
                    }
                }

                if (_currentDna is not null)
                {
                    stack.Children.Add(new TextBlock { Text = "SONG DNA", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush"), LetterSpacing = 0.6, Margin = new Thickness(0, 6, 0, 0) });
                    stack.Children.Add(SectionCard(_currentDna.TrackHeadline, _currentDna.TrackStory));
                    if (!string.IsNullOrWhiteSpace(_currentDna.ArtistBio))
                        stack.Children.Add(SectionCard("Artist Profile", _currentDna.ArtistBio));
                }
                else if (_relatedTracks.Count == 0)
                {
                    stack.Children.Add(EmptyHint("Finding related songs & Music DNA..."));
                }
            }
            sv.Content = stack;
            RightDockContentHost.Children.Add(sv);
        }
    }

    private void SeekToLyricTime(double lyricTimeSeconds)
    {
        double offset = 0;
        if (_audio.CurrentTrack is { } track) _store.LyricOffsets.TryGetValue(track.Id, out offset);
        _audio.Seek(Math.Max(0, lyricTimeSeconds - offset));
        RefreshLyricsAfterSeek();
    }

    private void AdjustLyricOffset(double delta)
    {
        if (_audio.CurrentTrack is not { } tr) return;
        _store.LyricOffsets.TryGetValue(tr.Id, out double cur);
        cur = Math.Clamp(Math.Round((cur + delta) * 4) / 4.0, -8.0, 8.0);
        _store.LyricOffsets[tr.Id] = cur;
        _store.SaveDebounced();
        _lastActiveLyricIdx = -1;
        ShowToast($"Lyric sync offset: {(cur >= 0 ? "+" : "")}{cur:0.00}s");
    }

    // --------------------------------------------------------- Live Search Suggestions

    internal bool SearchSuggestionsOpen => SearchSuggestFlyout.IsOpen;
    internal int SearchSuggestionsCount => _searchSuggestionButtons.Count;

    private void SetSearchSuggestionsVisible(bool visible)
    {
        _searchSuggestFadeTimer?.Stop();
        if (visible)
        {
            SearchSuggestPopup.Width = Math.Max(1, GlobalSearchBox.Bounds.Width);
            SearchSuggestPopup.IsVisible = true;
            SearchSuggestPopup.IsHitTestVisible = true;
            SearchSuggestFlyout.IsOpen = true;
            SearchSuggestPopup.Opacity = 1;
            return;
        }
        if (!SearchSuggestPopup.IsVisible) return;
        SearchSuggestPopup.IsHitTestVisible = false;
        SearchSuggestPopup.Opacity = 0;
        _searchSuggestFadeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(145), DispatcherPriority.Background, (_, _) =>
        {
            _searchSuggestFadeTimer?.Stop();
            if (!SearchSuggestPopup.IsHitTestVisible)
            {
                SearchSuggestPopup.IsVisible = false;
                SearchSuggestFlyout.IsOpen = false;
            }
        });
        _searchSuggestFadeTimer.Start();
    }

    private async void OnSearchBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        var txt = GlobalSearchBox.Text?.Trim() ?? "";
        ClearSearchBtn.IsVisible = txt.Length > 0;
        _suggestCts?.Cancel();
        if (txt.Length < 2 || !GlobalSearchBox.IsFocused)
        {
            SetSearchSuggestionsVisible(false);
            return;
        }
        var cts = new CancellationTokenSource();
        _suggestCts = cts;
        try
        {
            await Task.Delay(220, cts.Token);
            var list = await _yt.SuggestAsync(txt, cts.Token);
            if (cts.Token.IsCancellationRequested || list.Count == 0)
            {
                SetSearchSuggestionsVisible(false);
                return;
            }
            SearchSuggestList.Children.Clear();
            _searchSuggestionButtons.Clear();
            _searchSuggestionTexts.Clear();
            _searchSuggestionIndex = -1;
            foreach (var sug in list.Take(7))
            {
                var captured = sug;
                var btn = new Button
                {
                    Classes = { "navItem" },
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new PathIcon { Data = Icons.Search, Width = 13, Height = 13, Foreground = ResBrush("TextMutedBrush") },
                            new TextBlock { Text = captured, FontSize = 13 }
                        }
                    }
                };
                btn.Click += async (_, _) => await CommitSearchAsync(captured);
                _searchSuggestionButtons.Add(btn);
                _searchSuggestionTexts.Add(captured);
                SearchSuggestList.Children.Add(btn);
            }
            SetSearchSuggestionsVisible(true);
        }
        catch { }
    }

    private void MoveSearchSuggestionSelection(int direction)
    {
        if (_searchSuggestionButtons.Count == 0) return;
        _searchSuggestionIndex = _searchSuggestionIndex < 0
            ? (direction > 0 ? 0 : _searchSuggestionButtons.Count - 1)
            : (_searchSuggestionIndex + direction + _searchSuggestionButtons.Count) % _searchSuggestionButtons.Count;
        for (int i = 0; i < _searchSuggestionButtons.Count; i++)
        {
            bool selected = i == _searchSuggestionIndex;
            _searchSuggestionButtons[i].Background = selected ? ResBrush("BgHoverBrush") : Brushes.Transparent;
            _searchSuggestionButtons[i].BorderBrush = selected ? ResBrush("AccentBrush") : Brushes.Transparent;
            _searchSuggestionButtons[i].BorderThickness = selected ? new Thickness(1) : new Thickness(0);
        }
    }

    private async Task CommitSearchAsync(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;
        GlobalSearchBox.Text = query;
        SetSearchSuggestionsVisible(false);
        _searchSuggestionIndex = -1;
        _store.AddRecentSearch(query);
        await NavigateToAsync("search", () => ExecuteSearchViewAsync(query, "all"));
    }

    // --------------------------------------------------------- PC SPOTIFY-STYLE HOME, SEARCH & DETAIL VIEWS

    private static string GetTimeGreeting()
    {
        int hr = DateTime.Now.Hour;
        if (hr < 12) return "Good morning";
        if (hr < 18) return "Good afternoon";
        return "Good evening";
    }

    private async void OnCenterScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_activeNavKey != "home" || _loadingMoreHome || string.IsNullOrEmpty(_homeContinuation) || _homeShelvesContainer is null)
            return;

        double maxOffset = CenterScrollViewer.Extent.Height - CenterScrollViewer.Viewport.Height;
        if (maxOffset > 40 && CenterScrollViewer.Offset.Y >= maxOffset - 360)
            await AppendNextHomeContinuationAsync();
    }

    private async Task AppendNextHomeContinuationAsync()
    {
        if (_loadingMoreHome || string.IsNullOrEmpty(_homeContinuation) || _homeShelvesContainer is null)
            return;

        _loadingMoreHome = true;
        if (_homeLoadMoreBtn is not null)
        {
            _homeLoadMoreBtn.Content = "Loading more music…";
            _homeLoadMoreBtn.IsEnabled = false;
        }

        try
        {
            var next = await _yt.GetHomeAsync(continuation: _homeContinuation);
            _homeContinuation = next.Continuation;

            int added = 0;
            foreach (var shelf in next.Shelves)
            {
                ApplyDiscoveryExclusions(shelf);
                if (shelf.Tracks.Count == 0 && shelf.Cards.Count == 0) continue;
                if (string.IsNullOrWhiteSpace(shelf.Title) || _homeSeenTitles.Add(shelf.Title))
                {
                    _homeShelvesContainer.Children.Add(BuildShelfElement(shelf));
                    added++;
                }
            }

            if (added == 0 || string.IsNullOrEmpty(_homeContinuation))
            {
                _homeContinuation = null;
                if (_homeLoadMoreBtn is not null) _homeLoadMoreBtn.IsVisible = false;
            }
            else if (_homeLoadMoreBtn is not null)
            {
                _homeLoadMoreBtn.Content = "Load more shelves";
                _homeLoadMoreBtn.IsEnabled = true;
            }
        }
        catch
        {
            if (_homeLoadMoreBtn is not null)
            {
                _homeLoadMoreBtn.Content = "Load more shelves";
                _homeLoadMoreBtn.IsEnabled = true;
            }
        }
        finally
        {
            _loadingMoreHome = false;
        }
    }

    private async Task<List<ShelfSection>> GetHomeMoodGenresQuietlyAsync()
    {
        if (_homeMoodGenresCache is { Count: > 0 }) return _homeMoodGenresCache;
        var best = new List<ShelfSection>();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(28));
                var client = attempt == 0 ? _yt : new InnerTubeClient
                {
                    RegionGl = _store.Settings.RegionGl,
                    LanguageHl = _store.Settings.LanguageHl,
                };
                if (attempt > 0 && !string.IsNullOrWhiteSpace(_store.Settings.CookieHeader))
                    client.SetCookies(_store.Settings.CookieHeader, _store.Settings.DataSyncId);
                var shelves = await client.GetMoodAndGenresAsync(cts.Token);
                if (shelves.Count > best.Count) best = shelves;
                if (shelves.Count >= 2)
                {
                    _homeMoodGenresCache = shelves;
                    return shelves;
                }
            }
            catch { }
            if (attempt == 0) await Task.Delay(700);
        }
        if (best.Count > 0) _homeMoodGenresCache = best;
        return best;
    }

    private static List<ShelfSection> EnsureHomeGenreShelf(List<ShelfSection> shelves, ShelfSection? fallback)
    {
        if (shelves.Any(s => s.Title.Equals("Genres", StringComparison.OrdinalIgnoreCase))) return shelves;
        if (fallback is not null && (fallback.Cards.Count > 0 || fallback.Tracks.Count > 0)) shelves.Add(fallback);
        return shelves;
    }

    private void AppendHomeMoodGenres(IEnumerable<ShelfSection> shelves, StackPanel host)
    {
        if (!ReferenceEquals(_homeShelvesContainer, host)) return;
        foreach (var shelf in shelves)
        {
            if (shelf.Cards.Count == 0 && shelf.Tracks.Count == 0) continue;
            if (!string.IsNullOrWhiteSpace(shelf.Title) && !_homeSeenTitles.Add(shelf.Title)) continue;
            ApplyDiscoveryExclusions(shelf);
            host.Children.Add(BuildShelfElement(shelf));
        }
    }

    private async Task AppendHomeMoodGenresWhenReadyAsync(Task<List<ShelfSection>> pending, StackPanel host, ShelfSection? fallbackGenre)
    {
        try
        {
            var shelves = EnsureHomeGenreShelf(await pending.ConfigureAwait(false), fallbackGenre);
            Dispatcher.UIThread.Post(() =>
            {
                if (_activeNavKey == "home" && ReferenceEquals(_homeShelvesContainer, host))
                    AppendHomeMoodGenres(shelves, host);
            });
        }
        catch { }
    }

    internal async Task LoadHomeViewAsync(string? moodParams, string? moodBrowseId = null)
    {
        int loadGeneration = ++_homeLoadGeneration;
        bool IsCurrentHomeRender() => loadGeneration == _homeLoadGeneration && _activeNavKey == "home";
        _heroAutoTimer?.Stop();
        _activeMoodParams = moodParams;
        _activeMoodBrowseId = moodBrowseId;
        _homeContinuation = null;
        _homeShelvesContainer = null;
        _homeLoadMoreBtn = null;
        _homeSeenTitles.Clear();

        ShowLoadingSkeleton("Loading your music...");
        try
        {
            var feedTask = _yt.GetHomeAsync(moodParams, continuation: null, moodBrowseId: moodBrowseId);
            var brainTask = string.IsNullOrEmpty(moodParams)
                ? _extras.BuildEchoBrainShelfAsync(_store)
                : Task.FromResult<ShelfSection?>(null);
            var moodGenresTask = string.IsNullOrEmpty(moodParams)
                ? GetHomeMoodGenresQuietlyAsync()
                : Task.FromResult(new List<ShelfSection>());
            // Keep Home responsive; mood/genre browse art is appended as soon as its slower endpoint returns.
            await Task.WhenAll(feedTask, brainTask);
            if (!IsCurrentHomeRender()) return;

            var feed = feedTask.Result;
            foreach (var shelf in feed.Shelves) ApplyDiscoveryExclusions(shelf);
            var fallbackGenreShelf = feed.Shelves.FirstOrDefault(s => s.Title.Equals("Genres", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(moodParams)) _cachedHome = feed;
            else if (_cachedHome is not null && feed.MoodChips.Count == 0) feed.MoodChips = _cachedHome.MoodChips;

            if (!feed.Shelves.Any(s => s.Tracks.Count > 0))
            {
                try
                {
                    var qp = await _yt.SearchAsync(_store.Settings.RegionGl == "IN" ? "top hindi & english hits" : "top global hits", "songs");
                    if (!IsCurrentHomeRender()) return;
                    var quickPicks = qp.Tracks.Where(t => (!_store.Settings.HideVideoSongs || !t.IsVideo) && !_store.IsTrackExcluded(t)).Take(12).ToList();
                    if (quickPicks.Count > 0)
                        feed.Shelves.Insert(0, new ShelfSection { Title = "Quick Picks", Tracks = quickPicks });
                }
                catch { }
            }

            if (_dockOpen && _dockTab == "now" && _audio.CurrentTrack is null)
                RenderRightDock();

            var root = new StackPanel { Spacing = 22 };

            // 1. Spotify Desktop Mood Filter Pills + Greeting (Strictly music moods - ZERO broken "Podcasts" chip!)
            var chips = feed.MoodChips.Count > 0
                ? feed.MoodChips
                : new List<MoodChip>
                {
                    new() { Title = "Feel good" },
                    new() { Title = "Romance" },
                    new() { Title = "Relax" },
                    new() { Title = "Party" },
                    new() { Title = "Energize" },
                    new() { Title = "Workout" },
                    new() { Title = "Focus" },
                };

            var chipScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var allBtn = new Button
            {
                Classes = { string.IsNullOrEmpty(moodParams) ? "accentBtn" : "pill" },
                Content = "All",
                Padding = new Thickness(15, 6),
                FontSize = 13,
            };
            allBtn.Click += async (_, _) => { if (!string.IsNullOrEmpty(moodParams)) await LoadHomeViewAsync(null); };
            chipRow.Children.Add(allBtn);

            foreach (var chip in chips.Take(10))
            {
                var captured = chip;
                bool sel = !string.IsNullOrEmpty(moodParams) && moodParams == captured.Params;
                var b = new Button
                {
                    Classes = { sel ? "accentBtn" : "pill" },
                    Content = captured.Title,
                    Padding = new Thickness(15, 6),
                    FontSize = 13,
                };
                b.Click += async (_, _) =>
                {
                    if (string.IsNullOrEmpty(captured.Params))
                        await ExecuteSearchViewAsync(captured.Title + " music", "songs");
                    else if (sel)
                        await LoadHomeViewAsync(null);
                    else
                        await LoadHomeViewAsync(captured.Params, captured.BrowseId);
                };
                chipRow.Children.Add(b);
            }
            chipScroll.Content = chipRow;
            root.Children.Add(chipScroll);

            root.Children.Add(new TextBlock
            {
                Text = GetTimeGreeting(),
                FontSize = 26,
                FontWeight = FontWeight.ExtraBold,
                Margin = new Thickness(0, -4, 0, -4),
            });

            // 2. Wide Featured Spotlight Carousel, mirroring the mobile Home hero above the shelves
            var heroCandidates = feed.Shelves.SelectMany(s => s.Tracks).Where(t => !string.IsNullOrEmpty(t.Thumbnail)).DistinctBy(t => t.Id).Take(12).ToList();
            if (heroCandidates.Count > 0)
            {
                _heroAutoTimer?.Stop();
                var bannerHost = new Border { Opacity = 1 };
                int fadeGeneration = 0;
                void RenderBannerAt(int idx, bool animate = true)
                {
                    _heroCarouselIndex = ((idx % heroCandidates.Count) + heroCandidates.Count) % heroCandidates.Count;
                    var next = BuildDesktopSpotlightBanner(heroCandidates, _heroCarouselIndex, i => RenderBannerAt(i));
                    if (!animate || bannerHost.Child is null)
                    {
                        bannerHost.Child = next;
                        bannerHost.Opacity = 1;
                        return;
                    }
                    int generation = ++fadeGeneration;
                    bool swapped = false;
                    var started = DateTime.UtcNow;
                    DispatcherTimer? fade = null;
                    fade = new DispatcherTimer(AnimationFrameInterval, DispatcherPriority.Render, (_, _) =>
                    {
                        if (generation != fadeGeneration) { fade!.Stop(); return; }
                        double progress = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / 360.0, 0, 1);
                        if (progress < 0.5) bannerHost.Opacity = 1 - progress * 2;
                        else
                        {
                            if (!swapped) { bannerHost.Child = next; swapped = true; }
                            bannerHost.Opacity = (progress - 0.5) * 2;
                        }
                        if (progress < 1) return;
                        bannerHost.Opacity = 1;
                        fade!.Stop();
                    });
                    fade.Start();
                }
                RenderBannerAt(_heroCarouselIndex, animate: false);
                root.Children.Add(bannerHost);
                _heroAutoTimer = new DispatcherTimer(TimeSpan.FromSeconds(7), DispatcherPriority.Background, (_, _) =>
                {
                    if (_activeNavKey == "home" && heroCandidates.Count > 1) RenderBannerAt(_heroCarouselIndex + 1);
                });
                if (heroCandidates.Count > 1) _heroAutoTimer.Start();
            }


            // 3. Signature Official Spotify Desktop 2-Column Compact Quick-Access Tiles (up to 8 tiles = 2×4 grid!)
            var quickPool = feed.Shelves.SelectMany(s => s.Tracks).DistinctBy(t => t.Id).Take(8).ToList();
            if (quickPool.Count > 0)
            {
                var quickGrid = new UniformGrid { Columns = 2 };
                for (int i = 0; i < quickPool.Count; i++)
                {
                    int qIdx = i;
                    var tr = quickPool[i];
                    var qCard = new Border
                    {
                        Height = 56,
                        Background = ResBrush("BgCardBrush"),
                        CornerRadius = new CornerRadius(6),
                        ClipToBounds = true,
                        Margin = new Thickness(i % 2 == 0 ? 0 : 5, 0, i % 2 == 0 ? 5 : 0, 8),
                        Cursor = new Cursor(StandardCursorType.Hand),
                    };
                    qCard.PointerEntered += (_, _) => qCard.Background = ResBrush("BgHoverBrush");
                    qCard.PointerExited += (_, _) => qCard.Background = ResBrush("BgCardBrush");

                    var qRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("56,*,Auto") };
                    var qImg = new Image { Width = 56, Height = 56, Stretch = Stretch.UniformToFill };
                    LoadImageInto(qImg, tr.Thumbnail);
                    qRow.Children.Add(new Border { Width = 56, Height = 56, Background = ResBrush("BgSurfaceBrush"), Child = qImg });

                    var qMeta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0), Spacing = 1, ClipToBounds = true };
                    qMeta.Children.Add(new TextBlock
                    {
                        Text = tr.Title,
                        FontSize = 13.5,
                        FontWeight = FontWeight.Bold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    qMeta.Children.Add(new TextBlock
                    {
                        Text = tr.ArtistNames,
                        FontSize = 11.5,
                        FontWeight = FontWeight.Medium,
                        Foreground = ResBrush("TextSecondaryBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    Grid.SetColumn(qMeta, 1);
                    qRow.Children.Add(qMeta);

                    var playBadge = new Border
                    {
                        Width = 30,
                        Height = 30,
                        CornerRadius = new CornerRadius(15),
                        Background = ResBrush("AccentBrush"),
                        Margin = new Thickness(0, 0, 12, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new PathIcon { Data = Icons.Play, Width = 12, Height = 12, Foreground = ResBrush("OnAccentBrush") },
                    };
                    Grid.SetColumn(playBadge, 2);
                    qRow.Children.Add(playBadge);

                    qCard.Child = qRow;
                    qCard.PointerPressed += async (_, ev) =>
                    {
                        if (ev.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                            await _audio.PlayQueueAsync(quickPool, qIdx);
                    };
                    quickGrid.Children.Add(qCard);
                }
                root.Children.Add(quickGrid);
            }

            // 4. Made For You / Forgotten Favorites (Echo Brain)
            if (brainTask.Result is { Tracks.Count: > 0 } brainShelf)
            {
                brainShelf.Title = "Made For You";
                root.Children.Add(BuildShelfElement(brainShelf));
            }

            // 5. All Home Shelves + Infinite-Scroll Continuation Container (10–14+ shelves on PC!)
            var shelvesStack = new StackPanel { Spacing = 24 };
            _homeShelvesContainer = shelvesStack;
            bool moodGenresAdded = false;
            foreach (var shelf in feed.Shelves)
            {
                if (string.IsNullOrEmpty(moodParams) && shelf.Title.Equals("Genres", StringComparison.OrdinalIgnoreCase)) continue;
                if (shelf.Tracks.Count == 0 && shelf.Cards.Count == 0) continue;
                if (!string.IsNullOrWhiteSpace(shelf.Title)) _homeSeenTitles.Add(shelf.Title);
                shelvesStack.Children.Add(BuildShelfElement(shelf));
                if (!moodGenresAdded && shelf.Title.Contains("Similar to", StringComparison.OrdinalIgnoreCase))
                {
                    if (moodGenresTask.IsCompletedSuccessfully)
                        AppendHomeMoodGenres(EnsureHomeGenreShelf(moodGenresTask.Result, fallbackGenreShelf), shelvesStack);
                    moodGenresAdded = true;
                }
            }
            if (!moodGenresAdded && moodGenresTask.IsCompletedSuccessfully)
                AppendHomeMoodGenres(EnsureHomeGenreShelf(moodGenresTask.Result, fallbackGenreShelf), shelvesStack);
            root.Children.Add(shelvesStack);
            if (!moodGenresTask.IsCompleted)
                _ = AppendHomeMoodGenresWhenReadyAsync(moodGenresTask, shelvesStack, fallbackGenreShelf);

            _homeContinuation = feed.Continuation;
            if (!string.IsNullOrEmpty(_homeContinuation))
            {
                var moreBtn = new Button
                {
                    Classes = { "pill" },
                    Content = "Load more shelves",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(22, 9),
                    Margin = new Thickness(0, 6, 0, 8),
                };
                moreBtn.Click += async (_, _) => await AppendNextHomeContinuationAsync();
                _homeLoadMoreBtn = moreBtn;
                root.Children.Add(moreBtn);
            }

            if (feed.Shelves.Count == 0)
                root.Children.Add(EmptyHint("No shelves returned right now - search any song, artist, or album above."));

            if (!IsCurrentHomeRender()) return;
            SetCenterContent(root);
        }
        catch (Exception ex)
        {
            if (IsCurrentHomeRender())
                SetCenterContent(ErrorCard("Could not load Home feed", ex.Message, () => _ = LoadHomeViewAsync(moodParams, moodBrowseId)));
        }
    }

    private Control BuildDesktopSpotlightBanner(List<TrackItem> candidates, int curIdx, Action<int> onStep)
    {
        int n = candidates.Count;
        var mainTrack = candidates[curIdx];

        var card = new Border
        {
            Background = ResBrush("BgCardBrush"),
            BorderBrush = ResBrush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18),
            MinHeight = 194,
            ClipToBounds = true,
        };

        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("220,*,Auto") };

        var artImg = new Image { Width = 220, Height = 158, Stretch = Stretch.UniformToFill };
        LoadImageInto(artImg, InnertubeParsers.UpgradeThumb(mainTrack.Thumbnail, 544));
        grid.Children.Add(new Border
        {
            Width = 220,
            Height = 158,
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true,
            Background = ResBrush("BgSurfaceBrush"),
            BoxShadow = BoxShadows.Parse("0 10 28 0 #55000000"),
            Child = artImg,
        });

        var info = new StackPanel { Margin = new Thickness(20, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 6, ClipToBounds = true };
        info.Children.Add(new TextBlock
        {
            Text = "FEATURED SPOTLIGHT",
            FontSize = 10.5,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.0,
            Foreground = ResBrush("AccentBrush"),
        });
        info.Children.Add(new TextBlock
        {
            Text = mainTrack.Title,
            FontSize = 22,
            FontWeight = FontWeight.ExtraBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        info.Children.Add(new TextBlock
        {
            Text = mainTrack.ArtistNames,
            FontSize = 13.5,
            Foreground = ResBrush("TextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 6, 0, 0) };
        actions.Children.Add(BuildIconActionButton(Icons.Play, "Play spotlight", async () => await _audio.PlayQueueAsync(candidates, curIdx), accent: true));
        bool isLiked = _store.IsLiked(mainTrack.Id);
        Button saveBtn = null!;
        saveBtn = BuildIconActionButton(isLiked ? Icons.CheckCircle : Icons.HeartOutline, isLiked ? "Remove from Liked Songs" : "Save to Liked Songs", () =>
        {
            bool liked = _store.ToggleLike(mainTrack, _yt);
            ((PathIcon)saveBtn.Content!).Data = liked ? Icons.CheckCircle : Icons.HeartOutline;
            ToolTip.SetTip(saveBtn, liked ? "Remove from Liked Songs" : "Save to Liked Songs");
            UpdateNowPlayingUi();
        });
        actions.Children.Add(saveBtn);
        Button moreOptionsBtn = null!;
        moreOptionsBtn = BuildIconActionButton(Icons.More, "Spotlight options", () => OpenTrackContextMenu(mainTrack, null, moreOptionsBtn, PlacementMode.Bottom));
        actions.Children.Add(moreOptionsBtn);
        info.Children.Add(actions);

        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        if (n > 1)
        {
            var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
            var prevB = new Button { Classes = { "topNavCircle" }, Content = "‹", FontSize = 16 };
            var nextB = new Button { Classes = { "topNavCircle" }, Content = "›", FontSize = 16 };
            prevB.Click += (_, _) => onStep(curIdx - 1);
            nextB.Click += (_, _) => onStep(curIdx + 1);
            nav.Children.Add(prevB);
            nav.Children.Add(nextB);
            Grid.SetColumn(nav, 2);
            grid.Children.Add(nav);
        }

        card.Child = grid;
        return card;
    }

    internal async Task LoadExploreViewAsync()
    {
        ShowLoadingSkeleton("Loading Charts & Browse...");
        try
        {
            var shelves = await _yt.GetExploreAndChartsAsync();
            var root = new StackPanel { Spacing = 24 };

            var hdrStack = new StackPanel { Spacing = 3 };
            hdrStack.Children.Add(new TextBlock { Text = "Search & Charts", FontSize = 26, FontWeight = FontWeight.ExtraBold });
            hdrStack.Children.Add(new TextBlock { Text = $"Top 100 Charts & Trending Releases  •  Region: {_store.Settings.RegionGl}", FontSize = 13, Foreground = ResBrush("TextSecondaryBrush") });
            root.Children.Add(hdrStack);

            var chartTracks = shelves.SelectMany(s => s.Tracks).DistinctBy(t => t.Id).Take(100).ToList();
            if (chartTracks.Count == 0 && _cachedHome is not null)
                chartTracks = _cachedHome.Shelves.SelectMany(s => s.Tracks).DistinctBy(t => t.Id).Take(100).ToList();

            if (chartTracks.Count > 0)
            {
                int pageSize = 50;
                int totalPages = Math.Max(1, (int)Math.Ceiling(chartTracks.Count / (double)pageSize));
                var chartHost = new StackPanel { Spacing = 10 };

                void RenderChartPage(int pIdx)
                {
                    _chartPageIndex = Math.Clamp(pIdx, 0, totalPages - 1);
                    chartHost.Children.Clear();

                    var chartHdr = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
                    chartHdr.Children.Add(new TextBlock { Text = "Top Songs Chart", FontSize = 18, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });

                    var pagerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                    var prevP = new Button { Classes = { "topNavCircle" }, Content = "‹", FontSize = 16, IsEnabled = _chartPageIndex > 0 };
                    var nextP = new Button { Classes = { "topNavCircle" }, Content = "›", FontSize = 16, IsEnabled = _chartPageIndex < totalPages - 1 };
                    prevP.Click += (_, _) => RenderChartPage(_chartPageIndex - 1);
                    nextP.Click += (_, _) => RenderChartPage(_chartPageIndex + 1);
                    pagerRow.Children.Add(prevP);
                    pagerRow.Children.Add(new TextBlock { Text = $"{_chartPageIndex + 1} / {totalPages}", FontSize = 12.5, Foreground = ResBrush("TextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });
                    pagerRow.Children.Add(nextP);
                    Grid.SetColumn(pagerRow, 1);
                    chartHdr.Children.Add(pagerRow);
                    chartHost.Children.Add(chartHdr);

                    var groupBox = new Border
                    {
                        Background = ResBrush("BgCardBrush"),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(8, 6),
                    };
                    var chartColumns = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*") };
                    var leftRows = new StackPanel { Spacing = 2 };
                    var rightRows = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0, 0, 0) };
                    var slice = chartTracks.Skip(_chartPageIndex * pageSize).Take(pageSize).ToList();
                    int leftCount = (slice.Count + 1) / 2;
                    for (int i = 0; i < slice.Count; i++)
                    {
                        int globalRank = _chartPageIndex * pageSize + i + 1;
                        var tr = slice[i];
                        var rowGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("22,34,*,44,Auto"), Margin = new Thickness(3, 3) };
                        rowGrid.Children.Add(new TextBlock
                        {
                            Text = globalRank.ToString(),
                            FontSize = 14,
                            FontWeight = FontWeight.Bold,
                            Foreground = ResBrush("TextSecondaryBrush"),
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        });
                        var img = new Image { Width = 34, Height = 34, Stretch = Stretch.UniformToFill };
                        LoadImageInto(img, tr.Thumbnail);
                        var art = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(5), ClipToBounds = true, Background = ResBrush("BgSurfaceBrush"), Child = img };
                        Grid.SetColumn(art, 1);
                        rowGrid.Children.Add(art);

                        var meta = new StackPanel { Margin = new Thickness(6, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 1, ClipToBounds = true };
                        meta.Children.Add(new ScrollingTitleTextBlock { Text = tr.Title, FontSize = 13.5, FontWeight = FontWeight.SemiBold });
                        meta.Children.Add(new TextBlock { Text = tr.ArtistNames, FontSize = 12, Foreground = ResBrush("TextSecondaryBrush") });
                        Grid.SetColumn(meta, 2);
                        rowGrid.Children.Add(meta);

                        var dur = new TextBlock { Text = tr.FormattedDuration, FontSize = 12, Foreground = ResBrush("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
                        Grid.SetColumn(dur, 3);
                        rowGrid.Children.Add(dur);

                        var menu = BuildTrackMoreMenuButton(tr);
                        Grid.SetColumn(menu, 4);
                        rowGrid.Children.Add(menu);

                        var itemWrap = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(4, 2), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = rowGrid };
                        int playIdx = globalRank - 1;
                        itemWrap.PointerPressed += async (_, ev) =>
                        {
                            if (ev.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                                await _audio.PlayQueueAsync(chartTracks, playIdx);
                        };
                        AttachTrackRowInteractions(itemWrap, tr, moreButton: menu);
                        int targetColumn = i < leftCount ? 0 : 1;
                        Grid.SetColumn(itemWrap, targetColumn);
                        (targetColumn == 0 ? leftRows : rightRows).Children.Add(itemWrap);
                    }
                    Grid.SetColumn(leftRows, 0);
                    Grid.SetColumn(rightRows, 1);
                    chartColumns.Children.Add(leftRows);
                    chartColumns.Children.Add(rightRows);
                    groupBox.Child = chartColumns;
                    chartHost.Children.Add(groupBox);
                }

                RenderChartPage(0);
                root.Children.Add(chartHost);
            }

            foreach (var s in shelves.Where(s => s.Cards.Count > 0))
                root.Children.Add(BuildShelfElement(s));

            SetCenterContent(root);
        }
        catch (Exception ex)
        {
            SetCenterContent(ErrorCard("Explore failed", ex.Message, () => _ = LoadExploreViewAsync()));
        }
    }

    private void ApplyDiscoveryExclusions(ShelfSection shelf)
    {
        shelf.Tracks = shelf.Tracks.Where(t => (!_store.Settings.HideVideoSongs || !t.IsVideo) && !_store.IsTrackExcluded(t)).ToList();
        shelf.Cards = shelf.Cards.Where(c => !_store.IsMediaCardExcluded(c)).ToList();
    }

    private void ApplyDiscoveryExclusions(SearchPageResult result)
    {
        result.Tracks = result.Tracks.Where(t => !_store.IsTrackExcluded(t)).ToList();
        result.Videos = result.Videos.Where(t => !_store.IsTrackExcluded(t)).ToList();
        result.Albums = result.Albums.Where(c => !_store.IsMediaCardExcluded(c)).ToList();
        result.Artists = result.Artists.Where(c => !_store.IsMediaCardExcluded(c)).ToList();
        if (result.TopResultTrack is { } top && _store.IsTrackExcluded(top)) result.TopResultTrack = null;
        if (result.TopResultCard is { } topCard && _store.IsMediaCardExcluded(topCard)) result.TopResultCard = null;
    }

    private void ToggleDiscoveryExclusion(string kind, string? id, string name, string subtitle)
    {
        bool excluded = _store.ToggleContentExclusion(kind, id, name, subtitle);
        _audio.RemoveDiscoveryExcludedQueuedTracks();
        ShowToast(excluded ? $"{name} excluded from discovery" : $"{name} restored to discovery");
        if (_activeNavKey == "search" && !string.IsNullOrWhiteSpace(_activeSearchQuery))
            _ = ExecuteSearchViewAsync(_activeSearchQuery, _activeSearchFilter);
        else if (_activeNavKey == "home")
            _ = LoadHomeViewAsync(_activeMoodParams, _activeMoodBrowseId);
    }

    internal async Task ExecuteSearchViewAsync(string query, string filter)
    {
        _activeSearchQuery = query;
        _activeSearchFilter = filter;
        ShowLoadingSkeleton($"Searching for \"{query}\"...");
        try
        {
            var res = await _yt.SearchAsync(query, filter);
            ApplyDiscoveryExclusions(res);
            var root = new StackPanel { Spacing = 22 };

            root.Children.Add(new TextBlock { Text = $"Results for \"{query}\"", FontSize = 24, FontWeight = FontWeight.ExtraBold });

            var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var (fid, label) in new[] { ("all", "All"), ("songs", "Songs"), ("videos", "Videos"), ("albums", "Albums"), ("artists", "Artists"), ("playlists", "Playlists") })
            {
                var cf = fid;
                var b = new Button
                {
                    Classes = { string.Equals(filter, cf, StringComparison.OrdinalIgnoreCase) ? "accentBtn" : "pill" },
                    Content = label,
                    Padding = new Thickness(15, 6),
                };
                b.Click += async (_, _) => await ExecuteSearchViewAsync(query, cf);
                filterRow.Children.Add(b);
            }
            root.Children.Add(filterRow);

            var songs = res.Tracks.Where(t => !_store.Settings.HideVideoSongs || !t.IsVideo).ToList();
            var videos = _store.Settings.HideVideoSongs ? new List<TrackItem>() : res.Videos;

            // Combine playable tracks (Top Result + Songs + Videos) for immediate Spotify-Desktop "Top result + Songs" 2-column hero!
            var combinedPlayable = songs.Concat(videos).DistinctBy(t => t.Id).ToList();

            bool isAllFilter = string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase);
            bool hiddenVideoTopResult = _store.Settings.HideVideoSongs && res.TopResultTrack?.IsVideo == true;
            bool allowedTopCard = !hiddenVideoTopResult && res.TopResultCard is not null;
            if (isAllFilter && (!hiddenVideoTopResult || combinedPlayable.Count > 0) &&
                (allowedTopCard || (!hiddenVideoTopResult && res.TopResultTrack is not null) || combinedPlayable.Count > 0))
            {
                root.Children.Add(BuildSearchTopSummaryGrid(res, combinedPlayable));
            }

            if (songs.Count > 0)
            {
                root.Children.Add(SectionHeading("Songs", () => _ = _audio.PlayQueueAsync(songs, 0)));
                root.Children.Add(BuildTrackListTable(songs, showIndex: false));
            }

            if (videos.Count > 0)
            {
                // Keep Music Videos high in relevance order (matching OG Echo APK SearchSummaryPage!) so video-released tracks are immediately visible
                var vidSlice = isAllFilter
                    ? videos.Where(v => songs.All(s => s.Id != v.Id)).Take(8).ToList()
                    : videos;
                if (vidSlice.Count > 0)
                {
                    root.Children.Add(SectionHeading("Music Videos", () => _ = _audio.PlayQueueAsync(videos, 0)));
                    root.Children.Add(BuildTrackListTable(vidSlice, showIndex: false));
                }
            }

            if (res.Artists.Count > 0)
                root.Children.Add(BuildShelfElement(new ShelfSection { Title = "Artists", Cards = res.Artists }));
            if (res.Albums.Count > 0)
                root.Children.Add(BuildShelfElement(new ShelfSection { Title = "Albums", Cards = res.Albums }));
            if (res.Playlists.Count > 0)
                root.Children.Add(BuildShelfElement(new ShelfSection { Title = "Playlists", Cards = res.Playlists }));

            if (!string.IsNullOrEmpty(res.Continuation))
            {
                string? nextCont = res.Continuation;
                var seenMoreTrackIds = new HashSet<string>(songs.Concat(videos).Select(t => t.Id), StringComparer.Ordinal);
                var seenMoreCardIds = new HashSet<string>(res.Albums.Concat(res.Artists).Concat(res.Playlists).Select(c => c.Kind + ":" + c.Id), StringComparer.Ordinal);
                var extraHost = new StackPanel { Spacing = 4 };
                root.Children.Add(extraHost);

                var moreSearchBtn = new Button
                {
                    Classes = { "pill" },
                    Content = "Load more results",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(20, 8),
                };
                moreSearchBtn.Click += async (_, _) =>
                {
                    if (string.IsNullOrEmpty(nextCont)) return;
                    moreSearchBtn.IsEnabled = false;
                    moreSearchBtn.Content = "Loading…";
                    try
                    {
                        var requestedContinuation = nextCont;
                        var more = await _yt.SearchAsync(query, filter, requestedContinuation);
                        ApplyDiscoveryExclusions(more);
                        nextCont = string.Equals(more.Continuation, requestedContinuation, StringComparison.Ordinal)
                            ? null
                            : more.Continuation;
                        var moreTracks = (_store.Settings.HideVideoSongs ? more.Tracks.Where(t => !t.IsVideo) : more.Tracks)
                            .Concat(_store.Settings.HideVideoSongs ? Enumerable.Empty<TrackItem>() : more.Videos)
                            .Where(t => seenMoreTrackIds.Add(t.Id)).ToList();
                        if (moreTracks.Count > 0)
                            extraHost.Children.Add(BuildTrackListTable(moreTracks, showIndex: false));
                        var moreArtists = more.Artists.Where(c => seenMoreCardIds.Add(c.Kind + ":" + c.Id)).ToList();
                        var moreAlbums = more.Albums.Where(c => seenMoreCardIds.Add(c.Kind + ":" + c.Id)).ToList();
                        var morePlaylists = more.Playlists.Where(c => seenMoreCardIds.Add(c.Kind + ":" + c.Id)).ToList();
                        if (moreArtists.Count > 0)
                            extraHost.Children.Add(BuildShelfElement(new ShelfSection { Title = "More Artists", Cards = moreArtists }));
                        if (moreAlbums.Count > 0)
                            extraHost.Children.Add(BuildShelfElement(new ShelfSection { Title = "More Albums", Cards = moreAlbums }));
                        if (morePlaylists.Count > 0)
                            extraHost.Children.Add(BuildShelfElement(new ShelfSection { Title = "More Playlists", Cards = morePlaylists }));
                        if (moreTracks.Count == 0 && moreArtists.Count == 0 && moreAlbums.Count == 0 && morePlaylists.Count == 0)
                            ShowToast("No additional results came back from YouTube Music.");
                        if (string.IsNullOrEmpty(nextCont)) moreSearchBtn.IsVisible = false;
                        else { moreSearchBtn.Content = "Load more results"; moreSearchBtn.IsEnabled = true; }
                    }
                    catch (Exception ex)
                    {
                        moreSearchBtn.Content = "Load more results";
                        moreSearchBtn.IsEnabled = true;
                        ShowToast($"Couldn't load more results: {ex.Message}");
                    }
                };
                root.Children.Add(moreSearchBtn);
            }

            if (songs.Count == 0 && res.Albums.Count == 0 && res.Artists.Count == 0 && res.Playlists.Count == 0 && videos.Count == 0)
                root.Children.Add(EmptyHint($"No matches found for \"{query}\"."));

            SetCenterContent(root);
        }
        catch (Exception ex)
        {
            SetCenterContent(ErrorCard("Search failed", ex.Message, () => _ = ExecuteSearchViewAsync(query, filter)));
        }
    }

    private Control BuildSearchTopSummaryGrid(SearchPageResult res, List<TrackItem> playablePool)
    {
        var topGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("340,*"), Margin = new Thickness(0, 2, 0, 2) };

        // LEFT: Official Spotify Desktop "Top result" Card
        var leftStack = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 18, 0) };
        leftStack.Children.Add(new TextBlock { Text = "Top result", FontSize = 20, FontWeight = FontWeight.Bold });

        bool hiddenVideoTopResult = _store.Settings.HideVideoSongs && res.TopResultTrack?.IsVideo == true;
        var topCard = hiddenVideoTopResult ? null : res.TopResultCard;
        var topTrack = hiddenVideoTopResult ? null : res.TopResultTrack;
        topTrack ??= topCard is null ? playablePool.FirstOrDefault() : null;

        var heroCard = new Border
        {
            Background = ResBrush("BgCardBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18),
            MinHeight = 208,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        heroCard.PointerEntered += (_, _) => heroCard.Background = ResBrush("BgHoverBrush");
        heroCard.PointerExited += (_, _) => heroCard.Background = ResBrush("BgCardBrush");

        var cardGrid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto") };
        string artUrl = topTrack?.Thumbnail ?? topCard?.Thumbnail ?? "";
        bool roundArt = topCard?.Kind == "artist";
        var artImg = new Image { Width = 88, Height = 88, Stretch = Stretch.UniformToFill };
        LoadImageInto(artImg, InnertubeParsers.UpgradeThumb(artUrl, 400));
        cardGrid.Children.Add(new Border
        {
            Width = 88,
            Height = 88,
            CornerRadius = new CornerRadius(roundArt ? 44 : 8),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = ResBrush("BgSurfaceBrush"),
            BoxShadow = BoxShadows.Parse("0 8 20 0 #44000000"),
            Child = artImg,
        });

        var titleText = topTrack?.Title ?? topCard?.Title ?? "";
        var subText = topTrack is not null ? topTrack.ArtistNames : (topCard?.Subtitle ?? "");
        var badgeLabel = !hiddenVideoTopResult && !string.IsNullOrEmpty(res.TopResultTypeLabel)
            ? res.TopResultTypeLabel
            : (topTrack is not null ? (topTrack.IsVideo ? "Video" : "Song") : (topCard?.Kind ?? "Result"));

        var bottomRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Margin = new Thickness(0, 14, 0, 0) };
        var metaStack = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Bottom, ClipToBounds = true, Margin = new Thickness(0, 0, 10, 0) };
        metaStack.Children.Add(new TextBlock
        {
            Text = titleText,
            FontSize = 20,
            FontWeight = FontWeight.ExtraBold,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var subRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(subText))
        {
            subRow.Children.Add(new TextBlock
            {
                Text = subText,
                FontSize = 13,
                FontWeight = FontWeight.Medium,
                Foreground = ResBrush("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 175,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        subRow.Children.Add(new Border
        {
            Background = ResBrush("BgSurfaceBrush"),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(9, 2),
            Child = new TextBlock { Text = badgeLabel, FontSize = 11, FontWeight = FontWeight.Bold }
        });
        metaStack.Children.Add(subRow);
        bottomRow.Children.Add(metaStack);

        var bigPlayCircle = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = ResBrush("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Bottom,
            BoxShadow = BoxShadows.Parse("0 6 16 0 #55000000"),
            Child = new PathIcon { Data = Icons.Play, Width = 17, Height = 17, Foreground = ResBrush("OnAccentBrush") },
        };
        Grid.SetColumn(bigPlayCircle, 1);
        bottomRow.Children.Add(bigPlayCircle);

        Grid.SetRow(bottomRow, 2);
        cardGrid.Children.Add(bottomRow);
        heroCard.Child = cardGrid;

        heroCard.PointerPressed += async (_, ev) =>
        {
            if (!ev.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (topTrack is not null)
            {
                int idx = Math.Max(0, playablePool.FindIndex(p => p.Id == topTrack.Id));
                var q = playablePool.Count > 0 ? playablePool : new List<TrackItem> { topTrack };
                await _audio.PlayQueueAsync(q, idx);
            }
            else if (topCard is not null)
            {
                if (topCard.Kind == "artist") await NavigateToAsync("artist:" + topCard.Id, () => LoadArtistDetailAsync(topCard.Id));
                else if (topCard.Kind == "album") await NavigateToAsync("album:" + topCard.Id, () => LoadAlbumDetailAsync(topCard.Id));
                else await NavigateToAsync("playlist:" + topCard.Id, () => LoadPlaylistDetailAsync(topCard.Id));
            }
        };

        leftStack.Children.Add(heroCard);
        topGrid.Children.Add(leftStack);

        // RIGHT: Top 4 Playable Matches (Spotify Desktop right-side quick tracks)
        var rightStack = new StackPanel { Spacing = 10 };
        rightStack.Children.Add(new TextBlock { Text = "Top matches", FontSize = 20, FontWeight = FontWeight.Bold });
        var rightBox = new StackPanel { Spacing = 2 };
        foreach (var tr in playablePool.Take(4))
        {
            var captured = tr;
            var rBorder = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            rBorder.PointerEntered += (_, _) => rBorder.Background = ResBrush("BgCardBrush");
            rBorder.PointerExited += (_, _) => rBorder.Background = Brushes.Transparent;

            var rg = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("42,*,50,Auto,Auto") };
            var rimg = new Image { Width = 40, Height = 40, Stretch = Stretch.UniformToFill };
            LoadImageInto(rimg, captured.Thumbnail);
            rg.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(5), ClipToBounds = true, Background = ResBrush("BgSurfaceBrush"), Child = rimg });

            var rmeta = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 1, ClipToBounds = true };
            rmeta.Children.Add(new ScrollingTitleTextBlock { Text = captured.Title, FontSize = 13.5, FontWeight = FontWeight.SemiBold });
            rmeta.Children.Add(new TextBlock { Text = captured.ArtistNames, FontSize = 12, FontWeight = FontWeight.Medium, Foreground = ResBrush("TextSecondaryBrush") });
            Grid.SetColumn(rmeta, 1);
            rg.Children.Add(rmeta);

            var rdur = new TextBlock { Text = captured.FormattedDuration, FontSize = 12, Foreground = ResBrush("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetColumn(rdur, 2);
            rg.Children.Add(rdur);

            var rlikeIcon = new PathIcon
            {
                Data = _store.IsLiked(captured.Id) ? Icons.HeartFilled : Icons.HeartOutline,
                Width = 14,
                Height = 14,
                Foreground = _store.IsLiked(captured.Id) ? ResBrush("AccentBrush") : ResBrush("TextMutedBrush"),
            };
            var rlikeBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(6), Content = rlikeIcon };
            rlikeBtn.Click += (_, ev) =>
            {
                ev.Handled = true;
                bool liked = _store.ToggleLike(captured, _yt);
                rlikeIcon.Data = liked ? Icons.HeartFilled : Icons.HeartOutline;
                rlikeIcon.Foreground = liked ? ResBrush("AccentBrush") : ResBrush("TextMutedBrush");
            };
            Grid.SetColumn(rlikeBtn, 3);
            rg.Children.Add(rlikeBtn);

            var rmenu = BuildTrackMoreMenuButton(captured);
            Grid.SetColumn(rmenu, 4);
            rg.Children.Add(rmenu);

            rBorder.Child = rg;
            rBorder.PointerPressed += async (_, ev) =>
            {
                if (ev.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    await _audio.PlayQueueAsync(playablePool, Math.Max(0, playablePool.IndexOf(captured)));
            };
            AttachTrackRowInteractions(rBorder, captured, moreButton: rmenu);
            rightBox.Children.Add(rBorder);
        }
        if (playablePool.Count == 0)
            rightBox.Children.Add(EmptyHint("Select the Top Result card on the left to open."));

        rightStack.Children.Add(rightBox);
        Grid.SetColumn(rightStack, 1);
        topGrid.Children.Add(rightStack);
        return topGrid;
    }

    internal async Task LoadAlbumDetailAsync(string browseId)
    {
        ShowLoadingSkeleton("Loading Album...");
        try
        {
            var page = await _yt.GetAlbumAsync(browseId);
            RenderDetailPage(page);
        }
        catch (Exception ex)
        {
            SetCenterContent(ErrorCard("Failed to load album", ex.Message, () => _ = LoadAlbumDetailAsync(browseId)));
        }
    }

    internal async Task LoadPlaylistDetailAsync(string id)
    {
        ShowLoadingSkeleton("Loading Playlist...");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var page = await _yt.GetPlaylistAsync(id, timeout.Token);
            RenderDetailPage(page);
        }
        catch (Exception ex)
        {
            string message = ex is OperationCanceledException ? "The request timed out. Please try again." : ex.Message;
            SetCenterContent(ErrorCard("Failed to load playlist", message, () => _ = LoadPlaylistDetailAsync(id)));
        }
    }

    internal async Task LoadArtistDetailAsync(string channelId)
    {
        ShowLoadingSkeleton("Loading Artist...");
        try
        {
            var page = await _yt.GetArtistAsync(channelId);
            RenderDetailPage(page);
        }
        catch (Exception ex)
        {
            SetCenterContent(ErrorCard("Failed to load artist", ex.Message, () => _ = LoadArtistDetailAsync(channelId)));
        }
    }

    internal void RenderLocalPlaylistDetail(LocalPlaylist pl)
    {
        // Keep fractional Spotify durations through the sum; rounding each row first can add seconds.
        var total = TimeSpan.FromMilliseconds(pl.Tracks.Sum(t => Math.Max(0, t.PlaylistDurationSeconds)) * 1000.0);
        string runtime = total.TotalHours >= 1 ? $"{(int)total.TotalHours} hr {total.Minutes} min" : $"{(int)total.TotalMinutes} min {total.Seconds} sec";
        var ownerLabel = !string.IsNullOrWhiteSpace(pl.OwnerName) ? $"By {pl.OwnerName}" : (pl.Source == "Spotify" ? "Spotify public playlist" : "Local playlist");
        var page = new DetailPageResult
        {
            Id = pl.Id, Kind = "local-playlist", Title = pl.Name,
            Subtitle = $"{ownerLabel} • {pl.Tracks.Count} songs • {runtime}",
            Description = pl.Description, Thumbnail = pl.CustomCoverPath,
            Tracks = pl.Tracks.ToList(), IsLocalPlaylist = true,
        };
        RenderDetailPage(page, pl);
    }

    private Control BuildLocalPlaylistCover(LocalPlaylist playlist, double size)
    {
        if (!string.IsNullOrWhiteSpace(playlist.CustomCoverPath) && File.Exists(playlist.CustomCoverPath))
        {
            try
            {
                var image = new Image { Source = new Bitmap(playlist.CustomCoverPath), Stretch = Stretch.UniformToFill };
                return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(Math.Max(3, size * 0.07)), ClipToBounds = true, Child = image };
            }
            catch { }
        }
        if (playlist.Tracks.Count >= 4)
            return BuildPlaylistCollage(playlist.Tracks, size);

        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(GetAppIconAssetUri(_store.Settings.AppIconStyle)));
            var icon = new Image
            {
                Source = new Bitmap(stream),
                Width = size * 0.64,
                Height = size * 0.64,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Color[][] playlistGradients =
            {
                new[] { Color.Parse("#5A2CA0"), Color.Parse("#F16D7A") },
                new[] { Color.Parse("#175A87"), Color.Parse("#26B6A3") },
                new[] { Color.Parse("#8A3F78"), Color.Parse("#F2A65A") },
                new[] { Color.Parse("#3A5B7A"), Color.Parse("#6ED0C0") },
                new[] { Color.Parse("#9A3F5D"), Color.Parse("#ED7E52") },
                new[] { Color.Parse("#4D4A9B"), Color.Parse("#39A7B7") },
                new[] { Color.Parse("#CA5F2A"), Color.Parse("#8E367B") },
                new[] { Color.Parse("#385448"), Color.Parse("#A8C958") },
            };
            uint seed = 2166136261;
            unchecked { foreach (char ch in playlist.Name) seed = (seed ^ char.ToUpperInvariant(ch)) * 16777619; }
            var pair = playlistGradients[(int)(seed % (uint)playlistGradients.Length)];
            return new Border
            {
                Width = size, Height = size,
                CornerRadius = new CornerRadius(Math.Max(3, size * 0.07)),
                ClipToBounds = true,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = new GradientStops { new GradientStop(pair[0], 0), new GradientStop(pair[1], 1) }
                },
                Child = icon,
            };
        }
        catch
        {
            return BuildPlaylistCollage(playlist.Tracks, size);
        }
    }

    private Control BuildPlaylistCollage(IReadOnlyList<TrackItem> tracks, double size)
    {
        var grid = new Grid { RowDefinitions = RowDefinitions.Parse("*,*"), ColumnDefinitions = ColumnDefinitions.Parse("*,*"), Width = size, Height = size };
        for (int i = 0; i < 4; i++)
        {
            var img = new Image { Stretch = Stretch.UniformToFill };
            if (i < tracks.Count) LoadImageInto(img, tracks[i].Thumbnail);
            var cell = new Border { ClipToBounds = true, Background = ResBrush("BgSurfaceBrush"), Child = img };
            Grid.SetRow(cell, i / 2); Grid.SetColumn(cell, i % 2); grid.Children.Add(cell);
        }
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(Math.Max(3, size * 0.07)), ClipToBounds = true, Child = grid };
    }

    private Button BuildIconActionButton(Geometry icon, string tip, Action action, bool accent = false, double size = 40)
    {
        var button = new Button
        {
            Classes = { accent ? "accentBtn" : "ghost" },
            Width = size,
            Height = size,
            MinWidth = size,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new PathIcon { Data = icon, Width = 18, Height = 18 },
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, ev) => { ev.Handled = true; action(); };
        return button;
    }

    private Button BuildPlaylistMoreMenuButton(DetailPageResult page, LocalPlaylist? localOwner)
    {
        var button = new Button
        {
            Classes = { "ghost" }, Width = 40, Height = 40, MinWidth = 40, Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = new PathIcon { Data = Icons.More, Width = 18, Height = 18 },
        };
        ToolTip.SetTip(button, "More playlist options");
        button.Click += (_, ev) =>
        {
            ev.Handled = true;
            var menu = new ContextMenu();
            if (localOwner is not null)
            {
                var edit = new MenuItem { Header = "Edit playlist details…" };
                edit.Click += (_, _) => OpenEditPlaylistModal(localOwner);
                menu.Items.Add(edit);
                var delete = new MenuItem { Header = "Delete playlist" };
                delete.Click += async (_, _) =>
                {
                    _store.DeletePlaylist(localOwner.Id);
                    ShowToast($"Deleted \"{localOwner.Name}\"");
                    await NavigateToAsync("library", () => { RenderLibraryView("overview"); return Task.CompletedTask; });
                };
                menu.Items.Add(delete);
            }
            else
            {
                var copy = new MenuItem { Header = "Copy playlist link" };
                copy.Click += async (_, _) =>
                {
                    if (Clipboard is not null)
                    {
                        var url = page.Id.StartsWith("VL", StringComparison.Ordinal) || page.Id.StartsWith("PL", StringComparison.Ordinal)
                            ? "https://music.youtube.com/playlist?list=" + Uri.EscapeDataString(page.Id)
                            : "https://music.youtube.com/playlist?list=" + Uri.EscapeDataString(page.Id);
                        await Clipboard.SetTextAsync(url);
                        ShowToast("Playlist link copied");
                    }
                };
                menu.Items.Add(copy);
            }
            menu.Open(button);
        };
        return button;
    }

    private async Task ApplyPlaylistAmbientAsync(Border header, IReadOnlyList<TrackItem> tracks, string? customCoverPath = null)
    {
        try
        {
            Bitmap? bitmap = null;
            if (!string.IsNullOrWhiteSpace(customCoverPath) && File.Exists(customCoverPath))
                bitmap = new Bitmap(customCoverPath);
            else
            {
                var artUrl = tracks.Select(t => t.Thumbnail).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
                if (artUrl is null) return;
                bitmap = await ImageCache.LoadAsync(artUrl, _yt.SharedHttp, _store.ArtCacheDir);
            }
            if (bitmap is null) return;
            var color = ImageCache.ExtractVibrantColor(bitmap) ?? ThemeManager.GetPalette(_store.Settings.Theme).Accent;
            Dispatcher.UIThread.Post(() =>
            {
                if (!header.IsVisible) return;
                header.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(Color.FromArgb(188, color.R, color.G, color.B), 0),
                        new GradientStop(Color.FromArgb(142, color.R, color.G, color.B), 0.62),
                        new GradientStop(ThemeManager.GetPalette(_store.Settings.Theme).BgCard, 1),
                    },
                };
            });
        }
        catch { }
    }

    internal void RenderDetailPage(DetailPageResult page, LocalPlaylist? localOwner = null)
    {
        var root = new StackPanel { Spacing = 20 };
        PlaybackCollectionContext? playbackSource = localOwner is not null || page.Kind is "playlist" or "album"
            ? new PlaybackCollectionContext
            {
                Id = page.Id,
                Title = page.Title,
                Thumbnail = page.Thumbnail,
                IsLocal = localOwner is not null,
                Kind = localOwner is not null ? "playlist" : page.Kind,
            }
            : null;

        var hdrBorder = new Border
        {
            Background = ResBrush("BgCardBrush"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22),
        };
        var hdrGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*") };
        var artImg = new Image { Width = 176, Height = 176, Stretch = Stretch.UniformToFill };
        LoadImageInto(artImg, page.Thumbnail, BuildGeneratedMediaCover(new MediaCardItem { Id = page.Id, Kind = page.Kind, Title = page.Title, Subtitle = page.Subtitle }, 176));
        Control artwork = localOwner is not null
            ? BuildLocalPlaylistCover(localOwner, 176)
            : string.IsNullOrWhiteSpace(page.Thumbnail)
                ? BuildGeneratedMediaCover(new MediaCardItem { Id = page.Id, Kind = page.Kind, Title = page.Title, Subtitle = page.Subtitle }, 176)
                : artImg;
        var artWrap = new Border
        {
            Width = 176, Height = 176, CornerRadius = new CornerRadius(page.Kind == "artist" ? 88 : 8),
            ClipToBounds = true, Background = ResBrush("BgSurfaceBrush"),
            BoxShadow = BoxShadows.Parse("0 12 32 0 #66000000"), Child = artwork,
        };
        hdrGrid.Children.Add(artWrap);

        var infoStack = new StackPanel { Spacing = 7, Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, ClipToBounds = true };
        infoStack.Children.Add(new TextBlock
        {
            Text = localOwner?.Source == "Spotify" ? "SPOTIFY PLAYLIST" : page.Kind.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 0.8,
            Foreground = ResBrush("TextSecondaryBrush"),
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = page.Title,
            FontSize = 28,
            FontWeight = FontWeight.ExtraBold,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(page.Subtitle))
            infoStack.Children.Add(new TextBlock { Text = page.Subtitle, FontSize = 13.5, Foreground = ResBrush("TextSecondaryBrush") });
        if (!string.IsNullOrWhiteSpace(page.Description))
            infoStack.Children.Add(new TextBlock { Text = page.Description, FontSize = 12.5, Foreground = ResBrush("TextMutedBrush"), MaxLines = 2, TextWrapping = TextWrapping.Wrap });

        var btnRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        if (page.Tracks.Count > 0)
        {
            btnRow.Children.Add(BuildIconActionButton(Icons.Play, "Play", async () => await _audio.PlayQueueAsync(page.Tracks, 0, autoRadioIfSingle: false, collectionContext: playbackSource), accent: true, size: 48));
            btnRow.Children.Add(BuildIconActionButton(Icons.Shuffle, "Shuffle", async () =>
            {
                var copy = page.Tracks.Select(t => t.Clone()).OrderBy(_ => Random.Shared.Next()).ToList();
                await _audio.PlayQueueAsync(copy, 0, autoRadioIfSingle: false, collectionContext: playbackSource);
            }));
            btnRow.Children.Add(BuildIconActionButton(Icons.Download, "Download all", () =>
            {
                int count = 0;
                foreach (var t in page.Tracks.Where(x => !x.IsLocal && !_store.IsDownloaded(x.Id)))
                {
                    _store.EnqueueDownload(t, _resolver, _yt.SharedHttp);
                    count++;
                }
                ShowToast(count > 0 ? $"Queued {count} tracks for offline download" : "All tracks already downloaded!");
            }));
        }

        if (localOwner is null && (page.Kind is "album" or "playlist"))
        {
            var cardRef = new MediaCardItem { Id = page.Id, Kind = page.Kind, Title = page.Title, Subtitle = page.Subtitle, Thumbnail = page.Thumbnail };
            bool savedState = _store.IsAlbumSaved(page.Id);
            Button saveBtn = null!;
            saveBtn = BuildIconActionButton(savedState ? Icons.CheckCircle : Icons.Plus, savedState ? "Saved to Library" : "Save to Library", () =>
            {
                bool saved = _store.ToggleSaveAlbum(cardRef);
                ((PathIcon)saveBtn.Content!).Data = saved ? Icons.CheckCircle : Icons.Plus;
                ToolTip.SetTip(saveBtn, saved ? "Saved to Library" : "Save to Library");
                ShowToast(saved ? "Saved to Library" : "Removed from Library");
            });
            btnRow.Children.Add(saveBtn);
        }
        else if (page.Kind == "artist")
        {
            bool followed = _store.IsArtistFollowed(page.Id);
            Button followBtn = null!;
            followBtn = BuildIconActionButton(followed ? Icons.CheckCircle : Icons.User, followed ? "Following" : "Follow artist", () =>
            {
                var cardRef = new MediaCardItem { Id = page.Id, Kind = "artist", Title = page.Title, Subtitle = page.Subtitle, Thumbnail = page.Thumbnail };
                bool nowFollowed = _store.ToggleFollowArtist(cardRef);
                ((PathIcon)followBtn.Content!).Data = nowFollowed ? Icons.CheckCircle : Icons.User;
                ToolTip.SetTip(followBtn, nowFollowed ? "Following" : "Follow artist");
            });
            btnRow.Children.Add(followBtn);
        }

        if (localOwner is not null || page.Kind == "playlist")
            btnRow.Children.Add(BuildPlaylistMoreMenuButton(page, localOwner));
        infoStack.Children.Add(btnRow);
        Grid.SetColumn(infoStack, 1);
        hdrGrid.Children.Add(infoStack);
        hdrBorder.Child = hdrGrid;
        root.Children.Add(hdrBorder);
        if (localOwner is not null && localOwner.Tracks.Count > 0) _ = ApplyPlaylistAmbientAsync(hdrBorder, localOwner.Tracks, localOwner.CustomCoverPath);

        var tableHost = new Border();
        TextBox? trackFilterBox = null;
        void RefreshDetailTrackTable()
        {
            if (page.Tracks.Count == 0) { tableHost.Child = null; return; }
            string q = trackFilterBox?.Text?.Trim() ?? "";
            var subset = string.IsNullOrEmpty(q)
                ? page.Tracks
                : page.Tracks.Where(t => t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || t.ArtistNames.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            tableHost.Child = BuildTrackListTable(subset, showIndex: true, localPlaylistOwner: localOwner, showAlbumColumn: page.Kind != "album", collectionContext: playbackSource);
        }
        if (page.Tracks.Count > 8)
        {
            trackFilterBox = new TextBox
            {
                Watermark = "Filter in playlist or album…",
                Width = 280,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 7),
            };
            trackFilterBox.TextChanged += (_, _) => RefreshDetailTrackTable();
            root.Children.Add(trackFilterBox);
        }
        RefreshDetailTrackTable();
        if (page.Tracks.Count > 0) root.Children.Add(tableHost);

        bool CanLoadMoreDetail() => !string.IsNullOrWhiteSpace(page.Continuation)
            && (page.Kind is "playlist" or "browse" or "album"
                || page.Kind == "artist");
        if (CanLoadMoreDetail())
        {
            var loadMoreButton = new Button
            {
                Classes = { "pill" },
                Content = "Load more tracks",
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(20, 8),
            };
            loadMoreButton.Click += async (_, _) =>
            {
                string requestedContinuation = page.Continuation!;
                loadMoreButton.IsEnabled = false;
                loadMoreButton.Content = "Loading…";
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    DetailPageResult more;
                    if (page.Kind == "playlist")
                        more = await _yt.GetPlaylistContinuationAsync(requestedContinuation, timeout.Token);
                    else if (page.Kind == "artist" && requestedContinuation.StartsWith("VL", StringComparison.Ordinal))
                        more = await _yt.GetPlaylistAsync(requestedContinuation, timeout.Token);
                    else if (page.Kind == "artist")
                        more = await _yt.GetPlaylistContinuationAsync(requestedContinuation, timeout.Token);
                    else
                        more = await _yt.GetBrowseContinuationAsync(requestedContinuation, page.Id, page.Title, timeout.Token);

                    var existing = new HashSet<string>(page.Tracks.Select(t => t.Id), StringComparer.Ordinal);
                    foreach (var track in more.Tracks)
                        if (existing.Add(track.Id)) page.Tracks.Add(track);
                    page.Continuation = more.Continuation;
                    RefreshDetailTrackTable();
                    if (tableHost.Child is not null && !root.Children.Contains(tableHost)) root.Children.Add(tableHost);
                    foreach (var shelf in more.RelatedShelves)
                        root.Children.Add(BuildShelfElement(shelf));
                    if (string.IsNullOrWhiteSpace(page.Continuation)) loadMoreButton.IsVisible = false;
                    else { loadMoreButton.Content = "Load more tracks"; loadMoreButton.IsEnabled = true; }
                    if (more.Tracks.Count == 0 && more.RelatedShelves.Count == 0)
                        ShowToast("No additional tracks were returned.");
                }
                catch (Exception ex)
                {
                    loadMoreButton.Content = "Retry loading more";
                    loadMoreButton.IsEnabled = true;
                    ShowToast($"Could not load more tracks: {ex.Message}");
                }
            };
            root.Children.Add(loadMoreButton);
        }

        foreach (var rel in page.RelatedShelves)
            root.Children.Add(BuildShelfElement(rel));

        if (page.Tracks.Count == 0 && page.RelatedShelves.Count == 0 && !CanLoadMoreDetail())
            root.Children.Add(EmptyHint("No items were returned for this shelf. Try refreshing or browse another section."));

        SetCenterContent(root);
    }

    // --------------------------------------------------------- Reusable PC Spotify Card & Table Builders

    internal Control BuildShelfElement(ShelfSection shelf)
    {
        var wrap = new StackPanel { Spacing = 12 };
        string rawTitle = string.IsNullOrWhiteSpace(shelf.Title) ? "Recommended for you" : shelf.Title;
        var hdr = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto") };
        hdr.Children.Add(new TextBlock
        {
            Text = rawTitle,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (!string.IsNullOrEmpty(shelf.MoreBrowseId))
        {
            var seeAllBtn = new Button
            {
                Classes = { "ghost" },
                Content = "Show all",
                FontSize = 12.5,
                FontWeight = FontWeight.Bold,
                Foreground = ResBrush("TextSecondaryBrush"),
                Padding = new Thickness(10, 4),
            };
            var moreId = shelf.MoreBrowseId!;
            var moreParams = shelf.MoreParams;
            seeAllBtn.Click += async (_, _) =>
            {
                if (moreId.StartsWith("VL", StringComparison.Ordinal) || moreId.StartsWith("PL", StringComparison.Ordinal))
                {
                    try
                    {
                        await NavigateToAsync("playlist:" + moreId, () => LoadPlaylistDetailAsync(moreId));
                    }
                    catch (Exception ex)
                    {
                        SetCenterContent(ErrorCard($"Could not load {rawTitle}", ex.Message,
                            () => _ = NavigateToAsync("playlist:" + moreId, () => LoadPlaylistDetailAsync(moreId))));
                    }
                    return;
                }

                async Task OpenBrowseAsync()
                {
                    ShowLoadingSkeleton($"Loading {rawTitle}...");
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                        var page = await _yt.BrowseGenericAsync(moreId, moreParams, rawTitle, timeout.Token);
                        RenderDetailPage(page);
                    }
                    catch (Exception ex)
                    {
                        if (shelf.Tracks.Count > 0 || shelf.Cards.Count > 0)
                        {
                            var cachedShelf = new ShelfSection { Title = rawTitle, Tracks = shelf.Tracks.ToList(), Cards = shelf.Cards.ToList() };
                            ApplyDiscoveryExclusions(cachedShelf);
                            RenderDetailPage(new DetailPageResult
                            {
                                Id = moreId,
                                Kind = "playlist",
                                Title = rawTitle,
                                Tracks = cachedShelf.Tracks,
                                RelatedShelves = cachedShelf.Cards.Count > 0
                                    ? new List<ShelfSection> { new ShelfSection { Title = rawTitle, Cards = cachedShelf.Cards } }
                                    : new List<ShelfSection>(),
                            });
                            ShowToast("The full shelf could not be reached; showing the items already loaded.");
                        }
                        else
                        {
                            string message = ex is OperationCanceledException ? "The request timed out. Please try again." : ex.Message;
                            SetCenterContent(ErrorCard($"Could not load {rawTitle}", message,
                                () => _ = NavigateToAsync("browse:" + moreId, OpenBrowseAsync)));
                        }
                    }
                }
                await NavigateToAsync("browse:" + moreId, OpenBrowseAsync);
            };
            Grid.SetColumn(seeAllBtn, 1);
            hdr.Children.Add(seeAllBtn);
        }

        if (shelf.Tracks.Count > 0)
        {
            var playAllBtn = new Button
            {
                Classes = { "ghost" },
                Content = "Play all",
                FontSize = 12.5,
                FontWeight = FontWeight.Bold,
                Foreground = ResBrush("AccentBrush"),
                Padding = new Thickness(10, 4),
            };
            playAllBtn.Click += async (_, _) => await _audio.PlayQueueAsync(shelf.Tracks, 0);
            Grid.SetColumn(playAllBtn, 2);
            hdr.Children.Add(playAllBtn);
        }
        wrap.Children.Add(hdr);

        if (shelf.Tracks.Count > 0)
        {
            var grid = new UniformGrid { Columns = 2 };
            int count = Math.Min(10, shelf.Tracks.Count);
            for (int i = 0; i < count; i++)
            {
                var captured = shelf.Tracks[i];
                var card = new Border
                {
                    Background = ResBrush("BgCardBrush"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8),
                    Margin = new Thickness(i % 2 == 0 ? 0 : 5, 0, i % 2 == 0 ? 5 : 0, 8),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                card.PointerEntered += (_, _) => card.Background = ResBrush("BgHoverBrush");
                card.PointerExited += (_, _) => card.Background = ResBrush("BgCardBrush");

                var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("46,*,Auto") };
                var img = new Image { Width = 46, Height = 46, Stretch = Stretch.UniformToFill };
                LoadImageInto(img, captured.Thumbnail);
                row.Children.Add(new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(6), ClipToBounds = true, Background = ResBrush("BgSurfaceBrush"), Child = img });

                var meta = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 2, ClipToBounds = true };
                meta.Children.Add(new ScrollingTitleTextBlock { Text = captured.Title, FontSize = 13.5, FontWeight = FontWeight.SemiBold });
                meta.Children.Add(new TextBlock { Text = captured.ArtistNames, FontSize = 12, FontWeight = FontWeight.Medium, Foreground = ResBrush("TextSecondaryBrush") });
                Grid.SetColumn(meta, 1);
                row.Children.Add(meta);

                var menuBtn = BuildTrackMoreMenuButton(captured);
                Grid.SetColumn(menuBtn, 2);
                row.Children.Add(menuBtn);

                card.Child = row;
                card.PointerPressed += async (_, ev) =>
                {
                    if (ev.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                        await _audio.PlayQueueAsync(shelf.Tracks, Math.Max(0, shelf.Tracks.IndexOf(captured)));
                };
                AttachTrackRowInteractions(card, captured, moreButton: menuBtn);
                grid.Children.Add(card);
            }
            wrap.Children.Add(grid);
        }

        if (shelf.Cards.Count > 0)
        {
            var sc = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            foreach (var c in shelf.Cards.Take(24))
                row.Children.Add(BuildMediaCardTile(c));
            sc.Content = row;
            wrap.Children.Add(sc);
        }

        return wrap;
    }

    private Control BuildGeneratedMediaCover(MediaCardItem card, double size)
    {
        Color[][] palettes =
        {
            new[] { Color.Parse("#9E3D73"), Color.Parse("#392C79") },
            new[] { Color.Parse("#CC6334"), Color.Parse("#71344F") },
            new[] { Color.Parse("#187D78"), Color.Parse("#1A3F73") },
            new[] { Color.Parse("#66883C"), Color.Parse("#1D504B") },
            new[] { Color.Parse("#A44D35"), Color.Parse("#503687") },
            new[] { Color.Parse("#356E9A"), Color.Parse("#263960") },
            new[] { Color.Parse("#9A682C"), Color.Parse("#563467") },
            new[] { Color.Parse("#357B61"), Color.Parse("#174D43") },
        };
        uint seed = 2166136261;
        unchecked { foreach (char ch in card.Title) seed = (seed ^ char.ToUpperInvariant(ch)) * 16777619; }
        var pair = palettes[(int)(seed % (uint)palettes.Length)];
        var grid = new Grid();
        grid.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops { new GradientStop(pair[0], 0), new GradientStop(pair[1], 1) },
            },
        });
        grid.Children.Add(new Border
        {
            Width = size * 0.84,
            Height = size * 0.84,
            CornerRadius = new CornerRadius(size * 0.42),
            Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -size * 0.24, -size * 0.2, 0),
        });
        var icon = new PathIcon
        {
            Data = card.Kind == "artist" ? Icons.User : Icons.Lyrics,
            Width = size * 0.34,
            Height = size * 0.34,
            Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 0),
        };
        grid.Children.Add(icon);
        var caption = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12),
            Spacing = 3,
        };
        caption.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(card.Kind) ? "COLLECTION" : card.Kind.ToUpperInvariant(),
            FontSize = 8,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)),
            LetterSpacing = 0.6,
        });
        caption.Children.Add(new TextBlock
        {
            Text = card.Title,
            FontSize = 14,
            FontWeight = FontWeight.ExtraBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
        });
        grid.Children.Add(caption);
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(card.Kind == "artist" ? size * 0.5 : 7),
            ClipToBounds = true,
            Child = grid,
        };
    }

    internal Control BuildMediaCardTile(MediaCardItem card)
    {
        var hoverWash = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            Opacity = 0,
            IsHitTestVisible = false,
            Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(270), Easing = new CubicEaseOut() },
            },
        };
        Control WithSoftHover(Control content)
        {
            var layer = new Grid();
            layer.Children.Add(content);
            layer.Children.Add(hoverWash);
            return layer;
        }
        var border = new Border
        {
            Width = 162,
            Background = ResBrush("BgCardBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Cursor = new Cursor(StandardCursorType.Hand),
            ClipToBounds = true,
        };
        border.PointerEntered += (_, _) => hoverWash.Opacity = 1;
        border.PointerExited += (_, _) => hoverWash.Opacity = 0;

        if (card.Kind == "mood")
        {
            // InnerTube supplies mood/genre navigation labels but no artwork URLs. Render deliberate,
            // title-seeded vector cover art instead of presenting the platform green as a failed image.
            Color[][] moodPalettes =
            {
                new[] { Color.Parse("#D94B7B"), Color.Parse("#5C2C91") },
                new[] { Color.Parse("#E06A2B"), Color.Parse("#8C2E58") },
                new[] { Color.Parse("#147D78"), Color.Parse("#173A74") },
                new[] { Color.Parse("#497A37"), Color.Parse("#1C514A") },
                new[] { Color.Parse("#B45432"), Color.Parse("#533889") },
                new[] { Color.Parse("#316FA0"), Color.Parse("#1E395F") },
            };
            uint seed = 2166136261;
            unchecked
            {
                foreach (char ch in card.Title) seed = (seed ^ char.ToUpperInvariant(ch)) * 16777619;
            }
            var colors = moodPalettes[(int)(seed % (uint)moodPalettes.Length)];
            border.Height = 112;
            border.Padding = new Thickness(12);
            border.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops { new GradientStop(colors[0], 0), new GradientStop(colors[1], 1) },
            };
            var moodArt = new Grid();
            moodArt.Children.Add(new Border
            {
                Width = 110, Height = 110, CornerRadius = new CornerRadius(55),
                Background = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -43, -40, 0),
            });
            moodArt.Children.Add(new Border
            {
                Width = 68, Height = 68, CornerRadius = new CornerRadius(34),
                Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -14, -19, 0),
                Child = new PathIcon { Data = Icons.Lyrics, Width = 25, Height = 25, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var moodText = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 2 };
            moodText.Children.Add(new TextBlock { Text = "MOOD  /  GENRE", FontSize = 8.5, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), LetterSpacing = 0.7 });
            moodText.Children.Add(new TextBlock { Text = card.Title, FontSize = 15, FontWeight = FontWeight.ExtraBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxLines = 2 });
            moodArt.Children.Add(moodText);
            border.Child = WithSoftHover(moodArt);
            border.PointerPressed += async (_, _) =>
            {
                await NavigateToAsync("mood:" + card.Id, async () =>
                {
                    ShowLoadingSkeleton($"Loading {card.Title}...");
                    var p = await _yt.BrowseGenericAsync(card.Id, card.Params, card.Title);
                    RenderDetailPage(p);
                });
            };
            return border;
        }

        var st = new StackPanel { Spacing = 8, ClipToBounds = true };
        bool localPlaylist = card.Kind == "local-playlist";
        bool appIconFallback = localPlaylist && string.IsNullOrWhiteSpace(card.Thumbnail) && card.PreviewTracks.Count < 4;
        bool playlistCollage = localPlaylist && string.IsNullOrWhiteSpace(card.Thumbnail) && card.PreviewTracks.Count >= 4;
        Control artTile;
        if (appIconFallback)
        {
            artTile = BuildLocalPlaylistCover(new LocalPlaylist { Name = card.Title }, 138);
        }
        else if (playlistCollage)
        {
            artTile = BuildPlaylistCollage(card.PreviewTracks, 138);
        }
        else if (string.IsNullOrWhiteSpace(card.Thumbnail))
        {
            artTile = BuildGeneratedMediaCover(card, 138);
        }
        else
        {
            var img = new Image { Width = 138, Height = 138, Stretch = Stretch.UniformToFill };
            LoadImageInto(img, card.Thumbnail, BuildGeneratedMediaCover(card, 138));
            artTile = new Border
            {
                Width = 138,
                Height = 138,
                CornerRadius = new CornerRadius(card.Kind == "artist" ? 69 : 6),
                ClipToBounds = true,
                Background = ResBrush("BgSurfaceBrush"),
                Child = img,
            };
        }
        st.Children.Add(artTile);
        st.Children.Add(new TextBlock
        {
            Text = card.Title,
            FontSize = 13.5,
            FontWeight = FontWeight.Bold,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = card.Kind == "artist" ? TextAlignment.Center : TextAlignment.Left,
        });
        st.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(card.Subtitle) ? card.Kind : card.Subtitle,
            FontSize = 12,
            Foreground = ResBrush("TextSecondaryBrush"),
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = card.Kind == "artist" ? TextAlignment.Center : TextAlignment.Left,
        });
        border.Child = WithSoftHover(st);

        border.PointerPressed += async (_, ev) =>
        {
            if (ev.GetCurrentPoint(border).Properties.IsRightButtonPressed && (card.Kind is "artist" or "album"))
            {
                var menu = new ContextMenu();
                bool excluded = _store.IsContentExcluded(card.Kind, card.Id, card.Title);
                string noun = card.Kind == "artist" ? "artist" : "album";
                var action = new MenuItem { Header = excluded ? $"Restore {noun} to discovery" : card.Kind == "artist" ? $"Block artist: {card.Title}" : $"Exclude album: {card.Title}" };
                action.Click += (_, _) => ToggleDiscoveryExclusion(card.Kind, card.Id, card.Title, noun);
                menu.Items.Add(action);
                menu.Placement = PlacementMode.Pointer;
                menu.Open(border);
                ev.Handled = true;
                return;
            }
            if (!ev.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
            // Your Library's local/Spotify playlist tiles attach their own local detail route.
            // Do not also start the generic YouTube playlist request; its late response can replace
            // the local playlist page with an empty remote result.
            if (localPlaylist) return;
            if (card.Kind == "album")
                await NavigateToAsync("album:" + card.Id, () => LoadAlbumDetailAsync(card.Id));
            else if (card.Kind == "artist")
                await NavigateToAsync("artist:" + card.Id, () => LoadArtistDetailAsync(card.Id));
            else
                await NavigateToAsync("playlist:" + card.Id, () => LoadPlaylistDetailAsync(card.Id));
        };
        return border;
    }

    internal Control BuildTrackListTable(List<TrackItem> tracks, bool showIndex, LocalPlaylist? localPlaylistOwner = null, bool showAlbumColumn = true, PlaybackCollectionContext? collectionContext = null)
    {
        var listStack = new StackPanel { Spacing = 2 };
        string columns = showAlbumColumn ? "30,42,*,150,52,Auto,Auto,Auto" : "30,42,*,0,52,Auto,Auto,Auto";
        if (showIndex)
        {
            var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse(columns), Margin = new Thickness(10, 0, 10, 4), MinHeight = 32 };
            header.Children.Add(new TextBlock { Text = "#", FontSize = 11.5, Foreground = ResBrush("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center });
            var titleHeader = new TextBlock { Text = "Title", FontSize = 11.5, Foreground = ResBrush("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(titleHeader, 2); header.Children.Add(titleHeader);
            if (showAlbumColumn)
            {
                var albumHeader = new TextBlock { Text = "Album", FontSize = 11.5, Foreground = ResBrush("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 0) };
                Grid.SetColumn(albumHeader, 3); header.Children.Add(albumHeader);
            }
            listStack.Children.Add(header);
        }
        for (int i = 0; i < tracks.Count; i++)
        {
            int idx = i;
            var tr = tracks[i];
            var rowBorder = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            rowBorder.PointerEntered += (_, _) => rowBorder.Background = ResBrush("BgCardBrush");
            rowBorder.PointerExited += (_, _) => rowBorder.Background = Brushes.Transparent;

            var g = new Grid { ColumnDefinitions = ColumnDefinitions.Parse(columns) };

            g.Children.Add(new TextBlock
            {
                Text = showIndex ? (idx + 1).ToString() : "",
                FontSize = 12.5,
                Foreground = ResBrush("TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var img = new Image { Width = 40, Height = 40, Stretch = Stretch.UniformToFill };
            LoadImageInto(img, tr.Thumbnail);
            var artLayer = new Grid { Width = 40, Height = 40, ClipToBounds = true };
            artLayer.Children.Add(img);
            var artSpectrum = new SpectrumVisualizerControl { Width = 32, Height = 10, BarCount = 7, BarColor = ThemeManager.GetPalette(_store.Settings.Theme).Accent };
            var spectrumPlate = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(2, 0),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                IsVisible = false,
                IsHitTestVisible = false,
                Child = artSpectrum,
            };
            artLayer.Children.Add(spectrumPlate);
            _trackArtSpectra.Add((new WeakReference<Border>(spectrumPlate), new WeakReference<SpectrumVisualizerControl>(artSpectrum), tr.Id));
            var imgWrap = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(5), ClipToBounds = true, Background = ResBrush("BgSurfaceBrush"), Child = artLayer };
            Grid.SetColumn(imgWrap, 1);
            g.Children.Add(imgWrap);

            var titleCol = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 2, ClipToBounds = true };
            var titleRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
            titleRow.Children.Add(new ScrollingTitleTextBlock { Text = tr.Title, FontSize = 13.5, FontWeight = FontWeight.SemiBold });
            if (tr.Explicit)
            {
                var expBadge = new Border
                {
                    Background = ResBrush("BgHoverBrush"),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "E", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = ResBrush("TextSecondaryBrush") }
                };
                Grid.SetColumn(expBadge, 1);
                titleRow.Children.Add(expBadge);
            }
            titleCol.Children.Add(titleRow);
            titleCol.Children.Add(new TextBlock { Text = tr.ArtistNames, FontSize = 12, Foreground = ResBrush("TextSecondaryBrush") });
            Grid.SetColumn(titleCol, 2);
            g.Children.Add(titleCol);

            if (showAlbumColumn)
            {
                var albumText = new TextBlock
                {
                    Text = tr.Album?.Name ?? "",
                    FontSize = 12,
                    Foreground = ResBrush("TextMutedBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                Grid.SetColumn(albumText, 3);
                g.Children.Add(albumText);
            }

            var durText = new TextBlock
            {
                Text = tr.FormattedDuration,
                FontSize = 12,
                Foreground = ResBrush("TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(durText, 4);
            g.Children.Add(durText);

            var likeIcon = new PathIcon
            {
                Data = _store.IsLiked(tr.Id) ? Icons.HeartFilled : Icons.HeartOutline,
                Width = 14,
                Height = 14,
                Foreground = _store.IsLiked(tr.Id) ? ResBrush("AccentBrush") : ResBrush("TextMutedBrush"),
            };
            var likeBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(6), Content = likeIcon };
            likeBtn.Click += (_, ev) =>
            {
                ev.Handled = true;
                bool liked = _store.ToggleLike(tr, _yt);
                likeIcon.Data = liked ? Icons.HeartFilled : Icons.HeartOutline;
                likeIcon.Foreground = liked ? ResBrush("AccentBrush") : ResBrush("TextMutedBrush");
            };
            Grid.SetColumn(likeBtn, 5);
            g.Children.Add(likeBtn);

            if (!tr.IsLocal)
            {
                var dlIcon = new PathIcon
                {
                    Data = _store.IsDownloaded(tr.Id) ? Icons.CheckCircle : Icons.Download,
                    Width = 14,
                    Height = 14,
                    Foreground = _store.IsDownloaded(tr.Id) ? ResBrush("AccentBrush") : ResBrush("TextMutedBrush"),
                };
                var dlBtn = new Button { Classes = { "ghost" }, Padding = new Thickness(6), Content = dlIcon };
                dlBtn.Click += (_, ev) =>
                {
                    ev.Handled = true;
                    if (!_store.IsDownloaded(tr.Id))
                    {
                        _store.EnqueueDownload(tr, _resolver, _yt.SharedHttp);
                        ShowToast($"Downloading \"{tr.Title}\"");
                    }
                };
                Grid.SetColumn(dlBtn, 6);
                g.Children.Add(dlBtn);
            }

            var moreBtn = BuildTrackMoreMenuButton(tr, localPlaylistOwner);
            Grid.SetColumn(moreBtn, 7);
            g.Children.Add(moreBtn);

            rowBorder.Child = g;
            rowBorder.PointerPressed += async (_, ev) =>
            {
                if (ev.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    await _audio.PlayQueueAsync(tracks, idx, autoRadioIfSingle: tracks.Count == 1, collectionContext: collectionContext);
            };
            AttachTrackRowInteractions(rowBorder, tr, localPlaylistOwner, moreBtn);
            listStack.Children.Add(rowBorder);
        }
        return listStack;
    }

    private Button BuildTrackMoreMenuButton(TrackItem tr, LocalPlaylist? localOwner = null)
    {
        var btn = new Button
        {
            Classes = { "ghost" },
            Padding = new Thickness(6),
            Opacity = 0,
            IsHitTestVisible = false,
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(110) }
            },
            Content = new PathIcon { Data = Icons.MoreHorizontal, Width = 16, Height = 16 },
        };
        btn.Click += (_, ev) =>
        {
            ev.Handled = true;
            OpenTrackContextMenu(tr, localOwner, btn, PlacementMode.Bottom);
        };
        return btn;
    }

    private void AttachTrackRowInteractions(Control row, TrackItem track, LocalPlaylist? owner = null, Button? moreButton = null)
    {
        if (moreButton is not null)
        {
            row.PointerEntered += (_, _) => { moreButton.Opacity = 1; moreButton.IsHitTestVisible = true; };
            row.PointerExited += (_, _) => { moreButton.Opacity = 0; moreButton.IsHitTestVisible = false; };
        }
        row.AddHandler(InputElement.PointerPressedEvent, (_, ev) =>
        {
            if (ev.Handled || !ev.GetCurrentPoint(row).Properties.IsRightButtonPressed) return;
            // Mark handled before opening so a parent row/container cannot open another popup for
            // the same bubbled right-click.
            ev.Handled = true;
            OpenTrackContextMenu(track, owner, row, PlacementMode.Pointer);
        }, RoutingStrategies.Bubble);
    }

    private void OpenTrackContextMenu(TrackItem track, LocalPlaylist? owner, Control anchor, PlacementMode placement)
    {
        if (_openTrackContextMenu is { } previous)
        {
            _openTrackContextMenu = null;
            previous.Close();
        }

        var menu = BuildTrackContextMenu(track, owner);
        menu.Placement = placement;
        _openTrackContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_openTrackContextMenu, menu))
                _openTrackContextMenu = null;
        };
        menu.Open(anchor);
    }

    private MenuItem BuildIconMenuItem(string label, Geometry? icon = null, IBrush? iconForeground = null)
    {
        var item = new MenuItem
        {
            Padding = new Thickness(8, 4),
            MinHeight = 31,
            Foreground = ResBrush("TextPrimaryBrush"),
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            Header = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = FontWeight.Medium,
                Foreground = ResBrush("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        if (icon is not null)
        {
            item.Icon = new PathIcon
            {
                Data = icon,
                Width = 15,
                Height = 15,
                Foreground = iconForeground ?? ResBrush("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        return item;
    }

    private Control BuildLikedSongsMenuIcon()
    {
        return new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(2),
            ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.Parse("#450AF5"), 0),
                    new GradientStop(Color.Parse("#1ED760"), 1),
                }
            },
            Child = new PathIcon
            {
                Data = Icons.HeartFilled,
                Width = 10,
                Height = 10,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
    }

    private static void AddMenuDivider(ContextMenu menu)
    {
        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator)
            menu.Items.Add(new Separator
            {
                Height = 1,
                MinHeight = 1,
                Margin = new Thickness(8, 2),
                Opacity = 0.48,
                IsHitTestVisible = false,
                Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            });
    }

    private static Color MixColor(Color baseColor, Color tintColor, double tintAmount)
    {
        double amount = Math.Clamp(tintAmount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(baseColor.R * (1 - amount) + tintColor.R * amount),
            (byte)Math.Round(baseColor.G * (1 - amount) + tintColor.G * amount),
            (byte)Math.Round(baseColor.B * (1 - amount) + tintColor.B * amount));
    }

    private ContextMenu BuildTrackContextMenu(TrackItem tr, LocalPlaylist? localOwner = null)
    {
        var palette = ThemeManager.GetPalette(_store.Settings.Theme);
        var menu = new ContextMenu
        {
            MinWidth = 0,
            Background = new SolidColorBrush(MixColor(palette.BgSurface, palette.Accent, palette.IsDark ? 0 : 0.07)),
            BorderBrush = new SolidColorBrush(palette.BorderSubtle),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
        };

        var addToPlMenu = BuildIconMenuItem("Add to Playlist", Icons.Plus);
        var newPlItem = BuildIconMenuItem("Create New Playlist…", Icons.Plus);
        newPlItem.Click += (_, _) => OpenNewPlaylistModal(tr);
        addToPlMenu.Items.Add(newPlItem);
        foreach (var pl in _store.Playlists)
        {
            var cpl = pl;
            var pi = BuildIconMenuItem(cpl.Name, Icons.Library);
            pi.Click += (_, _) =>
            {
                bool added = _store.AddTrackToPlaylist(cpl.Id, tr);
                ShowToast(added ? $"Added to {cpl.Name}" : $"Already in {cpl.Name}");
            };
            addToPlMenu.Items.Add(pi);
        }
        menu.Items.Add(addToPlMenu);

        if (localOwner is not null)
        {
            var rmItem = BuildIconMenuItem($"Remove from {localOwner.Name}", Icons.Trash);
            rmItem.Click += (_, _) =>
            {
                _store.RemoveTrackFromPlaylist(localOwner.Id, tr.Id);
                RenderLocalPlaylistDetail(localOwner);
            };
            menu.Items.Add(rmItem);
        }

        bool isLiked = _store.IsLiked(tr.Id);
        var likeItem = BuildIconMenuItem(isLiked ? "Remove from Liked Songs" : "Add to Liked Songs");
        likeItem.Icon = BuildLikedSongsMenuIcon();
        likeItem.Click += (_, _) =>
        {
            bool liked = _store.ToggleLike(tr, _yt);
            ShowToast(liked ? "Added to Liked Songs" : "Removed from Liked Songs");
            UpdateNowPlayingUi();
            if (_dockOpen && _dockTab == "now") RenderRightDock();
        };
        menu.Items.Add(likeItem);

        AddMenuDivider(menu);
        var mAddQ = BuildIconMenuItem("Add to Queue", Icons.Queue);
        mAddQ.Click += (_, _) => _audio.AddToQueue(tr);
        menu.Items.Add(mAddQ);
        var mPlayNext = BuildIconMenuItem("Play Next", Icons.Next);
        mPlayNext.Click += (_, _) => _audio.PlayNextInQueue(tr);
        menu.Items.Add(mPlayNext);

        if (!tr.IsLocal)
        {
            AddMenuDivider(menu);
            var mRadio = BuildIconMenuItem("Go to Song Radio", Icons.Radio);
            mRadio.Click += async (_, _) =>
            {
                ShowToast($"Starting Radio from \"{tr.Title}\"...");
                var r = await _yt.GetRadioQueueAsync(tr.Id);
                r = r.Where(x => (!_store.Settings.HideVideoSongs || !x.IsVideo) && !_store.IsTrackExcluded(x)).ToList();
                if (r.Count > 0) await _audio.PlayQueueAsync(r, 0, autoRadioIfSingle: false);
            };
            menu.Items.Add(mRadio);
        }

        AddMenuDivider(menu);
        var excludeMenu = BuildIconMenuItem("Exclude from discovery", Icons.ShieldCheck);
        bool trackExcluded = _store.IsContentExcluded("track", tr.Id, tr.Title);
        var trackExclusion = BuildIconMenuItem(trackExcluded ? "Restore song to discovery" : "Exclude this song", Icons.Close);
        trackExclusion.Click += (_, _) => ToggleDiscoveryExclusion("track", tr.Id, tr.Title, tr.ArtistNames);
        excludeMenu.Items.Add(trackExclusion);
        if (tr.Album is { } menuAlbum)
        {
            bool albumExcluded = _store.IsContentExcluded("album", menuAlbum.Id, menuAlbum.Name);
            var albumExclusion = BuildIconMenuItem(albumExcluded ? $"Restore album: {menuAlbum.Name}" : $"Exclude album: {menuAlbum.Name}", Icons.Library);
            albumExclusion.Click += (_, _) => ToggleDiscoveryExclusion("album", menuAlbum.Id, menuAlbum.Name, tr.ArtistNames);
            excludeMenu.Items.Add(albumExclusion);
        }
        foreach (var artist in tr.Artists.Where(a => !string.IsNullOrWhiteSpace(a.Name)).DistinctBy(a => a.Id ?? a.Name))
        {
            var capturedArtist = artist;
            bool artistExcluded = _store.IsContentExcluded("artist", capturedArtist.Id, capturedArtist.Name);
            var artistExclusion = BuildIconMenuItem(artistExcluded ? $"Unblock artist: {capturedArtist.Name}" : $"Exclude artist: {capturedArtist.Name}", Icons.User);
            artistExclusion.Click += (_, _) => ToggleDiscoveryExclusion("artist", capturedArtist.Id, capturedArtist.Name, "Artist");
            excludeMenu.Items.Add(artistExclusion);
        }
        menu.Items.Add(excludeMenu);

        if (tr.Artists.FirstOrDefault()?.Id is { Length: > 0 } aid)
        {
            var goArt = BuildIconMenuItem("Go to Artist", Icons.User);
            goArt.Click += async (_, _) => await NavigateToAsync("artist:" + aid, () => LoadArtistDetailAsync(aid));
            menu.Items.Add(goArt);
        }
        if (tr.Album?.Id is { Length: > 0 } albid)
        {
            var goAlb = BuildIconMenuItem("Go to Album", Icons.Library);
            goAlb.Click += async (_, _) => await NavigateToAsync("album:" + albid, () => LoadAlbumDetailAsync(albid));
            menu.Items.Add(goAlb);
        }

        AddMenuDivider(menu);
        var mShare = BuildIconMenuItem("Share", Icons.Share);
        mShare.Click += async (_, _) => await ShareTrackUniversalAsync(tr);
        menu.Items.Add(mShare);
        return menu;
    }

    internal async Task ShareTrackUniversalAsync(TrackItem tr)
    {
        ShowToast("Resolving universal song.link...");
        var url = await _extras.GetUniversalShareLinkAsync(tr);
        if (Clipboard is not null)
            await Clipboard.SetTextAsync(url);
        ShowToast($"Copied link: {url}");
    }

    // --------------------------------------------------------- Modals & Helpers

    private void OpenContentExclusionsModal()
    {
        var stack = new StackPanel { Spacing = 14, Width = 476, HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(new TextBlock { Text = "Content exclusions", FontSize = 19, FontWeight = FontWeight.Bold });
        stack.Children.Add(new TextBlock { Text = "Blocked content is filtered from discovery, search recommendations, and radio. You can restore any item here.", FontSize = 12.5, Foreground = ResBrush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        var list = new StackPanel { Spacing = 6 };
        var empty = new TextBlock { Text = "No excluded songs, albums, or artists yet. Use a song’s ⋯ menu (including the now-playing bar) to exclude it.", FontSize = 13, Foreground = ResBrush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap, IsVisible = _store.ContentExclusions.Count == 0, Margin = new Thickness(2, 6) };
        void Refresh()
        {
            list.Children.Clear();
            var exclusions = _store.ContentExclusions.OrderBy(x => x.Kind).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            empty.IsVisible = exclusions.Count == 0;
            foreach (var exclusion in exclusions)
            {
                var item = exclusion;
                string kind = item.Kind switch { "artist" => "Artist", "album" => "Album", _ => "Song" };
                var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Margin = new Thickness(0, 1) };
                var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) };
                copy.Children.Add(new TextBlock { Text = item.Name, FontSize = 13.5, FontWeight = FontWeight.SemiBold });
                copy.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(item.Subtitle) ? kind : $"{kind} · {item.Subtitle}", FontSize = 11.5, Foreground = ResBrush("TextSecondaryBrush") });
                row.Children.Add(copy);
                var restore = new Button { Classes = { "pill" }, Content = "Restore", Padding = new Thickness(11, 5), VerticalAlignment = VerticalAlignment.Center };
                restore.Click += (_, _) => { _store.SetContentExcluded(item.Kind, item.Id, item.Name, item.Subtitle, false); Refresh(); };
                Grid.SetColumn(restore, 1);
                row.Children.Add(restore);
                list.Children.Add(new Border { Background = ResBrush("BgSurfaceBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 7), Child = row });
            }
        }
        Refresh();
        stack.Children.Add(empty);
        stack.Children.Add(new ScrollViewer { MaxHeight = 380, Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var done = new Button { Classes = { "accentBtn" }, Content = "Done" };
        done.Click += (_, _) => { ModalBackdrop.IsVisible = false; RenderSettingsView(); };
        footer.Children.Add(done);
        stack.Children.Add(footer);
        ModalCardHost.Child = stack;
        ModalBackdrop.IsVisible = true;
    }

    private void OpenNewPlaylistModal(TrackItem? initialTrack = null)
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new TextBlock { Text = "Create New Playlist", FontSize = 19, FontWeight = FontWeight.Bold });
        var nameBox = new TextBox { Watermark = "Playlist name (e.g. Late Night Vibes)", Padding = new Thickness(12, 9) };
        var descBox = new TextBox { Watermark = "Optional description", Padding = new Thickness(12, 9) };
        stack.Children.Add(nameBox);
        stack.Children.Add(descBox);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        var cancel = new Button { Classes = { "pill" }, Content = "Cancel" };
        cancel.Click += (_, _) => ModalBackdrop.IsVisible = false;
        var create = new Button { Classes = { "accentBtn" }, Content = "Create Playlist" };
        create.Click += async (_, _) =>
        {
            var pl = _store.CreatePlaylist(
                string.IsNullOrWhiteSpace(nameBox.Text) ? "My Echo Playlist" : nameBox.Text,
                descBox.Text ?? "",
                initialTrack is null ? null : new[] { initialTrack }
            );
            ModalBackdrop.IsVisible = false;
            ShowToast($"Created \"{pl.Name}\"");
            await NavigateToAsync("pl:" + pl.Id, () => { RenderLocalPlaylistDetail(pl); return Task.CompletedTask; });
        };
        btns.Children.Add(cancel);
        btns.Children.Add(create);
        stack.Children.Add(btns);

        ModalCardHost.Child = stack;
        ModalBackdrop.IsVisible = true;
        nameBox.Focus();
    }

    private void OpenEditPlaylistModal(LocalPlaylist playlist)
    {
        string selectedCover = playlist.CustomCoverPath;
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = "Edit playlist", FontSize = 19, FontWeight = FontWeight.Bold });
        var nameBox = new TextBox { Text = playlist.Name, Watermark = "Playlist title", Padding = new Thickness(12, 9) };
        var descBox = new TextBox { Text = playlist.Description, Watermark = "Description", Padding = new Thickness(12, 9), AcceptsReturn = true, MinHeight = 72, TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(nameBox);
        stack.Children.Add(descBox);

        var coverRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var preview = new Border { Width = 88, Height = 88, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = BuildLocalPlaylistCover(playlist, 88) };
        coverRow.Children.Add(preview);
        var coverControls = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        var coverStatus = new TextBlock { FontSize = 12, Foreground = ResBrush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        void UpdateCoverStatus()
        {
            coverStatus.Text = !string.IsNullOrWhiteSpace(selectedCover) && File.Exists(selectedCover)
                ? Path.GetFileName(selectedCover)
                : playlist.Tracks.Count >= 4 ? "Automatic cover collage from playlist tracks" : "App icon fallback; a collage is automatic with 4 or more tracks";
        }
        UpdateCoverStatus();
        var chooseCover = new Button { Classes = { "pill" }, Content = "Choose image" };
        chooseCover.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose playlist cover image", AllowMultiple = false });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            selectedCover = path;
            UpdateCoverStatus();
            try { preview.Child = new Image { Source = new Bitmap(path), Stretch = Stretch.UniformToFill }; } catch { }
        };
        var clearCover = new Button { Classes = { "ghost" }, Content = "Use automatic cover" };
        clearCover.Click += (_, _) =>
        {
            selectedCover = "";
            preview.Child = BuildLocalPlaylistCover(new LocalPlaylist { Source = playlist.Source, Tracks = playlist.Tracks }, 88);
            UpdateCoverStatus();
        };
        coverControls.Children.Add(coverStatus);
        var coverButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        coverButtons.Children.Add(chooseCover);
        coverButtons.Children.Add(clearCover);
        coverControls.Children.Add(coverButtons);
        coverRow.Children.Add(coverControls);
        stack.Children.Add(coverRow);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 9 };
        var cancel = new Button { Classes = { "pill" }, Content = "Cancel" };
        cancel.Click += (_, _) => ModalBackdrop.IsVisible = false;
        var save = new Button { Classes = { "accentBtn" }, Content = "Save changes" };
        save.Click += (_, _) =>
        {
            playlist.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? "My Echo Playlist" : nameBox.Text.Trim();
            playlist.Description = descBox.Text?.Trim() ?? "";
            playlist.CustomCoverPath = selectedCover;
            playlist.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _store.SaveDebounced();
            RefreshSidebarBadgesAndPlaylists();
            ModalBackdrop.IsVisible = false;
            RenderLocalPlaylistDetail(playlist);
            ShowToast("Playlist details updated");
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        stack.Children.Add(actions);
        ModalCardHost.Child = stack;
        ModalBackdrop.IsVisible = true;
        nameBox.Focus();
    }

    private void OpenSleepTimerModal()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new TextBlock { Text = "Sleep Timer", FontSize = 19, FontWeight = FontWeight.Bold });
        stack.Children.Add(new TextBlock { Text = "Automatically fade & pause playback after:", FontSize = 13, Foreground = ResBrush("TextSecondaryBrush") });
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (mins, label, eot) in new[] { (15, "15 min", false), (30, "30 min", false), (45, "45 min", false), (60, "60 min", false), (90, "90 min", false), (0, "End of Current Song", true), (0, "Turn Off", false) })
        {
            var m = mins; var end = eot;
            var b = new Button { Classes = { "pill" }, Content = label, Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (_, _) =>
            {
                _audio.SetSleepTimer(m, end);
                ModalBackdrop.IsVisible = false;
            };
            wrap.Children.Add(b);
        }
        stack.Children.Add(wrap);
        ModalCardHost.Child = stack;
        ModalBackdrop.IsVisible = true;
    }

    internal void OpenAccountSignInModal()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = "Google login & Guest Mode", FontSize = 19, FontWeight = FontWeight.Bold });

        stack.Children.Add(new Border
        {
            Background = ResBrush("AccentSoftBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9),
            Child = new TextBlock
            {
                Text = _yt.IsSignedIn
                    ? $"Connected to YouTube Music as {_store.Settings.AccountName ?? "YouTube Music account"}"
                    : "Guest Mode is active. Music playback and local features work without an account.",
                FontSize = 12.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = ResBrush("AccentBrush"),
                TextWrapping = TextWrapping.Wrap,
            }
        });

        var infoBody = new StackPanel { Spacing = 11, MaxWidth = 640 };
        var featureSection = new StackPanel { Spacing = 7 };
        featureSection.Children.Add(new TextBlock { Text = "WHAT GOOGLE LOGIN ENABLES", FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") });
        featureSection.Children.Add(new TextBlock
        {
            Text = "• Account status: Echo Music verifies and displays the YouTube Music profile name.\n" +
                   "• Likes: liking or unliking streamed tracks also attempts the matching YouTube Music action; Echo Music keeps its own local Liked Songs list. Local files stay local.\n" +
                   "• Lyrics: eligible transcript requests can use the signed-in session when YouTube exposes captions.",
            FontSize = 12,
            Foreground = ResBrush("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        infoBody.Children.Add(featureSection);

        var scopeCard = new Border { Background = ResBrush("BgCardBrush"), BorderBrush = ResBrush("BorderSubtleBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12) };
        scopeCard.Child = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = "NOT INCLUDED", FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = ResBrush("TextMutedBrush") },
                new TextBlock { Text = "This build does not import YouTube Music watch history, Your Library playlists, Recaps, or personalized Home shelves. It does not create an Echo Music account, connect Spotify, sync local playlists, provide Premium, or bypass YouTube restrictions.", FontSize = 12, Foreground = ResBrush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap },
            }
        };
        infoBody.Children.Add(scopeCard);

        var privacyCard = new Border { Background = ResBrush("AccentSoftBrush"), CornerRadius = new CornerRadius(9), Padding = new Thickness(12) };
        privacyCard.Child = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = "PRIVACY & CONTROL", FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = ResBrush("AccentBrush") },
                new TextBlock { Text = "Google handles your password in its login window. Echo Music receives session cookies; the saved Windows session is DPAPI-protected for your user, but the token is present in app memory while running, so zero risk cannot be promised. Switch back to Guest Mode here or revoke sessions in your Google Account security settings. The current cookie-based web integration is not Google’s official YouTube Data API OAuth flow; full account-library sync is intentionally not added through undocumented endpoints.", FontSize = 12, Foreground = ResBrush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap },
            }
        };
        infoBody.Children.Add(privacyCard);
        stack.Children.Add(new ScrollViewer { Content = infoBody, MaxHeight = 390, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        if (WebView2LoginHelper.IsSupportedOnCurrentOs)
        {
            var browserLoginBtn = new Button
            {
                Classes = { "accentBtn" },
                Content = "Continue with Google",
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            browserLoginBtn.Click += async (_, _) =>
            {
                ModalBackdrop.IsVisible = false;
                var res = await WebView2LoginHelper.ShowGoogleSignInPopupAsync(this, _store.DataDir);
                if (res is { } auth)
                {
                    _yt.SetCookies(auth.CookieHeader, auth.DataSyncId);
                    _store.Settings.CookieHeader = auth.CookieHeader;
                    _store.Settings.DataSyncId = auth.DataSyncId;
                    var info = await _yt.VerifyAccountAsync();
                    _store.Settings.AccountName = info?.Name ?? "YouTube Music account";
                    _store.Settings.AccountHandle = info?.Handle;
                    _store.SaveNow();
                    RefreshAccountBadge();
                    ShowToast($"Signed in as {_store.Settings.AccountName}!");
                }
            };
            stack.Children.Add(browserLoginBtn);
        }

        if (!WebView2LoginHelper.IsSupportedOnCurrentOs)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Google login is available in the Windows desktop app. You can continue using Guest Mode without it.",
                FontSize = 12,
                Foreground = ResBrush("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        if (_yt.IsSignedIn)
        {
            var signOut = new Button { Classes = { "pill" }, Content = "Switch to Guest Mode" };
            signOut.Click += (_, _) =>
            {
                _store.Settings.CookieHeader = null;
                _store.Settings.DataSyncId = null;
                _store.Settings.AccountName = null;
                _store.Settings.AccountHandle = null;
                _yt.SetCookies(null, null);
                _store.SaveNow();
                RefreshAccountBadge();
                ModalBackdrop.IsVisible = false;
                ShowToast("Switched back to Guest Mode");
            };
            row.Children.Add(signOut);
        }
        var close = new Button { Classes = { "pill" }, Content = "Close" };
        close.Click += (_, _) => ModalBackdrop.IsVisible = false;
        row.Children.Add(close);
        stack.Children.Add(row);

        ModalCardHost.Child = stack;
        ModalBackdrop.IsVisible = true;
    }

    private void OpenCommandPaletteModal()
    {
        var stack = new StackPanel { Spacing = 10 };
        var box = new TextBox { Watermark = "Type a command or search songs directly…", Padding = new Thickness(14, 10), FontSize = 14 };
        var list = new StackPanel { Spacing = 4 };

        var commands = new (string Label, Func<Task> Run)[]
        {
            ("Go to Home", () => NavigateToAsync("home", () => LoadHomeViewAsync(null))),
            ("Open Search & Top 100 Charts", () => NavigateToAsync("explore", LoadExploreViewAsync)),
            ("Launch Music Flow (Vertical Discovery)", () => NavigateToAsync("flow", LoadMusicFlowViewAsync)),
            ("Open Your Library", () => NavigateToAsync("library", () => { RenderLibraryView("overview"); return Task.CompletedTask; })),
            ("Open 10-Band Equalizer & Slowed+Reverb DSP", () => NavigateToAsync("eq", () => { RenderEqualizerView(); return Task.CompletedTask; })),
            ("Import Spotify Playlist URL", () => NavigateToAsync("spotify", () => { RenderSpotifyImportView(); return Task.CompletedTask; })),
            ("View Echo Wrapped & Listening Stats", () => NavigateToAsync("stats", () => { RenderStatsWrappedView(null); return Task.CompletedTask; })),
            ("Toggle Compact Mini-Player (Ctrl+M)", () => { ToggleMiniPlayerMode(); return Task.CompletedTask; }),
            ("Toggle Fullscreen Karaoke Player (F11)", () => { ToggleFullscreenOverlay(true); return Task.CompletedTask; }),
            ("Switch Theme: Spotify Green (Default)", () => { _store.Settings.Theme = "SpotifyGreen"; ApplyThemeAndTypography(); _store.SaveDebounced(); return Task.CompletedTask; }),
            ("Switch Theme: Echo Crimson Dark", () => { _store.Settings.Theme = "EchoDark"; ApplyThemeAndTypography(); _store.SaveDebounced(); return Task.CompletedTask; }),
            ("Switch Theme: AMOLED Pitch Black", () => { _store.Settings.Theme = "AmoledBlack"; ApplyThemeAndTypography(); _store.SaveDebounced(); return Task.CompletedTask; }),
            ("Open Settings (Ctrl+,)", () => NavigateToAsync("settings", () => { RenderSettingsView(); return Task.CompletedTask; })),
        };

        void Populate(string filter)
        {
            list.Children.Clear();
            if (!string.IsNullOrWhiteSpace(filter))
            {
                var searchBtn = new Button
                {
                    Classes = { "navItem" },
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new PathIcon { Data = Icons.Search, Width = 14, Height = 14, Foreground = ResBrush("AccentBrush") },
                            new TextBlock { Text = $"Search for \"{filter}\"", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                        }
                    },
                };
                searchBtn.Click += async (_, _) =>
                {
                    ModalBackdrop.IsVisible = false;
                    await NavigateToAsync("search", () => ExecuteSearchViewAsync(filter, "all"));
                };
                list.Children.Add(searchBtn);
            }
            foreach (var (lbl, act) in commands.Where(c => string.IsNullOrWhiteSpace(filter) || c.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(9))
            {
                var captured = act;
                var b = new Button { Classes = { "navItem" }, Content = lbl };
                b.Click += async (_, _) =>
                {
                    ModalBackdrop.IsVisible = false;
                    await captured();
                };
                list.Children.Add(b);
            }
        }

        box.TextChanged += (_, _) => Populate(box.Text ?? "");
        box.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(box.Text))
            {
                var q = box.Text.Trim();
                ModalBackdrop.IsVisible = false;
                await NavigateToAsync("search", () => ExecuteSearchViewAsync(q, "all"));
            }
        };
        Populate("");
        stack.Children.Add(box);
        stack.Children.Add(new ScrollViewer { MaxHeight = 360, Content = list });
        ModalCardHost.Child = stack;
        ModalBackdrop.IsVisible = true;
        box.Focus();
    }

    internal void ShowToast(string message)
    {
        _toastCts?.Cancel();
        var cts = new CancellationTokenSource();
        _toastCts = cts;
        ToastText.Text = message;
        ToastBanner.Opacity = 0;
        _toastTransform.Y = 10;
        ToastBanner.IsVisible = true;
        _ = RunToastSequenceAsync(cts.Token);
    }

    private async Task RunToastSequenceAsync(CancellationToken token)
    {
        try
        {
            await AnimateToastAsync(entering: true, token);
            await Task.Delay(2400, token);
            await AnimateToastAsync(entering: false, token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    ToastBanner.IsVisible = false;
                    ToastBanner.Opacity = 1;
                    _toastTransform.Y = 10;
                }
            });
        }
        catch (OperationCanceledException) { }
    }

    private async Task AnimateToastAsync(bool entering, CancellationToken token)
    {
        const int frames = 9;
        for (int frame = 0; frame <= frames; frame++)
        {
            token.ThrowIfCancellationRequested();
            double t = frame / (double)frames;
            double eased = 1 - Math.Pow(1 - t, 3);
            double opacity = entering ? eased : 1 - eased;
            double offset = entering ? 10 * (1 - eased) : 10 * eased;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ToastBanner.Opacity = opacity;
                _toastTransform.Y = offset;
            });
            if (frame < frames) await Task.Delay(16, token);
        }
    }

    internal void SetCenterContent(Control view)
    {
        CenterViewHost.Children.Clear();
        CenterViewHost.Children.Add(view);
    }

    internal void ShowLoadingSkeleton(string message)
    {
        var st = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 90, 0, 0) };
        st.Children.Add(new TextBlock { Text = message, FontSize = 14.5, FontWeight = FontWeight.SemiBold, Foreground = ResBrush("TextSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        SetCenterContent(st);
    }

    internal Control ErrorCard(string title, string detail, Action onRetry)
    {
        var b = new Border { Background = ResBrush("BgCardBrush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(24), MaxWidth = 520, Margin = new Thickness(0, 40, 0, 0) };
        var s = new StackPanel { Spacing = 10 };
        s.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.Bold });
        s.Children.Add(new TextBlock { Text = detail, FontSize = 12.5, Foreground = ResBrush("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        var retry = new Button { Classes = { "accentBtn" }, Content = "Retry", HorizontalAlignment = HorizontalAlignment.Left };
        retry.Click += (_, _) => onRetry();
        s.Children.Add(retry);
        b.Child = s;
        return b;
    }

    internal Control SectionHeading(string title, Action? onPlayAll = null)
    {
        var g = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        g.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        if (onPlayAll is not null)
        {
            var b = new Button { Classes = { "pill" }, Content = "Play all", Padding = new Thickness(13, 5), FontSize = 12 };
            b.Click += (_, _) => onPlayAll();
            Grid.SetColumn(b, 1);
            g.Children.Add(b);
        }
        return g;
    }

    internal Border SectionCard(string title, string body)
    {
        var st = new StackPanel { Spacing = 6 };
        st.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = ResBrush("TextSecondaryBrush") });
        st.Children.Add(new TextBlock { Text = body, FontSize = 12.5, Foreground = ResBrush("TextPrimaryBrush"), TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
        return new Border
        {
            Background = ResBrush("BgCardBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = st,
        };
    }

    internal Control EmptyHint(string msg) => new TextBlock
    {
        Text = msg,
        FontSize = 13.5,
        Foreground = ResBrush("TextMutedBrush"),
        Margin = new Thickness(4, 18),
        TextWrapping = TextWrapping.Wrap,
    };

    internal IBrush ResBrush(string key) =>
        Resources.TryGetResource(key, ActualThemeVariant, out var val) && val is IBrush b
            ? b
            : Brushes.Gray;

    internal void LoadImageInto(Image target, string? urlOrPath, Control? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath)) return;
        target.Transitions = null;
        target.Opacity = 1;
        Dispatcher.UIThread.Post(() =>
        {
            if (target.Source is not null) return;
            if (target.Parent is Control) StartImageLoadingShimmer(target);
            else
            {
                EventHandler<VisualTreeAttachmentEventArgs>? attached = null;
                attached = (_, _) =>
                {
                    target.AttachedToVisualTree -= attached;
                    if (target.Source is null) StartImageLoadingShimmer(target);
                };
                target.AttachedToVisualTree += attached;
            }
        }, DispatcherPriority.Background);

        _ = LoadImageIntoAsync(target, urlOrPath, fallback);
    }

    private async Task LoadImageIntoAsync(Image target, string? urlOrPath, Control? fallback)
    {
        Bitmap? bmp = null;
        try { bmp = await ImageCache.LoadAsync(urlOrPath, _yt.SharedHttp, _store.ArtCacheDir).ConfigureAwait(false); }
        catch { }
        Dispatcher.UIThread.Post(() =>
        {
            StopImageLoadingShimmer(target);
            if (bmp is not null)
            {
                target.Transitions = null;
                target.Opacity = 1;
                target.Source = bmp;
            }
            else
            {
                target.Transitions = null;
                target.Opacity = 1;
                if (fallback is not null)
                {
                    if (target.Parent is Border frame && ReferenceEquals(frame.Child, target))
                        frame.Child = fallback;
                    else if (target.Parent is Panel panel)
                    {
                        int childIndex = panel.Children.IndexOf(target);
                        if (childIndex >= 0) panel.Children[childIndex] = fallback;
                    }
                }
            }
        });

    }

    private void UpdateImageShimmerColors(Color accent)
    {
        byte[] alpha = { 6, 10, 24, 10, 6 };
        foreach (var state in _imageShimmers.Values)
            for (int i = 0; i < Math.Min(alpha.Length, state.Brush.GradientStops.Count); i++)
                state.Brush.GradientStops[i].Color = Color.FromArgb(alpha[i], accent.R, accent.G, accent.B);
    }

    private void EnsureImageShimmerTimer()
    {
        _imageShimmerTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render,
            (_, _) => UpdateImageShimmerFrame());
        _imageShimmerTimer.Interval = AnimationFrameInterval;
    }

    private void UpdateImageShimmerFrame()
    {
        foreach (var pair in _imageShimmers.ToArray())
        {
            var target = pair.Key;
            var state = pair.Value;
            if (target.Source is not null || (DateTime.UtcNow - state.StartedUtc).TotalSeconds > 12)
            {
                StopImageLoadingShimmer(target);
                continue;
            }
            if (!state.Frame.IsVisible) continue;
            double cycleMs = IsResourceSaverActive ? 3200 : 2600;
            double phase = ((DateTime.UtcNow - state.StartedUtc).TotalMilliseconds % cycleMs) / cycleMs;
            double center = -0.18 + phase * 1.36;
            state.A.Offset = Math.Clamp(center - 0.12, 0, 1);
            state.B.Offset = Math.Clamp(center, 0, 1);
            state.C.Offset = Math.Clamp(center + 0.12, 0, 1);
        }
    }

    private void StartImageLoadingShimmer(Image target)
    {
        if (_imageShimmers.ContainsKey(target) || target.Source is not null) return;
        if (IsResourceSaverActive)
        {
            Control? staticParent = target.Parent as Control;
            while (staticParent is not null && staticParent is not Border) staticParent = staticParent.Parent as Control;
            if (staticParent is Border staticFrame) staticFrame.Background = ResBrush("BgSurfaceBrush");
            return;
        }
        Control? parent = target.Parent as Control;
        while (parent is not null && parent is not Border) parent = parent.Parent as Control;
        if (parent is not Border frame) return;

        var accent = ThemeManager.GetPalette(_store.Settings.Theme).Accent;
        var a = new GradientStop(Color.FromArgb(10, accent.R, accent.G, accent.B), 0.28);
        var b = new GradientStop(Color.FromArgb(24, accent.R, accent.G, accent.B), 0.42);
        var c = new GradientStop(Color.FromArgb(10, accent.R, accent.G, accent.B), 0.56);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(6, accent.R, accent.G, accent.B), 0), a, b, c,
                new GradientStop(Color.FromArgb(6, accent.R, accent.G, accent.B), 1),
            },
        };
        frame.Background = brush;
        _imageShimmers[target] = new ImageShimmerState
        {
            Frame = frame,
            Brush = brush,
            A = a,
            B = b,
            C = c,
            StartedUtc = DateTime.UtcNow,
        };
        EnsureImageShimmerTimer();
        if (IsVisible && WindowState != WindowState.Minimized && !_imageShimmerTimer!.IsEnabled)
            _imageShimmerTimer.Start();
    }

    private void StopImageLoadingShimmer(Image target)
    {
        if (!_imageShimmers.Remove(target, out var state)) return;
        if (ReferenceEquals(state.Frame.Background, state.Brush))
            state.Frame.Background = ResBrush("BgSurfaceBrush");
        if (_imageShimmers.Count == 0) _imageShimmerTimer?.Stop();
    }

}
