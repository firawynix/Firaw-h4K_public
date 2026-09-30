Unicode true
!include MUI2.nsh
!include x64.nsh
!include "installer-common.nsh"
!ifndef ROOT
  !error "Use tools/build-release.ps1"
!endif
!if "${ARCH}" != "x64"
  !if "${ARCH}" != "x86"
    !error "ARCH must be x64 or x86"
  !endif
!endif
Name "Firaw h4K"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\Firaw h4K"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetOverwrite on
!define MUI_ICON "${ROOT}\Assets\firaw-beetle-v3.ico"
!define MUI_UNICON "${ROOT}\Assets\firaw-beetle-v3.ico"
!define MUI_LICENSEPAGE_CHECKBOX
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${ROOT}\packaging\consent.txt"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "PortugueseBR"
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1046 "ProductName" "Firaw h4K"
VIAddVersionKey /LANG=1046 "CompanyName" "Firawynix"
VIAddVersionKey /LANG=1046 "FileDescription" "Instalador offline Firaw h4K (${ARCH})"
VIAddVersionKey /LANG=1046 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1046 "LegalCopyright" "Firawynix"
; NSIS signs the generated uninstaller before embedding it in the installer.
!uninstfinalize '"${POWERSHELL}" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "${SIGN_SCRIPT}" -File "%1"' = 0

Function .onInit
  SetShellVarContext current
  SetRegView 32
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\Firaw h4K"
  !if "${ARCH}" == "x64"
    ${IfNot} ${RunningX64}
      MessageBox MB_ICONSTOP|MB_OK "Este pacote requer Windows x64." /SD IDOK
      SetErrorLevel 1633
      Abort
    ${EndIf}
  !endif
  !insertmacro RequireConsent
  !insertmacro AcquireInstallLock
  !insertmacro CheckRunning
FunctionEnd

Section "Aplicativo" Core
  SectionIn RO
  !insertmacro CheckRunning
  SetOutPath "$INSTDIR"
  ClearErrors
  File "${PAYLOAD}\Firaw-h4K.exe"
  IfErrors install_failed
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  IfErrors install_failed
  CreateDirectory "$SMPROGRAMS\Firaw"
  CreateShortcut "$SMPROGRAMS\Firaw\Firaw h4K.lnk" "$INSTDIR\Firaw-h4K.exe"
  CreateShortcut "$DESKTOP\Firaw h4K.lnk" "$INSTDIR\Firaw-h4K.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Firaw h4K"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Firawynix"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Firaw-h4K.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  IfErrors install_failed
  SetErrorLevel 0
  Goto install_done
  install_failed:
    SetErrorLevel 1603
    Abort "Nao foi possivel concluir a instalacao. Feche o aplicativo e tente novamente."
  install_done:
SectionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 32
  ; Only remove the fixed, per-user installation, even if this EXE was copied.
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\Firaw h4K"
  !insertmacro AcquireInstallLock
  !insertmacro CheckRunning
FunctionEnd

Section "Uninstall"
  !insertmacro CheckRunning
  SetOutPath "$TEMP"
  ClearErrors
  Delete "$INSTDIR\Firaw-h4K.exe"
  IfErrors uninstall_failed
  Delete "$INSTDIR\Uninstall.exe"
  IfErrors uninstall_failed
  Delete "$SMPROGRAMS\Firaw\Firaw h4K.lnk"
  Delete "$DESKTOP\Firaw h4K.lnk"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  IfErrors uninstall_failed
  RMDir "$INSTDIR"
  RMDir "$SMPROGRAMS\Firaw"
  SetErrorLevel 0
  Goto uninstall_done
  uninstall_failed:
    SetErrorLevel 1603
    Abort "Nao foi possivel concluir a remocao. Feche o aplicativo e tente novamente."
  uninstall_done:
SectionEnd
