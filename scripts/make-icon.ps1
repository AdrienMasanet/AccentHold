# Generates src/AccentHold/Assets/icon.ico: white "é" on a rounded accent-blue tile (DIB entries + PNG at 256px).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\AccentHold\Assets\icon.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-Tile([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $r = [Math]::Max(2, [int]($s * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($s - 2 * $r - 1, 0, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($s - 2 * $r - 1, $s - 2 * $r - 1, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc(0, $s - 2 * $r - 1, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x00, 0x67, 0xC0))
    $g.FillPath($bg, $path)
    $font = New-Object System.Drawing.Font('Segoe UI Semibold', [float]($s * 0.62), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString([char]0xE9, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, [float]($s * -0.02), $s, $s)), $fmt)
    $g.Dispose()
    return $bmp
}

# Classic BGRA DIB entry: BITMAPINFOHEADER + bottom-up pixels + empty AND mask.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([uint32]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]0); $w.Write([uint32]($s * $s * 4)); $w.Write([int]0); $w.Write([int]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) { $w.Write([int]$bmp.GetPixel($x, $y).ToArgb()) }
    }
    $maskRow = [Math]::Ceiling($s / 32.0) * 4
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Dispose()
    return $ms.ToArray()
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

$entries = foreach ($s in 16, 24, 32, 48, 64, 128, 256) {
    $bmp = New-Tile $s
    $bytes = if ($s -eq 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    $bmp.Dispose()
    ,@($s, $bytes)
}

$stream = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter($stream)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $s = $e[0]; $bytes = $e[1]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($e in $entries) { $w.Write([byte[]]$e[1]) }
$w.Dispose(); $stream.Dispose()
Write-Host "Wrote $out"
