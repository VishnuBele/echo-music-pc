<div align="center">
  <img src="https://raw.githubusercontent.com/VishnuBele/echo-music-pc/main/Assets/echo-logo.png" alt="Echo Music logo" width="116">

  <h1>Echo Music PC</h1>
  <p><strong>Your music. Your desktop. Your controls.</strong></p>
  <p>A feature-rich native music player for Windows 10/11 and 64-bit Linux.</p>

  [![Latest release](https://img.shields.io/github/v/release/VishnuBele/echo-music-pc?style=for-the-badge&label=latest%20release)](https://github.com/VishnuBele/echo-music-pc/releases)
  [![License](https://img.shields.io/badge/license-GPL--3.0-2ea44f?style=for-the-badge)](LICENSE.txt)
  [![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-6c63ff?style=for-the-badge)](#platform-support)
  [![Built with Avalonia](https://img.shields.io/badge/UI-Avalonia-8b5cf6?style=for-the-badge)](https://avaloniaui.net/)

  **[Download Echo Music](https://github.com/VishnuBele/echo-music-pc/releases) · [What's new](RELEASE-NOTES-v2.0.3.md) · [Linux guide](README-LINUX.md) · [License](LICENSE.txt)**

  <sub>Created by <strong>V!SHNU</strong> · Open source · GPL-3.0</sub>
</div>

---

## One player. A lot more room to listen.

Echo Music PC brings music discovery, streaming, synced lyrics, local listening and desktop controls together in one native app. Open it in **Guest Mode** and start listening—an account is not required for the core experience.

It is built for the desktop: a proper library, a queue, keyboard shortcuts, a mini-player, a full-screen player and audio controls that go beyond play and pause.

## Explore what you can do

| | | |
|:---:|---|---|
| 🔎 | **Discover music** | Search, suggestions, recommendations, charts, artists, albums, playlists, related tracks, mood/genre browsing and Music Flow. |
| 🎤 | **Follow the lyrics** | Synced lyrics, timing adjustments, translation/romanization options and a dedicated full-screen lyrics experience. Video uploads can use captions tied to the exact YouTube video instead of blindly borrowing lyrics from a shorter song version. |
| 🎧 | **Shape the sound** | 10-band equalizer and presets, playback speed with pitch-lock processing, volume normalization, crossfade and additional audio effects. |
| 📚 | **Keep your music close** | Local music, liked tracks, playlists, listening History, album and playlist History, offline downloads and Echo Wrapped. |
| 🔁 | **Bring playlists with you** | Import public Spotify playlists using a playlist URL or a local playlist file. No Spotify password is requested. |
| 🎨 | **Make it yours** | Themes, content filters, typography options, four seek-bar styles, Low / Auto / High performance modes and a customizable desktop layout. |
| 🖥️ | **Connect your desktop** | Keyboard shortcuts, compact mini-player, Command Palette, optional Discord Rich Presence and Windows media-session controls. |

### Playback and library

- Play streamed tracks or music stored on your computer.
- Manage the queue, liked songs and personal playlists.
- Save supported tracks for offline playback.
- Browse listening History, including album and playlist sessions.
- Use shuffle, repeat, seeking, playback-speed and volume controls.

### Lyrics that respect the recording

Echo Music can retrieve timed lyrics from multiple sources. For music videos and longer uploads, v2.0.3 adds an exact-video path: it checks captions associated with that video's ID, preserves caption timing, and prefers creator-provided captions when available. It avoids treating a title-only match for a shorter audio edit as though it were automatically synchronized to the longer video.

Lyrics availability still depends on the source exposing usable lyrics or captions. Some videos may have no matching captions; in that case, an unavailable result is better than confidently displaying lyrics for a different recording.

### Audio controls

- 10-band EQ with presets.
- Playback-speed control with pitch-preserving processing.
- Volume normalization and crossfade.
- Additional DSP effects and track-to-track DSP-state cleanup.

## Performance, without the mystery settings

Echo Music includes **Low**, **Auto** and **High** animation modes. These select internal animation update targets (approximately 30, 60 and up to 120 updates per second); they do not change your monitor's refresh rate or promise a particular FPS. Actual smoothness depends on the display, GPU, operating system, desktop compositor and current workload.

**Resource Saver** reduces controllable background costs, including artwork decode size, in-memory artwork-cache capacity, simultaneous artwork requests, stream buffering and nonessential visual activity. In **Auto** mode, reduced visual work can activate while Echo Music is not the foreground app. The goal is to reduce unnecessary work—not to promise a fixed RAM figure.

## Download and install

Get the available builds from the **[GitHub Releases page](https://github.com/VishnuBele/echo-music-pc/releases)**. Choose the package for your system and architecture.

> **Release status:** the repository's current `main` source is version **2.0.3**. At the time this README was updated, the latest published GitHub release and downloadable release assets were still **v2.0.2**. The 2.0.3 source changes are documented here, but do not assume a 2.0.3 installer is available until it appears on the Releases page.

### Windows 10/11 · x64

1. Open the [Releases page](https://github.com/VishnuBele/echo-music-pc/releases).
2. Download the Windows x64 **Setup `.exe`** from the latest published release.
3. Run the installer and follow the prompts. Installation is per-user; the packaged Windows build includes its .NET runtime, so a separate .NET installation is not normally needed.

The installer is currently **unsigned**. Windows SmartScreen or antivirus software may show a reputation warning. Verify that the installer came from this repository and compare its SHA-256 checksum when one is published. **Do not disable antivirus protection** to install the app.

### Linux · x86-64

1. Download the Linux x86-64 `.tar.gz` package from the [Releases page](https://github.com/VishnuBele/echo-music-pc/releases).
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

- **Guest Mode is available:** you do not need to sign in to use the core player.
- **Spotify playlist import:** uses a public playlist URL or a local playlist file; Echo Music does not ask for your Spotify password.
- **Optional Windows sign-in:** the YouTube Music web integration is cookie-based; it is not an official Google OAuth integration and does not provide full account-library sync, listening history, Recaps or a personalized Home feed.
- **Local data:** settings, playlists, cached artwork, downloads and other app data are stored in your user profile.
- **Session protection:** optional Windows sign-in data is protected for the Windows user at rest. This does not make an active session immune to malware or session-hijacking risks.

## Credits and license

Echo Music PC is an independent desktop project created by **V!SHNU**, released under the **GNU General Public License v3.0**. See [LICENSE.txt](LICENSE.txt) for the full license and attribution.

This PC edition is separate from, and is not officially affiliated with or endorsed by, the original [Echo Music Android project](https://github.com/EchoMusicApp/Echo-Music). Refer to that project's repository for its own authorship and licensing information.

---

<div align="center">
  <strong>Made by V!SHNU</strong><br>
  <sub>Listen your way. Built for the desktop. · GPL-3.0</sub>
</div>
