<div align="center">
  <img src="https://raw.githubusercontent.com/VishnuBele/echo-music-pc/main/Assets/echo-logo.png" alt="Echo Music logo" width="112">

  <h1>Echo Music</h1>
  <p><strong>Your music. Your flow. Your desktop.</strong></p>
  <p>A feature-packed native music player for Windows and Linux — built to make the Echo experience feel right at home on PC.</p>

  <p>
    <a href="https://github.com/VishnuBele/echo-music-pc/releases/latest"><img src="https://img.shields.io/github/v/release/VishnuBele/echo-music-pc?display_name=tag&label=Latest%20release" alt="Latest release"></a>
    <a href="https://github.com/VishnuBele/echo-music-pc/blob/main/LICENSE.txt"><img src="https://img.shields.io/badge/License-GPL--3.0-blue.svg" alt="GPL-3.0 license"></a>
    <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?logo=windows&logoColor=white" alt="Windows 10 and 11">
    <img src="https://img.shields.io/badge/Linux-x86--64-FCC624?logo=linux&logoColor=black" alt="Linux x86-64">
  </p>

  <p>
    <a href="https://github.com/VishnuBele/echo-music-pc/releases/latest"><strong>⬇ Download Echo Music</strong></a>
    &nbsp;·&nbsp;
    <a href="#features">Explore features</a>
    &nbsp;·&nbsp;
    <a href="#keyboard-shortcuts">Keyboard shortcuts</a>
  </p>

  <sub>Made by <strong>V!SHNU</strong> · Open source · GPL-3.0</sub>
</div>

---

## A music player that feels at home on PC

Echo Music brings a full listening experience to your desktop: discover music, manage playlists, follow synced lyrics, fine-tune playback, and keep your controls close. Start in **Guest Mode** without signing in, then explore the optional integrations available on Windows.

The Windows and Linux editions share the core player, but platform-specific features mean they are not identical.

## Features

| | |
|---|---|
| **Discover** | Search, recommendations, charts, albums, artists, playlists, and related music. |
| **Lyrics & fullscreen** | Synced lyrics, lyric timing controls, translation options, and a fullscreen player. |
| **Your music library** | Play local music, manage playlists, import public Spotify playlists, and use supported offline downloads. |
| **Detailed playback controls** | 10-band equalizer, presets, playback speed, volume normalization, crossfade, and more. |
| **Built for desktop** | Themes, content filters, keyboard shortcuts, mini-player, Command Palette, and Echo Wrapped. |
| **Stay in sync** | Optional Discord Rich Presence and Windows media-session integration. |
| **Optional YouTube Music features** | Google sign-in is available on Windows; see the limitations below before using it. |

## Download

**Latest release: [Echo Music PC releases](https://github.com/VishnuBele/echo-music-pc/releases/latest)**

| Platform | Download | Notes |
|---|---|---|
| **Windows 10/11 · x64** | [`EchoMusic-Setup-v2.0.1-x64.exe`](https://github.com/VishnuBele/echo-music-pc/releases/download/v2.0.1/EchoMusic-Setup-v2.0.1-x64.exe) | Per-user installer; .NET runtime included. |
| **Linux · x86-64** | [`EchoMusic-Linux-v2.0.1-x64.tar.gz`](https://github.com/VishnuBele/echo-music-pc/releases/download/v2.0.1/EchoMusic-Linux-v2.0.1-x64.tar.gz) | Self-contained package; .NET runtime included. |

For newer versions, use the **[latest releases page](https://github.com/VishnuBele/echo-music-pc/releases/latest)** rather than relying on a version-specific link.

### Windows installation

1. Download the Windows installer from the release page.
2. Run it and follow the setup screens.
3. Launch **Echo Music** from the installed app.

The installer installs for your Windows user and includes the .NET runtime. A separate .NET installation or administrator account is not required.

> **Security note:** The Windows installer is unsigned, so Windows SmartScreen or security software may show a warning. Download it only from this repository's official Releases page. Do not disable antivirus protection to install it. If a trusted SHA-256 checksum is published with a release, compare the downloaded file against it before opening the installer.

### Linux installation (x86-64)

1. Download and extract `EchoMusic-Linux-v2.0.1-x64.tar.gz`.
2. Open a terminal in the extracted folder.
3. Run:

   ```sh
   chmod +x install.sh
   ./install.sh
   ```

The installer creates a per-user installation and an application-menu shortcut; it does not require administrator privileges. To uninstall, run:

```sh
~/.local/opt/echo-music/uninstall.sh
```

The package includes the .NET runtime, but your Linux desktop still needs its usual graphics, font, and audio libraries. This build is for **x86-64**, not ARM64.

## Platform differences

| Feature | Windows 10/11 | Linux x86-64 |
|---|:---:|:---:|
| Core player and library | ✓ | ✓ |
| Guest Mode | ✓ | ✓ |
| Discord Rich Presence | ✓ | — |
| Windows media-session integration | ✓ | — |
| Optional Google sign-in for supported YouTube Music features | ✓ | — |

Linux stays in Guest Mode. Audio-device behaviour varies with your distribution and desktop audio stack. Windows integrations are not available in the Linux edition.

## Important notes about integrations

- **Spotify playlist import:** Use a public playlist URL or a local playlist file. Echo Music does not ask for your Spotify password.
- **Google sign-in:** Optional and Windows-only. This is **not an official Google OAuth integration**. It does not provide a complete YouTube Music account experience, including full library access, listening history, Recaps, or a personalized Home feed.
- **Guest Mode:** You can use Echo Music without signing in. Linux remains in Guest Mode.

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

## What's new in v2.0.1

This release focuses on playback behaviour and small interface refinements:

- Added a subtle active-theme tint to the track context menu.
- Smoothed the colour gradient used for playlist artwork.
- Added a keyboard-shortcut guide in Settings.
- Improved audio flushing when playback is paused or the selected track changes, helping prevent leftover audio from the previous selection.
- Stabilised the Settings content width.

See the [full v2.0.1 release notes](https://github.com/VishnuBele/echo-music-pc/releases/tag/v2.0.1).

## Privacy & local data

You can listen without signing in. Echo Music stores settings, playlists, cached artwork, downloads, and other local data in your user profile.

On Windows, optional sign-in data is protected for your Windows user at rest. This does not protect an active session from every malware or session-hijacking risk. If you prefer not to sign in, use Guest Mode; you can also review or revoke active sessions in your Google Account security settings.

Echo Music does not ask you to disable antivirus software.

## Credits & license

Echo Music PC is an independent desktop implementation inspired by the original [Echo Music Android project](https://github.com/EchoMusicApp/Echo-Music) by its original developers.

**Original project:** [Echo Music — GitHub Releases](https://github.com/EchoMusicApp/Echo-Music/releases)

All credit for the original Android application belongs to its respective creators and contributors. Echo Music PC is a separate desktop project and is not an official release of the original Android app.

Echo Music PC is made by **V!SHNU** and released under the **GNU General Public License v3.0 (GPL-3.0)**. See [`LICENSE.txt`](https://github.com/VishnuBele/echo-music-pc/blob/main/LICENSE.txt) for the license and applicable attribution details.

Please refer to the [original project's repository](https://github.com/EchoMusicApp/Echo-Music) for its own license, credits, and notices.

---

<div align="center">
  <sub>Built with care for the desktop listening experience. 🎧</sub>
</div>
