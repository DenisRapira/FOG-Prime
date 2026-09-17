param(
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [string]$ProfilesPath = (Join-Path $PSScriptRoot '..\FOG.Agent\profiles.json')
)

$ErrorActionPreference = 'Stop'
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$profiles = Get-Content -LiteralPath $ProfilesPath -Raw | ConvertFrom-Json
foreach ($profile in $profiles) {
    if ($profile.executable -ne 'FOG.Engine.exe') { throw 'Unexpected engine executable.' }
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $runtime 'FOG.Engine.exe'
    $info.WorkingDirectory = $runtime
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.ArgumentList.Add('--dry-run')
    foreach ($argument in $profile.arguments) {
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
        Write-Host "PASS native parameters: $($profile.id)"
    }
    finally { $process.Dispose() }
}
