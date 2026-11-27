[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory)

$ErrorActionPreference = 'Stop'
if (Get-Process -Name 'FOG Prime', 'FOG.Agent', 'FOG.Engine' -ErrorAction SilentlyContinue) {
    throw 'Close existing FOG instances first. No existing process was stopped.'
}
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$agentPath = Join-Path $package 'app/FOG.Agent.exe'
$enginePath = Join-Path $package 'app/runtime/FOG.Engine.exe'
$log = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'FOG Prime/logs/fog-prime.log'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FogWindowSleepTest {
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
}
'@
$info = [Diagnostics.ProcessStartInfo]::new((Join-Path $package 'FOG Prime.exe'))
$info.WorkingDirectory = $package
$info.UseShellExecute = $false
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.Environment['FOG_PRIME_NO_AUTOSTART'] = '1'
$ui = $null
$agent = $null
function Assert-NoEngine {
    if (Get-Process -Name 'FOG.Engine' -ErrorAction SilentlyContinue | Where-Object Path -eq $enginePath) {
        throw 'An Engine started during the no-autostart window test.'
    }
}
try {
    $ui = [Diagnostics.Process]::Start($info)
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($ui.HasExited) { throw 'UI exited before browser readiness.' }
        $ui.Refresh()
        $agent = Get-Process -Name 'FOG.Agent' -ErrorAction SilentlyContinue | Where-Object Path -eq $agentPath | Select-Object -First 1
        if ($agent -and $ui.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 200
    }
    if (!$agent -or $ui.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Window or nested Agent did not start.' }
    $agentId = $agent.Id
    Assert-NoEngine
    [void][FogWindowSleepTest]::ShowWindowAsync($ui.MainWindowHandle, 6)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 200
        $text = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
    } while ($text -notmatch 'WebView2 sleep requested; accepted=True' -and [DateTime]::UtcNow -lt $deadline)
    if ($text -notmatch 'WebView2 sleep requested; accepted=True') { throw 'Renderer did not confirm suspension.' }
    for ($i = 0; $i -lt 3; $i++) {
        [void][FogWindowSleepTest]::ShowWindowAsync($ui.MainWindowHandle, 9)
        Start-Sleep -Milliseconds 100
        [void][FogWindowSleepTest]::ShowWindowAsync($ui.MainWindowHandle, 6)
        Start-Sleep -Milliseconds 100
    }
    [void][FogWindowSleepTest]::ShowWindowAsync($ui.MainWindowHandle, 9)
    Start-Sleep -Seconds 2
    $ui.Refresh()
    $agent.Refresh()
    if ($ui.HasExited -or $agent.HasExited -or [FogWindowSleepTest]::IsIconic($ui.MainWindowHandle)) {
        throw 'Restore failed or the original Agent was lost.'
    }
    $text = Get-Content -LiteralPath $log -Raw
    if ($text -match 'Unhandled dispatcher|WebView2 process failure|sleep transition failed|WebView2 recovery failed') {
        throw 'Browser lifecycle errors were recorded.'
    }
    Assert-NoEngine
    if (!$ui.CloseMainWindow() -or !$ui.WaitForExit(15000) -or !$agent.WaitForExit(5000)) {
        throw 'Window or original Agent did not close cleanly.'
    }
    Write-Host "PASS: real WebView2 suspend/rapid restore, same Agent PID $agentId, no native Engine, clean UI/Agent close."
}
finally {
    if ($ui) {
        if (!$ui.HasExited) { $ui.Kill($true); [void]$ui.WaitForExit(5000) }
        $ui.Dispose()
    }
    if ($agent) {
        if (!$agent.HasExited) { $agent.Kill($true); [void]$agent.WaitForExit(5000) }
        $agent.Dispose()
    }
}
