; Goni PDF 가상 프린터 설치 프로그램 (NSIS 3)
Unicode true
ManifestDPIAware true
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!define APPNAME   "Goni PDF 가상 프린터"
!define EXENAME   "GoniPdfPrinter.exe"
!define REGKEY    "Software\Microsoft\Windows\CurrentVersion\Uninstall\GoniPdfPrinter"
!define RUNKEY    "Software\Microsoft\Windows\CurrentVersion\Run"
!define BINDIR    "..\bin"

Name "${APPNAME}"
OutFile "${BINDIR}\GoniPdfPrinterSetup-${VERSION}.exe"
InstallDir "$PROGRAMFILES64\GoniPdfPrinter"
BrandingText "Goni PDF ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1042 "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=1042 "FileDescription" "${APPNAME} 설치"
VIAddVersionKey /LANG=1042 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1042 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1042 "LegalCopyright" ""

!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"

!define MUI_ICON   "${BINDIR}\app.ico"
!define MUI_UNICON "${BINDIR}\app.ico"
!define MUI_ABORTWARNING

!define MUI_WELCOMEPAGE_TEXT "모든 프로그램에서 인쇄할 때 열기 암호와 편집·인쇄·복사 권한이 설정된 PDF로 저장하는 가상 프린터 '$\"Goni PDF$\"'를 설치합니다.$\r$\n$\r$\n설치 중 인쇄 스풀러에 프린터가 등록됩니다."
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Goni PDF 트레이 앱 실행"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Korean"

!define POWERSHELL "$WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"

Function .onInit
  ${IfNot} ${RunningX64}
    StrCpy $INSTDIR "$PROGRAMFILES\GoniPdfPrinter"
  ${EndIf}
  ${If} ${RunningX64}
    ${DisableX64FSRedirection}
    SetRegView 64
  ${EndIf}
FunctionEnd

Function un.onInit
  ${If} ${RunningX64}
    ${DisableX64FSRedirection}
    SetRegView 64
  ${EndIf}
FunctionEnd

Function LaunchApp
  ; 설치 프로그램은 관리자 권한이므로 explorer를 통해 현재 사용자 권한으로 실행
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${EXENAME}"'
FunctionEnd

Section "Install"
  SetOutPath "$INSTDIR"

  DetailPrint "실행 중인 Goni PDF 종료"
  nsExec::Exec '"$WINDIR\System32\taskkill.exe" /F /IM ${EXENAME}'
  Pop $0
  Sleep 500

  File "${BINDIR}\${EXENAME}"
  File "..\README.md"
  File "printer.ps1"

  DetailPrint "가상 프린터 등록 중..."
  nsExec::ExecToLog '"${POWERSHELL}" -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\printer.ps1" -Action install'
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP|MB_OK "가상 프린터 등록에 실패했습니다. (코드 $0)$\r$\n설치 로그를 확인하세요."
    Abort
  ${EndIf}

  ; 로그온 시 자동 실행 (모든 사용자)
  WriteRegStr HKLM "${RUNKEY}" "GoniPdfPrinter" '"$INSTDIR\${EXENAME}"'

  ; 시작 메뉴
  CreateDirectory "$SMPROGRAMS\Goni PDF"
  CreateShortCut "$SMPROGRAMS\Goni PDF\Goni PDF 트레이 앱.lnk" "$INSTDIR\${EXENAME}"
  CreateShortCut "$SMPROGRAMS\Goni PDF\Goni PDF 기본 보안 설정.lnk" "$INSTDIR\${EXENAME}" "--settings"
  CreateShortCut "$SMPROGRAMS\Goni PDF\Goni PDF 제거.lnk" "$INSTDIR\Uninstall.exe"

  ; 제어판 '앱 및 기능' 등록
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr   HKLM "${REGKEY}" "DisplayName"     "${APPNAME}"
  WriteRegStr   HKLM "${REGKEY}" "DisplayVersion"  "${VERSION}"
  WriteRegStr   HKLM "${REGKEY}" "Publisher"       "Goni"
  WriteRegStr   HKLM "${REGKEY}" "DisplayIcon"     "$INSTDIR\${EXENAME}"
  WriteRegStr   HKLM "${REGKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKLM "${REGKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKLM "${REGKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${REGKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${REGKEY}" "NoRepair" 1
  WriteRegDWORD HKLM "${REGKEY}" "EstimatedSize" 200

  ; 자동 설치(/S)일 때도 트레이 앱 실행
  IfSilent 0 +2
    Call LaunchApp
SectionEnd

Section "Uninstall"
  DetailPrint "실행 중인 Goni PDF 종료"
  nsExec::Exec '"$WINDIR\System32\taskkill.exe" /F /IM ${EXENAME}'
  Pop $0
  Sleep 500

  DetailPrint "가상 프린터 제거 중..."
  nsExec::ExecToLog '"${POWERSHELL}" -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\printer.ps1" -Action uninstall'
  Pop $0

  DeleteRegValue HKLM "${RUNKEY}" "GoniPdfPrinter"
  DeleteRegKey HKLM "${REGKEY}"

  Delete "$SMPROGRAMS\Goni PDF\*.lnk"
  RMDir  "$SMPROGRAMS\Goni PDF"

  Delete "$INSTDIR\${EXENAME}"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\printer.ps1"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir  "$INSTDIR"
  ; 사용자 설정(%APPDATA%\GoniPdfPrinter)은 유지
SectionEnd
