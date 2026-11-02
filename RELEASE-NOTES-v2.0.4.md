# Echo Music PC v2.0.4

## Highlights

- Virtualizes playlist rows and Home shelf cards near the visible viewport while retaining full collections, ordering, actions, and playback access.
- Resource Saver reduces decorative motion, refresh cadence, artwork concurrency/decoding, and prefetch work; it also applies when the app is minimized or unfocused.
- Uses standard-resolution YouTube artwork for ordinary cards and requests max-resolution art only for large Now Playing artwork, with lower-resolution fallbacks.
- Improves playlist editor spacing, roundness controls, and artwork placeholders while covers load.
- Enforces declared track-duration bounds, clamps seeks/positions, and retains codec-readiness and premature-EOF recovery protections.
- The Windows setup detects an existing per-user Echo Music installation, preselects its path, and labels the action **Update Echo Music**; otherwise it says **Install Echo Music**.

## Validation notes

- Linux Release build completed with 0 warnings and 0 errors.
- Local self-tests passed through the playback-duration and codec-readiness checks. The full live-stream self-test could not finish because YouTube required anonymous-client bot verification.
- Windows builds can be produced in this environment, but Windows UI/audio runtime behavior has not been exercised here.
