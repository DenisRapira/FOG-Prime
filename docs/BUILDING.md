# Building FOG Prime

## Prerequisites

- Windows x64
- .NET SDK 8
- PowerShell 7 or Windows PowerShell 5.1
- Cygwin x64 with `gcc-core`, `make`, and `zlib-devel`

## Build the application

```powershell
dotnet restore .\FOG.Prime.sln --locked-mode
dotnet build .\FOG.Prime.sln -c Release --no-restore
```

## Build the Engine

```powershell
.\FOG.Engine\build\windows\build-engine.ps1 `
  -CygwinRoot C:\cygwin `
  -OutputDirectory .\FOG.Engine\artifacts\win-x64

.\FOG.Engine\build\windows\prepare-runtime.ps1 `
  -CygwinRoot C:\cygwin `
  -OutputDirectory .\FOG.Engine\artifacts\win-x64
```

The build uses the pinned source in `FOG.Engine/upstream`; it does not download a prebuilt Engine executable.

## Package

The destination must be absent or empty:

```powershell
.\build\package.ps1 `
  -EngineRuntime .\FOG.Engine\artifacts\win-x64 `
  -OutputDirectory .\release\FOG-Prime `
  -Version 0.1.0

.\build\verify-repository.ps1 -PackageDirectory .\release\FOG-Prime
```

## Integration tests

Run these from an elevated PowerShell session on a disposable package directory. They do not stop VPN clients or unrelated applications.

```powershell
.\build\test-package-security.ps1 -PackageDirectory .\release\FOG-Prime
.\build\test-startup-reliability.ps1 -PackageDirectory .\release\FOG-Prime -Cycles 10
.\build\test-ui-lifecycle.ps1 -PackageDirectory .\release\FOG-Prime
```

The security test modifies temporary package copies only. The reliability and UI tests start the real network Engine and clean up all FOG processes when complete.

## Create a GitHub release

Push a signed or annotated tag such as `v0.1.0`. The `Release` workflow builds the Engine and application from source, verifies the package, uploads a CI artifact, and creates a GitHub Release ZIP.
