# Echo Music

Echo Music is my native desktop music player for **Windows 10/11** and **64-bit Linux**. I built it around a simple idea: a full-featured music player should work in Guest Mode, feel at home on a PC, and keep the controls and settings easy to find.

**Made by V!SHNU · GPL-3.0**

## Download and install

Download the current release from the [Echo Music releases page](https://github.com/VishnuBele/echo-music-pc/releases).

### Windows 10/11

1. Download `EchoMusic-Setup-v2.0.2-x64.exe` from the release assets.
2. Open it and follow the setup screens. It installs for your Windows user and includes the .NET runtime; you do not need to install .NET separately or use an administrator account.

The installer is **unsigned**. Windows SmartScreen or antivirus software may display a warning. Check that the file came from the official release and verify its SHA-256 before opening it. I never ask you to turn off antivirus protection.

### Linux (x86-64)

1. Download and extract `EchoMusic-Linux-v2.0.2-x64.tar.gz`.
2. Open a terminal in the extracted folder and run:

   ```sh
   chmod +x install.sh
   ./install.sh
   ```

The installer adds a per-user copy and an application-menu shortcut. To uninstall, run `~/.local/opt/echo-music/uninstall.sh`. The Linux package requires the .NET 8 Runtime and your desktop’s usual graphics and audio libraries. This build is for x86-64 Linux, not ARM64.

## What I built into Echo Music

- Search, browse recommendations, charts, albums, artists, playlists and related music.
- Synced lyrics, lyric timing controls, translation options and a fullscreen player.
- Guest Mode, local music, playlist management, public Spotify playlist import and offline downloads.
- A 10-band equalizer, presets, playback speed, volume normalization, crossfade and other playback controls.
- Themes, content filters, keyboard shortcuts, a mini-player, the Command Palette and Echo Wrapped.
- Optional Discord Rich Presence and optional Google sign-in on Windows for YouTube Music features.

Spotify playlist import uses a public playlist URL or a local playlist file; it does not ask for a Spotify password. Google sign-in is optional, is not an official Google OAuth integration, and does not provide access to a full YouTube Music library, listening history, Recaps or personalized Home feed. Linux stays in Guest Mode.

## Keyboard shortcuts

Shortcuts that use a single letter are available when a text field is not focused.

| Keys | Action |
| --- | --- |
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

## About this release — v2.0.2

This release focuses on the requested flicker and memory fixes. Lyrics and album art now update without opacity pulses or fade flashes; History updates the changed song row in place, and the Now Playing dock avoids rebuilding its cards when artwork or track details arrive. Resource Saver now limits artwork resolution and cache size, concurrent image loads, stream-page buffering, and next-track prewarming. Earlier playback, lyric-sync, scrolling, sidebar, and Windows output-device fixes remain. The two remaining Android-specific guest-playback measures are intentionally deferred for a later release.

## Privacy and local data

You can use Echo Music without signing in. On Windows, optional sign-in data is protected for your Windows user at rest, but that does not protect an active session from every malware or session-hijacking risk. Use Guest Mode if you do not want to sign in, and review or revoke active sessions in your Google Account security settings when needed.

Echo Music stores its settings, playlists, cached artwork, downloads and other local data in your user profile. The app does not disable antivirus software.

## Platform notes

I develop and test Echo Music on Windows. Linux shares the core player and library code, but some Windows integrations are unavailable there; audio-device behavior also depends on the Linux distribution and desktop audio stack. Windows media-session features and Google sign-in are Windows-only. The two editions share the same core experience but are not identical.

## Credits and license

Echo Music PC is made by **V!SHNU** and released under the **GNU General Public License v3.0**. See `LICENSE.txt` and the About section in the app for attribution and license details.
