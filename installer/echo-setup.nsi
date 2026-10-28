; =============================================================================
; Echo Music - Native Windows x64 Setup Installer (NSIS Modern UI 2)
; Compiles on Windows or Linux via `makensis` using solid LZMA compression.
; Installs per-user (like Spotify / VS Code) with zero UAC elevation required,
; zero external runtimes (.NET 8 & Skia GPU bundled), Start Menu + Desktop
; shortcuts, and full Windows "Installed Apps" uninstaller registration.
; =============================================================================

Unicode true
ManifestDPIAware true
XPStyle on
SetCompressor /SOLID /FINAL lzma
SetCompressorDictSize 16

!define APP_NAME "Echo Music"
!define APP_EXE "EchoMusic.exe"
!define APP_VERSION "2.0.0"
!define APP_PUBLISHER "V!SHNU"
!define APP_URL "https://github.com/VishnuBele/echo-music-pc"
!define SOURCE_DIR "${__FILEDIR__}/.."
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\EchoMusic"

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "${SOURCE_DIR}/publish-win-x64"
!endif
!ifndef OUT_FILE
  !define OUT_FILE "${SOURCE_DIR}/../EchoMusic-Setup-v2.0.0-x64.exe"
!endif
!ifndef PAYLOAD_INCLUDE
  !error "PAYLOAD_INCLUDE must point to the generated per-file progress include"
!endif

Name "${APP_NAME}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\EchoMusic"
InstallDirRegKey HKCU "Software\EchoMusic" "InstallDir"
RequestExecutionLevel user
BrandingText "${APP_NAME} v${APP_VERSION} - Native PC Edition"

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "${PAYLOAD_INCLUDE}"

!define MUI_BGCOLOR "111820"
!define MUI_TEXTCOLOR "EAF2F4"
!define MUI_FONT "Segoe UI"
!define MUI_FONT_SIZE 9

!define MUI_ICON "${SOURCE_DIR}/Assets/echo.ico"
!define MUI_UNICON "${SOURCE_DIR}/Assets/echo.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${SOURCE_DIR}/installer/wizard.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP "${SOURCE_DIR}/installer/header.bmp"
!define MUI_HEADERIMAGE_RIGHT
!define MUI_ABORTWARNING

; Pages
!define MUI_WELCOMEPAGE_TITLE "Welcome to Echo Music for PC"
!define MUI_WELCOMEPAGE_TEXT "This wizard will install Echo Music v${APP_VERSION} (Native 64-bit Windows Edition) on your computer.$\r$\n$\r$\nFeatures included out of the box:$\r$\n  • Ad-free high-bitrate Opus streaming & Echo Brain$\r$\n  • Real-time Synced Karaoke Lyrics + Translation$\r$\n  • Music Flow, Music DNA, Spotify Import & Wrapped$\r$\n  • 10-Band Equalizer, Slowed+Reverb DSP & Offline Mode$\r$\n$\r$\nNo Java, FFmpeg, or extra runtimes needed. Click Next to continue."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${SOURCE_DIR}/LICENSE.txt"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_TEXT "Echo Music is installed. The custom setup window will launch it when you choose Launch Echo Music. You can also start it from the Start menu or desktop shortcut."
!insertmacro MUI_PAGE_FINISH

; Uninstaller Pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; PE Version Info
VIProductVersion "2.0.0.0"
VIAddVersionKey /LANG=${LANG_ENGLISH} "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=${LANG_ENGLISH} "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey /LANG=${LANG_ENGLISH} "FileDescription" "${APP_NAME} Windows x64 Setup"
VIAddVersionKey /LANG=${LANG_ENGLISH} "FileVersion" "${APP_VERSION}.0"
VIAddVersionKey /LANG=${LANG_ENGLISH} "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=${LANG_ENGLISH} "LegalCopyright" "GPL-3.0 - Echo Music Project"

Section "Echo Music Core Application" SecMain
  SectionIn RO

  ; Gently close running instance if upgrading
  nsExec::ExecToLog 'taskkill /F /IM "${APP_EXE}"'

  SetOutPath "$INSTDIR"
  Delete "$INSTDIR\echo.ico"
  Delete "$INSTDIR\echo-emerald-v26.ico"
  StrCpy $R1 0
  FileOpen $R2 "$INSTDIR\.echo-install-progress" w
  FileWrite $R2 "0"
  FileClose $R2
  SetFileAttributes "$INSTDIR\\.echo-install-progress" HIDDEN|SYSTEM
  !insertmacro InstallPayload

  ; Remember install path
  WriteRegStr HKCU "Software\EchoMusic" "InstallDir" "$INSTDIR"

  ; Associate the unpackaged SMTC session, process and window with one friendly Windows identity.
  WriteRegStr HKCU "Software\Classes\AppUserModelId\EchoMusic.Desktop.EmeraldWave.v20" "DisplayName" "Echo Music"
  WriteRegStr HKCU "Software\Classes\AppUserModelId\EchoMusic.Desktop.EmeraldWave.v20" "IconUri" "$INSTDIR\echo-emerald-v20.ico"

  ; Create Uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Start Menu & Desktop Shortcuts (versioned icon filename busts Windows Explorer icon cache!)
  Delete "$DESKTOP\Echo Music.lnk"
  Delete "$SMPROGRAMS\Echo Music\Echo Music.lnk"
  CreateDirectory "$SMPROGRAMS\Echo Music"
  CreateShortcut "$SMPROGRAMS\Echo Music\Echo Music.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\echo-emerald-v20.ico" 0
  CreateShortcut "$SMPROGRAMS\Echo Music\Uninstall Echo Music.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\echo-emerald-v20.ico" 0
  CreateShortcut "$DESKTOP\Echo Music.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\echo-emerald-v20.ico" 0

  ; Notify Windows Shell (SHCNE_ASSOCCHANGED | SHCNF_FLUSHNOWAIT) so Taskbar & Desktop icons update immediately!
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0x1000, p 0, p 0)'

  ; Register in Windows Settings -> Apps -> Installed Apps
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\echo-emerald-v20.ico"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "${APP_URL}"
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  nsExec::ExecToLog 'taskkill /F /IM "${APP_EXE}"'

  Delete "$DESKTOP\Echo Music.lnk"
  Delete "$SMSTARTUP\Echo Music.lnk"
  RMDir /r "$SMPROGRAMS\Echo Music"
  RMDir /r "$INSTDIR"

  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "EchoMusic"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "Software\EchoMusic"
  DeleteRegKey HKCU "Software\Classes\AppUserModelId\EchoMusic.Desktop.EmeraldWave.v20"
SectionEnd
