# Echo Music PC v2.0.2

Echo Music PC 2.0.2 focuses on playback reliability, lower Resource Saver overhead, History behavior and UI polish. The v2.0.1 fixes remain in place.

## Playback and streaming

- Reworked seek scheduling so decoder seeks are handled by the playback worker instead of doing potentially blocking decoder work in the UI-side seek call. Newer seek requests can supersede stale work, and stale decoded audio is discarded.
- Improved WebM/Opus seek accuracy by skipping decoded samples that fall before the requested timestamp, reducing the chance of hearing audio from before the seek position.
- Expanded the stream-resolver fallback sequence with a TVHTML5 client and propagated the appropriate Origin/Referer headers for range-based media requests. This may improve stream compatibility, but stream availability can still depend on YouTube and network conditions.
- Added Windows audio-endpoint change notifications so device additions, removals and default-output changes can be detected by the app.

## Library and History

- Added playlist and album entries to listening History, with collection entries deduplicated when the same collection is played again.
- Updated the History page to move or refresh the affected track row in place instead of rebuilding the entire track section for every playback-history update.
- Preserved local-file paths in History-derived track items and improved collection context for playlist/album playback.

## UI and controls

- Removed repeated Music Flow lyric/card opacity pulses and album-art fade transitions that could appear as flickering or flashing.
- Added scrolling marquee behavior for overflowing single-line titles instead of always clipping long names.
- Refined mouse-wheel scrolling state so new wheel input can retarget ongoing motion, and rounded the scrollbar thumb styling.
- Improved the search-suggestions visual test path and popup presentation.
- Added four seek-slider styles: Bar, Wavy, Slim and Squiggly. The player and fullscreen player use the selected style.
- Added Low / Auto / High performance modes with different animation-update targets. Actual visible smoothness depends on the display, hardware and current workload.

## Resource Saver

When Resource Saver is active, Echo Music now:

- Decodes newly loaded artwork at up to 384 pixels instead of the normal 640-pixel target.
- Uses a bounded least-recently-used image cache with a 12-image capacity.
- Limits simultaneous artwork fetches to two.
- Reduces active stream-page buffering from roughly 18 MiB to roughly 3 MiB per track and disables light next-track prewarming.
- Reduces nonessential visual activity, including title marquees and artwork shimmer cadence.

These measures reduce controllable cache, network and animation costs; they do **not** guarantee a particular RAM figure or eliminate every possible playback stall.

## Packages

- **Windows 10/11 x64:** `EchoMusic-Setup-v2.0.2-x64.exe`
- **Linux x86-64:** `EchoMusic-Linux-v2.0.2-x64.tar.gz`
- **Source:** `EchoMusic-v2.0.2-source.zip`

The Windows installer is unsigned unless the release explicitly states otherwise. Verify the source and any published SHA-256 before running it. Do not disable security software to install the app.
