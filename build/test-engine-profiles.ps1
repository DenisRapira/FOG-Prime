param(
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [string]$ProfilesPath = (Join-Path $PSScriptRoot '..\FOG.Agent\profiles.json')
)

$ErrorActionPreference = 'Stop'
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$profiles = Get-Content -LiteralPath $ProfilesPath -Raw | ConvertFrom-Json
$games = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\FOG.Agent\games.json') -Raw | ConvertFrom-Json
$gameDomains = @($games | ForEach-Object { $_.domains })
foreach ($profile in $profiles) {
  foreach ($gameFilterEnabled in @($false, $true)) {
    if ($profile.executable -ne 'FOG.Engine.exe') { throw 'Unexpected engine executable.' }
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $runtime 'FOG.Engine.exe'
    $info.WorkingDirectory = $runtime
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.ArgumentList.Add('--dry-run')
    $arguments = @($profile.arguments)
    if ($gameFilterEnabled) {
        $arguments = @($arguments | ForEach-Object {
            if ($_.StartsWith('--hostlist-domains=', [StringComparison]::Ordinal)) {
                $domains = [Collections.Generic.List[string]]::new()
                $domains.AddRange([string[]]$_.Substring('--hostlist-domains='.Length).Split(','))
                if ($domains -contains 'robrun.com') {
                    foreach ($domain in $gameDomains) {
                        if ($domains -notcontains $domain) { $domains.Add($domain) }
                    }
                    '--hostlist-domains=' + ($domains -join ',')
                }
                else { $_ }
            }
            else { $_ }
        })
    }
    else {
        $boundary = [Array]::IndexOf($arguments, '--filter-tcp=1024-65535')
        if ($boundary -le 0 -or $arguments[$boundary - 1] -ne '--new') { throw 'Missing game filter boundary.' }
        $arguments = @($arguments[0..($boundary - 2)] | ForEach-Object {
            if ($_ -like '--wf-tcp=*') { '--wf-tcp=80,443,2053,2083,2087,2096,8443' }
            elseif ($_ -like '--wf-udp=*') { '--wf-udp=443,19294-19344,50000-50100' }
            else { $_ }
        })
    }
    foreach ($argument in $arguments) {
        $info.ArgumentList.Add($argument.Replace('${runtime}', $runtime))
    }
    $process = [Diagnostics.Process]::Start($info)
    try {
        if (-not $process.WaitForExit(10000)) {
            $process.Kill()
            $process.WaitForExit(3000) | Out-Null
            throw "Profile validation timed out: $($profile.id)"
        }
        if ($process.ExitCode -ne 0) { throw "Engine rejected profile $($profile.id): $($process.ExitCode)" }
        Write-Host "PASS native parameters: $($profile.id), game filter=$gameFilterEnabled"
    }
    finally { $process.Dispose() }
  }
}
