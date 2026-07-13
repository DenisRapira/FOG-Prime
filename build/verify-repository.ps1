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

$engineSource = Get-Content (Join-Path $root "FOG.Engine\upstream\nfq\desync.c") -Raw
if ($engineSource -notmatch '(?s)IsDiscordIpDiscoveryRequest\(fake_data, fake_size\).*memcpy\(discord_fake \+ 4, dis->data_payload \+ 4, 4\)') {
    throw "FOG Engine is missing Discord Voice SSRC synchronization."
}

$ids = @($profiles | ForEach-Object id)
if (($ids | Select-Object -Unique).Count -ne $ids.Count) { throw "Profile IDs must be unique." }

foreach ($profile in $profiles) {
    if ($profile.executable -ne "FOG.Engine.exe") { throw "Profile $($profile.id) uses an untrusted executable." }
    if (-not $profile.arguments -or @($profile.arguments).Count -eq 0) { throw "Profile $($profile.id) has no arguments." }
    if (-not (@($profile.arguments) -contains "--wf-udp=443,19294-19344,50000-50100")) {
        throw "Profile $($profile.id) does not use the validated Discord voice UDP range."
    }
    if (-not (@($profile.arguments) -contains "--filter-udp=19294-19344,50000-50100")) {
        throw "Profile $($profile.id) does not filter the validated Discord voice UDP range."
    }
    if (-not (@($profile.arguments) -contains "--filter-l7=discord,stun")) {
        throw "Profile $($profile.id) is missing Discord voice protocol filtering."
    }
    $youtubeDomains = '--hostlist-domains=youtube.com,youtu.be,googlevideo.com,ytimg.com,youtubei.googleapis.com,ggpht.com'
    if (-not (@($profile.arguments) -contains $youtubeDomains) -or
        -not (@($profile.arguments) -contains '--dpi-desync=hostfakesplit') -or
        -not (@($profile.arguments) -contains '--dpi-desync-hostfakesplit-mod=host=www.google.com')) {
        throw "Profile $($profile.id) is missing the YouTube TLS strategy."
    }
    $voiceDecoy = '${runtime}\voice_decoy_quic.bin'
    $discordFakes = @($profile.arguments | Where-Object { $_ -like '--dpi-desync-fake-discord=*' })
    if ($discordFakes.Count -ne 2 -or
        $discordFakes[0] -ne '--dpi-desync-fake-discord=${runtime}\stun.bin' -or
        $discordFakes[1] -ne "--dpi-desync-fake-discord=$voiceDecoy" -or
        -not (@($profile.arguments) -contains "--dpi-desync-fake-stun=$voiceDecoy") -or
        -not (@($profile.arguments) -contains '--dpi-desync-repeats=3')) {
        throw "Profile $($profile.id) is missing the validated ordered voice handshake."
    }
    $joined = $profile.arguments -join " "
    if ($joined -match '(?i)(cmd\.exe|powershell|\.bat\b|service\.bat|winws\.exe)') {
        throw "Profile $($profile.id) contains a forbidden legacy or shell reference."
    }
}

$pipeServerSource = Get-Content (Join-Path $root "FOG.Agent\AgentPipeServer.cs") -Raw
$supervisorSource = Get-Content (Join-Path $root "FOG.Agent\EngineSupervisor.cs") -Raw
$healthSource = Get-Content (Join-Path $root "FOG.Agent\ConnectionHealthChecker.cs") -Raw
$windowSource = Get-Content (Join-Path $root "FOG.WebView2\MainWindow.xaml.cs") -Raw
if ($pipeServerSource -notmatch '"shutdown"\s*=>') { throw "Agent shutdown command is missing." }
if ($supervisorSource -notmatch 'RecoverIfNeededAsync') { throw "Automatic Engine recovery is missing." }
if ($supervisorSource -notmatch 'var fallback = catalog\.All\[0\]') { throw "Degraded profile fallback is missing." }
if ($healthSource -notmatch 'youtube\.com/generate_204') { throw "YouTube health probe is missing." }
if ($windowSource -notmatch 'Closing\s*\+=\s*OnClosing') { throw "Window close cleanup is missing." }

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
        "runtime\voice_decoy_quic.bin",
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
