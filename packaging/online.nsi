Unicode true
!include MUI2.nsh
!include "installer-common.nsh"
Name "Firaw h4K - Instalador online"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\Firaw h4K"
RequestExecutionLevel user
SetCompressor /SOLID lzma
!define MUI_ICON "${ROOT}\Assets\firaw-beetle-v3.ico"
!define MUI_LICENSEPAGE_CHECKBOX
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${ROOT}\packaging\consent.txt"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "PortugueseBR"
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1046 "ProductName" "Firaw h4K"
VIAddVersionKey /LANG=1046 "CompanyName" "Firawynix"
VIAddVersionKey /LANG=1046 "FileDescription" "Instalador online universal Firaw h4K"
VIAddVersionKey /LANG=1046 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1046 "LegalCopyright" "Firawynix"

Function .onInit
  SetShellVarContext current
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\Firaw h4K"
  !insertmacro RequireConsent
  !insertmacro CheckRunning
FunctionEnd

Section "Instalar" Core
  SectionIn RO
  !insertmacro CheckRunning
  SetOutPath "$PLUGINSDIR"
  File "${ROOT}\packaging\download-install.ps1"
  DetailPrint "Baixando e verificando o pacote para este Windows..."
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\download-install.ps1"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP|MB_OK "A instalacao nao foi concluida. $1" /SD IDOK
    ${If} $0 == 1618
      SetErrorLevel 1618
    ${Else}
      SetErrorLevel 1603
    ${EndIf}
    Abort
  ${EndIf}
  SetErrorLevel 0
SectionEnd
