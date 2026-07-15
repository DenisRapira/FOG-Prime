param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory
)

$ErrorActionPreference = "Stop"
$package = (Resolve-Path $PackageDirectory).Path
$tempRoot = Join-Path $env:TEMP ("fog-prime-security-" + [guid]::NewGuid().ToString("N"))
$pipeName = "fog-prime-agent-v3"

function Stop-FogProcesses {
    Get-Process -Name "FOG Prime", "FOG.Agent", "FOG.Engine" -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $_.Kill($true)
            $_.WaitForExit(3000) | Out-Null
        }
        catch [System.InvalidOperationException] {
            # The process exited while cleanup was running.
        }
        finally {
            $_.Dispose()
        }
    }
}

function New-TestPackage([string]$name) {
    $path = Join-Path $tempRoot $name
    Copy-Item -LiteralPath $package -Destination $path -Recurse
    return $path
}

function Start-TestAgent([string]$path, [string]$token) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $path "FOG.Agent.exe"
    $info.WorkingDirectory = $path
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.Environment["FOG_PRIME_SESSION_TOKEN"] = $token
    return [Diagnostics.Process]::Start($info)
}

function Send-AgentRequest([string]$command, [string]$token, [int]$connectTimeout = 8000) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new(".", $pipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect($connectTimeout)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 1024, $true)
        try {
            $correlation = [guid]::NewGuid().ToString("N")
            $request = @{ command = $command; correlationId = $correlation; sessionToken = $token } | ConvertTo-Json -Compress
            $writer.WriteLine($request)
            $writer.Flush()
            $responseLine = $reader.ReadLine()
            $response = $responseLine | ConvertFrom-Json
            if ($response.correlationId -ne $correlation -and $response.state -ne "invalid") {
                throw "Agent returned a mismatched correlation ID. Expected $correlation, received $($response.correlationId). Request: $request Response: $responseLine"
            }
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

function Assert-AgentRejectsStartup([string]$path, [string]$description) {
    Stop-FogProcesses
    $token = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $agent = Start-TestAgent $path $token
    try {
        if (-not $agent.WaitForExit(6000)) {
            throw "$description did not stop the Agent."
        }
        if (Get-Process -Name "FOG.Engine" -ErrorAction SilentlyContinue) {
            throw "$description allowed FOG Engine to start."
        }
    }
    finally {
        if (-not $agent.HasExited) { $agent.Kill($true) }
        $agent.Dispose()
    }
}

New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    Stop-FogProcesses

    $baseline = New-TestPackage "baseline"
    $token = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $agent = Start-TestAgent $baseline $token
    try {
        $ping = Send-AgentRequest "ping" $token
        if (-not $ping.ok -or $ping.message -ne "pong") { throw "Authenticated Agent ping failed." }

        $start = Send-AgentRequest "start" $token 10000
        if (-not $start.ok -or -not $start.snapshot.engineRunning) { throw "Baseline Engine startup failed." }

        $unauthorized = Send-AgentRequest "shutdown" ""
        if ($unauthorized.ok -or $agent.HasExited -or -not (Get-Process -Name "FOG.Engine" -ErrorAction SilentlyContinue)) {
            throw "Unauthenticated shutdown was not rejected."
        }

        $shutdown = Send-AgentRequest "shutdown" $token
        if (-not $shutdown.ok) { throw "Authenticated shutdown failed." }
        if (-not $agent.WaitForExit(6000)) { throw "Agent did not exit after authenticated shutdown." }
        Write-Host "PASS: authenticated lifecycle and unauthorized command rejection"
    }
    finally {
        if (-not $agent.HasExited) { $agent.Kill($true) }
        $agent.Dispose()
        Stop-FogProcesses
    }

    $profilesTamper = New-TestPackage "profiles-tamper"
    Add-Content -LiteralPath (Join-Path $profilesTamper "profiles.json") -Value " "
    Assert-AgentRejectsStartup $profilesTamper "Modified profiles.json"
    Write-Host "PASS: modified profiles.json rejected"

    $manifestTamper = New-TestPackage "manifest-tamper"
    Add-Content -LiteralPath (Join-Path $manifestTamper "runtime.manifest.json") -Value " "
    Stop-FogProcesses
    $token = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $agent = Start-TestAgent $manifestTamper $token
    try {
        $ping = Send-AgentRequest "ping" $token
        if (-not $ping.ok) { throw "Agent did not start for manifest tamper test." }
        $response = Send-AgentRequest "start" $token 10000
        if ($response.ok -or (Get-Process -Name "FOG.Engine" -ErrorAction SilentlyContinue)) {
            throw "Modified runtime.manifest.json was accepted."
        }
        Send-AgentRequest "shutdown" $token | Out-Null
        Write-Host "PASS: modified runtime.manifest.json rejected"
    }
    finally {
        if (-not $agent.HasExited) { $agent.Kill($true) }
        $agent.Dispose()
        Stop-FogProcesses
    }

    $runtimeTamper = New-TestPackage "runtime-tamper"
    Add-Content -LiteralPath (Join-Path $runtimeTamper "runtime\voice_decoy_quic.bin") -Value "tamper"
    Stop-FogProcesses
    $token = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $agent = Start-TestAgent $runtimeTamper $token
    try {
        $ping = Send-AgentRequest "ping" $token
        if (-not $ping.ok) { throw "Agent did not start for runtime tamper test." }
        $response = Send-AgentRequest "start" $token 10000
        if ($response.ok -or (Get-Process -Name "FOG.Engine" -ErrorAction SilentlyContinue)) {
            throw "Modified runtime file was accepted."
        }
        Send-AgentRequest "shutdown" $token | Out-Null
        Write-Host "PASS: modified runtime file rejected"
    }
    finally {
        if (-not $agent.HasExited) { $agent.Kill($true) }
        $agent.Dispose()
        Stop-FogProcesses
    }

    $agentTamper = New-TestPackage "agent-tamper"
    Add-Content -LiteralPath (Join-Path $agentTamper "FOG.Agent.exe") -Value "tamper"
    $ui = Start-Process -FilePath (Join-Path $agentTamper "FOG Prime.exe") -WorkingDirectory $agentTamper -PassThru -WindowStyle Hidden
    try {
        Start-Sleep -Seconds 5
        if (Get-Process -Name "FOG.Agent", "FOG.Engine" -ErrorAction SilentlyContinue) {
            throw "UI accepted a modified FOG.Agent.exe."
        }
        Write-Host "PASS: modified FOG.Agent.exe rejected by UI"
    }
    finally {
        if (-not $ui.HasExited) { $ui.Kill($true) }
        $ui.Dispose()
        Stop-FogProcesses
    }

    Write-Host "FOG Prime package security tests passed."
}
finally {
    Stop-FogProcesses
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

exit 0
