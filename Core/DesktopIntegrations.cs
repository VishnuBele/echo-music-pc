using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

#if WINDOWS_NATIVE
using Microsoft.Web.WebView2.Core;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;
#endif

namespace EchoMusic.Core;

// =============================================================================
// 1. NATIVE WINDOWS 10/11 SMTC (Album-Art Volume / Quick-Settings / Lock-Screen Flyout)
// =============================================================================
public sealed class WindowsSmtcBridge : IDisposable
{
#if WINDOWS_NATIVE
    private MediaPlayer? _player;
    private SystemMediaTransportControls? _smtc;
#endif

    public WindowsSmtcBridge(AudioEngine engine, Func<bool> isEnabled)
    {
#if WINDOWS_NATIVE
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            _player = new MediaPlayer();
            _player.CommandManager.IsEnabled = false;
            _smtc = _player.SystemMediaTransportControls;
            _smtc.IsEnabled = isEnabled();
            _smtc.IsPlayEnabled = true;
            _smtc.IsPauseEnabled = true;
            _smtc.IsNextEnabled = true;
            _smtc.IsPreviousEnabled = true;
            _smtc.IsStopEnabled = true;

            _smtc.ButtonPressed += (sender, args) =>
            {
                if (!isEnabled()) return;
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        engine.Resume();
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        engine.Pause();
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        _ = engine.NextAsync();
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                        _ = engine.PreviousAsync();
                        break;
                    case SystemMediaTransportControlsButton.Stop:
                        engine.Pause();
                        break;
                }
            };
        }
        catch { /* safe fallback on stripped Server Core */ }
#endif
    }

    public void SetEnabled(bool enabled)
    {
#if WINDOWS_NATIVE
        if (_smtc is not null)
        {
            try { _smtc.IsEnabled = enabled; } catch { }
        }
#endif
    }

    public void UpdateTrackAndState(TrackItem? track, bool isPlaying, double posSec, double durSec)
    {
#if WINDOWS_NATIVE
        if (_smtc is null) return;
        try
        {
            if (track is null)
            {
                _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                return;
            }

            _smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

            var updater = _smtc.DisplayUpdater;
            updater.AppMediaId = Win32TaskbarIconHelper.AppUserModelId;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = track.Title;
            updater.MusicProperties.Artist = track.ArtistNames;
            updater.MusicProperties.AlbumTitle = track.Album?.Name ?? "Echo Music";

            var thumbUrl = InnertubeParsers.UpgradeThumb(track.Thumbnail, 544);
            if (!string.IsNullOrEmpty(thumbUrl) && thumbUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(thumbUrl));
            }
            updater.Update();

            if (durSec > 0)
            {
                var timeline = new SystemMediaTransportControlsTimelineProperties
                {
                    StartTime = TimeSpan.Zero,
                    MinSeekTime = TimeSpan.Zero,
                    Position = TimeSpan.FromSeconds(Math.Clamp(posSec, 0, durSec)),
                    MaxSeekTime = TimeSpan.FromSeconds(durSec),
                    EndTime = TimeSpan.FromSeconds(durSec),
                };
                _smtc.UpdateTimelineProperties(timeline);
            }
        }
        catch { }
#endif
    }

    public void Dispose()
    {
#if WINDOWS_NATIVE
        try { _player?.Dispose(); } catch { }
#endif
    }
}

// =============================================================================
// 2. DISCORD RICH PRESENCE (Zero-DLL Pure-C# Named Pipe IPC Client)
// =============================================================================
public sealed class DiscordRpcClient : IDisposable
{
    // Public Discord Application ID registered for music player Rich Presence ("Echo Music")
    private const string OfficialDiscordClientId = "1518210534070292541";


    private NamedPipeClientStream? _pipe;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private bool _handshaken;
    private string? _connectedClientId;
    private bool _disposed;

    public bool IsEnabled { get; set; } = true;

    public async Task UpdatePresenceAsync(TrackItem? track, bool isPlaying, double posSec, double durSec)
    {
        if (_disposed) return;
        if (!IsEnabled || track is null || !isPlaying)
        {
            await ClearPresenceAsync();
            return;
        }

        await _ioLock.WaitAsync();
        try
        {
            if (!await EnsureConnectedAsync()) return;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long startMs = nowMs - (long)(posSec * 1000);
            long endMs = durSec > 1 ? startMs + (long)(durSec * 1000) : 0;

            // Discord IPC accepts registered application asset keys, not arbitrary image URLs.
            // Sending the YouTube cover URL as large_image makes Discord reject SET_ACTIVITY;
            // keep the activity payload portable and let Discord show the app's configured icon.
            var activity = new JsonObject
            {
                ["type"] = 2, // ActivityType.Listening
                ["details"] = ClipStr(track.Title, 120),
                ["state"] = ClipStr($"by {(!string.IsNullOrWhiteSpace(track.AlbumArtist) ? track.AlbumArtist : track.ArtistNames)}", 120),
            };

            if (endMs > startMs)
            {
                activity["timestamps"] = new JsonObject
                {
                    ["start"] = startMs,
                    ["end"] = endMs,
                };
            }

            if (!track.IsLocal && !string.IsNullOrEmpty(track.Id))
            {
                activity["buttons"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["label"] = "Listen on Song.link",
                        ["url"] = $"https://song.link/y/{track.Id}"
                    }
                };
            }

            var frame = new JsonObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new JsonObject
                {
                    ["pid"] = Environment.ProcessId,
                    ["activity"] = activity,
                },
                ["nonce"] = Guid.NewGuid().ToString("N"),
            };

            await WriteFrameAsync(1, frame.ToJsonString());
            _ = await TryReadFrameAsync(250);
        }
        catch
        {
            ResetPipe();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task ClearPresenceAsync()
    {
        if (_pipe is null || !_pipe.IsConnected) return;
        await _ioLock.WaitAsync();
        try
        {
            if (_pipe is { IsConnected: true })
            {
                var frame = new JsonObject
                {
                    ["cmd"] = "SET_ACTIVITY",
                    ["args"] = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = null },
                    ["nonce"] = Guid.NewGuid().ToString("N"),
                };
                await WriteFrameAsync(1, frame.ToJsonString());
            }
        }
        catch { ResetPipe(); }
        finally { _ioLock.Release(); }
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        var desiredClientId = OfficialDiscordClientId;
        if (_pipe is { IsConnected: true } && _handshaken && _connectedClientId == desiredClientId) return true;
        ResetPipe();

        for (int i = 0; i <= 9; i++)
        {
            string pipeName = OperatingSystem.IsWindows()
                ? $"discord-ipc-{i}"
                : Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/tmp", $"discord-ipc-{i}");
            try
            {
                var p = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var cts = new CancellationTokenSource(120);
                await p.ConnectAsync(cts.Token);
                _pipe = p;

                var hs = new JsonObject { ["v"] = 1, ["client_id"] = desiredClientId };
                await WriteFrameAsync(0, hs.ToJsonString());
                var reply = await TryReadFrameAsync(800);
                if (reply is not null && IsReadyFrame(reply))
                {
                    _handshaken = true;
                    _connectedClientId = desiredClientId;
                    return true;
                }
                ResetPipe();
            }
            catch
            {
                ResetPipe();
            }
        }
        return false;
    }

    private async Task WriteFrameAsync(int opcode, string json)
    {
        if (_pipe is null) return;
        var payload = Encoding.UTF8.GetBytes(json);
        var hdr = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(hdr.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(hdr.AsSpan(4, 4), payload.Length);
        await _pipe.WriteAsync(hdr);
        await _pipe.WriteAsync(payload);
        await _pipe.FlushAsync();
    }

    private async Task<string?> TryReadFrameAsync(int timeoutMs)
    {
        if (_pipe is null) return null;
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (_pipe is { IsConnected: true })
            {
                var hdr = new byte[8];
                if (!await ReadExactlyAsync(_pipe, hdr, cts.Token)) return null;
                int opcode = BinaryPrimitives.ReadInt32LittleEndian(hdr.AsSpan(0, 4));
                int len = BinaryPrimitives.ReadInt32LittleEndian(hdr.AsSpan(4, 4));
                if (len < 0 || len > 262144) return null;
                var payload = new byte[len];
                if (len > 0 && !await ReadExactlyAsync(_pipe, payload, cts.Token)) return null;
                if (opcode == 3) // Discord IPC PING
                {
                    await WriteFrameAsync(4, Encoding.UTF8.GetString(payload));
                    continue;
                }
                if (opcode == 2) { ResetPipe(); return null; } // Discord IPC CLOSE
                return Encoding.UTF8.GetString(payload);
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (IOException) { ResetPipe(); }
        catch (ObjectDisposedException) { ResetPipe(); }
        return null;
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer[offset..], ct);
            if (n <= 0) return false;
            offset += n;
        }
        return true;
    }

    private static bool IsReadyFrame(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("evt", out var evt)
                && evt.ValueKind == JsonValueKind.String
                && string.Equals(evt.GetString(), "READY", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void ResetPipe()
    {
        _handshaken = false;
        _connectedClientId = null;
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
    }

    private static string ClipStr(string s, int max) =>
        string.IsNullOrEmpty(s) ? "Echo Music" : (s.Length <= max ? s : s[..(max - 1)] + "…");

    public void Dispose()
    {
        _disposed = true;
        ResetPipe();
    }
}

// =============================================================================
// 3. SPONSORBLOCK SERVICE (Auto-Skips Non-Music Skits, Intros & Outros)
// =============================================================================
public sealed record SkipSegment(double StartSec, double EndSec, string Category);

public sealed class SponsorBlockService
{
    private sealed record CacheEntry(List<SkipSegment> Segments, DateTimeOffset ExpiresAt);
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public SponsorBlockService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<SkipSegment>> GetSegmentsAsync(string videoId, string categoriesCsv, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(videoId) || videoId.StartsWith("local:", StringComparison.Ordinal))
            return new();

        var categories = categoriesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToLowerInvariant()).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        if (categories.Length == 0) return new();
        var key = videoId + "|" + string.Join(',', categories);
        if (_cache.TryGetValue(key, out var hit))
        {
            if (hit.ExpiresAt > DateTimeOffset.UtcNow) return hit.Segments.ToList();
            _cache.TryRemove(key, out _);
        }

        try
        {
            var jsonArr = new JsonArray(categories.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()).ToJsonString();
            var url = $"https://sponsor.ajay.app/api/skipSegments?videoID={Uri.EscapeDataString(videoId)}&categories={Uri.EscapeDataString(jsonArr)}";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var res = await _http.GetAsync(url, cts.Token);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _cache[key] = new CacheEntry(new(), DateTimeOffset.UtcNow.AddMinutes(5));
                return new();
            }
            if (!res.IsSuccessStatusCode) return new(); // Do not cache transient failures or category-specific errors.

            var arr = JsonNode.Parse(await res.Content.ReadAsStringAsync(cts.Token)) as JsonArray;
            var list = new List<SkipSegment>();
            if (arr is not null)
            {
                foreach (var item in arr)
                {
                    if (item?["segment"] is JsonArray seg && seg.Count >= 2)
                    {
                        double st = seg[0]?.GetValue<double>() ?? 0;
                        double en = seg[1]?.GetValue<double>() ?? 0;
                        string cat = item["category"]?.GetValue<string>() ?? "unknown";
                        if (double.IsFinite(st) && double.IsFinite(en) && st >= 0 && en > st && en - st >= 1.5)
                            list.Add(new SkipSegment(st, en, cat));
                    }
                }
            }
            list.Sort((a, b) => a.StartSec.CompareTo(b.StartSec));
            _cache[key] = new CacheEntry(list, DateTimeOffset.UtcNow.AddMinutes(30));
            return list.ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new();
        }
        catch
        {
            return new();
        }
    }
}

// =============================================================================
// 5. OPTIONAL WEBVIEW2 GOOGLE SIGN-IN POPUP (Guest Mode Remains 100% Default!)
// =============================================================================
public static class WebView2LoginHelper
{
    public static bool IsSupportedOnCurrentOs =>
#if WINDOWS_NATIVE
        OperatingSystem.IsWindows();
#else
        false;
#endif

    public static async Task<(string CookieHeader, string? DataSyncId)?> ShowGoogleSignInPopupAsync(Window owner, string dataDir)
    {
#if WINDOWS_NATIVE
        if (!OperatingSystem.IsWindows()) return null;

        var tcs = new TaskCompletionSource<(string, string?)?>();
        string? authProfileDir = null;
        var popup = new Window
        {
            Title = "Google login - Echo Music",
            Width = 520,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#0E1017")),
        };

        var statusLabel = new TextBlock
        {
            Text = "Continue with Google. Echo Music reads the YouTube Music session after Google completes login; your password is not collected by the app.",
            FontSize = 12,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(14, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        var hostBorder = new Border { Margin = new Thickness(0, 38, 0, 0) };
        var grid = new Grid();
        grid.Children.Add(statusLabel);
        grid.Children.Add(hostBorder);
        popup.Content = grid;

        CoreWebView2Controller? controller = null;

        popup.Opened += async (_, _) =>
        {
            try
            {
                var hwnd = popup.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd == IntPtr.Zero) return;

                authProfileDir = Path.Combine(dataDir, "AuthSessions", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(authProfileDir);
                var env = await CoreWebView2Environment.CreateAsync(null, authProfileDir);
                controller = await env.CreateCoreWebView2ControllerAsync(hwnd);

                void SyncBounds()
                {
                    if (controller is null) return;
                    double scale = popup.RenderScaling;
                    int topPx = (int)(42 * scale);
                    int wPx = Math.Max(100, (int)(popup.ClientSize.Width * scale));
                    int hPx = Math.Max(100, (int)(popup.ClientSize.Height * scale) - topPx);
                    controller.Bounds = new System.Drawing.Rectangle(0, topPx, wPx, hPx);
                }

                SyncBounds();
                popup.SizeChanged += (_, _) => SyncBounds();

                controller.CoreWebView2.NavigationCompleted += async (_, _) =>
                {
                    try
                    {
                        var src = controller.CoreWebView2.Source ?? "";
                        if (!src.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase)) return;

                        var cookies = await controller.CoreWebView2.CookieManager.GetCookiesAsync("https://music.youtube.com");
                        bool hasAuth = cookies.Any(c => c.Name is "SAPISID" or "__Secure-3PAPISID");
                        if (!hasAuth) return;

                        var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                        string? dataSync = null;
                        try
                        {
                            var rawCfg = await controller.CoreWebView2.ExecuteScriptAsync("window.ytcfg && window.ytcfg.get ? window.ytcfg.get('DATASYNC_ID') : ''");
                            var unquoted = JsonSerializer.Deserialize<string>(rawCfg);
                            if (!string.IsNullOrEmpty(unquoted))
                                dataSync = unquoted.Split("||")[0];
                        }
                        catch { }

                        tcs.TrySetResult((cookieHeader, dataSync));
                        Dispatcher.UIThread.Post(() => popup.Close());
                    }
                    catch { }
                };

                controller.CoreWebView2.Navigate("https://accounts.google.com/ServiceLogin?ltmpl=music&service=youtube&continue=https%3A%2F%2Fmusic.youtube.com%2F");
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Google login could not open ({ex.Message}). Guest Mode is still available.";
            }
        };

        popup.Closed += (_, _) =>
        {
            try { controller?.CoreWebView2.CookieManager.DeleteAllCookies(); } catch { }
            try { controller?.Close(); } catch { }
            if (!string.IsNullOrWhiteSpace(authProfileDir))
            {
                string profileToDelete = authProfileDir;
                _ = Task.Run(async () =>
                {
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        try { await Task.Delay(250); Directory.Delete(profileToDelete, recursive: true); break; }
                        catch (DirectoryNotFoundException) { break; }
                        catch { }
                    }
                });
            }
            tcs.TrySetResult(null);
        };

        await popup.ShowDialog(owner);
        return await tcs.Task;
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}

// =============================================================================
// 6. SINGLE-INSTANCE IPC & DYNAMIC STREAM CONFIG UPDATER
// =============================================================================
public static class SingleInstanceIpc
{
    private const string PipeName = "EchoMusicDesktop_SingleInstance_v2";

    public static bool TryForwardArgsToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(160);
            var payload = Encoding.UTF8.GetBytes(string.Join("\n", args));
            client.Write(payload, 0, payload.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void StartServer(Action<string[]> onFilesOrActivate)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var sr = new StreamReader(server, Encoding.UTF8);
                    var text = await sr.ReadToEndAsync();
                    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    onFilesOrActivate(lines);
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        });
    }
}

public static class Win32TaskbarIconHelper
{
    public const string AppUserModelId = "EchoMusic.Desktop.EmeraldWave.v20";

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
        public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr StringValue;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    private static readonly PropertyKey AppUserModelIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, out IPropertyStore propertyStore);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, uint flags, ref Guid riid, out IPropertyStore propertyStore);

    private const uint WM_SETICON = 0x0080;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static void RegisterFreshAppUserModelId()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch { }
    }

    public static void SetWindowAppUserModelId(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return;
        IntPtr appId = IntPtr.Zero;
        IPropertyStore? store = null;
        try
        {
            appId = Marshal.StringToCoTaskMemUni(AppUserModelId);
            var value = new PropVariant { VariantType = 31, StringValue = appId }; // VT_LPWSTR
            var key = AppUserModelIdKey;
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) >= 0)
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
        }
        catch { }
        finally
        {
            if (store is not null && Marshal.IsComObject(store))
            {
                try { Marshal.FinalReleaseComObject(store); } catch { }
            }
            if (appId != IntPtr.Zero) Marshal.FreeCoTaskMem(appId);
        }
    }

    public static void SetInstalledShortcutsAppUserModelId()
    {
        if (!OperatingSystem.IsWindows()) return;
        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Echo Music", "Echo Music.lnk");
        string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Echo Music.lnk");
        var iid = typeof(IPropertyStore).GUID;
        foreach (var path in new[] { startMenu, desktop })
        {
            if (!File.Exists(path)) continue;
            IPropertyStore? store = null;
            IntPtr appId = IntPtr.Zero;
            try
            {
                if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 2, ref iid, out store) < 0) continue;
                appId = Marshal.StringToCoTaskMemUni(AppUserModelId);
                var value = new PropVariant { VariantType = 31, StringValue = appId };
                var key = AppUserModelIdKey;
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            catch { }
            finally
            {
                if (store is not null && Marshal.IsComObject(store))
                {
                    try { Marshal.FinalReleaseComObject(store); } catch { }
                }
                if (appId != IntPtr.Zero) Marshal.FreeCoTaskMem(appId);
            }
        }
    }

    private static void SetInstalledShortcutIcons(string iconPath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(iconPath)) return;
        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Echo Music", "Echo Music.lnk");
        string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Echo Music.lnk");
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return;
            foreach (var shortcutPath in new[] { startMenu, desktop })
            {
                if (!File.Exists(shortcutPath)) continue;
                object? shortcut = null;
                try
                {
                    dynamic dispatch = shell;
                    shortcut = dispatch.CreateShortcut(shortcutPath);
                    dynamic link = shortcut;
                    link.IconLocation = $"{iconPath},0";
                    link.Save();
                }
                catch { }
                finally
                {
                    if (shortcut is not null && Marshal.IsComObject(shortcut))
                        try { Marshal.FinalReleaseComObject(shortcut); } catch { }
                }
            }
            SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
                try { Marshal.FinalReleaseComObject(shell); } catch { }
        }
    }

    public static void EnforceNativeWindowIcon(Window window, string? style = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;

            string baseDir = AppContext.BaseDirectory;
            string iconRelative = (style ?? "WaveDefault") switch
            {
                "Eko" => Path.Combine("Assets", "echo-eko.ico"),
                "EchoCat" => Path.Combine("Assets", "MobileIcons", "echo-cat.ico"),
                "CrazyBlue" => Path.Combine("Assets", "MobileIcons", "crazy-blue.ico"),
                "Sky" => Path.Combine("Assets", "MobileIcons", "sky.ico"),
                "MobileBillie" => Path.Combine("Assets", "MobileIcons", "billie-eilish.ico"),
                "MobileCat" => Path.Combine("Assets", "MobileIcons", "cat.ico"),
                "MobileCraft" => Path.Combine("Assets", "MobileIcons", "craft.ico"),
                "MobileLegacy" => Path.Combine("Assets", "MobileIcons", "legacy.ico"),
                "MobileNew" => Path.Combine("Assets", "MobileIcons", "new.ico"),
                "MobilePookie" => Path.Combine("Assets", "MobileIcons", "pookie.ico"),
                "MobileSabrina" => Path.Combine("Assets", "MobileIcons", "sabrina-1.ico"),
                "MobileSabrina2" => Path.Combine("Assets", "MobileIcons", "sabrina-2.ico"),
                "MobileSadie" => Path.Combine("Assets", "MobileIcons", "sadie.ico"),
                "MonoWhite" => Path.Combine("Assets", "MobileIcons", "echo-white.ico"),
                "MonoBlack" => Path.Combine("Assets", "MobileIcons", "echo-black.ico"),
                _ => Path.Combine("Assets", "echo-emerald-v20.ico"),
            };
            string icoPath = Path.Combine(baseDir, iconRelative);
            if (!File.Exists(icoPath)) icoPath = Path.Combine(baseDir, "echo-emerald-v20.ico");
            if (!File.Exists(icoPath)) icoPath = Path.Combine(baseDir, "echo.ico");

            IntPtr hBig = IntPtr.Zero;
            IntPtr hSmall = IntPtr.Zero;

            if (File.Exists(icoPath))
            {
                hBig = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, 256, 256, LR_LOADFROMFILE);
                if (hBig == IntPtr.Zero)
                    hBig = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, 48, 48, LR_LOADFROMFILE);
                hSmall = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
            }
            if (hBig == IntPtr.Zero)
            {
                // Fallback to embedded PE icon resource ID 32512 (IDI_APPLICATION) / #1
                var hMod = GetModuleHandleW(null);
                hBig = LoadImageW(hMod, "#32512", IMAGE_ICON, 48, 48, LR_DEFAULTSIZE);
                hSmall = LoadImageW(hMod, "#32512", IMAGE_ICON, 16, 16, 0);
            }

            if (hSmall != IntPtr.Zero) SendMessageW(handle, WM_SETICON, (IntPtr)0, hSmall); // ICON_SMALL
            if (hBig != IntPtr.Zero) SendMessageW(handle, WM_SETICON, (IntPtr)1, hBig);     // ICON_BIG
            SetInstalledShortcutIcons(icoPath);
        }
        catch { }
    }
}

public static class DynamicConfigService
{
    public const string CurrentAppVersion = "2.0.0";

    public static void ApplyOverridesIfPresent(string dataDir)
    {
        try
        {
            var path = Path.Combine(dataDir, "clients_override.json");
            if (!File.Exists(path)) return;
            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node?["visionOsVersion"]?.GetValue<string>() is { Length: > 0 } vo)
                YouTubeClients.VisionOs = YouTubeClients.VisionOs with { Version = vo };
            if (node?["androidVrVersion"]?.GetValue<string>() is { Length: > 0 } avr)
                YouTubeClients.AndroidVr = YouTubeClients.AndroidVr with { Version = avr };
            if (node?["webRemixVersion"]?.GetValue<string>() is { Length: > 0 } wr)
                YouTubeClients.WebRemix = YouTubeClients.WebRemix with { Version = wr };
        }
        catch { }
    }

    public static async Task<string> CheckUpdatesAndRefreshClientsAsync(HttpClient http, InnerTubeClient yt, string dataDir)
    {
        await yt.EnsureVisitorDataAsync(force: true);
        try
        {
            // Refresh latest live WEB_REMIX INNERTUBE_CLIENT_VERSION directly from music.youtube.com!
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://music.youtube.com/sw.js_data");
            req.Headers.TryAddWithoutValidation("User-Agent", YouTubeClients.WebUa);
            using var res = await http.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var txt = await res.Content.ReadAsStringAsync();
                var m = System.Text.RegularExpressions.Regex.Match(txt, @"1\.202\d{5}\.\d{2}\.\d{2}");
                if (m.Success)
                {
                    YouTubeClients.WebRemix = YouTubeClients.WebRemix with { Version = m.Value };
                    var overridePath = Path.Combine(dataDir, "clients_override.json");
                    var obj = new JsonObject { ["webRemixVersion"] = m.Value, ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O") };
                    await File.WriteAllTextAsync(overridePath, obj.ToJsonString());
                    return $"Stream clients refreshed (WEB_REMIX {m.Value} + VISIONOS {YouTubeClients.VisionOs.Version}) - Echo Music v{CurrentAppVersion} is up to date!";
                }
            }
        }
        catch { }
        return $"Visitor token & stream clients refreshed - Echo Music v{CurrentAppVersion} is active!";
    }
}
