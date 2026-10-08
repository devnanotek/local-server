# DEVNANOTEK uygulama simgesi (devnanotek.ico) — devnanotek.net favicon'u ile birebir:
# koyu yuvarlak kare (#232425) + altın "D" (#FAA41A). WPF ile vektörden çizilir (her boyutta keskin).
# Kullanım: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\src\DevNanotek\Assets\devnanotek.ico'))

Add-Type -AssemblyName PresentationCore, WindowsBase
$ErrorActionPreference = 'Stop'

$dPath = 'M16 14h17c11 0 18 7 18 18S44 50 33 50H16V14zm10 9v18h6c6 0 9-3 9-9s-3-9-9-9h-6z'
$bg = [System.Windows.Media.Color]::FromRgb(0x23, 0x24, 0x25)
$gold = [System.Windows.Media.Color]::FromRgb(0xFA, 0xA4, 0x1A)

function Render([int]$size) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $s = $size / 64.0
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform($s, $s)))
    $rect = New-Object System.Windows.Rect(0, 0, 64, 64)
    $dc.DrawRoundedRectangle((New-Object System.Windows.Media.SolidColorBrush($bg)), $null, $rect, 16, 16)
    $geo = [System.Windows.Media.Geometry]::Parse($dPath)
    $dc.DrawGeometry((New-Object System.Windows.Media.SolidColorBrush($gold)), $null, $geo)
    $dc.Pop(); $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @(); foreach ($sz in $sizes) { $images += ,(Render $sz) }

$full = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force (Split-Path $full) | Out-Null
$fs = [IO.File]::Create($full); $bw = New-Object IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $b = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([int]$images[$i].Length); $bw.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close(); $fs.Close()

# Önizleme ve uygulama içi logo PNG'si
[IO.File]::WriteAllBytes([IO.Path]::ChangeExtension($full, '.png'), (Render 256))
Get-Item $full | Select-Object FullName, Length
