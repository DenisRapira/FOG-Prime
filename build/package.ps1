param(
    [Parameter(Mandatory)]
    [string]$EngineRuntime,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\release\FOG-Prime"),
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runtime = (Resolve-Path $EngineRuntime).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$staging = Join-Path $env:TEMP ("fog-prime-package-" + [guid]::NewGuid().ToString("N"))

if (-not (Test-Path (Join-Path $runtime "FOG.Engine.exe"))) {
    throw "Engine runtime must contain FOG.Engine.exe."
}

if ((Test-Path $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
    throw "Output directory must be empty: $output"
}

New-Item -ItemType Directory -Force -Path $staging, $output | Out-Null

try {
    Copy-Item $runtime (Join-Path $output "runtime") -Recurse -Force
    $trustedManifest = Join-Path $staging "trusted-runtime.manifest.json"
    & (Join-Path $root "FOG.Engine\build\windows\new-runtime-manifest.ps1") -RuntimeDirectory (Join-Path $output "runtime") -OutputPath $trustedManifest -Version $Version

    dotnet publish (Join-Path $root "FOG.Agent\FOG.Agent.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:FogRuntimeManifestPath=$trustedManifest" -o (Join-Path $staging "agent")
    if ($LASTEXITCODE -ne 0) { throw "Agent publish failed." }

    $agentPath = Join-Path $staging "agent\FOG.Agent.exe"
    $agentHashPath = Join-Path $staging "trusted-agent.sha256"
    (Get-FileHash -LiteralPath $agentPath -Algorithm SHA256).Hash | Set-Content -LiteralPath $agentHashPath -Encoding ascii -NoNewline

    dotnet publish (Join-Path $root "FOG.WebView2\FOG.WebView2.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:FogAgentHashPath=$agentHashPath" -o (Join-Path $staging "ui")
    if ($LASTEXITCODE -ne 0) { throw "UI publish failed." }

    Copy-Item (Join-Path $staging "ui\FOG Prime.exe") (Join-Path $output "FOG Prime.exe") -Force
    Copy-Item $agentPath (Join-Path $output "FOG.Agent.exe") -Force
    Copy-Item (Join-Path $root "FOG.Agent\profiles.json") (Join-Path $output "profiles.json") -Force
    Copy-Item $trustedManifest (Join-Path $output "runtime.manifest.json") -Force
    Copy-Item (Join-Path $root "FOG.Engine\THIRD_PARTY_NOTICES.md") (Join-Path $output "THIRD_PARTY_NOTICES.md") -Force
    Copy-Item (Join-Path $root "FOG.Engine\UPSTREAM.md") (Join-Path $output "UPSTREAM.md") -Force

    Get-ChildItem $output -Filter '*.xml' -File | Remove-Item -Force
    Get-ChildItem $output -Filter '*.pdb' -File | Remove-Item -Force
    Write-Host "FOG Prime package created: $output"
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}
