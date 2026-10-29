# Echo Music PC v2.0.2

This release is limited to the requested flicker and Resource Saver fixes, plus the version update. Earlier playback, lyrics, sidebar, output-device and presentation work is retained. Guest playback measures are unchanged in this release; the remaining Android-specific measures are deferred.

## Changes

- Removed repeated Music Flow lyric-line/card opacity pulses and album-art fade-in transitions that could look like cards flashing.
- History updates now add or move only the changed song row while the History page is open, rather than rebuilding the full section every playback-history update. Temporary row controls are detached before reparenting to keep Avalonia’s single-parent rule.
- The Now Playing dock no longer rebuilds its full card stack when artwork finishes loading; the track-information card is appended in place after it becomes available.
- Resource Saver reduces newly decoded artwork to 384 pixels, trims the in-memory artwork cache to 12 images, limits concurrent saver-mode artwork fetches to two, reduces image response limits, and caps stream-page buffering to about 3 MiB per active track instead of about 18 MiB. It also prevents light next-track prewarming while active.
- Version metadata and package names are updated to 2.0.2.

These changes reduce several controllable cache and animation costs; actual Windows working-set memory depends on the display backend, page contents, audio queue and runtime, so a particular RAM figure is not guaranteed.

## Packages

- Windows 10/11 x64: `EchoMusic-Setup-v2.0.2-x64.exe`
- Linux x86-64: `EchoMusic-Linux-v2.0.2-x64.tar.gz`
- Source: `EchoMusic-v2.0.2-source.zip`
