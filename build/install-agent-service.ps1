param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot "..\release\FOG-Prime")
)

$ErrorActionPreference = "Stop"
$principal = [Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session."
}

$package = (Resolve-Path $PackageDirectory).Path
$agent = Join-Path $package "FOG.Agent.exe"
if (-not (Test-Path $agent)) { throw "FOG.Agent.exe was not found in $package" }

sc.exe stop "FOG Prime Agent" | Out-Null
sc.exe delete "FOG Prime Agent" | Out-Null
Start-Sleep -Seconds 1

sc.exe create "FOG Prime Agent" binPath= "\"$agent\"" start= auto DisplayName= "FOG Prime Agent" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Windows service creation failed." }

sc.exe failure "FOG Prime Agent" reset= 86400 actions= restart/5000/restart/15000/restart/30000 | Out-Null
sc.exe failureflag "FOG Prime Agent" 1 | Out-Null
sc.exe start "FOG Prime Agent" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "FOG Prime Agent could not be started." }

Write-Host "FOG Prime Agent installed and started."
