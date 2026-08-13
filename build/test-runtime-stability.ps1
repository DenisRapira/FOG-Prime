param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,
    [ValidateRange(30, 300)]
    [int]$DurationSeconds = 60,
    [ValidateRange(20, 180)]
    [int]$ReadyTimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
$package = (Resolve-Path $PackageDirectory).Path
$statePath = Join-Path $env:LOCALAPPDATA "FOG Prime\state.json"
$processNames = @("FOG Prime", "FOG.Agent", "FOG.Engine")

function Get-PackageProcesses {
    $matches = @()
    Get-Process -Name $processNames -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $path = $_.MainModule.FileName
            if ($path -and $path.StartsWith($package, [StringComparison]::OrdinalIgnoreCase)) {
                $matches += $_
            }
            else {
                $_.Dispose()
            }
        }
        catch {
            $_.Dispose()
        }
    }
    return $matches
}

function Stop-PackageProcesses {
    Get-PackageProcesses | ForEach-Object {
        try {
            if (-not $_.HasExited) {
                $_.Kill()
                $_.WaitForExit(3000) | Out-Null
            }
        }
        finally {
            $_.Dispose()
        }
    }
}

function Wait-Ready([datetime]$notBefore) {
    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if (-not (Test-Path $statePath)) {
            continue
        }

        try {
            $state = Get-Content $statePath -Raw | ConvertFrom-Json
            if ([datetime]$state.updatedAt -ge $notBefore.AddSeconds(-1) -and
                $state.state -eq "ready" -and
                $state.connectionHealthy -and
                ($state.engineRunning -or $state.transportMode -eq "direct")) {
                return $state
            }
        }
        catch {
            # The Agent may atomically replace the state file while it is read.
        }
    }

    throw "FOG did not reach a healthy state before the stability test."
}

$ui = $null
try {
    Stop-PackageProcesses
    $started = Get-Date
    $ui = Start-Process -FilePath (Join-Path $package "FOG Prime.exe") -WorkingDirectory $package -PassThru
    $ready = Wait-Ready $started
    Start-Sleep -Seconds 10

    $samples = @{}
    $expectedPids = @{}
    $deadline = (Get-Date).AddSeconds($DurationSeconds)
    while ((Get-Date) -lt $deadline) {
        $processes = @(Get-PackageProcesses)
        try {
            if (@($processes | Where-Object ProcessName -eq "FOG Prime").Count -ne 1 -or
                @($processes | Where-Object ProcessName -eq "FOG.Agent").Count -ne 1) {
                throw "FOG UI or Agent exited or duplicated during the stability window."
            }

            foreach ($process in $processes) {
                $process.Refresh()
                if ($expectedPids.ContainsKey($process.ProcessName) -and
                    $expectedPids[$process.ProcessName] -ne $process.Id) {
                    throw "$($process.ProcessName) restarted during the stability window."
                }

                $expectedPids[$process.ProcessName] = $process.Id
                if (-not $samples.ContainsKey($process.ProcessName)) {
                    $samples[$process.ProcessName] = @()
                }
                $samples[$process.ProcessName] += [pscustomobject]@{
                    PrivateBytes = $process.PrivateMemorySize64
                    WorkingSet = $process.WorkingSet64
                    Handles = $process.HandleCount
                }
            }
        }
        finally {
            $processes | ForEach-Object { $_.Dispose() }
        }

        Start-Sleep -Seconds 5
    }

    foreach ($name in $samples.Keys) {
        $series = @($samples[$name])
        $privateGrowth = $series[-1].PrivateBytes - $series[0].PrivateBytes
        $handleGrowth = $series[-1].Handles - $series[0].Handles
        if ($privateGrowth -gt 128MB) {
            throw "$name private memory grew by more than 128 MB during the stability window."
        }
        if ($handleGrowth -gt 64) {
            throw "$name handle count grew by more than 64 during the stability window."
        }

        Write-Host ("{0}: private delta={1:N1} MB; working set={2:N1} MB; handle delta={3}" -f
            $name,
            ($privateGrowth / 1MB),
            ($series[-1].WorkingSet / 1MB),
            $handleGrowth)
    }

    if (-not $ui.CloseMainWindow()) {
        throw "FOG UI did not accept a normal close request."
    }
    if (-not $ui.WaitForExit(12000)) {
        throw "FOG UI did not exit after a normal close request."
    }

    Start-Sleep -Seconds 1
    $remaining = @(Get-PackageProcesses)
    try {
        if ($remaining.Count -gt 0) {
            throw "FOG package processes remained after UI close: $($remaining.ProcessName -join ', ')."
        }
    }
    finally {
        $remaining | ForEach-Object { $_.Dispose() }
    }

    Write-Host "PASS: stable PIDs + bounded memory/handles + clean close; mode=$($ready.transportMode)"
}
finally {
    if ($ui) {
        if (-not $ui.HasExited) {
            $ui.Kill()
            $ui.WaitForExit(3000) | Out-Null
        }
        $ui.Dispose()
    }
    Stop-PackageProcesses
}
