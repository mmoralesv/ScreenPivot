# Generates the app icon and the MSIX logo PNGs into src/ScreenPivot.App/Assets,
# and the Store listing box art into docs/store/art.
# Needs Windows PowerShell 5.1 (System.Drawing): powershell -ExecutionPolicy Bypass -File tools\generate-assets.ps1
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\ScreenPivot.App\Assets'),
    [string]$StoreArtDir = (Join-Path $PSScriptRoot '..\docs\store\art')
)

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

$accent = [System.Drawing.Color]::FromArgb(255, 0, 120, 212)
$alert = [System.Drawing.Color]::FromArgb(255, 247, 99, 12)

# Glyph: a large "remote" screen with a small host notification badge in front of it.
function Draw-Glyph([System.Drawing.Graphics]$g, [float]$x, [float]$y, [float]$size) {
    $g.SmoothingMode = 'AntiAlias'
    $u = $size / 16
    $screen = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = New-Object System.Drawing.RectangleF(($x + 1 * $u), ($y + 2 * $u), (14 * $u), (10 * $u))
    $d = 2 * $u
    $screen.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $screen.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $screen.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $screen.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $screen.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $accent), $screen)
    $g.FillRectangle([System.Drawing.Brushes]::White, ($x + 2.5 * $u), ($y + 3.5 * $u), (11 * $u), (7 * $u))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $accent), ($x + 6 * $u), ($y + 12 * $u), (4 * $u), (1.5 * $u))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $accent), ($x + 4.5 * $u), ($y + 13.5 * $u), (7 * $u), (1 * $u))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $alert), ($x + 9.5 * $u), ($y + 0.5 * $u), (6 * $u), (6 * $u))
    $g.FillEllipse([System.Drawing.Brushes]::White, ($x + 11.75 * $u), ($y + 2.75 * $u), (1.5 * $u), (1.5 * $u))
}

function New-Png([string]$name, [int]$w, [int]$h, [double]$glyphRatio) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $size = [Math]::Min($w, $h) * $glyphRatio
    Draw-Glyph $g (($w - $size) / 2) (($h - $size) / 2) $size
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

New-Png 'Square44x44Logo.png' 44 44 0.9
New-Png 'Square44x44Logo.targetsize-24_altform-unplated.png' 24 24 1.0
New-Png 'Square150x150Logo.png' 150 150 0.6
New-Png 'Wide310x150Logo.png' 310 150 0.6
New-Png 'StoreLogo.png' 50 50 0.9
New-Png 'SplashScreen.png' 620 300 0.5

# Multi-size .ico (PNG-compressed entries) for the exe, the window and the tray.
$sizes = 16, 20, 24, 32, 48, 64, 256
$images = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Glyph $g 0 0 $s
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'ScreenPivot.ico'), $out.ToArray())
Write-Host "Assets generados en $OutDir"

# Store listing "1:1 box art": opaque light background, glyph centered at ~55%.
New-Item -ItemType Directory -Force $StoreArtDir | Out-Null
foreach ($s in 2160, 1080, 300) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 235, 243, 252))
    $size = $s * 0.55
    Draw-Glyph $g (($s - $size) / 2) (($s - $size) / 2) $size
    $g.Dispose()
    $bmp.Save((Join-Path $StoreArtDir "BoxArt_${s}x${s}.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
Write-Host "Arte de la Store generado en $StoreArtDir"
