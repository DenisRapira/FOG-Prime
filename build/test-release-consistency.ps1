[CmdletBinding()]
param([string]$ExpectedVersion)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$status = Get-Content -LiteralPath (Join-Path $root 'docs/release-status.json') -Raw | ConvertFrom-Json
if ($status.schemaVersion -ne 1) { throw 'Unsupported release status schema.' }
foreach ($version in @($status.stable.version, $status.candidate.version)) {
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
}
if ($status.candidate.status -notin @('unreleased', 'prerelease', 'released')) { throw 'Invalid candidate status.' }
if ($status.stable.url -cne "https://github.com/DenisRapira/FOG-Prime/releases/tag/v$($status.stable.version)") {
    throw 'Stable release URL does not match its version.'
}
if ($ExpectedVersion -and $ExpectedVersion -cne $status.candidate.version) {
    throw 'Requested version does not match the source candidate.'
}
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'FOG.WebView2/FOG.WebView2.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -cne $status.candidate.version) { throw 'Project version does not match candidate metadata.' }
$framework = [string]$project.Project.PropertyGroup.TargetFramework
if ($framework -notmatch '^net(\d+)\.0-windows$' -or $status.candidate.framework -cne ".NET $($Matches[1])") {
    throw 'Candidate framework does not match the application project.'
}
$html = Get-Content -LiteralPath (Join-Path $root 'FOG.WebView2/wwwroot/index.html') -Raw
if (-not $html.Contains("<title>FOG Prime $version</title>")) { throw 'Embedded UI version is stale.' }
if (-not (Test-Path -LiteralPath (Join-Path $root "docs/RELEASE-$version.md"))) { throw 'Candidate release notes are missing.' }
foreach ($relative in @('docs/index.html', 'docs/ru/index.html')) {
    $page = Get-Content -LiteralPath (Join-Path $root $relative) -Raw
    foreach ($entry in @(@('stable', $status.stable.version), @('candidate', $status.candidate.version))) {
        $marker = 'data-' + $entry[0] + '-version="' + $entry[1] + '"'
        if (-not $page.Contains($marker)) { throw "$relative is missing release marker $marker" }
    }
}
Write-Host "Release metadata consistent: stable $($status.stable.version); candidate $version ($($status.candidate.status))."
