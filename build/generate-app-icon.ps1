[CmdletBinding()]
param([string]$PreviewPath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'FOG.WebView2/Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$frames = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size * 4, $size * 4)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $ground = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#101212'))
    $lime = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#7fee64'))
    $edge = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#303630'), 2)
    $small = $null
    $scaled = $null
    $stream = [IO.MemoryStream]::new()
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform(($size * 4 / 256), ($size * 4 / 256))
        $tile = [Drawing.PointF[]]@(
            [Drawing.PointF]::new(28, 6), [Drawing.PointF]::new(228, 6),
            [Drawing.PointF]::new(250, 28), [Drawing.PointF]::new(250, 228),
            [Drawing.PointF]::new(228, 250), [Drawing.PointF]::new(28, 250),
            [Drawing.PointF]::new(6, 228), [Drawing.PointF]::new(6, 28))
        $mark = [Drawing.PointF[]]@(
            [Drawing.PointF]::new(74, 58), [Drawing.PointF]::new(194, 58),
            [Drawing.PointF]::new(176, 90), [Drawing.PointF]::new(108, 90),
            [Drawing.PointF]::new(108, 112), [Drawing.PointF]::new(172, 112),
            [Drawing.PointF]::new(154, 144), [Drawing.PointF]::new(108, 144),
            [Drawing.PointF]::new(108, 198), [Drawing.PointF]::new(74, 198))
        $graphics.FillPolygon($ground, $tile)
        $graphics.DrawPolygon($edge, $tile)
        $graphics.FillPolygon($lime, $mark)
        $small = [Drawing.Bitmap]::new($size, $size)
        $scaled = [Drawing.Graphics]::FromImage($small)
        $scaled.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scaled.DrawImage($bitmap, 0, 0, $size, $size)
        $small.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($stream.ToArray())
        if ($size -eq 256 -and $PreviewPath) {
            $small.Save([IO.Path]::GetFullPath($PreviewPath), [Drawing.Imaging.ImageFormat]::Png)
        }
    }
    finally {
        $stream.Dispose()
        if ($scaled) { $scaled.Dispose() }
        if ($small) { $small.Dispose() }
        $graphics.Dispose(); $bitmap.Dispose(); $ground.Dispose(); $lime.Dispose(); $edge.Dispose()
    }
}
$file = [IO.File]::Create((Join-Path $assets 'fog-prime.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
}
finally { $writer.Dispose() }
Write-Host 'Generated embedded application icon: nine sizes, 16-256 px.'
