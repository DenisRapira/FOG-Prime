[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PackageDirectory).Path
$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in @('FOG Prime.exe', 'app/FOG.Agent.exe', 'app/profiles.json', 'app/runtime.manifest.json',
    'app/payload-provenance.json', 'app/game-filter-provenance.json', 'app/build-info.json', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    [void]$expected.Add($name)
}
foreach ($name in @('ENGINE-NOTICES.md', 'UPSTREAM.md', 'Engine-MIT-LICENSE.txt', 'WinDivert-LICENSE.txt',
    'Cygwin-NOTICE.txt', 'DotNet-LICENSE.txt', 'DotNet-ThirdPartyNotices.txt', 'WebView2-LICENSE.txt', 'WebView2-NOTICE.txt')) {
    [void]$expected.Add("notices/$name")
}
foreach ($name in @('FOG.Engine.exe', 'cygwin1.dll', 'WinDivert.dll', 'WinDivert64.sys')) {
    [void]$expected.Add("app/runtime/$name")
}
$profiles = Get-Content -LiteralPath (Join-Path $root 'app/profiles.json') -Raw | ConvertFrom-Json
foreach ($profile in $profiles) {
    foreach ($argument in $profile.arguments) {
        if ($argument -match '\$\{runtime\}[\\/]+([A-Za-z0-9_.-]+)$') {
            [void]$expected.Add("app/runtime/$($Matches[1])")
        } elseif ($argument.Contains('${runtime}')) {
            throw 'Runtime profile paths must name a flat file.'
        }
    }
}
$actual = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
foreach ($item in $actual) {
    $relative = [IO.Path]::GetRelativePath($root, $item.FullName).Replace('\', '/')
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Package contains a reparse point: $relative" }
    if ($item.PSIsContainer) {
        if ($relative -notin @('app', 'app/runtime', 'notices')) { throw "Unexpected package directory: $relative" }
    } elseif (-not $expected.Remove($relative)) {
        throw "Unexpected package file: $relative"
    } elseif ($item.Length -eq 0) {
        throw "Empty package file: $relative"
    }
}
if ($expected.Count) { throw "Missing package files: $([string]::Join(', ', $expected))" }
$build = Get-Content -LiteralPath (Join-Path $root 'app/build-info.json') -Raw | ConvertFrom-Json
$hashedFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in $actual | Where-Object { -not $_.PSIsContainer }) {
    $relative = [IO.Path]::GetRelativePath($root, $item.FullName).Replace('\', '/')
    if ($relative -eq 'app/build-info.json') { continue }
    [void]$hashedFiles.Add($relative)
    $entry = $build.files.PSObject.Properties[$relative]
    if (-not $entry -or $entry.Value -notmatch '^[A-Fa-f0-9]{64}$' -or
        (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash -ine $entry.Value) {
        throw "Build-info hash is missing or mismatched: $relative"
    }
}
foreach ($entry in $build.files.PSObject.Properties) {
    if (-not $hashedFiles.Remove($entry.Name)) { throw "Unexpected build-info file path: $($entry.Name)" }
}
if ($hashedFiles.Count) { throw 'Build-info does not cover every package file except itself.' }
if ($build.runtimeDriver.file -cne 'app/runtime/WinDivert64.sys' -or
    $build.runtimeDriver.sha256 -ine $build.files.'app/runtime/WinDivert64.sys') {
    throw 'Build-info driver provenance must use the package-root-relative app/runtime path and matching hash.'
}
foreach ($entry in $build.signing.PSObject.Properties) {
    if ($entry.Name -notin @('FOG Prime.exe', 'app/FOG.Agent.exe', 'app/runtime/FOG.Engine.exe')) {
        throw "Unexpected signing metadata path: $($entry.Name)"
    }
}
Write-Host "PASS: minimal package layout ($(@($actual | Where-Object { -not $_.PSIsContainer }).Count) files); no audit reports, screenshots, symbols, source or development tools."
