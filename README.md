<div align="center">
  <img src="https://raw.githubusercontent.com/VishnuBele/echo-music-pc/main/Assets/echo-logo.png" alt="Echo Music logo" width="124">

  <h1>Echo Music PC</h1>
  <p><strong>Your music. Your desktop. Your controls.</strong></p>
  <p>A feature-rich native music player for Windows 10/11 and 64-bit Linux.</p>

  [![Latest release](https://img.shields.io/github/v/release/VishnuBele/echo-music-pc?style=for-the-badge&label=latest%20release)](https://github.com/VishnuBele/echo-music-pc/releases)
  [![License](https://img.shields.io/badge/license-GPL--3.0-2ea44f?style=for-the-badge)](LICENSE.txt)
  [![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-6c63ff?style=for-the-badge)](#platform-support)
  [![Built with Avalonia](https://img.shields.io/badge/UI-Avalonia-8b5cf6?style=for-the-badge)](https://avaloniaui.net/)

  **[Download Echo Music](https://github.com/VishnuBele/echo-music-pc/releases) · [What's new](RELEASE-NOTES-v2.0.5.md) · [Linux guide](README-LINUX.md) · [License](LICENSE.txt)**

  <sub>Created and maintained by <strong>V!SHNU</strong> · Open source · GPL-3.0</sub>
</div>

---

## One player. A lot more room to listen.

Echo Music PC brings music discovery, streaming, synced lyrics, local listening and desktop controls together in one native application. Start in **Guest Mode**—an account is not required for the core experience.

Built for the desktop: a proper library, a queue, keyboard shortcuts, a mini-player, a full-screen player and audio controls that go well beyond play and pause.


## Explore what you can do

| | | |
|:---:|---|---|
| 🔎 | **Discover music** | Search, suggestions, recommendations, charts, artists, albums, playlists, related tracks, moods, genres, Music Flow and Music DNA. |
| 🎤 | **Follow the lyrics** | Synced lyrics, timing adjustments, translation/romanization options and a dedicated full-screen lyrics experience. Video uploads can use captions tied to the exact YouTube video instead of blindly borrowing lyrics from a shorter version. |
| 🎧 | **Shape the sound** | 10-band equalizer and presets, playback speed with pitch-lock processing, volume normalization, crossfade and additional audio effects. |
| 📚 | **Keep your music close** | Local music, liked tracks, playlists, listening History, album and playlist History, offline downloads and Echo Wrapped. |
| 🔁 | **Bring playlists with you** | Import supported public Spotify and YouTube Music links, plus local `.CSV`, `.M3U` and `.M3U8` files. No Spotify password is requested. |
| 🧠 | **Let Echo Brain continue** | Smart radio continuation, Music DNA, recommendation shelves and queue refresh that keeps manually queued tracks intact. |
| 🎨 | **Make it yours** | Themes, content filters, typography options, four seek-bar styles, Low / Auto / High performance modes and Resource Saver. |
| 🖥️ | **Connect your desktop** | Keyboard shortcuts, compact mini-player, Command Palette, optional Discord Rich Presence and Windows media-session controls. |

## What's new in v2.0.5

### Home that stays predictable

- **Suggested for You stays put.** The selected idle recommendation remains stable across Home redraws, mood changes, section changes and navigation. It does not randomly change just because the page was rebuilt.
- **Refresh means refresh.** The new Home Refresh action fetches the Home view again, rotates the selected suggestion, and reshuffles the featured Spotlight and quick-access picks—even when the service returns familiar items.
- **The Play action plays what is shown.** The recommendation card captures its displayed track; clicking its artwork or Play button starts that exact item rather than a different recommendation selected during a later redraw.
- **No surprise startup playback.** Home loading and refreshing do not start the Suggested for You track. Video-type tracks are excluded from the idle suggestion; the existing Hide Video Songs setting continues to govern the main discovery shelves.

### More control when Play is pressed while idle

A new setting controls an explicit Play click when nothing is currently loaded:

- **Do nothing** — the default.
- **Play last song before closing** — restores the most recent playable track from listening History.

This applies to the player transport controls, including the main player, mini-player and full-screen player. It does not start music automatically when Echo Music launches.

### Smarter Echo Brain queue refresh

- Refreshing Home can refresh the automatically generated radio tail for the currently playing track.
- The current song and manually queued tracks are preserved; only the generated recommendation tail is replaced.
- Older background requests are invalidated when the queue changes, preventing stale recommendations from being appended after a newer selection or refresh.
- Recent radio results and listening History are used to filter repeat recommendations where possible.
- Crossfade preparation waits for an active generated-queue refresh, reducing the chance of an old next-track target winning a race against the refreshed queue.

### Playback recovery that knows the difference between a codec failure and an access block

- AAC/MP4 and WebM/Opus stream attempts now require the requested codec family. An Opus attempt can no longer quietly resolve to AAC and make both codec paths behave as if they were independent.
- Windows checks that a decoder produces usable audio before accepting a candidate. The warm-up window is capped at three seconds and looks for decoded PCM, not merely a successfully resolved URL.
- If a track fails at the selected quality for a genuine quality/decoder reason, Echo can retry that track at **Best**. The global quality preference is left unchanged, and a successful fallback is limited to that track (and remembered for the app session to avoid repeating the same slow failure).
- An upstream anonymous-playback block is reported as an access restriction. Repeating the same request at Best cannot remove a server-side block, so Echo avoids presenting it as an ordinary quality problem.
- New default-on **Skip a track after two playback errors** setting: when both codec attempts fail and another queued item exists, playback advances instead of remaining stuck on the failed track. For a single-track selection with Echo Brain continuation enabled, the failed item can wait for its radio tail and then move to the next recommendation.
- Detailed recovery information is written to `PlaybackDiagnostics.log` in the application data directory.

### Crossfade default

The default track-to-track crossfade is now **5 seconds**. Existing settings are migrated once where necessary; the value remains adjustable in Settings and can be set to Off.

### Update notices

Update notices now use the release publication timestamp as the displayed release date and render the release notes with Markdown-style headings, lists, emphasis and links.

See **[RELEASE-NOTES-v2.0.5.md](RELEASE-NOTES-v2.0.5.md)** for implementation details, packaging information and validation limits.

## Playback and library

- Stream music or play files stored on the computer.
- Manage the queue, liked songs and personal playlists.
- Save supported tracks for offline playback.
- Browse listening History, including album and playlist sessions.
- Use shuffle, repeat, seeking, playback-speed and volume controls.
- Import public Spotify playlists, albums, tracks and profiles (public playlist browsing); import public YouTube Music playlists and individual YouTube/YouTube Music video links.

### Lyrics that respect the recording

Echo Music can retrieve timed lyrics from multiple sources. For music videos and longer uploads, the exact-video caption path checks captions associated with that video's ID, preserves caption timing and prefers creator-provided captions when usable. It avoids treating a title-only match for a shorter audio edit as though it were automatically synchronized to the longer video.

Lyrics availability still depends on the source exposing usable lyrics or captions. Some videos have no matching captions; in that case, an unavailable result is better than confidently displaying lyrics for a different recording.

### Audio controls

- 10-band EQ with presets.
- Playback-speed control with pitch-preserving processing.
- Volume normalization and crossfade.
- Additional DSP effects and track-to-track DSP-state cleanup.
- Audio output and playback behaviour depend on the operating system and available devices.

## Performance, without the mystery settings

Echo Music includes **Low**, **Auto** and **High** animation modes. These select internal animation update targets of approximately 30, 60 and up to 120 updates per second; they do not change the monitor's refresh rate or promise a particular FPS. Actual smoothness depends on the display, GPU, operating system, desktop compositor and current workload.

**Resource Saver** reduces controllable background costs, including artwork decode size, in-memory artwork-cache capacity, simultaneous artwork requests, stream buffering and nonessential visual activity. In **Auto** mode, reduced visual work can activate while Echo Music is not the foreground app. Audio maintenance continues; visual work is reduced. The goal is to avoid unnecessary work—not to promise a fixed RAM figure.

## Download and install

Get published builds from the **[GitHub Releases page](https://github.com/VishnuBele/echo-music-pc/releases)**. The latest published tag currently remains **v2.0.4**. v2.0.5 package builds exist on the current source branch, but the v2.0.5 GitHub Release and downloadable assets have not yet been published.

### Windows 10/11 · x64

1. Open the [Releases page](https://github.com/VishnuBele/echo-music-pc/releases).
2. Download the Windows x64 Setup `.exe` from the latest published release.
3. Run the installer and follow the prompts.

The v2.0.5 Windows package is a branded, self-contained setup installer. The Echo Music payload includes the .NET runtime; **Microsoft Edge WebView2 Runtime** remains required for embedded web content/sign-in features. Setup installs per-user, detects an existing installation, can reuse its location for an update, and registers an uninstaller and Start Menu/Desktop shortcuts.

The installer is **unsigned** unless a release explicitly says otherwise. Windows SmartScreen or antivirus software may show a reputation warning. Confirm that the file came from this repository's Releases page and verify any published SHA-256 checksum. **Do not disable antivirus protection** to install the app.

### Linux · x86-64

The v2.0.5 Linux package is a **self-contained** x86-64 archive (about 43 MB compressed); it includes the .NET runtime, so a separate .NET 8 installation is not required.

1. Download `EchoMusic-Linux-v2.0.5-x64.tar.gz` when it appears in the [Releases page](https://github.com/VishnuBele/echo-music-pc/releases).
2. Extract the archive.
3. Open a terminal in the extracted folder and run:

   ```sh
   chmod +x install.sh
   ./install.sh
   ```

The installer adds a per-user application-menu entry and icon under your home directory. To uninstall:

```sh
~/.local/opt/echo-music/uninstall.sh
```

Linux still needs the normal system graphics and audio libraries. Audio uses ALSA's default output, which is commonly routed through PulseAudio or PipeWire by the desktop. This package targets **x86-64**, not ARM64. See [README-LINUX.md](README-LINUX.md) for Linux-specific details.

## Build from source

The project targets **.NET 8** and uses **C# / Avalonia** for its native desktop UI.

```bash
# Windows x64 custom setup installer
bash installer/build-windows-setup.sh

# Self-contained Linux x64 archive
SELF_CONTAINED=true bash installer/build-linux-package.sh
```

The Windows packaging script uses NSIS (`makensis`) and Python 3 to assemble the payload manifest. Code signing can be supplied through the build script's signing environment variables; without a signing certificate, the Windows installer is unsigned.

## Keyboard shortcuts

Single-letter shortcuts work when a text field is not focused.

| Keys | Action |
|---|---|
| `Space` | Play or pause |
| `←` / `→` | Seek 5 seconds backward or forward |
| `Ctrl` + `←` / `→` | Previous or next track |
| `F` or `F11` | Toggle the fullscreen player |
| `M` | Mute or unmute |
| `S` | Toggle shuffle |
| `R` | Cycle repeat mode |
| `L` | Like or unlike the current song |
| `Ctrl` + `F` | Focus search |
| `Ctrl` + `K` | Open the Command Palette |
| `Ctrl` + `Q` | Toggle the queue / lyrics panel |
| `Ctrl` + `,` | Open Settings |
| `Ctrl` + `M` | Toggle the mini-player |
| `Alt` + `←` / `→` | Go back or forward in navigation history |
| `Esc` | Close the topmost dialog or fullscreen player; clear search when applicable |

## Platform support

| Feature | Windows 10/11 x64 | Linux x86-64 |
|---|:---:|:---:|
| Core player, search and library | ✓ | ✓ |
| Guest Mode | ✓ | ✓ |
| Synced lyrics and exact-video caption lookup | ✓ | ✓ |
| Local music, playlists and offline downloads | ✓ | ✓ |
| Equalizer and playback DSP | ✓ | ✓ |
| Music Flow and Echo Brain | ✓ | ✓ |
| Discord Rich Presence | ✓ | ✓ |
| Windows media-session controls | ✓ | — |
| Optional Google sign-in integration | ✓ | — |

Linux remains in Guest Mode. Windows-specific integrations, including Windows media-session controls and the optional Google sign-in flow, are not included in the Linux edition. Linux audio-device behaviour can vary with the distribution and its ALSA/PulseAudio/PipeWire configuration.

## Accounts, privacy and local data

- **Guest Mode:** the core player does not require sign-in.
- **Spotify import:** public metadata is used; Echo Music does not request a Spotify password or Spotify OAuth credentials.
- **YouTube Music import:** accepts supported public playlist and individual video links. Private playlists, channel/profile browsing and auto-mix/radio links are not supported by this importer.
- **Optional Windows sign-in:** the YouTube Music web integration is cookie-based; it is not an official Google OAuth integration and does not provide full account-library sync, listening history, Recaps or a personalized Home feed.
- **Local data:** settings, playlists, cached artwork, downloads and other app data are stored in your user profile.
- **Session protection:** optional Windows sign-in data is protected for the Windows user at rest. This does not make an active session immune to malware or session-hijacking risks.

## Credits and license

Echo Music PC is maintained by **V!SHNU** and released under the **GNU General Public License v3.0**. The PC edition retains attribution to the original Echo Music project and its contributors. See [LICENSE.txt](LICENSE.txt) and the About section in the app for attribution and license details.

---

<div align="center">
  <img src="https://raw.githubusercontent.com/VishnuBele/echo-music-pc/main/Assets/echo-logo.png" alt="Echo Music" width="52">
  <br>
  <strong>Made by V!SHNU</strong>
  <br>
  <sub>Listen your way. Built for the desktop. · GPL-3.0</sub>
</div>
