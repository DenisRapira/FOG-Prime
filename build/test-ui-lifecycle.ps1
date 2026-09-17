param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,
    [ValidateRange(10, 120)]
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
$package = (Resolve-Path $PackageDirectory).Path
$started = Get-Date
$ui = Start-Process -FilePath (Join-Path $package "FOG Prime.exe") -WorkingDirectory $package -PassThru

try {
    $ready = $false
    $statePath = Join-Path $env:ProgramData "FOG Prime\state.json"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $agent = @(Get-Process -Name "FOG.Agent" -ErrorAction SilentlyContinue)
        $engine = @(Get-Process -Name "FOG.Engine" -ErrorAction SilentlyContinue)
        if (Test-Path $statePath) {
            $state = Get-Content $statePath -Raw | ConvertFrom-Json
            $updated = [datetime]$state.updatedAt
            if ($state.state -eq "ready" -and $state.engineRunning -and $updated -ge $started.AddSeconds(-1) -and $agent.Count -eq 1 -and $engine.Count -eq 1) {
                $ready = $true
                break
            }
        }
    }

    if (-not $ready) { throw "UI did not reach the ready state within $TimeoutSeconds seconds." }
    if (-not $ui.CloseMainWindow()) { throw "UI did not accept a normal close request." }
    if (-not $ui.WaitForExit(12000)) { throw "UI did not exit after a normal close request." }

    Start-Sleep -Seconds 1
    $remaining = @(Get-Process -Name "FOG Prime", "FOG.Agent", "FOG.Engine" -ErrorAction SilentlyContinue)
    if ($remaining.Count -gt 0) { throw "FOG processes remained after UI close: $($remaining.ProcessName -join ', ')." }

    Write-Host "PASS: UI ready, $(@($state.probes | Where-Object ok).Count)/$(@($state.probes).Count) probes, clean close with zero FOG processes"
}
finally {
    if (-not $ui.HasExited) {
        $ui.Kill($true)
        $ui.WaitForExit()
    }
    $ui.Dispose()

    @(Get-Process -Name "FOG Prime", "FOG.Agent", "FOG.Engine" -ErrorAction SilentlyContinue) | ForEach-Object {
        try { $_.Kill($true) } catch [InvalidOperationException] { } finally { $_.Dispose() }
    }
}
