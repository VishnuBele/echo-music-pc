# Echo Music for PC

A desktop music player for **Windows 10/11** and **64-bit Linux**, made by **V!SHNU**. Guest Mode is available, and you can use the app without signing in.

## Download and install

Get the installer or Linux package from the [Echo Music releases page](https://github.com/VishnuBele/echo-music-pc/releases).

### Windows 10/11

1. Download `EchoMusic-Setup-v2.0.0-x64.exe`.
2. Open the file and follow the on-screen steps. It installs for your Windows user and does not require an administrator password or a separate .NET installation.

The installer is unsigned, so Windows may show a security warning. Only continue if you trust the download source. **Do not turn off your antivirus to install it.**

### Linux (64-bit)

1. Download and extract `EchoMusic-Linux-v2.0.0-x64.tar.gz`.
2. Open a terminal in the extracted folder and run:

   ```sh
   chmod +x install.sh
   ./install.sh
   ```

The installer creates a per-user copy and an application-menu shortcut. The package includes what Echo Music needs to run, but your Linux desktop may need additional system audio or graphics components. Linux support is for x86-64 systems; sound and desktop compatibility can vary.

## What you can do

- **Discover and play music:** browse recommendations, charts, new releases, moods and genres; search for music; and explore related tracks.
- **Enjoy lyrics:** view synced lyrics, adjust their timing, translate them, or use fullscreen Karaoke mode with **F11**. Lyrics availability varies by song.
- **Manage playlists and your music:** make playlists, scan your local music, import public Spotify playlists, and save tracks for offline listening. Spotify sign-in is not required for public playlist imports.
- **Make playback your own:** use the 10-band equalizer, presets, playback effects, volume normalization and crossfade.
- **Personalize the app:** choose a theme, filter content, and manage excluded songs, albums or artists in Settings.
- **Use handy extras:** open the mini-player with **Ctrl+M**, the Command Palette with **Ctrl+K**, view your Echo Wrapped listening summary, and back up or restore app data.
- **Share and connect:** share tracks with universal links, or enable Discord Rich Presence if you want friends to see what you’re listening to.

## Sign-in and privacy

Sign-in is optional; Guest Mode remains usable. On Windows, optional Google sign-in offers limited YouTube Music features. It is **not Google’s official sign-in integration** and does not import your full music library, listening history, Recaps or personalized Home feed. Linux does not include Google sign-in.

On Windows, saved sign-in data is protected for your Windows user, but that protection does not make an active session immune to malware. Use Guest Mode if you prefer not to sign in, and review or revoke sessions in your Google Account security settings when needed.

## Platform differences

Some features rely on Windows and are not available on Linux, including the Windows installer, Windows media controls and Google sign-in. Linux uses Guest Mode. The editions offer similar core music-player features, but they are not identical. The Linux package has had a build and startup check; it has not been tested on every distribution or audio setup. The Windows installer has not been runtime-tested on every Windows configuration.

## Reset Echo Music on Windows

To clear Echo Music’s saved settings and data:

1. Exit the app completely, including from the system tray if it is running there.
2. Press **Win+R**, enter `%LOCALAPPDATA%\EchoMusic`, and press Enter.
3. Delete the `EchoMusic` folder, then reopen the app.

This clears app settings, playlists, listening history, saved sign-in, downloads and cached artwork. It does **not** uninstall Echo Music or delete music files in your original folders.

## Version 2.0.0

The first public PC release includes Windows and Linux x64 packages, an updated installer, improved player controls and layout, playlist sorting, and a quieter update check. The update check only links to a newer release; it does not download or install updates automatically.

## Credits and license

Echo Music PC is by **V!SHNU** and is released under **GNU GPL v3.0**. Project lineage and license notices are available in the app’s **About** section and in `LICENSE.txt`.
