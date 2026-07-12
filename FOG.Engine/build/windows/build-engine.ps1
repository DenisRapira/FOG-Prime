param(
    [Parameter(Mandatory)]
    [string]$CygwinRoot,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\..\artifacts\win-x64")
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$source = Join-Path $root "upstream"
$bash = Join-Path $CygwinRoot "bin\bash.exe"

if (-not (Test-Path $bash)) { throw "Cygwin bash was not found: $bash" }
if (-not (Test-Path $source)) { throw "Pinned upstream source was not found: $source" }

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$cygSource = & $bash -lc "cygpath -u '$source'"
$cygOutput = & $bash -lc "cygpath -u '$OutputDirectory'"

& $bash -lc "set -euo pipefail; cd '$cygSource'; make -C nfq cygwin; cp nfq/winws.exe '$cygOutput/FOG.Engine.exe'"
if ($LASTEXITCODE -ne 0) { throw "FOG Engine build failed." }

Write-Host "FOG.Engine.exe built in $OutputDirectory"
