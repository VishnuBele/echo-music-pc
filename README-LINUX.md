# Echo Music v2.0.5 for Linux (x86-64)

This compact Linux package is framework-dependent: install the **.NET 8 runtime** before launching. Your desktop also needs its normal graphics and audio libraries. Audio uses ALSA's default output, commonly routed through PulseAudio or PipeWire. If startup reports `libasound.so.2` is missing, install the ALSA runtime (`libasound2`/`libasound2t64` on Debian or Ubuntu, `alsa-lib` on Fedora or Arch).

## Run

1. Extract `EchoMusic-Linux-v2.0.5-x64.tar.gz`.
2. Run `./EchoMusic` or `./install.sh` to add the desktop launcher.
3. Run `./uninstall.sh` to remove the launcher.

Application data is stored in the platform-specific user data directory.
