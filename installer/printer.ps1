# 설치 프로그램에서 호출: 프린터/포트 등록 및 제거
#   printer.ps1 -Action install | uninstall
param([Parameter(Mandatory = $true)][ValidateSet('install', 'uninstall')][string]$Action)

$ErrorActionPreference = 'Stop'
$PrinterName = 'Goni PDF'
$PortName    = '\\.\pipe\GoniPdfPrinter'
$DriverName  = 'Microsoft Print To PDF'

try {
    if ($Action -eq 'install') {
        if (-not (Get-PrinterDriver -Name $DriverName -ErrorAction SilentlyContinue)) {
            Write-Output "드라이버 '$DriverName' 활성화"
            Enable-WindowsOptionalFeature -Online -FeatureName 'Printing-PrintToPDFServices-Features' -All -NoRestart | Out-Null
            if (-not (Get-PrinterDriver -Name $DriverName -ErrorAction SilentlyContinue)) { Add-PrinterDriver -Name $DriverName }
        }

        if (-not (Get-PrinterPort -Name $PortName -ErrorAction SilentlyContinue)) {
            Write-Output "포트 추가: $PortName"
            try { Add-PrinterPort -Name $PortName }
            catch {
                Write-Output "Add-PrinterPort 실패, 레지스트리로 등록: $($_.Exception.Message)"
                New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Ports' -Name $PortName -Value '' -PropertyType String -Force | Out-Null
                Restart-Service Spooler -Force
                Start-Sleep -Seconds 2
            }
        }

        if (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue) { Remove-Printer -Name $PrinterName }
        Write-Output "프린터 추가: $PrinterName"
        Add-Printer -Name $PrinterName -DriverName $DriverName -PortName $PortName
        Set-Printer -Name $PrinterName -Comment '인쇄 시 암호/권한이 설정된 PDF로 저장합니다.'
    }
    else {
        if (Get-Printer -Name $PrinterName -ErrorAction SilentlyContinue) {
            Write-Output "프린터 제거: $PrinterName"
            Remove-Printer -Name $PrinterName
            Start-Sleep -Seconds 1
        }
        if (Get-PrinterPort -Name $PortName -ErrorAction SilentlyContinue) {
            Write-Output "포트 제거: $PortName"
            try { Remove-PrinterPort -Name $PortName } catch { Write-Output "포트 제거 실패(무시): $($_.Exception.Message)" }
        }
    }
    Write-Output '완료'
    exit 0
}
catch {
    Write-Output "오류: $($_.Exception.Message)"
    exit 1
}
