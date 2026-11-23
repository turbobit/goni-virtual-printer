# Goni PDF 가상 프린터 빌드 (.NET Framework 4.x 내장 C# 컴파일러 사용 - 추가 설치 불필요)
param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out  = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '.NET Framework 4.x 컴파일러(csc.exe)를 찾을 수 없습니다.' }

# 아이콘 생성 (PNG 내장 ICO)
$ico = Join-Path $out 'app.ico'
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
$paper = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 245, 245))
$g.FillRectangle($paper, 40, 16, 176, 224)
$g.DrawRectangle((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 120, 120, 120)), 4), 40, 16, 176, 224)
$red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 200, 30, 40))
$g.FillRectangle($red, 16, 120, 224, 92)
$font = New-Object System.Drawing.Font('Arial', 54, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
$g.DrawString('PDF', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 16, 120, 224, 92), $sf)
$g.Dispose()
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()
$fs = [System.IO.File]::Create($ico)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)
$bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$png.Length); $bw.Write([uint32]22)
$bw.Write($png); $bw.Close()

$srcs = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$exe = Join-Path $out 'GoniPdfPrinter.exe'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    /out:$exe /win32icon:$ico `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll `
    $srcs
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (exit $LASTEXITCODE)" }
Write-Host "빌드 완료: $exe"

# 설치 프로그램 (NSIS 3 필요: https://nsis.sourceforge.io)
$makensis = @(
    (Get-Command makensis.exe -ErrorAction SilentlyContinue | ForEach-Object Source),
    "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
    "$env:ProgramFiles\NSIS\makensis.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if ($makensis) {
    & $makensis /V2 "/DVERSION=$Version" (Join-Path $root 'installer\GoniPdfPrinter.nsi')
    if ($LASTEXITCODE -ne 0) { throw "설치 프로그램 빌드 실패 (exit $LASTEXITCODE)" }
    Write-Host "설치 프로그램: $(Join-Path $out "GoniPdfPrinterSetup-$Version.exe")"
} else {
    Write-Host 'NSIS(makensis.exe)가 없어 설치 프로그램은 건너뜁니다.' -ForegroundColor Yellow
}
