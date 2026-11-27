[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$solution = Join-Path (Split-Path $PSScriptRoot -Parent) 'FOG.Prime.sln'
$output = & dotnet list $solution package --vulnerable --include-transitive --no-restore --format json --output-version 1
if ($LASTEXITCODE -ne 0) { throw 'NuGet advisory lookup failed; no clean result is claimed.' }
$report = ($output -join "`n") | ConvertFrom-Json
if ($report.version -ne 1 -or @($report.projects).Count -eq 0 -or $report.problems) {
    throw 'Incomplete NuGet advisory report.'
}
$affected = @(
    foreach ($project in $report.projects) {
        if ($project.problems) { throw 'NuGet advisory report contains project errors.' }
        foreach ($framework in $project.frameworks) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($package.vulnerabilities) { $package.id }
            }
        }
    }
)
if ($affected.Count -gt 0) { throw "Known NuGet advisories: $(($affected | Sort-Object -Unique) -join ', ')" }
Write-Host 'PASS: NuGet reports no known advisories for restored managed dependencies. Native components and unknown vulnerabilities are not covered.'
