; Silent self-extracting bootstrap. The user-facing setup is the bundled C#/Avalonia app.
Unicode true
RequestExecutionLevel user
SetCompressor /SOLID /FINAL lzma
SetCompressorDictSize 32
SilentInstall silent
SilentUninstall silent

!ifndef CUSTOM_SETUP_DIR
  !error "CUSTOM_SETUP_DIR is required"
!endif
!ifndef CORE_SETUP
  !error "CORE_SETUP is required"
!endif
!ifndef OUT_FILE
  !error "OUT_FILE is required"
!endif

!define SOURCE_DIR "${__FILEDIR__}/.."

Name "Echo Music Setup"
Icon "${SOURCE_DIR}/Assets/echo.ico"
OutFile "${OUT_FILE}"

Section
  System::Call 'kernel32::GetCurrentProcessId() i .r0'
  StrCpy $INSTDIR "$TEMP\EchoMusicSetup-v2.0.2-$0"
  SetOutPath "$INSTDIR"
  File /r "${CUSTOM_SETUP_DIR}\*.*"
  File /oname=EchoMusic-CoreSetup.exe "${CORE_SETUP}"
  ExecWait '"$INSTDIR\EchoMusicSetup.exe"'
  RMDir /r "$INSTDIR"
SectionEnd
