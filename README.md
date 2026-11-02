<div align="center">
  <img src="https://raw.githubusercontent.com/VishnuBele/echo-music-pc/main/Assets/echo-logo.png" alt="Echo Music logo" width="116">

  <h1>Echo Music PC</h1>
  <p><strong>Your music. Your desktop. Your controls.</strong></p>
  <p>A feature-rich native music player for Windows 10/11 and 64-bit Linux.</p>

  [![Latest release](https://img.shields.io/github/v/release/VishnuBele/echo-music-pc?style=for-the-badge&label=latest%20release)](https://github.com/VishnuBele/echo-music-pc/releases)
  [![License](https://img.shields.io/badge/license-GPL--3.0-2ea44f?style=for-the-badge)](LICENSE.txt)
  [![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-6c63ff?style=for-the-badge)](#platform-support)
  [![Built with Avalonia](https://img.shields.io/badge/UI-Avalonia-8b5cf6?style=for-the-badge)](https://avaloniaui.net/)

  **[Download Echo Music](https://github.com/VishnuBele/echo-music-pc/releases) · [What's new in v2.0.4](RELEASE-NOTES-v2.0.4.md) · [Linux guide](README-LINUX.md) · [License](LICENSE.txt)**

  <sub>Created and maintained by <strong>V!SHNU</strong> · Open source · GPL-3.0</sub>
</div>

---

## One player. A lot more room to listen.

Echo Music PC brings music discovery, streaming, synced lyrics, local listening and desktop controls together in one native app. Open it in **Guest Mode** and start listening—an account is not required for the core experience.

Built for the desktop: a proper library, queue, keyboard shortcuts, mini-player, full-screen player and audio controls that go beyond play and pause.

## Explore what you can do

| | | |
|:---:|---|---|
| 🔎 | **Discover music** | Search, suggestions, recommendations, charts, artists, albums, playlists, related tracks, mood/genre browsing and Music Flow. |
| 🎤 | **Follow the lyrics** | Synced lyrics, timing adjustments, translation/romanization options and a dedicated full-screen lyrics experience. Video uploads can use captions tied to the exact YouTube video instead of borrowing lyrics from a different recording. |
| 🎧 | **Shape the sound** | 10-band equalizer and presets, playback speed with pitch-lock processing, volume normalization, crossfade and additional audio effects. |
| 📚 | **Keep your music close** | Local music, liked tracks, playlists, listening History, album and playlist History, offline downloads and Echo Wrapped. |
| 🔁 | **Bring playlists with you** | Import public Spotify playlists, albums, tracks and public profiles, plus public YouTube Music playlists and individual song/video links. Local CSV/M3U/M3U8 files are supported too. |
| 🎨 | **Make it yours** | Themes, content filters, typography options, four seek-bar styles, Low / Auto / High performance modes and a customizable desktop layout. |
| 🖥️ | **Connect your desktop** | Keyboard shortcuts, compact mini-player, Command Palette, optional Discord Rich Presence and Windows media-session controls. |

### Playback and library

- Play streamed tracks or music stored on your computer.
- Manage the queue, liked songs and personal playlists.
- Save supported tracks for offline playback.
- Browse listening History, including album and playlist sessions.
- Use shuffle, repeat, seeking, playback-speed and volume controls.

### Lyrics that respect the recording

Echo Music can retrieve timed lyrics from multiple sources. For music videos and longer uploads, the exact-video lookup path checks captions associated with that video's ID and preserves their timing. When a matching timed source is unavailable, Echo Music avoids confidently presenting lyrics from an unrelated version of the track.

Lyrics availability still depends on what the source exposes. Some videos have no usable captions, and word-level timing is not guaranteed for every song.

### Audio controls

- 10-band EQ with presets.
- Playback-speed control with pitch-preserving processing.
- Volume normalization and crossfade.
- Additional DSP effects and track-to-track processing-state cleanup.

## What's new in v2.0.4

This release focuses on **large-library responsiveness, artwork efficiency and playback reliability**, with improvements to playlist importing and the Windows setup experience.

| Area | v2.0.4 improvements |
|---|---|
| **Large playlists** | Long playlist detail pages virtualize track rows around the visible area instead of keeping every row active at once. Track order, filtering and playback actions remain available. |
| **Artwork loading** | Artwork requests are deferred until cards approach the viewport. Ordinary cards use right-sized thumbnails; high-resolution artwork is reserved for the large Now Playing presentation. |
| **Resource Saver** | Reduces nonessential visual work, artwork decoding/cache usage, concurrent image work and stream prefetching. Audio playback and low-rate background maintenance continue. |
| **Playback recovery** | Distinguishes a real track ending from a stream or decoder failure. Premature cutoffs trigger a refreshed stream retry; persistent failures pause playback instead of silently advancing the queue. Seek and playback positions are bounded by known track duration. |
| **Playlist importing** | Adds public YouTube Music playlist and individual song/video URL importing. Spotify matching is more selective about title, artist, duration and version differences such as live or remix recordings. |
| **Windows setup** | Detects an existing per-user installation, preselects its install location and labels the action as an update when applicable. |

See the **[v2.0.4 release notes](RELEASE-NOTES-v2.0.4.md)** for implementation details and validation limitations.

## Performance, without the mystery settings

Echo Music includes **Low**, **Auto** and **High** animation modes. These select internal animation update targets (approximately 30, 60 and up to 120 updates per second); they do not change your monitor's refresh rate or promise a particular FPS. Actual smoothness depends on the display, GPU, operating system, desktop compositor and current workload.

**Resource Saver** reduces controllable background costs, including artwork decode size, in-memory artwork-cache capacity, simultaneous artwork requests, stream prefetching and nonessential visual activity. In **Auto** mode, reduced visual work can activate while Echo Music is not the foreground app. Audio playback and essential background maintenance continue. The goal is to reduce unnecessary work—not to promise a fixed RAM figure.

## Download and install

Get the available builds from the **[GitHub Releases page](https://github.com/VishnuBele/echo-music-pc/releases)**. Choose the package for your system and architecture.

### Windows 10/11 · x64

1. Download **`EchoMusic-Setup-v2.0.4-x64.exe`** from the v2.0.4 release assets.
2. Run the installer and follow the prompts. Installation is per-user; the packaged Windows build includes its .NET runtime, so a separate .NET installation is not normally needed.

The installer is **unsigned**. Windows SmartScreen or antivirus software may show a reputation warning. Verify that the installer came from this repository and compare its SHA-256 checksum when one is published. **Do not disable antivirus protection** to install the app.

### Linux · x86-64

1. Download **`EchoMusic-Linux-v2.0.4-x64.tar.gz`** from the v2.0.4 release assets.
2. Extract the archive.
3. Open a terminal in the extracted folder and run:

   ```sh
   chmod +x install.sh
   ./install.sh
   ```

The compact Linux package is framework-dependent and requires the **.NET 8 runtime** (`Microsoft.NETCore.App` 8.0), plus the usual graphics and audio libraries supplied by your distribution. The installer checks for .NET 8 before installing. It installs under your home directory and does not need administrator privileges.

To uninstall the per-user Linux installation:

```sh
~/.local/opt/echo-music/uninstall.sh
```

See [README-LINUX.md](README-LINUX.md) for Linux-specific details and the self-contained packaging option. This package targets **x86-64**, not ARM64.

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
| Synced lyrics and video-caption lookup | ✓ | ✓ |
| Local music and playlists | ✓ | ✓ |
| Equalizer and playback DSP | ✓ | ✓ |
| Discord Rich Presence | ✓ | ✓ |
| Windows media-session controls | ✓ | — |
| Optional Google sign-in integration | ✓ | — |

Linux stays in Guest Mode. Windows-specific integrations, including Windows media-session controls and the optional Google sign-in flow, are not included in the Linux edition. Linux audio-device behaviour can vary with the distribution and its ALSA/PulseAudio/PipeWire configuration.

## Accounts, privacy and local data

- **Guest Mode:** the core player does not require an account.
- **Spotify import:** public playlist, album, track and profile links are supported; profile links expose public playlists. No Spotify password or OAuth flow is requested.
- **YouTube Music import:** public playlist links and individual song/video links are supported. Channel/profile browsing, private playlists and auto-mix/radio links are not supported by the importer.
- **Windows sign-in:** optional Google sign-in is available for some YouTube Music web features through a cookie-based integration. It is not an official Google OAuth integration and does not provide full YouTube Music library sync, listening history, Recaps or a personalized Home feed.
- **Linux:** remains in Guest Mode; Windows-only account and media-session integrations are unavailable.
- **Local data:** settings, playlists, cached artwork, downloads and other app data are stored in your user profile. Optional Windows sign-in data is protected for the Windows user at rest, but an active session cannot be guaranteed safe from every malware or session-hijacking risk.

## Credits and license

Echo Music PC is created and maintained by **V!SHNU**, and released under the **GNU General Public License v3.0**. See [LICENSE.txt](LICENSE.txt) for the full license and attribution.

This PC edition is based on the Echo Music project lineage and remains separately maintained for desktop use. See the [original Echo Music Android project](https://github.com/EchoMusicApp/Echo-Music) for its own source and project information.

---

<div align="center">
  <img src="https://raw.githubusercontent.com/VishnuBele/echo-music-pc/main/Assets/echo-logo.png" alt="Echo Music" width="42">
  <br>
  <strong>Made by V!SHNU</strong>
  <br>
  <sub>Your music. Your desktop. Your controls. · GPL-3.0</sub>
</div>
