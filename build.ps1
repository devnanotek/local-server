# DevNanotek derleme betiği
#   powershell -ExecutionPolicy Bypass -File build.ps1
# Çıktı: dist\DevNanotek.exe  (tek dosya, .NET Framework 4.8 — Windows 10/11'de ek kurulum gerekmez)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'src\DevNanotek\DevNanotek.csproj'
$dist = Join-Path $root 'dist'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) {
    Write-Host '.NET SDK bulunamadı. Kurmak için:  winget install Microsoft.DotNet.SDK.8' -ForegroundColor Red
    exit 1
}

if (-not (Test-Path (Join-Path $root 'src\DevNanotek\Assets\devnanotek.ico'))) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\make-icon.ps1')
}

$tmp = Join-Path $root 'build\release'
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
& $dotnet build $proj -c Release -o $tmp --nologo
if ($LASTEXITCODE -ne 0) { Write-Host 'Derleme başarısız.' -ForegroundColor Red; exit 1 }

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item (Join-Path $tmp 'DevNanotek.exe') $dist -Force
if (Test-Path (Join-Path $tmp 'DevNanotek.exe.config')) { Copy-Item (Join-Path $tmp 'DevNanotek.exe.config') $dist -Force }
Copy-Item (Join-Path $root 'src\DevNanotek\Resources\Kilavuz.html') $dist -Force
Copy-Item (Join-Path $root 'src\DevNanotek\Resources\Guide.html') $dist -Force
Copy-Item (Join-Path $root 'src\DevNanotek\Assets\Fonts\OFL-BebasNeue.txt') $dist -Force
# Linux sürümü (satır sonları LF olarak kalır)
New-Item -ItemType Directory -Force -Path (Join-Path $dist 'linux') | Out-Null
Copy-Item (Join-Path $root 'linux\devnanotek.sh') (Join-Path $dist 'linux') -Force
Copy-Item (Join-Path $root 'linux\KURULUM.md') (Join-Path $dist 'linux') -Force

Write-Host ''
Write-Host "Hazır: $dist\DevNanotek.exe" -ForegroundColor Green
Get-ChildItem $dist | Select-Object Name, Length | Format-Table -AutoSize
