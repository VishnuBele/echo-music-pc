# Echo Music PC — Linux x64

This is a self-contained Linux x86-64 build of the same C#/Avalonia desktop application source as the Windows edition. It includes the .NET runtime, so a separate .NET installation is not required. It still depends on libraries provided by the Linux desktop environment (X11/Wayland, graphics, fontconfig, and ALSA/PulseAudio/PipeWire audio components).

## Install

1. Extract `EchoMusic-Linux-v2.0.0-x64.tar.gz`.
2. Open a terminal in the extracted folder and run:

   ```sh
   chmod +x install.sh
   ./install.sh
   ```

3. Start **Echo Music** from your desktop application menu, or run `~/.local/opt/echo-music/EchoMusic`.

The installer only copies files into your user profile and creates a `.desktop` launcher. It does not require administrator privileges. To remove it, run `~/.local/opt/echo-music/uninstall.sh`.

## Platform notes

The same shared UI, streaming, library, lyrics, DSP, Discord Rich Presence, and playlist code is built for Linux. Windows-only behavior (the Windows installer, native caption buttons, WebView2 Google sign-in, DPAPI-backed secret storage, and Windows media-session integration) is necessarily different or unavailable. Guest Mode remains available on Linux. Linux audio device support depends on the distribution's audio stack and installed native libraries. This x64 build is not for ARM64 Linux.

This package built successfully and the window opened in an Xvfb startup smoke test; audio playback and every distribution/hardware combination have not been tested. No software can be guaranteed free of bugs. Please test playback, device selection, sleep/resume, and Guest Mode on your own distro before sharing it as a release.
