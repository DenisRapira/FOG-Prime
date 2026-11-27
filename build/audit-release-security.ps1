param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [string]$OutputPath,
    [switch]$VirusTotalLookup,
    [switch]$AllowLegacyLayout
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$apiKey = [Environment]::GetEnvironmentVariable('VIRUSTOTAL_API_KEY')
if (!$apiKey) { $apiKey = [Environment]::GetEnvironmentVariable('VT_API_KEY') }
$infoRelativePath = 'app/build-info.json'
$layout = 'app'
if (-not (Test-Path -LiteralPath (Join-Path $package 'app') -PathType Container)) {
    if (-not $AllowLegacyLayout) { throw 'Current packages require app/build-info.json. Use -AllowLegacyLayout only to inventory a historical root-layout package.' }
    $infoRelativePath = 'build-info.json'
    $layout = 'legacy-root'
    Write-Warning 'Inventorying an explicitly selected historical root-layout package; this is not current package acceptance.'
}
$info = Get-Content -LiteralPath (Join-Path $package $infoRelativePath) -Raw | ConvertFrom-Json
$files = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object Extension -in '.exe', '.dll', '.sys' | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $signature = Get-AuthenticodeSignature -LiteralPath $_.FullName
    $expected = $info.files.PSObject.Properties[$relative]
    $vt = [ordered]@{ status = 'not_requested'; reportUrl = "https://www.virustotal.com/gui/file/$hash/detection" }
    if ($VirusTotalLookup) {
        if (!$apiKey) { $vt.status = 'api_key_missing' }
        else {
            try {
                # GET by hash only. This script never uploads a file or requests reanalysis.
                $result = Invoke-RestMethod -Method Get -Uri "https://www.virustotal.com/api/v3/files/$hash" `
                    -Headers @{ 'x-apikey' = $apiKey } -TimeoutSec 20 -MaximumRedirection 0
                if ($result.data.id -cne $hash) { throw 'VirusTotal returned a different file identity.' }
                $vt.status = 'report_retrieved'
                $vt.lastAnalysisUnix = $result.data.attributes.last_analysis_date
                $vt.statistics = $result.data.attributes.last_analysis_stats
            }
            catch {
                $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { $null }
                $vt.status = if ($code -eq 404) { 'not_found' } elseif ($code -eq 429) { 'rate_limited' } else { 'lookup_failed' }
                $vt.httpStatus = $code
            }
            Start-Sleep -Seconds 16
        }
    }
    [ordered]@{
        file = $relative; bytes = $_.Length; sha256 = $hash
        buildHashMatches = [bool]($expected -and $expected.Value -ieq $hash)
        signature = $signature.Status.ToString(); signer = $signature.SignerCertificate.Subject
        virusTotal = $vt
    }
})
$defender = Get-Service -Name WinDefend -ErrorAction SilentlyContinue
$report = [ordered]@{
    schemaVersion = 1; generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    packageVersion = $info.version; packageLayout = $layout; buildInfoFile = $infoRelativePath; files = $files
    defenderServiceStatus = if ($defender) { $defender.Status.ToString() } else { 'unavailable' }
    localAntivirusScan = 'not_performed'
    uploadsPerformed = $false
    virusTotalVerdict = 'not_certified'
    limitations = @('Hash matches and valid signatures do not establish absence of malware.',
        'Missing, unavailable or zero-detection reports are not a security certification.',
        'No antivirus settings, exclusions, certificates or network settings were changed.')
}
$json = $report | ConvertTo-Json -Depth 12
if ($OutputPath) {
    $destination = [IO.Path]::GetFullPath($OutputPath)
    if (Test-Path -LiteralPath $destination) { throw 'Choose a new report output path; existing files are not overwritten.' }
    $json | Set-Content -LiteralPath $destination -Encoding utf8
}
$json
