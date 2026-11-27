# Contributing

1. Open an issue for behavior changes that affect networking, privilege, updates, or runtime packaging.
2. Create a focused branch and keep third-party source changes separate from product code changes.
3. Run:

```powershell
dotnet --version
dotnet restore .\FOG.Prime.sln --locked-mode
dotnet build .\FOG.Prime.sln -c Release --no-restore
.\build\verify-repository.ps1 -SkipBuild
```

4. Explain security and compatibility impact in the pull request.
5. Preserve `FOG.Engine/UPSTREAM.md`, upstream licenses, and third-party notices.

Use the SDK selected by `global.json` (currently 10.0.404 with latest-patch roll-forward) from the repository root. A matching global installation is sufficient; no developer-specific SDK path is required. This tree is the unpublished 2.0.3/.NET 10 candidate; the published stable version is 2.0.2/.NET 8. Do not turn candidate notes into a publication claim.

The verifier runs the custom `FOG.Agent.Tests` executable plus repository checks. The 97/97 scoped tests passed with a zero-warning/error Release build as of 2026-11-27; they do not prove live driver, VPN, voice, NAT, antivirus or anti-cheat compatibility or accept a newly packaged ZIP. Record exactly what was run and what was skipped. See [Building](docs/BUILDING.md) before integration testing: startup-reliability is retired, and UI-lifecycle requires explicit live-test opt-in. The live lifecycle/driver/VPN scenarios have not been run. Do not run network/driver tests on a working desktop without explicit authorization. The full native audit remains incomplete.

Follow [Patch and Release Acceptance](docs/PATCH-CHECKLIST.md) for release work: the workflow creates a draft, not an automatically stable release. Manual promotion requires acceptance of the exact downloadable ZIP/hash and resolution of publication gates. Update [release status](docs/release-status.json) only to reflect actual publication.

Retain copyright/permission notices and exact dependency provenance. Follow the [distribution checklist](docs/LICENSE-COMPLIANCE.md), including unresolved corresponding-source access and packet/list asset licensing gates; the MIT license for original code does not cover every bundled dependency.

Do not commit release binaries, private host lists, personal network details, build caches, signing keys, or secrets.
