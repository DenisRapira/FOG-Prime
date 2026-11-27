param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [ValidateRange(10, 300)][int]$Seconds = 20
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (Get-Process -Name 'FOG Prime', 'FOG.Agent' -ErrorAction SilentlyContinue) {
    throw 'Close FOG before the isolated idle-load measurement.'
}
$previous = $env:FOG_PRIME_NO_AUTOSTART
$env:FOG_PRIME_NO_AUTOSTART = '1'
$ui = $null
$agent = $null
try {
    $startup = [Diagnostics.Stopwatch]::StartNew()
    $ui = Start-Process -FilePath (Join-Path $package 'FOG Prime.exe') -WorkingDirectory $package -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        if ($ui.HasExited) { throw 'UI exited before measurement.' }
        $agent = Get-Process -Name 'FOG.Agent' -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -eq (Join-Path $package 'app/FOG.Agent.exe')
        } | Select-Object -First 1
        if ($agent) { break }
    }
    if (!$agent) { throw 'Agent did not start.' }
    $startup.Stop()
    Start-Sleep -Seconds 5
    if (Get-Process -Name 'FOG.Engine' -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -eq (Join-Path $package 'app/runtime/FOG.Engine.exe')
    }) { throw 'Unexpected Engine in no-autostart measurement.' }
    $ui.Refresh()
    $agent.Refresh()
    $uiCpu = $ui.TotalProcessorTime.TotalMilliseconds
    $agentCpu = $agent.TotalProcessorTime.TotalMilliseconds
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $Seconds
    $clock.Stop()
    $ui.Refresh()
    $agent.Refresh()
    if ($ui.HasExited -or $agent.HasExited) { throw 'FOG exited during measurement.' }
    [pscustomobject]@{
        package = $package
        mode = 'UI and Agent only; no autostart; native Engine and WebView2 child processes excluded'
        elapsedSeconds = [math]::Round($clock.Elapsed.TotalSeconds, 3)
        launcherToAgentMilliseconds = [math]::Round($startup.Elapsed.TotalMilliseconds, 3)
        uiCpuMilliseconds = [math]::Round($ui.TotalProcessorTime.TotalMilliseconds - $uiCpu, 3)
        agentCpuMilliseconds = [math]::Round($agent.TotalProcessorTime.TotalMilliseconds - $agentCpu, 3)
        uiWorkingSetMiB = [math]::Round($ui.WorkingSet64 / 1MB, 2)
        agentWorkingSetMiB = [math]::Round($agent.WorkingSet64 / 1MB, 2)
        uiPrivateMiB = [math]::Round($ui.PrivateMemorySize64 / 1MB, 2)
        agentPrivateMiB = [math]::Round($agent.PrivateMemorySize64 / 1MB, 2)
    }
}
finally {
    $env:FOG_PRIME_NO_AUTOSTART = $previous
    if ($ui) {
        if (!$ui.HasExited) {
            $ui.CloseMainWindow() | Out-Null
            if (!$ui.WaitForExit(15000)) { $ui.Kill(); $ui.WaitForExit(5000) | Out-Null }
        }
        $ui.Dispose()
    }
    if ($agent) { $agent.Dispose() }
}
