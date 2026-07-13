param(
    [Parameter(Mandatory)]
    [string]$CygwinRoot,
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$fake = Join-Path $root "upstream\files\fake"
$cygwin = Join-Path $CygwinRoot "bin\cygwin1.dll"

if (-not (Test-Path (Join-Path $output "FOG.Engine.exe"))) { throw "FOG.Engine.exe was not built." }
if (-not (Test-Path $cygwin)) { throw "cygwin1.dll was not found: $cygwin" }

Copy-Item $cygwin $output -Force
Copy-Item (Join-Path $fake "quic_initial_www_google_com.bin") $output -Force
Copy-Item (Join-Path $fake "discord-ip-discovery-with-port.bin") $output -Force
Copy-Item (Join-Path $fake "stun.bin") $output -Force
Copy-Item (Join-Path $fake "tls_clienthello_www_google_com.bin") $output -Force

$temporary = Join-Path $env:TEMP ("fog-windivert-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $archive = Join-Path $temporary "windivert.zip"
    Invoke-WebRequest -Uri "https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip" -OutFile $archive -UseBasicParsing
    Expand-Archive $archive -DestinationPath $temporary

    $dll = Get-ChildItem $temporary -Recurse -Filter WinDivert.dll |
        Where-Object { (Split-Path (Split-Path $_.FullName -Parent) -Leaf) -eq "x64" } |
        Select-Object -First 1
    $driver = Get-ChildItem $temporary -Recurse -Filter WinDivert64.sys |
        Where-Object { (Split-Path (Split-Path $_.FullName -Parent) -Leaf) -eq "x64" } |
        Select-Object -First 1

    if (-not $dll -or -not $driver) { throw "Official WinDivert x64 runtime was not found." }
    Copy-Item $dll.FullName, $driver.FullName -Destination $output -Force
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "FOG Engine runtime prepared: $output"
