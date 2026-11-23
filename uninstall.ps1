# Goni PDF 가상 프린터 제거 (관리자 권한 필요 - 자동으로 권한 상승 요청)
$ErrorActionPreference = 'Stop'
$PrinterName = 'Goni PDF'
$PortName    = '\\.\pipe\GoniPdfPrinter'
$InstallDir  = Join-Path $env:ProgramFiles 'GoniPdfPrinter'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"") -Wait
    exit
}

try {
    Get-Process -Name 'GoniPdfPrinter' -ErrorAction SilentlyContinue | Stop-Process -Force
    if (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue) { Remove-Printer -Name $PrinterName }
    Start-Sleep -Seconds 1
    if (Get-PrinterPort -Name $PortName -ErrorAction SilentlyContinue) {
        try { Remove-PrinterPort -Name $PortName } catch { Write-Host "포트 제거 실패(무시): $($_.Exception.Message)" }
    }
    Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'GoniPdfPrinter' -ErrorAction SilentlyContinue
    if (Test-Path $InstallDir) {
        Set-Location $env:TEMP
        Remove-Item $InstallDir -Recurse -Force
    }
    Write-Host '제거 완료. (사용자 설정 %APPDATA%\GoniPdfPrinter 는 유지됩니다)' -ForegroundColor Green
} catch {
    Write-Host "제거 실패: $($_.Exception.Message)" -ForegroundColor Red
}
Read-Host '엔터 키를 누르면 창이 닫힙니다'
