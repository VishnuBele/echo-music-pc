# Echo Music for Linux (x86-64)

This is the native C#/Avalonia Linux x86-64 package. It requires the **.NET 8 Runtime** for Linux x64, plus your desktop's normal graphics, font and audio libraries (for example ALSA, PulseAudio or PipeWire). If .NET 8 is not installed, install the runtime using Microsoft's [.NET Linux installation instructions](https://learn.microsoft.com/dotnet/core/install/linux).

## Install

1. Download and extract `EchoMusic-Linux-v2.0.2-x64.tar.gz`.
2. Open a terminal in the extracted folder and run:

   ```sh
   chmod +x install.sh
   ./install.sh
   ```

3. Launch **Echo Music** from your application menu, or run `~/.local/opt/echo-music/EchoMusic`.

The installer works in your user profile and does not need administrator privileges. To remove Echo Music, run:

```sh
~/.local/opt/echo-music/uninstall.sh
```

## Notes

Guest Mode is available on Linux. Windows-only integrations—such as the Windows installer, Windows media-session controls and Google sign-in—are not included in this edition. Audio-device behavior depends on your distribution and desktop audio stack. This package is for x86-64 Linux, not ARM64.

Echo Music is made by **V!SHNU** and is licensed under **GNU GPL-3.0**. See `LICENSE.txt` for the full license.
