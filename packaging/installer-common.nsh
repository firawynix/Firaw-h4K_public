!include FileFunc.nsh
!include LogicLib.nsh
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\FirawH4K"

!macro RequireConsent
  IfSilent 0 consent_done
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/ACCEPTCONSENT" $1
  IfErrors 0 consent_done
  SetErrorLevel 1602
  Abort
  consent_done:
!macroend

; Called for both install and uninstall. The OS releases the handle on exit.
!macro AcquireInstallLock
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\FirawH4K.Install") p.r0 ?e'
  Pop $1
  ${If} $0 == 0
  ${OrIf} $1 == 183
    SetErrorLevel 1618
    Abort
  ${EndIf}
!macroend

!macro CheckRunning
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=check-running.ps1 "${ROOT}\packaging\check-running.ps1"
  System::Call 'kernel32::SetEnvironmentVariableW(w "FIRAW_H4K_TARGET_EXE", w "$INSTDIR\Firaw-h4K.exe") i.r0'
  ${If} $0 == 0
    SetErrorLevel 1603
    Abort
  ${EndIf}
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\check-running.ps1"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    ${If} $0 == 1618
      MessageBox MB_ICONEXCLAMATION|MB_OK "Firaw h4K esta em uso. Pressione Ctrl+Alt+F12, feche o aplicativo e tente novamente. Nenhum processo sera encerrado." /SD IDOK
      SetErrorLevel 1618
    ${Else}
      MessageBox MB_ICONSTOP|MB_OK "Nao foi possivel verificar se o aplicativo esta fechado. Instalacao/remocao cancelada. $1" /SD IDOK
      SetErrorLevel 1603
    ${EndIf}
    Abort
  ${EndIf}
!macroend
