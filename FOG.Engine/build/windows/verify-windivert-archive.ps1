param([Parameter(Mandatory)][string]$ArchivePath)

$ErrorActionPreference = 'Stop'
$archive = Get-Item -LiteralPath $ArchivePath
if ($archive.PSIsContainer -or $archive.Length -ne 405137 -or
    (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash -ine
    '63CB41763BB4B20F600B6DE04E991A9C2BE73279E317D4D82F237B150C5F3F15') {
    throw 'Official WinDivert 2.2.2-A archive integrity check failed.'
}
Write-Host 'PASS: pinned official WinDivert 2.2.2-A archive'
