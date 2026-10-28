# Publish Echo Music PC v2.0.0 with GitHub Desktop

This guide is for the first public release. Do not commit the large installers into the source tree; attach them to the GitHub Release instead.

## 1. Get the files ready

1. Download and install **GitHub Desktop** from [desktop.github.com](https://desktop.github.com/), then sign in to your GitHub account.
2. Download and extract `EchoMusic-v2.0.0-source.zip` to a folder such as `Documents/EchoMusic`.
3. Keep these release files together for later: `EchoMusic-Setup-v2.0.0-x64.exe`, `EchoMusic-Linux-v2.0.0-x64.tar.gz`, and `SHA256SUMS-v2.0.0.txt`.
4. Verify each download against `SHA256SUMS-v2.0.0.txt` before publishing. In Windows PowerShell, run `Get-FileHash .\\EchoMusic-Setup-v2.0.0-x64.exe -Algorithm SHA256` (repeat for the Linux archive and source ZIP) and compare the `Hash` field.

## 2. Add the source folder in GitHub Desktop

1. In GitHub Desktop, choose **File → Add Local Repository…** and select the extracted `EchoMusic` source folder.
2. If GitHub Desktop says it is not a Git repository, use its **create a repository** option. Keep the repository path on the extracted source folder, name it `echo-music-pc`, and do not add a second README, license, or `.gitignore`—those are already in the source.
3. Review the **Changes** tab. The installer `.exe` and Linux `.tar.gz` should not be in this source folder.
4. Enter a summary such as `Initial Echo Music PC v2.0.0 source` and click **Commit to main**.
5. Click **Publish repository**. The automatic updater is configured for the public repository `VishnuBele/echo-music-pc`. If your GitHub username is different, change that owner/repository in both the startup and Settings update-check calls before building. Keep the repository public if you want the unauthenticated startup update check to work.

## 3. Create the GitHub Release

GitHub Desktop publishes the repository, but create the Release on GitHub.com:

1. Open the repository page and choose **Releases → Draft a new release** (or **Create a new release**).
2. Create tag **`v2.0.0`** on the `main` branch and set the title to **Echo Music PC v2.0.0**.
3. Add release notes describing the changes and state that the Windows installer is unsigned.
4. Attach `EchoMusic-Setup-v2.0.0-x64.exe`, `EchoMusic-Linux-v2.0.0-x64.tar.gz`, and `SHA256SUMS-v2.0.0.txt`.
5. Publish the release and mark it as the latest release. The app checks for a newer version at startup, but it never downloads or installs without user action.

## Check Windows without a local VM

The source includes `.github/workflows/build.yml`. After you publish the repository, open its **Actions** tab and run **Cross-platform build** (or push a commit to `main`). GitHub-hosted Windows and Linux runners will compile the two platform targets and attach downloadable build artifacts to the workflow run. This checks compilation and packaging inputs, not Windows UI behavior: for that, run the installer on a real Windows 10/11 PC and verify the icon, playback, settings, and update prompt there. The Windows runner is hosted by GitHub; you do not need to set up a VM on your computer.

## Before you publish

- Check the `LICENSE.txt` and make sure you are comfortable publishing the GPL-3.0 source and required notices.
- This project has no trusted Authenticode certificate. Do not describe the Windows installer as signed; Windows SmartScreen or antivirus reputation warnings may appear.
- Test the Windows installer on a real Windows 10/11 PC and the Linux archive on a supported Linux x64 desktop. A successful build is not the same as a Windows runtime test.
