param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,
    [ValidateRange(1, 50)]
    [int]$Cycles = 10
)

$ErrorActionPreference = "Stop"
$package = (Resolve-Path $PackageDirectory).Path
$pipeName = "fog-prime-agent-v3"

function Stop-FogProcesses {
    Get-Process -Name "FOG Prime", "FOG.Agent", "FOG.Engine" -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill($true); $_.WaitForExit(3000) } catch [InvalidOperationException] { } finally { $_.Dispose() }
    }
}

function Start-TestAgent([string]$token) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $package "FOG.Agent.exe"
    $info.WorkingDirectory = $package
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.Environment["FOG_PRIME_SESSION_TOKEN"] = $token
    return [Diagnostics.Process]::Start($info)
}

function Send-AgentRequest([string]$command, [string]$token, [int]$connectTimeout = 8000) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new(".", $pipeName, [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect($connectTimeout)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 1024, $true)
        try {
            $correlation = [guid]::NewGuid().ToString("N")
            $writer.WriteLine((@{ command = $command; correlationId = $correlation; sessionToken = $token } | ConvertTo-Json -Compress))
            $writer.Flush()
            $response = $reader.ReadLine() | ConvertFrom-Json
            if ($response.correlationId -ne $correlation) { throw "Mismatched Agent response." }
            return $response
        }
        finally {
            $writer.Dispose()
            $reader.Dispose()
        }
    }
    finally {
        $pipe.Dispose()
    }
}

try {
    for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
        Stop-FogProcesses
        $token = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $agent = Start-TestAgent $token
        try {
            $ping = Send-AgentRequest "ping" $token
            if (-not $ping.ok) { throw "Cycle ${cycle}: Agent ping failed." }

            $first = Send-AgentRequest "start" $token 10000
            $failedProbes = @($first.snapshot.probes | Where-Object { -not $_.ok })
            if (-not $first.ok -or $first.state -ne "ready" -or -not $first.snapshot.engineRunning -or $failedProbes.Count -gt 0) {
                throw "Cycle ${cycle}: cold start was not ready. Failed probes: $($failedProbes.name -join ', ')."
            }

            $engine = Get-Process -Name "FOG.Engine" -ErrorAction Stop | Select-Object -First 1
            $engineId = $engine.Id
            $engine.Dispose()

            $second = Send-AgentRequest "start" $token 10000
            $engineAfter = Get-Process -Name "FOG.Engine" -ErrorAction Stop | Select-Object -First 1
            try {
                if (-not $second.ok -or $second.state -ne "ready" -or $engineAfter.Id -ne $engineId -or $second.snapshot.activeProfile -ne $first.snapshot.activeProfile) {
                    throw "Cycle ${cycle}: repeated start restarted or changed the healthy Engine."
                }
            }
            finally {
                $engineAfter.Dispose()
            }

            $state = Get-Content (Join-Path $env:ProgramData "FOG Prime\state.json") -Raw | ConvertFrom-Json
            if ($state.state -ne "ready" -or -not $state.engineRunning -or $state.probes.Count -lt 6) {
                throw "Cycle ${cycle}: immediate diagnostic state was not saved."
            }

            $shutdown = Send-AgentRequest "shutdown" $token
            if (-not $shutdown.ok -or -not $agent.WaitForExit(6000)) { throw "Cycle ${cycle}: clean shutdown failed." }
            if (Get-Process -Name "FOG.Agent", "FOG.Engine" -ErrorAction SilentlyContinue) { throw "Cycle ${cycle}: FOG process remained after shutdown." }

            Write-Host "PASS: cycle $cycle/$Cycles, profile $($first.snapshot.activeProfile)"
        }
        finally {
            if (-not $agent.HasExited) { $agent.Kill($true) }
            $agent.Dispose()
            Stop-FogProcesses
        }
    }

    Write-Host "FOG Prime startup reliability test passed: $Cycles/$Cycles."
}
finally {
    Stop-FogProcesses
}
