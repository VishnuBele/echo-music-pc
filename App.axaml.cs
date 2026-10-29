using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using EchoMusic.Core;
using EchoMusic.UI;

namespace EchoMusic;

public sealed partial class App : Application
{
    public static bool VisualSmokeMode { get; set; }
    public static bool VisualSuggestionsOnly { get; set; }
    public static string? VisualSmokeOutDir { get; set; }
    public static string[] StartupFileArgs { get; set; } = Array.Empty<string>();

    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var store = new AppStore();
        DynamicConfigService.ApplyOverridesIfPresent(store.DataDir);
        var pal = ThemeManager.GetPalette(store.Settings.Theme);
        ThemeManager.ApplyToResources(Resources, pal);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWin = new MainWindow(store);
            desktop.MainWindow = mainWin;
            SetupSystemTrayIcon(desktop, mainWin);

            if (!VisualSmokeMode)
            {
                SingleInstanceIpc.StartServer(incomingArgs =>
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        mainWin.Show();
                        if (mainWin.WindowState == WindowState.Minimized) mainWin.WindowState = WindowState.Normal;
                        mainWin.Activate();
                        var files = incomingArgs.Where(File.Exists).ToList();
                        if (files.Count > 0)
                            await mainWin.Engine.PlayLocalFilePathsAsync(files);
                    });
                });

                var initialFiles = StartupFileArgs.Where(File.Exists).ToList();
                if (initialFiles.Count > 0)
                {
                    mainWin.Opened += async (_, _) => await mainWin.Engine.PlayLocalFilePathsAsync(initialFiles);
                }
            }

            if (VisualSmokeMode && !string.IsNullOrEmpty(VisualSmokeOutDir))
            {
                _ = RunVisualSmokeSequenceAsync(desktop, mainWin, VisualSmokeOutDir!);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void UpdateTrayIcon(string appIconStyle)
    {
        if (_trayIcon is null) return;
        try
        {
            using var stream = AssetLoader.Open(new Uri(MainWindow.GetAppIconAssetUri(appIconStyle)));
            using var bitmap = new Bitmap(stream);
            _trayIcon.Icon = new WindowIcon(bitmap);
        }
        catch { }
    }

    private void SetupSystemTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, MainWindow win)
    {
        try
        {
            using var iconStream = AssetLoader.Open(new Uri(MainWindow.GetAppIconAssetUri(win.Store.Settings.AppIconStyle)));
            using var trayBmp = new Bitmap(iconStream);
            var menu = new NativeMenu();

            var showItem = new NativeMenuItem("Open Echo Music");
            showItem.Click += (_, _) =>
            {
                win.Show();
                if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
                win.Activate();
            };

            var playItem = new NativeMenuItem("Play / Pause");
            playItem.Click += (_, _) => win.Engine.TogglePlayPause();

            var nextItem = new NativeMenuItem("Next Track");
            nextItem.Click += async (_, _) => await win.Engine.NextAsync();

            var prevItem = new NativeMenuItem("Previous Track");
            prevItem.Click += async (_, _) => await win.Engine.PreviousAsync();

            var quitItem = new NativeMenuItem("Quit Echo Music");
            quitItem.Click += (_, _) =>
            {
                win.AllowTrueExit = true;
                win.Close();
                desktop.Shutdown();
            };

            menu.Items.Add(showItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(playItem);
            menu.Items.Add(nextItem);
            menu.Items.Add(prevItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(quitItem);

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(trayBmp),
                ToolTipText = "Echo Music",
                Menu = menu,
                IsVisible = true,
            };
            _trayIcon.Clicked += (_, _) =>
            {
                win.Show();
                if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
                win.Activate();
            };
        }
        catch
        {
            // Headless X11 without status-notifier tray daemon: safely ignore
        }
    }

    private static async Task RunVisualSmokeSequenceAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindow win, string outDir)
    {
        Directory.CreateDirectory(outDir);
        try
        {
            if (App.VisualSuggestionsOnly)
            {
                await Task.Delay(2500); // allow the home view and search control to finish attaching
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    win.GlobalSearchBox.Focus();
                    win.GlobalSearchBox.Text = "kill chori";
                });
                await Task.Delay(2200);
                Console.WriteLine($"[visual-suggestions] popupOpen={win.SearchSuggestionsOpen}, rows={win.SearchSuggestionsCount}");
                await SaveWindowRenderAsync(win, Path.Combine(outDir, "search-suggestions.png"));
                await Task.Delay(180);
                desktop.Shutdown();
                return;
            }

            // 1. Default Spotify Green Home + Live Now Playing Right Panel
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                win.Store.Settings.Theme = "SpotifyGreen";
                win.Store.Settings.FontChoice = "SpotifyCircular";
                win.Store.Settings.PlayerStyle = "Circle";
                win.ApplyThemeAndTypography();
            });
            await Task.Delay(5200);
            var sr = await win._yt.SearchAsync("Blinding Lights The Weeknd", "songs");
            if (sr.Tracks.Count > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() => win.Engine.PlayQueueAsync(sr.Tracks.Take(6), 0, autoRadioIfSingle: false));
                await Task.Delay(4500);
            }
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "01_home_spotify_green.png"));

            // 1b. Scroll Home down to verify extended multi-page PC Home shelves (no abrupt 1-2 scroll cutoff!)
            await Dispatcher.UIThread.InvokeAsync(() => win.CenterScrollViewer.Offset = new Vector(0, 920));
            await Task.Delay(1200);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "01b_home_extended_scroll.png"));
            await Dispatcher.UIThread.InvokeAsync(() => win.CenterScrollViewer.Offset = new Vector(0, 9200));
            await Task.Delay(900);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "01c_home_mood_genres.png"));

            // 2. Search suggestions: confirm the popup stays centered beneath the search field.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                win.GlobalSearchBox.Focus();
                win.GlobalSearchBox.Text = "kill";
            });
            await Task.Delay(900);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "01d_search_suggestions_anchored.png"));

            // 2b. Search 'kill chori' - verify Top Result card + Top matches + Music Videos parity with OG Echo APK!
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                win.GlobalSearchBox.Text = "kill chori";
                return win.NavigateToAsync("search", () => win.ExecuteSearchViewAsync("kill chori", "all"));
            });
            await Task.Delay(3800);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "02_search_kill_chori.png"));
            // Verify the audio-only setting filters the Top Result hero as well as rows and quick matches.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                win.Store.Settings.HideVideoSongs = true;
                return win.ExecuteSearchViewAsync("kill chori", "all");
            });
            await Task.Delay(1800);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "02e_search_audio_only_filter.png"));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                win.Store.Settings.HideVideoSongs = false;
                return win.ExecuteSearchViewAsync("kill chori", "all");
            });
            await Task.Delay(1200);
            var kc = await win._yt.SearchAsync("kill chori", "all");
            if (kc.TopResultTrack is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => win.Engine.PlaySingleAsync(kc.TopResultTrack));
                await Dispatcher.UIThread.InvokeAsync(() => win.SetDockTab("lyrics", ensureOpen: true));
                await Task.Delay(350);
                await SaveWindowRenderAsync(win, Path.Combine(outDir, "02b_kill_chori_lyrics_loading.png"));
                await Task.Delay(25000); // allow timed sources to finish before showing any plain-text fallback
                await SaveWindowRenderAsync(win, Path.Combine(outDir, "02c_kill_chori_now_playing.png"));
                await Task.Delay(1200);
                await SaveWindowRenderAsync(win, Path.Combine(outDir, "02c_kill_chori_lyrics_result.png"));
                await Dispatcher.UIThread.InvokeAsync(() => win.NavigateToAsync("flow", win.LoadMusicFlowViewAsync));
                await Task.Delay(900);
                await SaveWindowRenderAsync(win, Path.Combine(outDir, "02d_music_flow_lyrics.png"));
            }

            // 3. Your Library
            await Dispatcher.UIThread.InvokeAsync(() => win.NavigateToAsync("library", () => { win.RenderLibraryView("overview"); return Task.CompletedTask; }));
            await Task.Delay(1000);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "03_library.png"));

            // 3b. Temporary Spotify-style local playlist smoke: artwork, icon actions, header and album column.
            if (kc.Tracks.Count >= 4)
            {
                var smokePlaylist = win.Store.CreatePlaylist("Ufff · UI smoke", "Temporary layout check", kc.Tracks.Take(5));
                smokePlaylist.Source = "Spotify";
                smokePlaylist.OwnerName = "V!SHNU";
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() => win.NavigateToAsync("pl:" + smokePlaylist.Id, () => { win.RenderLocalPlaylistDetail(smokePlaylist); return Task.CompletedTask; }));
                    await Task.Delay(900);
                    await SaveWindowRenderAsync(win, Path.Combine(outDir, "03b_playlist_controls_and_table.png"));
                }
                finally
                {
                    win.Store.DeletePlaylist(smokePlaylist.Id);
                    win.RefreshSidebarBadgesAndPlaylists();
                }
            }

            // 4. Settings (showing Spotify Green + Spotify Circular active)
            await Dispatcher.UIThread.InvokeAsync(() => win.NavigateToAsync("settings", () => { win.RenderSettingsView(); return Task.CompletedTask; }));
            await Task.Delay(900);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "04_settings.png"));
            await Dispatcher.UIThread.InvokeAsync(() => win.CenterScrollViewer.Offset = new Vector(0, 470));
            await Task.Delay(450);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "04c_settings_icon_previews.png"));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                win.ApplyLeftSidebarMode("Compact", rerenderSettings: false);
                win.RenderSettingsView();
                win.CenterScrollViewer.Offset = new Vector(0, 0);
            });
            await Task.Delay(350);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "04f_settings_compact_sidebar.png"));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                win.ApplyLeftSidebarMode("Hidden", rerenderSettings: false);
                win.RenderSettingsView();
                win.CenterScrollViewer.Offset = new Vector(0, 0);
            });
            await Task.Delay(350);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "04g_settings_hidden_sidebar.png"));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                win.ApplyLeftSidebarMode("Expanded", rerenderSettings: false);
                win.Store.Settings.Theme = "EchoLight";
                win.ApplyThemeAndTypography();
                win.RenderSettingsView();
                win.CenterScrollViewer.Offset = new Vector(0, 0);
            });
            await Task.Delay(350);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "04h_light_theme_navigation.png"));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                win.Store.Settings.Theme = "SpotifyGreen";
                win.ApplyThemeAndTypography();
                win.RenderSettingsView();
            });
            await Dispatcher.UIThread.InvokeAsync(() => win.NavigateToAsync("about", () => { win.RenderAboutView(); return Task.CompletedTask; }));
            await Task.Delay(600);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "04b_about.png"));
            await Dispatcher.UIThread.InvokeAsync(() => win.ToggleLeftSidebar());
            await Task.Delay(240);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "04d_left_sidebar_collapsed.png"));
            await Dispatcher.UIThread.InvokeAsync(() => win.ToggleLeftSidebar());
            await Dispatcher.UIThread.InvokeAsync(() => win.ToggleRightDock(false));
            await Task.Delay(240);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "04e_right_sidebar_collapsed.png"));
            await Dispatcher.UIThread.InvokeAsync(() => win.ToggleRightDock(true));
            await Task.Delay(240);

            // 5. Fullscreen Blurred-Artwork & Karaoke Lyrics Player
            await Dispatcher.UIThread.InvokeAsync(() => win.FullscreenOverlay.IsVisible = true);
            await Task.Delay(1200);
            await SaveWindowRenderAsync(win, Path.Combine(outDir, "05_fullscreen_player.png"));

            // Verify a plain/native lyric response renders as separate lines rather than one paragraph.
            await Dispatcher.UIThread.InvokeAsync(() => win.FullscreenOverlay.IsVisible = false);
            var starboy = await win._yt.SearchAsync("Starboy The Weeknd", "songs");
            var starboyTrack = starboy.Tracks.FirstOrDefault();
            if (starboyTrack is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => win.Engine.PlaySingleAsync(starboyTrack));
                await Dispatcher.UIThread.InvokeAsync(() => win.SetDockTab("lyrics", ensureOpen: true));
                await Task.Delay(350);
                await SaveWindowRenderAsync(win, Path.Combine(outDir, "06_starboy_lyrics_loading.png"));
                await Task.Delay(7650);
                await SaveWindowRenderAsync(win, Path.Combine(outDir, "06_starboy_formatted_lyrics.png"));
                await Dispatcher.UIThread.InvokeAsync(() => win.FullscreenOverlay.IsVisible = true);
                await Task.Delay(500);
                await SaveWindowRenderAsync(win, Path.Combine(outDir, "06b_starboy_fullscreen_lyrics.png"));
            }
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(outDir, "smoke_error.txt"), ex.ToString());
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                win.AllowTrueExit = true;
                win.Close();
                desktop.Shutdown();
            });
        }
    }

    private static async Task SaveWindowRenderAsync(Window win, string filePath)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var size = new PixelSize((int)Math.Max(800, win.Bounds.Width), (int)Math.Max(600, win.Bounds.Height));
            using var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
            rtb.Render(win);
            rtb.Save(filePath);
        });
    }
}
