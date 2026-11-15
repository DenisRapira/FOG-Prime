param([Parameter(Mandatory)][string]$AssetDirectory)
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $AssetDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $directory 'manifest.json') -Raw | ConvertFrom-Json
$expected = @('ACTIVE_GAME_UDP.bin', 'tls_clienthello_www_sferum_ru.bin', 'game-ipset.txt', 'game-exclude.txt')
if ($manifest.commit -ne '176f56dc3f11ce5915c26abcdde903f93d835b64' -or
    @($manifest.sha256.PSObject.Properties).Count -ne $expected.Count) { throw 'Unexpected game filter provenance.' }
foreach ($name in $expected) {
    $file = Join-Path $directory $name
    if (!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -gt 1MB -or
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $manifest.sha256.$name) {
        throw "Game filter asset integrity check failed: $name"
    }
}
foreach ($name in @('game-ipset.txt', 'game-exclude.txt')) {
    $entries = @(Get-Content -LiteralPath (Join-Path $directory $name) | ForEach-Object { ($_ -split '#', 2)[0].Trim() } | Where-Object { $_ })
    if (!$entries.Count) { throw "Game filter set must not be empty: $name" }
    foreach ($entry in $entries) {
        $parts = $entry.Split('/')
        $ip = $null
        if ($parts.Count -gt 2 -or ![Net.IPAddress]::TryParse($parts[0], [ref]$ip)) { throw "Invalid game filter network: $entry" }
        $maximum = if ($ip.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork) { 32 } else { 128 }
        if ($parts.Count -eq 2) {
            $prefix = 0
            if (![int]::TryParse($parts[1], [ref]$prefix) -or $prefix -le 0 -or $prefix -gt $maximum) { throw "Unbounded or invalid game filter network: $entry" }
        }
    }
    if ($name -eq 'game-exclude.txt') {
        foreach ($required in @('0.0.0.0/8','10.0.0.0/8','127.0.0.0/8','172.16.0.0/12','192.168.0.0/16','169.254.0.0/16','224.0.0.0/4','100.64.0.0/10','::1','fc00::/7','fe80::/10')) {
            if ($required -notin $entries) { throw "Missing local-network exclusion: $required" }
        }
    }
}
Write-Host 'PASS pinned game assets, bounded nonempty IP set and local-network exclusions.'
