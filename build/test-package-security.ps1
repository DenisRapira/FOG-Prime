param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory
)

$ErrorActionPreference = "Stop"
$package = (Resolve-Path $PackageDirectory).Path
$tempRoot = Join-Path $env:TEMP ("fog-prime-security-" + [guid]::NewGuid().ToString("N"))
$pipeName = "fog-prime-agent-v3"

function New-SessionToken {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
        return ([BitConverter]::ToString($bytes)).Replace("-", "")
    }
    finally {
        $generator.Dispose()
    }
}

function Stop-TestProcesses {
    Get-Process -Name "FOG Prime", "FOG.Agent", "FOG.Engine" -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $path = $_.MainModule.FileName
            if ($path -and $path.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
                $_.Kill()
                $_.WaitForExit(3000) | Out-Null
            }
        }
        catch [InvalidOperationException] {
            # The process exited during cleanup.
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
    $pipe = [IO.Pipes.NamedPipeClientStream]::new(
        ".",
        $pipeName,
        [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect($connectTimeout)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 1024, $true)
        try {
            $correlation = [guid]::NewGuid().ToString("N")
            $request = @{
                command = $command
                correlationId = $correlation
                sessionToken = $token
            } | ConvertTo-Json -Compress
            $writer.WriteLine($request)
            $writer.Flush()
            $response = $reader.ReadLine() | ConvertFrom-Json
            if ($response.ok -and $response.correlationId -ne $correlation) {
                throw "Agent returned a mismatched correlation ID."
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
    $token = New-SessionToken
    $agent = Start-TestAgent $path $token
    try {
        if (-not $agent.WaitForExit(8000)) {
            throw "$description did not stop the Agent."
        }
    }
    finally {
        if (-not $agent.HasExited) {
            $agent.Kill()
            $agent.WaitForExit(3000) | Out-Null
        }
        $agent.Dispose()
    }
}

New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    $baseline = New-TestPackage "baseline"
    $token = New-SessionToken
    $agent = Start-TestAgent $baseline $token
    try {
        $ping = Send-AgentRequest "ping" $token
        if (-not $ping.ok -or $ping.message -ne "pong") {
            throw "Authenticated Agent ping failed."
        }

        $unauthorized = Send-AgentRequest "shutdown" (New-SessionToken)
        if ($unauthorized.ok -or $agent.HasExited) {
            throw "Unauthenticated shutdown was not rejected."
        }

        $shutdown = Send-AgentRequest "shutdown" $token
        if (-not $shutdown.ok -or -not $agent.WaitForExit(8000)) {
            throw "Authenticated Agent shutdown failed."
        }
        Write-Host "PASS: authenticated pipe lifecycle and unauthorized rejection"
    }
    finally {
        if (-not $agent.HasExited) {
            $agent.Kill()
            $agent.WaitForExit(3000) | Out-Null
        }
        $agent.Dispose()
        Stop-TestProcesses
    }

    $profilesTamper = New-TestPackage "profiles-tamper"
    Add-Content -LiteralPath (Join-Path $profilesTamper "profiles.json") -Value " "
    Assert-AgentRejectsStartup $profilesTamper "Modified profiles.json"
    Write-Host "PASS: modified profiles.json rejected"

    $manifestTamper = New-TestPackage "manifest-tamper"
    Add-Content -LiteralPath (Join-Path $manifestTamper "runtime.manifest.json") -Value " "
    Assert-AgentRejectsStartup $manifestTamper "Modified runtime.manifest.json"
    Write-Host "PASS: modified runtime.manifest.json rejected"

    $runtimeTamper = New-TestPackage "runtime-tamper"
    Add-Content -LiteralPath (Join-Path $runtimeTamper "runtime\voice_decoy_quic.bin") -Value "tamper"
    Assert-AgentRejectsStartup $runtimeTamper "Modified runtime file"
    Write-Host "PASS: modified runtime file rejected"

    $agentTamper = New-TestPackage "agent-tamper"
    Add-Content -LiteralPath (Join-Path $agentTamper "FOG.Agent.exe") -Value "tamper"
    $ui = Start-Process -FilePath (Join-Path $agentTamper "FOG Prime.exe") -WorkingDirectory $agentTamper -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Seconds 4
        $unexpected = @(Get-Process -Name "FOG.Agent", "FOG.Engine" -ErrorAction SilentlyContinue | Where-Object {
            try { $_.MainModule.FileName.StartsWith($agentTamper, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
        })
        if ($unexpected.Count -gt 0) {
            throw "UI accepted a modified FOG.Agent.exe."
        }
        Write-Host "PASS: modified FOG.Agent.exe rejected by UI"
    }
    finally {
        if (-not $ui.HasExited) {
            $ui.Kill()
            $ui.WaitForExit(3000) | Out-Null
        }
        $ui.Dispose()
        Stop-TestProcesses
    }

    Write-Host "FOG Prime package security tests passed."
}
finally {
    Stop-TestProcesses
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
