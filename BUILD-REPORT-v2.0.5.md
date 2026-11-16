# Echo Music PC v2.0.5 — setup and Linux package

**Build date:** 26 October 2026

## Current test artifact

- `EchoMusic-Setup-v2.0.5-x64.exe` — Windows x64 custom setup installer with a self-contained Echo Music payload. It installs/uninstalls the app and includes the bundled .NET runtime. WebView2 Runtime remains required for embedded sign-in features. Unsigned; SmartScreen may warn.
- `EchoMusic-Linux-v2.0.5-x64.tar.gz` — self-contained Linux x64 bundle (about 43 MB compressed), with install/uninstall scripts; no .NET runtime installation needed.

## Changes in this test build

- The idle Suggested for You item remains stable across ordinary Home reloads, mood changes, and navigation, rotating only on explicit Home Refresh. Its own explicit Play button (and artwork) plays the exact suggestion shown. Global transport Play controls (player bar, fullscreen, mini-player) use the Settings choice: default Do nothing, or play the last track recorded in history. Nothing autoplays on app launch. Video-type tracks are excluded from the idle suggestion; the existing Hide Video Songs setting still controls main Home shelves.
- New default-on setting: after two playback errors from the codec attempts, automatically skip the failed song and load the next queued track instead of stopping on it. If a single-song pick is waiting for its radio tail, it skips when the next recommendation arrives.
- Radio queue refresh rejects stale recommendations and stale crossfade preparations so an old queued target cannot override a refreshed queue.
- The previous playback log showed the supposed WebM/Opus attempt resolving to the same AAC/MP4 stream as the AAC attempt. Codec requests now require their requested family across player profiles, so a nominal Opus attempt cannot silently be AAC. If a song fails at the configured quality, Echo retries at Best for that song only. The global setting is unchanged; the switch toast appears only after the Best deck is usable. If both attempts fail, the user sees one short message while detailed reasons go to PlaybackDiagnostics.log. Early-cutoff retry is likewise pinned to Best for that song.
- Windows decoder warm-up is capped at three seconds. A real quality/decode failure retries that track at Best only (global setting unchanged), and the track stays pinned to Best for this app session to skip repeating the slow failing quality on the next play. The latest diagnostics confirm “kiss me again (Ambient)” still cannot play: its AAC/MP4 stream produces 0 PCM samples even at Best, while YouTube blocks the Opus request for anonymous playback. Other tracks in the same log successfully play Opus, so this points to a per-track/provider access/stream issue, not proof of a general playback fix. Echo now avoids a redundant Best race after the explicit access-block response and shows that reason rather than a misleading quality error; it cannot override YouTube's block.
- Five-second crossfade default and settings migration remain in place.

## Validation and limitations

- Built the Windows custom setup installer through the repository NSIS build script and built the self-contained Linux x64 tar.gz package; both completed successfully. Windows setup was not launched in this Linux runner, so Windows installation and playback are not runtime-verified here.
- Source-level review confirms the suggestion's explicit Play action plays its captured track and idle transport behavior follows the new setting. Automatic skip is enabled by default after two codec-candidate errors when a next queued track exists. The live online playback path has previously hit YouTube anonymous access blocks; no end-to-end success is claimed for the blocked song.
- No GitHub release was created or published.
