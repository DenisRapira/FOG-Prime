param(
    [switch]$SkipBuild,
    [string]$PackageDirectory
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if (-not $SkipBuild) {
    dotnet build (Join-Path $root "FOG.Prime.sln") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Solution build failed." }
}

$profilesPath = Join-Path $root "FOG.Agent\profiles.json"
$profiles = Get-Content $profilesPath -Raw | ConvertFrom-Json
if (-not $profiles -or @($profiles).Count -lt 2) { throw "At least two automatic profiles are required." }

$ids = @($profiles | ForEach-Object id)
if (($ids | Select-Object -Unique).Count -ne $ids.Count) { throw "Profile IDs must be unique." }

foreach ($profile in $profiles) {
    if ($profile.executable -ne "FOG.Engine.exe") { throw "Profile $($profile.id) uses an untrusted executable." }
    if (-not $profile.arguments -or @($profile.arguments).Count -eq 0) { throw "Profile $($profile.id) has no arguments." }
    if (-not (@($profile.arguments) -contains "--wf-udp=443,19294-19344,50000-65535")) {
        throw "Profile $($profile.id) does not capture the complete Discord voice UDP range."
    }
    if (-not (@($profile.arguments) -contains "--filter-udp=19294-19344,50000-65535")) {
        throw "Profile $($profile.id) does not filter the complete Discord voice UDP range."
    }
    if (-not (@($profile.arguments) -contains "--filter-l7=discord,stun")) {
        throw "Profile $($profile.id) is missing Discord voice protocol filtering."
    }
    $joined = $profile.arguments -join " "
    if ($joined -match '(?i)(cmd\.exe|powershell|\.bat\b|service\.bat|winws\.exe)') {
        throw "Profile $($profile.id) contains a forbidden legacy or shell reference."
    }
}

$applicationSources = Get-ChildItem (Join-Path $root "FOG.Agent"), (Join-Path $root "FOG.Protocol"), (Join-Path $root "FOG.WebView2") -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|release|publish)[\\/]' }
$forbidden = $applicationSources | Select-String -Pattern 'service\.bat|general \(|winws\.exe|cmd\.exe' -CaseSensitive:$false
if ($forbidden) {
    $paths = ($forbidden.Path | Select-Object -Unique) -join ', '
    throw "Application source contains forbidden legacy launcher references: $paths"
}

if ($PackageDirectory) {
    $package = (Resolve-Path $PackageDirectory).Path
    $required = @(
        "FOG Prime.exe",
        "FOG.Agent.exe",
        "profiles.json",
        "runtime.manifest.json",
        "runtime\FOG.Engine.exe",
        "runtime\WinDivert.dll",
        "runtime\WinDivert64.sys",
        "THIRD_PARTY_NOTICES.md",
        "UPSTREAM.md"
    )
    foreach ($relative in $required) {
        if (-not (Test-Path (Join-Path $package $relative))) { throw "Package file is missing: $relative" }
    }

    $manifest = Get-Content (Join-Path $package "runtime.manifest.json") -Raw | ConvertFrom-Json
    $runtime = Join-Path $package "runtime"
    foreach ($entry in $manifest.sha256.PSObject.Properties) {
        $path = Join-Path $runtime $entry.Name
        if (-not (Test-Path $path)) { throw "Manifest file is missing: $($entry.Name)" }
        $actual = (Get-FileHash $path -Algorithm SHA256).Hash
        if ($actual -ne $entry.Value) { throw "Runtime hash mismatch: $($entry.Name)" }
    }
}

Write-Host "FOG Prime repository verification passed."
