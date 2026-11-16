# Echo Music PC v2.0.5

**Build date:** 26 October 2026

## Test-build changes

- Suggested for You stays stable across Home rerenders, mood/section changes, and navigation; explicit Home Refresh rotates it. Its own Play button and artwork play the displayed recommendation. Home load/refresh never autoplay. Video-type tracks are excluded from the idle sidebar suggestion.
- Settings has “When Play is pressed while idle”: “Do nothing” (default) or “Play last song before closing.” This applies to explicit transport Play clicks only and does not autoplay at app launch.
- New default-on “Skip a track after two playback errors” setting automatically advances after both codec attempts fail, rather than leaving playback stopped on that song.
- Radio queue refresh validates pending results and crossfade targets so a stale queued recommendation cannot take over after refresh.
- Codec resolution now strictly separates AAC/MP4 and WebM/Opus attempts; the old log showed both codec paths resolving to AAC/MP4. If a track fails at the selected quality or cuts off early, Echo retries it at Best for that song only. The global preference is unchanged; a success toast appears only after the retry works, and a failure shows one concise message with details in PlaybackDiagnostics.log.
- Faster per-track recovery: a genuine decode/quality failure gets one Best-quality retry without touching the global preference; later attempts for that song in the session skip the failed quality. Anonymous-playback blocks are identified separately so Echo does not waste time repeating an identical codec race at Best when the server is denying access. The current diagnostics show this server-side block, which software cannot override.
- Five-second crossfade remains the default transition; it can be disabled in Settings.

## Packages

- `EchoMusic-Setup-v2.0.5-x64.exe` — Windows x64 installer; self-contained app payload. WebView2 Runtime remains required for embedded sign-in. Unsigned; SmartScreen may warn.
- `EchoMusic-Linux-v2.0.5-x64.tar.gz` — self-contained Linux x64 package, about 43 MB compressed; includes install and uninstall scripts.
