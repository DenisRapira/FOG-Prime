param(
    [Parameter(Mandatory)]
    [string]$EngineRuntime,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\release\FOG-Prime"),
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [string]$ProfilesPath = (Join-Path $PSScriptRoot "..\FOG.Agent\profiles.json"),
    [string]$UpstreamCommit = "1a1fc38c8ea05b481eebcbd338df48cdcca23c15",
    [ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [string]$SignToolPath,
    [uri]$TimestampServer,
    [switch]$AllowDevelopmentSignature,
    [switch]$CompressBundles
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runtime = (Resolve-Path $EngineRuntime).Path
$profiles = (Resolve-Path $ProfilesPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$appOutput = Join-Path $output 'app'
$buildInfoPath = Join-Path $appOutput 'build-info.json'
$staging = Join-Path $env:TEMP ("fog-prime-package-" + [guid]::NewGuid().ToString("N"))
$signatures = [ordered]@{}
$bundleCompressionValue = ([bool]$CompressBundles).ToString().ToLowerInvariant()
if ($CertificateThumbprint -and (!$SignToolPath -or !(Test-Path -LiteralPath $SignToolPath -PathType Leaf))) {
    throw 'Signing requires an explicit Windows SDK SignTool path.'
}
if ($AllowDevelopmentSignature -and !$CertificateThumbprint) {
    throw 'Development signing requires a certificate thumbprint.'
}

if (-not (Test-Path (Join-Path $runtime "FOG.Engine.exe"))) {
    throw "Engine runtime must contain FOG.Engine.exe."
}
$driverPath = Join-Path $runtime 'WinDivert64.sys'
if (!(Test-Path -LiteralPath $driverPath -PathType Leaf)) { throw 'The working runtime requires its original WinDivert64.sys.' }

if ((Test-Path $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
    throw "Output directory must be empty: $output"
}

New-Item -ItemType Directory -Force -Path $staging, $output, $appOutput | Out-Null

try {
    $driverPin = [IO.File]::Open($driverPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $driverSignature = Get-AuthenticodeSignature -LiteralPath $driverPath
    if ($driverSignature.Status -ne 'Valid') { throw 'The original runtime driver must have a valid Windows signature.' }
    $driverHash = (Get-FileHash -LiteralPath $driverPath -Algorithm SHA256).Hash
    $runtimeOutput = Join-Path $appOutput 'runtime'
    New-Item -ItemType Directory -Path $runtimeOutput | Out-Null
    $profileData = Get-Content -LiteralPath $profiles -Raw | ConvertFrom-Json
    $requiredRuntime = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @('FOG.Engine.exe', 'cygwin1.dll', 'WinDivert.dll', 'WinDivert64.sys')) {
        [void]$requiredRuntime.Add($name)
    }
    foreach ($profile in $profileData) {
        foreach ($argument in $profile.arguments) {
            if ($argument -match '\$\{runtime\}[\\/]+([A-Za-z0-9_.-]+)$') {
                [void]$requiredRuntime.Add($Matches[1])
            } elseif ($argument.Contains('${runtime}')) {
                throw 'Runtime profile paths must name a flat file.'
            }
        }
    }
    foreach ($name in $requiredRuntime) {
        $source = Join-Path $runtime $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination $runtimeOutput
        }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $runtimeOutput 'WinDivert64.sys') -Algorithm SHA256).Hash -cne $driverHash) {
        throw 'The original signed driver changed during copying.'
    }
    $payloadDirectory = Join-Path $root 'FOG.Engine/payloads/flowseal-1.10.2'
    $payloadManifest = Get-Content (Join-Path $payloadDirectory 'manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in $payloadManifest.sha256.PSObject.Properties) {
        if ($entry.Name -notmatch '^[A-Za-z0-9_]+\.bin$') { throw 'Invalid payload filename.' }
        $sourcePayload = Join-Path $payloadDirectory $entry.Name
        if ((Get-FileHash -LiteralPath $sourcePayload -Algorithm SHA256).Hash -ne $entry.Value) {
            throw "Payload integrity check failed: $($entry.Name)"
        }
        Copy-Item -LiteralPath $sourcePayload -Destination (Join-Path $runtimeOutput $entry.Name) -Force
    }
    Copy-Item -LiteralPath (Join-Path $payloadDirectory 'manifest.json') -Destination (Join-Path $appOutput 'payload-provenance.json')
    $gameDirectory = Join-Path $root 'FOG.Engine/payloads/flowseal-1.10.4'
    & (Join-Path $PSScriptRoot 'verify-game-assets.ps1') -AssetDirectory $gameDirectory
    $gameManifest = Get-Content -LiteralPath (Join-Path $gameDirectory 'manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in $gameManifest.sha256.PSObject.Properties) {
        if ($entry.Name -notmatch '^[A-Za-z0-9_-]+\.(bin|txt)$') { throw 'Invalid game filter asset filename.' }
        $sourceAsset = Join-Path $gameDirectory $entry.Name
        if ((Get-FileHash -LiteralPath $sourceAsset -Algorithm SHA256).Hash -ine $entry.Value) { throw "Game filter asset integrity check failed: $($entry.Name)" }
        $targetAsset = Join-Path $runtimeOutput $entry.Name
        Copy-Item -LiteralPath $sourceAsset -Destination $targetAsset
        if ((Get-FileHash -LiteralPath $targetAsset -Algorithm SHA256).Hash -ine $entry.Value) { throw 'Game filter asset changed during copying.' }
    }
    Copy-Item -LiteralPath (Join-Path $gameDirectory 'manifest.json') -Destination (Join-Path $appOutput 'game-filter-provenance.json')
    foreach ($name in $requiredRuntime) {
        if (-not (Test-Path -LiteralPath (Join-Path $runtimeOutput $name) -PathType Leaf)) {
            throw "Required runtime file is missing: $name"
        }
    }
    if ($CertificateThumbprint) {
        $signatures['app/runtime/FOG.Engine.exe'] = & (Join-Path $PSScriptRoot 'sign-application.ps1') `
            -FilePath (Join-Path $runtimeOutput 'FOG.Engine.exe') -CertificateThumbprint $CertificateThumbprint `
            -SignToolPath $SignToolPath -TimestampServer $TimestampServer -AllowDevelopmentSignature:$AllowDevelopmentSignature
    }
    $trustedManifest = Join-Path $staging "trusted-runtime.manifest.json"
    & (Join-Path $root "FOG.Engine\build\windows\new-runtime-manifest.ps1") -RuntimeDirectory $runtimeOutput -OutputPath $trustedManifest -Version $Version -UpstreamCommit $UpstreamCommit

    dotnet publish (Join-Path $root "FOG.Agent\FOG.Agent.csproj") -c Release -r win-x64 --self-contained true "-p:NuGetLockFilePath=packages.win-x64.lock.json" -p:RestoreLockedMode=true -p:PublishSingleFile=true "-p:EnableCompressionInSingleFile=$bundleCompressionValue" -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" "-p:FogRuntimeManifestPath=$trustedManifest" "-p:FogProfilesPath=$profiles" -o (Join-Path $staging "agent")
    if ($LASTEXITCODE -ne 0) { throw "Agent publish failed." }

    $agentPath = Join-Path $staging "agent\FOG.Agent.exe"
    if ($CertificateThumbprint) {
        $signatures['app/FOG.Agent.exe'] = & (Join-Path $PSScriptRoot 'sign-application.ps1') `
            -FilePath $agentPath -CertificateThumbprint $CertificateThumbprint `
            -SignToolPath $SignToolPath -TimestampServer $TimestampServer -AllowDevelopmentSignature:$AllowDevelopmentSignature
    }
    $agentHashPath = Join-Path $staging "trusted-agent.sha256"
    (Get-FileHash -LiteralPath $agentPath -Algorithm SHA256).Hash | Set-Content -LiteralPath $agentHashPath -Encoding ascii -NoNewline

    dotnet publish (Join-Path $root "FOG.WebView2\FOG.WebView2.csproj") -c Release -r win-x64 --self-contained true "-p:NuGetLockFilePath=packages.win-x64.lock.json" -p:RestoreLockedMode=true -p:PublishSingleFile=true "-p:EnableCompressionInSingleFile=$bundleCompressionValue" -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" "-p:FogAgentHashPath=$agentHashPath" -o (Join-Path $staging "ui")
    if ($LASTEXITCODE -ne 0) { throw "UI publish failed." }

    if ($CertificateThumbprint) {
        $signatures['FOG Prime.exe'] = & (Join-Path $PSScriptRoot 'sign-application.ps1') `
            -FilePath (Join-Path $staging 'ui/FOG Prime.exe') -CertificateThumbprint $CertificateThumbprint `
            -SignToolPath $SignToolPath -TimestampServer $TimestampServer -AllowDevelopmentSignature:$AllowDevelopmentSignature
    }

    Copy-Item (Join-Path $staging "ui\FOG Prime.exe") (Join-Path $output "FOG Prime.exe") -Force
    Copy-Item $agentPath (Join-Path $appOutput "FOG.Agent.exe") -Force
    Copy-Item $profiles (Join-Path $appOutput "profiles.json") -Force
    Copy-Item $trustedManifest (Join-Path $appOutput "runtime.manifest.json") -Force
    Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") (Join-Path $output "THIRD_PARTY_NOTICES.md") -Force
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $output "LICENSE") -Force
    $notices = Join-Path $output 'notices'
    New-Item -ItemType Directory -Path $notices -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'FOG.Engine/THIRD_PARTY_NOTICES.md') -Destination (Join-Path $notices 'ENGINE-NOTICES.md')
    Copy-Item -LiteralPath (Join-Path $root 'FOG.Engine/UPSTREAM.md') -Destination (Join-Path $notices 'UPSTREAM.md')
    Copy-Item -LiteralPath (Join-Path $root 'FOG.Engine/licenses/WinDivert-LICENSE.txt') -Destination $notices
    Copy-Item -LiteralPath (Join-Path $root 'FOG.Engine/upstream/docs/LICENSE.txt') -Destination (Join-Path $notices 'Engine-MIT-LICENSE.txt')
    foreach ($name in @('Cygwin-NOTICE.txt', 'DotNet-LICENSE.txt', 'DotNet-ThirdPartyNotices.txt', 'WebView2-LICENSE.txt', 'WebView2-NOTICE.txt')) {
        Copy-Item -LiteralPath (Join-Path $root "licenses/$name") -Destination $notices
    }
    & (Join-Path $PSScriptRoot 'test-app-icon.ps1') -ExecutablePath (Join-Path $output 'FOG Prime.exe')
    $commit = git -C $root rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source revision.' }
    $changes = git -C $root status --porcelain
    $sdk = dotnet --version
    Get-ChildItem $output -Filter '*.xml' -File | Remove-Item -Force
    Get-ChildItem $output -Filter '*.pdb' -File | Remove-Item -Force
    $fileHashes = [ordered]@{}
    Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object FullName -ne $buildInfoPath | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($output.TrimEnd('\').Length + 1).Replace('\', '/')
        $fileHashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    [ordered]@{
        version = $Version
        sourceCommit = $commit
        workingTreeModified = [bool]$changes
        dotnetSdk = $sdk
        singleFileCompression = [bool]$CompressBundles
        signing = $signatures
        applicationSigningMode = if (!$CertificateThumbprint) { 'unsigned' } elseif ($AllowDevelopmentSignature) { 'development' } else { 'production' }
        runtimeDriver = [ordered]@{
            file = 'app/runtime/WinDivert64.sys'; sha256 = $driverHash
            windowsTrustStatus = $driverSignature.Status.ToString()
            signerSubject = $driverSignature.SignerCertificate.Subject
            signerThumbprint = $driverSignature.SignerCertificate.Thumbprint
            originalBytesPreserved = $true
        }
        files = $fileHashes
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $buildInfoPath -Encoding UTF8

    & (Join-Path $PSScriptRoot 'test-package-layout.ps1') -PackageDirectory $output
    Write-Host "FOG Prime package created: $output"
}
finally {
    if ($driverPin) { $driverPin.Dispose() }
    $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    if (-not $resolvedStaging.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolvedStaging)).StartsWith('fog-prime-package-')) {
        throw "Unsafe staging cleanup path."
    }
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force -ErrorAction SilentlyContinue
}
