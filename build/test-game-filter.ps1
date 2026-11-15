param([string]$AssetDirectory = (Join-Path $PSScriptRoot '../FOG.Engine/payloads/flowseal-1.10.4'))
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $AssetDirectory).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) ('fog-game-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    foreach ($case in @('empty', 'comments', 'default-route', 'missing-exclusion', 'tamper')) {
        $fixture = Join-Path $temp $case
        Copy-Item -LiteralPath $source -Destination $fixture -Recurse
        $name = if ($case -eq 'missing-exclusion') { 'game-exclude.txt' } elseif ($case -eq 'tamper') { 'ACTIVE_GAME_UDP.bin' } else { 'game-ipset.txt' }
        $value = switch ($case) {
            'empty' { '' }
            'comments' { '# no networks' }
            'default-route' { '0.0.0.0/0' }
            'missing-exclusion' { '127.0.0.0/8' }
            'tamper' { 'corrupt' }
        }
        Set-Content -LiteralPath (Join-Path $fixture $name) -Value $value
        if ($case -ne 'tamper') {
            $manifestPath = Join-Path $fixture 'manifest.json'
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $manifest.sha256.$name = (Get-FileHash -LiteralPath (Join-Path $fixture $name) -Algorithm SHA256).Hash
            $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
        }
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'verify-game-assets.ps1') -AssetDirectory $fixture }
        catch { $rejected = $true }
        if (!$rejected) { throw "Unsafe game asset accepted: $case" }
        Write-Host "PASS game asset rejection: $case"
    }
}
finally {
    $resolved = [IO.Path]::GetFullPath($temp)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase) -or
        !([IO.Path]::GetFileName($resolved)).StartsWith('fog-game-test-')) { throw 'Unsafe cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
