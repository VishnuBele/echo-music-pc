# Echo Music PC v2.0.2

This update focuses on playback reliability, a lighter Resource Saver mode, improved listening History and UI refinements.

## What's changed

- **More robust seeking:** decoder seeks are scheduled on the playback worker, stale decoded audio is discarded, and WebM/Opus decoding skips samples before the requested seek point.
- **Stream compatibility work:** added a TVHTML5 resolver fallback and appropriate media Origin/Referer headers. Playback can still depend on upstream availability and network conditions.
- **Windows audio devices:** detect audio endpoint additions, removals and default-output changes.
- **Better History:** record playlists and albums, deduplicate repeated collection entries, and update changed track rows without rebuilding the entire list every time.
- **Less visual flicker:** removed repeated Music Flow opacity pulses and album-art fades that could look like flashing.
- **Long titles and scrolling:** overflowing titles can scroll instead of always truncating; wheel-scroll motion can be retargeted by new input, and scrollbar thumbs are rounded.
- **Custom player controls:** four seek-slider styles—Bar, Wavy, Slim and Squiggly—and Low / Auto / High animation modes.
- **Resource Saver:** smaller artwork decode targets, a 12-image in-memory cache, at most two concurrent saver-mode artwork fetches, lower stream buffering and no light next-track prewarming.

Resource Saver reduces several controllable memory and workload costs, but actual RAM use and smoothness depend on the system and current content. This release does not guarantee that every streaming stall is eliminated.

## Downloads

- **Windows 10/11 (x64):** `EchoMusic-Setup-v2.0.2-x64.exe`
- **Linux (x86-64):** `EchoMusic-Linux-v2.0.2-x64.tar.gz`
- **Source:** `EchoMusic-v2.0.2-source.zip`

The Windows installer is unsigned unless stated otherwise. Verify downloads from this repository and use the published SHA-256, when available. Do not disable antivirus protection.
