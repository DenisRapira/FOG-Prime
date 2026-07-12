$ErrorActionPreference = "Stop"
$principal = [Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session."
}

sc.exe stop "FOG Prime Agent" | Out-Null
sc.exe delete "FOG Prime Agent" | Out-Null
Write-Host "FOG Prime Agent removed."
