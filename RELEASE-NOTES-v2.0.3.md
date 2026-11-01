# Echo Music PC v2.0.3

This maintenance release focuses on cross-device scrolling, desktop packaging, memory/per-frame work, and update-check resilience while preserving the existing playback and lyric-provider behavior.

## Changes
- Replaced explicit-Euler scroll easing with an exact critically damped step, so irregular frame timing is less likely to produce oscillation.
- Cancel pending wheel easing as soon as direct pointer interaction starts inside a scroll viewer. This prevents a stale wheel target from fighting scrollbar-thumb and touch dragging.
- Defer off-screen artwork requests until cards approach the viewport; reduce decoded artwork dimensions and the retained in-memory artwork-cache limit to lower thumbnail and network pressure.
- On Linux, use the window manager's native caption controls instead of drawing a second in-app minimize/maximize/close set.
- Install the Linux launcher icon into the standard per-user hicolor icon theme directory and include its PNG in the bundle.
- Make update checks fall back to GitHub's public Releases page when the anonymous REST API is unavailable or rate-limited. The app still never downloads or installs updates automatically.

## Scope notes
- This build does not alter lyric-provider selection, transcript fallback, or counterpart timing-map behavior.
- The installer is unsigned because no code-signing certificate is configured. Windows SmartScreen or antivirus reputation warnings may occur.
- A startup update prompt appears only when GitHub has a published release with a version newer than the installed build. No release was published by preparing this local build.


---

## Small UI and background-resume follow-up

## Changes
- Playlist ambient header color now blends smoothly and continuously into the root background, avoiding a dark band through the gradient.
- Current-song play badges on Home quick picks and Search top-result cards swap the play triangle for the existing live spectrum style while that exact track is playing. Track-list artwork spectrum indicators remain unchanged.
- Resource Saver hover text now states both the selected mode and whether it is currently active, with shorter interaction hints.
- Windows activity detection treats stale/hidden foreground handles and desktop-shell transitions as no competing app. This should let visible Echo animations resume automatically when another app closes, while still reducing work when an actual external app owns the foreground.

## Linux
- Windows-specific foreground detection remains guarded by the platform check; Linux visibility behavior is unchanged, and the shared card/gradient changes compile for Linux.
- To keep the deliverables under the workspace size limit, the provided Linux archive is the compact framework-dependent x64 build and requires .NET 8 (`Microsoft.NETCore.App` 8.0). The install script checks this before installing. `SELF_CONTAINED=true` still builds a larger self-contained package.

## Validation notes
- Build and subsystem self-tests are run before packaging. Actual Windows behavior still needs the user's Windows check, especially after closing Discord; no Windows desktop was available here.
- The installer is unsigned and may trigger SmartScreen or antivirus reputation warnings.
