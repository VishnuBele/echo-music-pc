<div align="center">

# Echo Music PC

**Your music, on your desktop.**

A feature-rich native music player for Windows 10/11 and 64-bit Linux, built by **V!SHNU**.

[![Latest release](https://img.shields.io/github/v/release/VishnuBele/echo-music-pc?style=for-the-badge)](https://github.com/VishnuBele/echo-music-pc/releases)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue?style=for-the-badge)](LICENSE.txt)
[![Windows and Linux](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-6c63ff?style=for-the-badge)](https://github.com/VishnuBele/echo-music-pc)

[Downloads](https://github.com/VishnuBele/echo-music-pc/releases) · [Release notes](RELEASE-NOTES-v2.0.2.md) · [License](LICENSE.txt)

</div>

---

## The essentials

Echo Music brings music discovery, playback, lyrics, library management and desktop controls into one app. Start in **Guest Mode**—you do not need to sign in just to use the player.

- **Discover:** search, recommendations, charts, albums, artists, playlists and related music.
- **Listen:** synchronized lyrics, lyric timing controls, translation options, fullscreen player and mini-player.
- **Build your library:** local music, playlists, listening History, offline downloads and Echo Wrapped.
- **Tune your sound:** 10-band equalizer, presets, playback speed, volume normalization, crossfade and additional playback effects.
- **Make it yours:** themes, content filters, keyboard shortcuts, Command Palette and customizable seek-bar styles.
- **Connect your desktop:** optional Discord Rich Presence and Windows media-session integration.

## Features

| Area | What’s included |
| --- | --- |
| **Music discovery** | Search suggestions, recommendations, charts, artists, albums, playlists, related tracks and Music Flow |
| **Player** | Synced lyrics, lyric timing, translation, fullscreen player, compact mini-player, queue and playback controls |
| **Audio** | 10-band EQ and presets, playback-speed control, volume normalization, crossfade and additional DSP effects |
| **Your library** | Local files, playlists, liked tracks, playback History, album/playlist History, downloads and Echo Wrapped |
| **Playlist import** | Import public Spotify playlists from a URL or local playlist file—no Spotify password required |
| **Appearance** | Themes, content filters, rounded UI details, scrolling long titles and four seek-slider styles: Bar, Wavy, Slim and Squiggly |
| **Performance** | Low / Auto / High animation modes and Resource Saver controls for artwork, caching, buffering and background work |
| **Desktop integration** | Discord Rich Presence, keyboard shortcuts and Windows media-session controls |

### Performance and Resource Saver

The **Low**, **Auto** and **High** modes select different animation-update targets (30, 60 and up to 120 updates per second respectively). The visible result depends on your display, hardware and current workload; these settings do not change your monitor’s refresh rate or guarantee a particular frame rate.

**Resource Saver** reduces several controllable costs, including artwork resolution and cache size, simultaneous artwork fetches, stream-page buffering, next-track prewarming and nonessential animation. It is intended to reduce app workload, not to promise a fixed RAM usage.

## Download and install

Get the available packages from the [GitHub Releases page](https://github.com/VishnuBele/echo-music-pc/releases).

### Windows 10/11 (x64)

1. Download **EchoMusic-Setup-v2.0.2-x64.exe** from the 2.0.2 release assets when published.
2. Run the installer and follow the setup screens. Installation is per-user and the Windows package includes the .NET runtime.

The installer is currently **unsigned**. Windows may show a SmartScreen or antivirus reputation warning. Confirm that the installer came from this repository’s release page and verify its published SHA-256 when available. **Do not disable antivirus protection** to install it.

### Linux (x86-64)

1. Download and extract **EchoMusic-Linux-v2.0.2-x64.tar.gz**.
2. Open a terminal in the extracted folder and run:

   ```sh
   chmod +x install.sh
   ./install.sh
   ```

The package requires the **.NET 8 Runtime** and the usual desktop graphics, font and audio libraries (such as ALSA, PulseAudio or PipeWire). It installs for your user and does not need administrator privileges. To uninstall, run:

```sh
~/.local/opt/echo-music/uninstall.sh
```

This package is for x86-64 Linux, not ARM64.

## Keyboard shortcuts

Single-letter shortcuts work when a text field is not focused.

| Keys | Action |
| --- | --- |
| **Space** | Play or pause |
| **Left / Right Arrow** | Seek 5 seconds backward or forward |
| **Ctrl + Left / Right Arrow** | Previous or next track |
| **F** or **F11** | Toggle the fullscreen player |
| **M** | Mute or unmute |
| **S** | Toggle shuffle |
| **R** | Cycle repeat mode |
| **L** | Like or unlike the current song |
| **Ctrl + F** | Focus search |
| **Ctrl + K** | Open the Command Palette |
| **Ctrl + Q** | Toggle the queue / lyrics panel |
| **Ctrl + ,** | Open Settings |
| **Ctrl + M** | Toggle the mini-player |
| **Alt + Left / Right Arrow** | Go back or forward in navigation history |
| **Esc** | Close the topmost dialog or fullscreen player; clear search when applicable |

## Platform and account notes

- **Guest Mode:** listening does not require signing in.
- **Windows sign-in:** optional Google sign-in is available for some YouTube Music features through a cookie-based web integration. It is **not** an official Google OAuth integration and does not provide full YouTube Music library sync, listening history, Recaps or a personalized Home feed.
- **Linux:** remains in Guest Mode. Windows-only features such as Google sign-in and Windows media-session integration are unavailable.
- **Streaming:** playback and discovery depend on upstream service availability and can be affected by service-side changes, rate limits or network conditions.
- **Local data:** settings, playlists, artwork cache, downloads and other app data are stored in your user profile. Saved session data is protected for your Windows user at rest, but an active running session cannot be guaranteed safe from every malware or session-hijacking risk.

## Credits and license

Echo Music PC is an independent desktop project made by **V!SHNU** and released under the **GNU General Public License v3.0**. See [LICENSE.txt](LICENSE.txt) for the complete license.

This PC edition is separate from—and is not officially affiliated with or endorsed by—the original [Echo Music Android project](https://github.com/EchoMusicApp/Echo-Music). The original project and its maintainers deserve credit for their work and project lineage; consult its repository for its own source, authorship and licensing details.

---

<div align="center">

**Made by V!SHNU** · Open source · GPL-3.0

</div>
