# Goni PDF 가상 프린터 설치 (관리자 권한 필요 - 자동으로 권한 상승 요청)
#   - Windows 내장 "Microsoft Print To PDF" 드라이버 사용 (별도 드라이버 설치 없음)
#   - 프린터 포트: 로컬 포트 \\.\pipe\GoniPdfPrinter  → 트레이 앱이 PDF를 수신
param([switch]$NoStart)

$ErrorActionPreference = 'Stop'
$PrinterName = 'Goni PDF'
$PortName    = '\\.\pipe\GoniPdfPrinter'
$DriverName  = 'Microsoft Print To PDF'
$InstallDir  = Join-Path $env:ProgramFiles 'GoniPdfPrinter'
$ExeName     = 'GoniPdfPrinter.exe'
$RunKey      = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host '관리자 권한으로 다시 실행합니다...'
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($NoStart) { $argList += '-NoStart' }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList -Wait
    exit
}

try {
    $src = Join-Path $PSScriptRoot "bin\$ExeName"
    if (-not (Test-Path $src)) { throw "빌드 결과가 없습니다. 먼저 build.ps1을 실행하세요: $src" }

    # 1) 드라이버 확인
    if (-not (Get-PrinterDriver -Name $DriverName -ErrorAction SilentlyContinue)) {
        Write-Host "'$DriverName' 드라이버가 없어 Windows 기능을 켭니다..."
        Enable-WindowsOptionalFeature -Online -FeatureName 'Printing-PrintToPDFServices-Features' -All -NoRestart | Out-Null
        if (-not (Get-PrinterDriver -Name $DriverName -ErrorAction SilentlyContinue)) {
            Add-PrinterDriver -Name $DriverName
        }
    }

    # 2) 실행 중인 트레이 앱 종료 후 파일 복사
    Get-Process -Name 'GoniPdfPrinter' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    Copy-Item $src (Join-Path $InstallDir $ExeName) -Force
    $readme = Join-Path $PSScriptRoot 'README.md'
    if (Test-Path $readme) { Copy-Item $readme $InstallDir -Force }
    Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $InstallDir -Force

    # 3) 포트 (로컬 포트 → 명명된 파이프)
    if (-not (Get-PrinterPort -Name $PortName -ErrorAction SilentlyContinue)) {
        try {
            Add-PrinterPort -Name $PortName
        } catch {
            # 대체 방법: 로컬 포트 레지스트리 등록 후 스풀러 재시작
            New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Ports' -Name $PortName -Value '' -PropertyType String -Force | Out-Null
            Restart-Service Spooler -Force
            Start-Sleep -Seconds 2
        }
    }

    # 4) 프린터
    if (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue) {
        Remove-Printer -Name $PrinterName
    }
    Add-Printer -Name $PrinterName -DriverName $DriverName -PortName $PortName
    Set-Printer -Name $PrinterName -Comment '인쇄 시 암호/권한이 설정된 PDF로 저장합니다.'

    # 5) 로그온 시 자동 실행 (모든 사용자)
    New-ItemProperty -Path $RunKey -Name 'GoniPdfPrinter' -Value "`"$(Join-Path $InstallDir $ExeName)`"" -PropertyType String -Force | Out-Null

    # 6) 현재 사용자 권한(비관리자)으로 트레이 앱 실행
    if (-not $NoStart) {
        Start-Process explorer.exe -ArgumentList "`"$(Join-Path $InstallDir $ExeName)`""
    }

    Write-Host ''
    Write-Host "설치 완료: 프린터 '$PrinterName' 이(가) 추가되었습니다." -ForegroundColor Green
    Write-Host "설치 위치: $InstallDir"
} catch {
    Write-Host "설치 실패: $($_.Exception.Message)" -ForegroundColor Red
}
Read-Host '엔터 키를 누르면 창이 닫힙니다'
