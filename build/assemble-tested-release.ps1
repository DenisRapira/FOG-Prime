[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TestedPackage,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$candidate = (Resolve-Path -LiteralPath $TestedPackage).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Release destination must not exist.' }
& (Join-Path $PSScriptRoot 'test-package-layout.ps1') -PackageDirectory $candidate
$commit = git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify release source.' }
$changes = git -C $root status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit the release source before assembling tested binaries.' }
$info = Get-Content -LiteralPath (Join-Path $candidate 'app/build-info.json') -Raw | ConvertFrom-Json
$status = Get-Content -LiteralPath (Join-Path $root 'docs/release-status.json') -Raw | ConvertFrom-Json
if ($info.version -cne $status.candidate.version) { throw 'Tested package does not match release version.' }
Copy-Item -LiteralPath $candidate -Destination $output -Recurse
foreach ($name in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $output $name) -Force
}
Copy-Item -LiteralPath (Join-Path $root 'FOG.Engine/THIRD_PARTY_NOTICES.md') -Destination (Join-Path $output 'notices/ENGINE-NOTICES.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'FOG.Engine/UPSTREAM.md') -Destination (Join-Path $output 'notices/UPSTREAM.md') -Force
foreach ($name in @('Cygwin-NOTICE.txt', 'DotNet-LICENSE.txt', 'DotNet-ThirdPartyNotices.txt', 'WebView2-LICENSE.txt', 'WebView2-NOTICE.txt')) {
    Copy-Item -LiteralPath (Join-Path $root "licenses/$name") -Destination (Join-Path $output "notices/$name") -Force
}
$hashes = [ordered]@{}
foreach ($file in Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName) {
    $relative = [IO.Path]::GetRelativePath($output, $file.FullName).Replace('\', '/')
    if ($relative -eq 'app/build-info.json') { continue }
    $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ($relative -notlike 'notices/*' -and $relative -notin @('LICENSE', 'THIRD_PARTY_NOTICES.md') -and
        $hashes[$relative] -cne $info.files.PSObject.Properties[$relative].Value) {
        throw "A tested runtime/configuration file changed: $relative"
    }
}
# Preserve the original build provenance instead of relabeling it as a clean build.
$info.files = $hashes
$info | Add-Member -NotePropertyName releaseSourceCommit -NotePropertyValue $commit -Force
$info | Add-Member -NotePropertyName reusedTestedBinaries -NotePropertyValue $true -Force
$info | Add-Member -NotePropertyName releasePackagingChanges -NotePropertyValue @('dependency-notices', 'source-access-metadata') -Force
$info | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'app/build-info.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'test-package-layout.ps1') -PackageDirectory $output
Write-Host "PASS: exact tested runtime/configuration bytes retained; release source $commit; original build provenance preserved."
