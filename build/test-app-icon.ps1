[CmdletBinding()]
param([string]$ExecutablePath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$bytes = [IO.File]::ReadAllBytes((Join-Path $root 'FOG.WebView2/Assets/fog-prime.ico'))
if ([BitConverter]::ToUInt16($bytes, 0) -ne 0 -or [BitConverter]::ToUInt16($bytes, 2) -ne 1) {
    throw 'Invalid application ICO header.'
}
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
if ([BitConverter]::ToUInt16($bytes, 4) -ne $sizes.Count) { throw 'Missing icon sizes.' }
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $entry = 6 + 16 * $i
    $size = if ($bytes[$entry] -eq 0) { 256 } else { [int]$bytes[$entry] }
    $length = [BitConverter]::ToUInt32($bytes, $entry + 8)
    $offset = [BitConverter]::ToUInt32($bytes, $entry + 12)
    if ($size -ne $sizes[$i] -or $offset + $length -gt $bytes.Length) { throw 'Invalid icon frame.' }
    $stream = [IO.MemoryStream]::new($bytes, [int]$offset, [int]$length)
    $frame = $null
    try {
        $frame = [Drawing.Bitmap]::new($stream)
        if ($frame.Width -ne $size -or $frame.Height -ne $size) { throw 'Icon frame has the wrong dimensions.' }
        $green = 0
        for ($y = 0; $y -lt $size; $y++) {
            for ($x = 0; $x -lt $size; $x++) {
                $pixel = $frame.GetPixel($x, $y)
                if ($pixel.A -gt 200 -and $pixel.G -gt 150 -and $pixel.R -lt 200 -and $pixel.B -lt 160) { $green++ }
            }
        }
        if ($green -lt $size * $size * 0.1) { throw 'Icon frame is blank or missing its mark.' }
    }
    finally { if ($frame) { $frame.Dispose() }; $stream.Dispose() }
}
if ($ExecutablePath) {
    $icon = [Drawing.Icon]::ExtractAssociatedIcon((Resolve-Path -LiteralPath $ExecutablePath).Path)
    if (!$icon) { throw 'Windows could not extract the executable icon.' }
    $bitmap = $null
    try {
        $bitmap = $icon.ToBitmap()
        $green = 0
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                if ($pixel.G -gt 150 -and $pixel.R -lt 200 -and $pixel.B -lt 160) { $green++ }
            }
        }
        if ($green -lt $bitmap.Width * $bitmap.Height * 0.1) { throw 'Executable is missing the FOG icon.' }
    }
    finally { if ($bitmap) { $bitmap.Dispose() }; $icon.Dispose() }
}
Write-Host 'PASS: all nine icon sizes render the FOG mark; requested executable icon verified.'
