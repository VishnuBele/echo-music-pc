# Echo Music for Linux (x86-64)

The compact v2.0.3 Linux package is a framework-dependent native C#/Avalonia build and requires the **.NET 8 runtime** (`Microsoft.NETCore.App` 8.0) to be installed on the system. The installer checks for it and explains the requirement if it is missing. Graphics and audio libraries are also provided by your desktop/distribution. Audio is written to ALSA's default output; on common desktop systems that routes through PulseAudio or PipeWire. If startup reports that `libasound.so.2` is missing, install the ALSA runtime (`libasound2`/`libasound2t64` on Debian or Ubuntu, `alsa-lib` on Fedora or Arch).

To build a self-contained package that bundles .NET, set `SELF_CONTAINED=true` when running `installer/build-linux-package.sh`. The self-contained package is much larger.

## Install

1. Download and extract `EchoMusic-Linux-v2.0.3-x64.tar.gz`.
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
