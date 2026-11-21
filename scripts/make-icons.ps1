# Draws the Vaka mark (branding/vaka.svg, a small panel) as the Windows icon and installer logo.
# Run after changing the mark; the outputs are committed:
#   src/Vaka/vaka.ico          exe icon, tray icon (embedded), installer icon
#   installer/Bundle/logo.png  setup window logo
# The shapes below mirror branding/vaka.svg (viewBox 0 0 32 32); keep them in step.
param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function Get-MarkBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $c = { param($hex, $a = 255) [System.Drawing.Color]::FromArgb($a, [Convert]::ToInt32($hex.Substring(0, 2), 16), [Convert]::ToInt32($hex.Substring(2, 2), 16), [Convert]::ToInt32($hex.Substring(4, 2), 16)) }

    # Small sizes are hand-hinted on the pixel grid: a scaled 1 px outline would fall between pixels and blur.
    # Frame = outer box (x, y, w, h) with a 1 px outline and cut corners; content = left edge and width of the
    # title line and bar row; bars split the content width around a 2 px gap.
    $hinted = @{
        16 = @{ Frame = 1, 3, 14, 10; ContentX = 4; ContentW = 8;  TitleY = 6; TitleH = 1; BarY = 8;  BarH = 2 }
        20 = @{ Frame = 1, 4, 18, 12; ContentX = 4; ContentW = 12; TitleY = 7; TitleH = 2; BarY = 11; BarH = 2 }
        24 = @{ Frame = 1, 4, 22, 16; ContentX = 5; ContentW = 14; TitleY = 8; TitleH = 2; BarY = 13; BarH = 2 }
    }
    if ($hinted.ContainsKey($size)) {
        $h = $hinted[$size]
        $fx, $fy, $fw, $fh = $h.Frame
        $g.SmoothingMode = 'None'
        $blue = New-Object System.Drawing.SolidBrush (& $c '4aa3ff')
        $g.FillRectangle((New-Object System.Drawing.SolidBrush (& $c '17212d')), $fx + 1, $fy + 1, $fw - 2, $fh - 2)
        $g.FillRectangle($blue, $fx + 1, $fy, $fw - 2, 1); $g.FillRectangle($blue, $fx + 1, $fy + $fh - 1, $fw - 2, 1)
        $g.FillRectangle($blue, $fx, $fy + 1, 1, $fh - 2); $g.FillRectangle($blue, $fx + $fw - 1, $fy + 1, 1, $fh - 2)
        $g.FillRectangle((New-Object System.Drawing.SolidBrush (& $c 'e6edf3' 153)), $h.ContentX, $h.TitleY, $h.ContentW, $h.TitleH)
        $barW = ($h.ContentW - 2) / 2
        $g.FillRectangle($blue, $h.ContentX, $h.BarY, $barW, $h.BarH)
        $g.FillRectangle((New-Object System.Drawing.SolidBrush (& $c '5ad17a')), $h.ContentX + $barW + 2, $h.BarY, $barW, $h.BarH)
        $g.Dispose()
        return $bmp
    }

    $s = $size / 32.0
    $g.ScaleTransform($s, $s)

    # Frame: rect 2,6 28x20 rx3, fill #17212d, stroke #4aa3ff 2
    $frame = New-RoundRect 2 6 28 20 3
    $g.FillPath((New-Object System.Drawing.SolidBrush (& $c '17212d')), $frame)
    $g.DrawPath((New-Object System.Drawing.Pen (& $c '4aa3ff'), 2), $frame)
    # Bars: CPU (blue), GPU (green), and a title line (#e6edf3 at 60%)
    $g.FillPath((New-Object System.Drawing.SolidBrush (& $c '4aa3ff')), (New-RoundRect 7 17 8 3 1.5))
    $g.FillPath((New-Object System.Drawing.SolidBrush (& $c '5ad17a')), (New-RoundRect 17 17 8 3 1.5))
    $g.FillPath((New-Object System.Drawing.SolidBrush (& $c 'e6edf3' 153)), (New-RoundRect 7 11 18 3 1.5))
    $g.Dispose()
    $bmp
}

function Get-MarkPng([int]$size) {
    $bmp = Get-MarkBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# Classic icon entry: BITMAPINFOHEADER (double height), bottom-up BGRA rows, then an all-zero AND mask.
function Get-MarkDib([int]$size) {
    $bmp = Get-MarkBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32](2 * $size))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($size * $size * 4)); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $w.Write([byte]$p.B); $w.Write([byte]$p.G); $w.Write([byte]$p.R); $w.Write([byte]$p.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    $bmp.Dispose()
    , $ms.ToArray()
}

# .ico: classic bitmap entries up to 64 px (read by every Windows and .NET API), PNG at 256 px.
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = $sizes | ForEach-Object { if ($_ -ge 256) { , (Get-MarkPng $_) } else { , (Get-MarkDib $_) } }
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
$ico = Join-Path $Root 'src\Vaka\vaka.ico'
[System.IO.File]::WriteAllBytes($ico, $out.ToArray())

$logo = Join-Path $Root 'installer\Bundle\logo.png'
[System.IO.File]::WriteAllBytes($logo, (Get-MarkPng 64))
Write-Host "Wrote $ico ($($sizes -join ', ') px) and $logo"
