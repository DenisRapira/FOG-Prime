param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,
    [ValidateRange(20, 180)]
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
$package = (Resolve-Path $PackageDirectory).Path
$testRoot = Join-Path $env:TEMP ("fog-eac-lifecycle-" + [guid]::NewGuid().ToString("N"))
$statePath = Join-Path $env:LOCALAPPDATA "FOG Prime\state.json"

function Stop-FogProcesses {
    Get-Process -Name "FOG Prime", "FOG.Agent", "FOG.Engine" -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $path = $_.MainModule.FileName
            if ($path -and $path.StartsWith($package, [StringComparison]::OrdinalIgnoreCase)) {
                $_.Kill()
                $_.WaitForExit(3000) | Out-Null
            }
        }
        catch [InvalidOperationException] {
            # The process exited while cleanup was running.
        }
        finally {
            $_.Dispose()
        }
    }
}

function Wait-FogState([scriptblock]$predicate, [string]$failureMessage, [datetime]$notBefore) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if (-not (Test-Path $statePath)) {
            continue
        }

        try {
            $snapshot = Get-Content $statePath -Raw | ConvertFrom-Json
            if ([datetime]$snapshot.updatedAt -lt $notBefore.AddSeconds(-1)) {
                continue
            }

            if (& $predicate $snapshot) {
                return $snapshot
            }
        }
        catch {
            # The Agent may atomically replace the file while it is being read.
        }
    }

    throw $failureMessage
}

$fakeEac = $null
$ui = $null
try {
    Stop-FogProcesses
    $protectedSignalAlreadyActive = @(Get-Process -Name "EasyAntiCheat", "EasyAntiCheat_EOS", "EasyAntiCheatLauncher", "EACLauncher", "start_protected_game" -ErrorAction SilentlyContinue).Count -gt 0
    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
    $fakeEacPath = Join-Path $testRoot "EasyAntiCheat_EOS.exe"
    Copy-Item (Join-Path $env:SystemRoot "System32\PING.EXE") $fakeEacPath
    $fakeEac = Start-Process -FilePath $fakeEacPath -ArgumentList "-t", "127.0.0.1" -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 500
    if ($fakeEac.HasExited -or $fakeEac.ProcessName -ne "EasyAntiCheat_EOS") {
        throw "The EAC process simulator did not start with the expected process name."
    }

    $started = Get-Date
    $ui = Start-Process -FilePath (Join-Path $package "FOG Prime.exe") -WorkingDirectory $package -PassThru
    $duringGame = Wait-FogState {
        param($state)
        $state.state -eq "ready" -and
        $state.connectionHealthy -and
        $state.compatibility.reason -eq "protectedGame" -and
        ($state.engineRunning -or $state.transportMode -eq "direct") -and
        @($state.probes | Where-Object { $_.ok }).Count -eq 5
    } "FOG did not remain fully ready while the EAC process was active." $started

    $engineId = $null
    if ($duringGame.engineRunning) {
        $engine = Get-Process -Name "FOG.Engine" -ErrorAction Stop | Select-Object -First 1
        $engineId = $engine.Id
        $engine.Dispose()
    }

    $fakeEac.Kill()
    $fakeEac.WaitForExit(3000) | Out-Null
    $afterGame = Wait-FogState {
        param($state)
        $state.state -eq "ready" -and
        $state.connectionHealthy -and
        ($state.engineRunning -or $state.transportMode -eq "direct") -and
        (($protectedSignalAlreadyActive -and $state.compatibility.reason -eq "protectedGame") -or
         (-not $protectedSignalAlreadyActive -and
          ($null -eq $state.compatibility -or $state.compatibility.reason -eq "externalTunnel")))
    } "FOG did not settle after the EAC process exited." $started

    if ($engineId) {
        $engineAfter = Get-Process -Name "FOG.Engine" -ErrorAction Stop | Select-Object -First 1
        try {
            if ($engineAfter.Id -ne $engineId) {
                throw "FOG restarted a healthy Engine when EAC exited."
            }
        }
        finally {
            $engineAfter.Dispose()
        }
    }

    if (-not $ui.CloseMainWindow()) {
        throw "FOG UI did not accept a normal close request."
    }
    if (-not $ui.WaitForExit(12000)) {
        throw "FOG UI did not exit after a normal close request."
    }

    Start-Sleep -Seconds 1
    $remaining = @(Get-Process -Name "FOG Prime", "FOG.Agent", "FOG.Engine" -ErrorAction SilentlyContinue)
    if ($remaining.Count -gt 0) {
        throw "FOG processes remained after UI close: $($remaining.ProcessName -join ', ')."
    }

    Write-Host "PASS: EAC active + 5/5 probes + stable transport + settled compatibility state + clean close"
}
finally {
    if ($fakeEac -and -not $fakeEac.HasExited) {
        $fakeEac.Kill()
        $fakeEac.WaitForExit(3000) | Out-Null
    }
    if ($fakeEac) {
        $fakeEac.Dispose()
    }

    if ($ui -and -not $ui.HasExited) {
        $ui.Kill()
        $ui.WaitForExit(3000) | Out-Null
    }
    if ($ui) {
        $ui.Dispose()
    }
    Stop-FogProcesses
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
