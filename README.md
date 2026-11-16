# Echo Music PC v2.0.5

Native desktop music player for Windows and Linux.

## v2.0.5 changes

See [release notes](RELEASE-NOTES-v2.0.5.md) for Home recommendation/queue refresh, restored five-second track crossfade, and formatted update notices.

## Windows

Run `EchoMusic-Setup-v2.0.5-x64.exe` for the branded self-contained setup/update-in-place flow. It bundles .NET, adds shortcuts, and registers an uninstaller. Microsoft Edge WebView2 Runtime is required for embedded web content. The installer is unsigned and may trigger SmartScreen warnings.

## Linux

The supplied Linux x64 package is compact and requires the **.NET 8 runtime** plus normal desktop graphics/audio libraries. See [Linux instructions](README-LINUX.md). A self-contained Linux package can also be built with `SELF_CONTAINED=true`.

## Build

- .NET 8 SDK
- Windows branded installer: `bash installer/build-windows-setup.sh`
- Linux x64 package: `SELF_CONTAINED=false bash installer/build-linux-package.sh`
- For a local update-popup preview in a test build only, add `-p:UpdatePopupPreview=true`; normal builds prompt only when a newer release exists.

This project is licensed under GPL-3.0; see `LICENSE.txt`.
