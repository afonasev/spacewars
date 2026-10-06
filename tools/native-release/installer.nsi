Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
Name "Spacewars ${VERSION}"
!define MUI_ICON "${PAYLOAD}/Spacewars.ico"
!define MUI_UNICON "${PAYLOAD}/Spacewars.ico"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\Spacewars"
InstallDirRegKey HKLM "Software\Spacewars" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Добро пожаловать в Spacewars"
!define MUI_WELCOMEPAGE_TEXT "Установите Spacewars — стратегию о борьбе за территории.$\r$\n$\r$\nОбновления доступны отдельным пунктом в главном меню. Загрузка начинается только по вашему нажатию.$\r$\n$\r$\nЭто тестовая сборка без подписи издателя."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Запустить Spacewars"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchGame
!define MUI_FINISHPAGE_SHOWREADME
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Создать ярлык на рабочем столе"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION CreateDesktopShortcut
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"
Section "Spacewars (обязательно)" SEC_GAME
 SectionIn RO
 SetRegView 64
 SetOutPath "$INSTDIR"
 File /r /x active.json "${PAYLOAD}/*"
 ExecWait '"$INSTDIR\Spacewars.exe" --activate ${RELEASE}' $0
 ${If} $0 != 0
  MessageBox MB_ICONSTOP "Не удалось активировать установленную версию. Закройте игру и повторите установку. Рабочая версия сохранена."
  Abort
 ${EndIf}
 WriteRegStr HKLM "Software\Spacewars" "InstallDir" "$INSTDIR"
 WriteUninstaller "$INSTDIR\Uninstall.exe"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spacewars" "DisplayName" "Spacewars"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spacewars" "DisplayVersion" "${VERSION}"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spacewars" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spacewars" "DisplayIcon" "$INSTDIR\Spacewars.ico"
 SetShellVarContext all
 CreateDirectory "$SMPROGRAMS\Spacewars"
 CreateShortcut "$SMPROGRAMS\Spacewars\Spacewars.lnk" "$INSTDIR\Spacewars.exe" "" "$INSTDIR\Spacewars.ico"
SectionEnd
Function CreateDesktopShortcut
 SetShellVarContext all
 ClearErrors
 CreateShortcut "$DESKTOP\Spacewars.lnk" "$INSTDIR\Spacewars.exe" "" "$INSTDIR\Spacewars.ico"
 ${If} ${Errors}
  MessageBox MB_ICONEXCLAMATION "Не удалось создать ярлык на общем рабочем столе Windows. Spacewars доступен в меню «Пуск»."
 ${Else}
  IfFileExists "$DESKTOP\Spacewars.lnk" +2 0
  MessageBox MB_ICONEXCLAMATION "Ярлык не найден на рабочем столе Windows. Spacewars доступен в меню «Пуск»."
 ${EndIf}
FunctionEnd
Function .onInstSuccess
 IfSilent 0 +2
 Call CreateDesktopShortcut
FunctionEnd
Function LaunchGame
 ; Explorer forwards the launch to the interactive shell; game must run unelevated.
 ExecShell "open" "$WINDIR\explorer.exe" '$\"$INSTDIR\Spacewars.exe$\"'
FunctionEnd
Section "Uninstall"
 SetRegView 64
 SetShellVarContext all
 Delete "$DESKTOP\Spacewars.lnk"
 Delete "$SMPROGRAMS\Spacewars\Spacewars.lnk"
 RMDir "$SMPROGRAMS\Spacewars"
 RMDir /r "$INSTDIR\releases"
 Delete "$INSTDIR\active.json"
 Delete "$INSTDIR\apply.lock"
 Delete "$INSTDIR\Spacewars.exe"
 Delete "$INSTDIR\Spacewars.ico"
 Delete "$INSTDIR\Uninstall.exe"
 RMDir "$INSTDIR"
 DeleteRegKey HKLM "Software\Spacewars"
 DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spacewars"
SectionEnd
