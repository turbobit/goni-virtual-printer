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
# 캡처용 창이 포커스를 빼앗아 사용자 입력이 들어가지 않도록
$formType.GetField('SuppressActivation', [Reflection.BindingFlags]'Static,NonPublic').SetValue($null, $true)

function New-Settings([hashtable]$values) {
    $cfg = [Activator]::CreateInstance($settingsType, $true)
    $settingsType.GetField('LastFolder').SetValue($cfg, 'C:\Users\사용자\Documents')
    $settingsType.GetField('FixedFolder').SetValue($cfg, 'C:\Users\사용자\Documents\PDF\{yyyy}-{MM}')
    foreach ($k in $values.Keys) {
        $f = $settingsType.GetField($k)
        $v = $values[$k]
        if ($f.FieldType.IsEnum) { $v = [Enum]::ToObject($f.FieldType, $v) }
        $f.SetValue($cfg, $v)
    }
    $cfg
}

function Save-Form($cfg, [bool]$jobMode, [string]$title, [string]$file, [int]$tab = 0) {
    $form = [Activator]::CreateInstance($formType, $flags, $null, @($cfg, $jobMode, $title), $null)
    $form.StartPosition = 'Manual'
    $form.Location = New-Object System.Drawing.Point(-4000, 0)
    $form.ShowInTaskbar = $false
    $form.Show()
    $tabs = $form.Controls | Where-Object { $_ -is [System.Windows.Forms.TabControl] } | Select-Object -First 1
    $tabs.SelectedIndex = $tab
    1..3 | ForEach-Object { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 100 }
    $bmp = New-Object System.Drawing.Bitmap ([int]$form.Width), ([int]$form.Height)
    $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height))
    $form.Close()
    $path = Join-Path $outDir $file
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "생성: $path"
}

# 1) 첫 인쇄 시 기본 화면 (보안 탭)
Save-Form (New-Settings @{}) $true '분기 보고서.docx - Word' 'dialog-default.png'

# 2) 열기 암호 + 권한 제한을 지정한 예시 (보안 탭)
Save-Form (New-Settings @{
    RequireOpenPassword = $true; OpenPassword = 'open1234'
    RequirePermissionPassword = $true; PermissionPassword = 'owner5678'
    Print = 1; Edit = 3; AllowCopy = $false
}) $true '계약서_최종.hwp - 한글' 'dialog-configured.png'

# 3) 저장 설정 탭: 지정 폴더 + 날짜_제목 형식
Save-Form (New-Settings @{
    RequirePermissionPassword = $true; PermissionPassword = 'owner5678'
    FolderMode = 1; FileNamePattern = '{date}_{title}'
}) $true '분기 보고서.docx - Word' 'dialog-save.png' 1

# 4) 트레이 메뉴의 기본 설정 창 (기타 옵션 탭)
Save-Form (New-Settings @{
    RequirePermissionPassword = $true; PermissionPassword = 'owner5678'
    Print = 2; Edit = 2; AllowCopy = $true; SkipDialog = $true
}) $false $null 'dialog-settings.png' 2
