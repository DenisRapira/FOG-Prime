[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('fog-package-options-' + [guid]::NewGuid().ToString('N'))
try {
    $runtime = Join-Path $fixture 'runtime'
    $output = Join-Path $fixture 'occupied-output'
    [void][IO.Directory]::CreateDirectory($runtime)
    [void][IO.Directory]::CreateDirectory($output)
    foreach ($path in @((Join-Path $runtime 'FOG.Engine.exe'), (Join-Path $runtime 'WinDivert64.sys'), (Join-Path $output 'keep.txt'))) {
        [IO.File]::WriteAllBytes($path, [byte[]]@(1))
    }
    # The occupied-output guard runs before signing, publishing or native execution.
    foreach ($options in @(@{}, @{ CompressBundles = $false }, @{ CompressBundles = $true })) {
        $rejected = $false
        try {
            & (Join-Path $PSScriptRoot 'package.ps1') -EngineRuntime $runtime -OutputDirectory $output -Version '2.0.3' @options
        }
        catch {
            if ($_.Exception.Message -notlike 'Output directory must be empty:*') { throw }
            $rejected = $true
        }
        if (!$rejected) { throw 'Package options did not reach the expected pre-publication guard.' }
        if ([IO.File]::ReadAllBytes((Join-Path $output 'keep.txt'))[0] -ne 1) { throw 'Occupied output was modified.' }
    }
    Write-Host 'PASS: default, explicit disabled and enabled compression options bind without changing occupied output.'
}
finally {
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (!$resolved.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or
        ![IO.Path]::GetFileName($resolved).StartsWith('fog-package-options-')) {
        throw 'Unsafe package-option fixture cleanup path.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
