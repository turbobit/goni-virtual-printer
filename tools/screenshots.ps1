# README용 설정 창 스크린샷 생성 (docs/images/*.png)
#   빌드된 bin\GoniPdfPrinter.exe 의 SecurityForm 을 화면 밖에 띄워 그대로 캡처한다.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root 'docs\images'
New-Item -ItemType Directory -Force $outDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace Native -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[void][Native.Dpi]::SetProcessDPIAware()
[System.Windows.Forms.Application]::EnableVisualStyles()
$asm = [Reflection.Assembly]::LoadFrom((Join-Path $root 'bin\GoniPdfPrinter.exe'))
$settingsType = $asm.GetType('GoniPdfPrinter.AppSettings')
$formType = $asm.GetType('GoniPdfPrinter.SecurityForm')
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'

function New-Settings([hashtable]$values) {
    $cfg = [Activator]::CreateInstance($settingsType, $true)
    $cfg.GetType().GetField('OutputFolder').SetValue($cfg, 'C:\Users\사용자\Documents')
    foreach ($k in $values.Keys) {
        $f = $settingsType.GetField($k)
        $v = $values[$k]
        if ($f.FieldType.IsEnum) { $v = [Enum]::ToObject($f.FieldType, $v) }
        $f.SetValue($cfg, $v)
    }
    $cfg
}

function Save-Form($cfg, [bool]$jobMode, [string]$name, [string]$file) {
    $form = [Activator]::CreateInstance($formType, $flags, $null, @($cfg, $jobMode, $name), $null)
    $form.StartPosition = 'Manual'
    $form.Location = New-Object System.Drawing.Point(-4000, 0)
    $form.ShowInTaskbar = $false
    $form.Show()
    1..3 | ForEach-Object { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 100 }
    $bmp = New-Object System.Drawing.Bitmap ([int]$form.Width), ([int]$form.Height)
    $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height))
    $form.Close()
    $path = Join-Path $outDir $file
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "생성: $path"
}

# 1) 첫 인쇄 시 기본 화면
Save-Form (New-Settings @{}) $true '분기 보고서' 'dialog-default.png'

# 2) 열기 암호 + 권한 제한을 지정한 예시
Save-Form (New-Settings @{
    RequireOpenPassword = $true; OpenPassword = 'open1234'
    RequirePermissionPassword = $true; PermissionPassword = 'owner5678'
    Print = 1; Edit = 3; AllowCopy = $false
}) $true '계약서_최종' 'dialog-configured.png'

# 3) 트레이 메뉴의 기본 보안 설정 창
Save-Form (New-Settings @{
    RequirePermissionPassword = $true; PermissionPassword = 'owner5678'
    Print = 2; Edit = 2; AllowCopy = $true; SkipDialog = $true
}) $false $null 'dialog-settings.png'
