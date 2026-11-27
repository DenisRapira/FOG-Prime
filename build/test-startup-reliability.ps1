[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,
    [ValidateRange(1, 50)]
    [int]$Cycles = 10
)

$ErrorActionPreference = 'Stop'
throw 'Retired live test: obsolete shared-pipe IPC. Run build/verify-repository.ps1 and follow docs/PATCH-CHECKLIST.md for isolated manual acceptance. No processes were started or stopped.'
