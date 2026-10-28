# Publishing Echo Music PC v2.0.1

I keep the source repository separate from the packaged downloads. The Windows installer and Linux archive should be attached to the GitHub Release, not committed into the source tree.

## Prepare the source repository

1. Open the `EchoMusic` source folder in GitHub Desktop, or use the GitHub workflow you already use for the project.
2. Confirm the source contains the updated `README.md`, `README-LINUX.md`, `LICENSE.txt`, and the v2.0.1 code.
3. Keep the large release files out of the source commit:
   - `EchoMusic-Setup-v2.0.1-x64.exe`
   - `EchoMusic-Linux-v2.0.1-x64.tar.gz`
4. Commit the source changes with a message such as **Prepare Echo Music PC v2.0.1** and publish them to `VishnuBele/echo-music-pc`.

## Create the GitHub Release

1. On GitHub, open **Releases → Draft a new release**.
2. Create the tag **`v2.0.1`** and use the title **Echo Music PC v2.0.1**.
3. Paste the notes from `RELEASE-NOTES-v2.0.1.md` into the release description.
4. Attach `EchoMusic-Setup-v2.0.1-x64.exe`, `EchoMusic-Linux-v2.0.1-x64.tar.gz`, `EchoMusic-v2.0.1-source.zip`, and `SHA256SUMS-v2.0.1.txt`.
5. Publish the release when the source and release assets are ready.

The Windows installer is unsigned. Do not describe it as signed; SmartScreen or antivirus reputation warnings may occur. I do not upload or publish this release on your behalf.
