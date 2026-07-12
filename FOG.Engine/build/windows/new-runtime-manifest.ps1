param(
    [Parameter(Mandatory)]
    [string]$RuntimeDirectory,
    [Parameter(Mandatory)]
    [string]$OutputPath,
    [string]$Version = "0.1.0",
    [string]$UpstreamCommit = "1a1fc38c8ea05b481eebcbd338df48cdcca23c15"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $RuntimeDirectory).Path
$hashes = [ordered]@{}
Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring($root.Length).TrimStart('\', '/') -replace '\\', '/'
    $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}

[ordered]@{
    version = $Version
    upstreamCommit = $UpstreamCommit
    sha256 = $hashes
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
