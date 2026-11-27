# Building FOG Prime

These instructions describe FOG Prime 2.0.3/.NET 10, not the previous 2.0.2/.NET 8 archive. The published 2.0.3 package retains the user-accepted runtime bytes; see RELEASE-ACCEPTANCE-2.0.3.md for build and release provenance.

## Prerequisites

- Windows x64
- .NET SDK selected by `global.json` (currently 10.0.404, `latestPatch` roll-forward)
- PowerShell 7.4 or newer recommended; do not assume every script supports Windows PowerShell 5.1 (some use modern .NET process/cryptography APIs)
- Cygwin x64 with `gcc-core`, `make`, and `zlib-devel`

## Build the application

Run from the repository root. A matching globally installed SDK is sufficient; a private SDK installation on a developer's drive is not required. `dotnet --version` confirms SDK selection. If using a portable SDK, put its directory on this shell's `PATH` and confirm the same selection; do not copy a local machine path into project instructions.

```powershell
dotnet --version
dotnet restore .\FOG.Prime.sln --locked-mode
dotnet build .\FOG.Prime.sln -c Release --no-restore
.\build\verify-repository.ps1 -SkipBuild
```

The verifier runs the custom console test harness, not `dotnet test` discovery. As of 2026-11-27, the candidate Release build completed with zero warnings/errors and 108/108 scoped tests passed, including Start/Stop serialization, obsolete-probe cancellation, stale topology evidence and browser sleep/restore races. Packaging-option regression checks cover default, disabled and enabled compression binding without publishing or executing native files. These results do not verify a fresh package; earlier candidate hashes remain historical. A solution build alone does not embed all packaging trust resources and is not a complete runnable distribution.

## Build the Engine

Set `$CygwinRoot` to your Cygwin installation directory before running these examples. Native compilation uses the retained source tree; runtime preparation additionally downloads the official WinDivert 2.2.2 archive and verifies it with `verify-windivert-archive.ps1`. Restore, runtime preparation and signing timestamps can require network access. Do not run them in an offline/no-network audit.

```powershell
.\FOG.Engine\build\windows\build-engine.ps1 `
  -CygwinRoot $CygwinRoot `
  -OutputDirectory .\FOG.Engine\artifacts\win-x64

.\FOG.Engine\build\windows\prepare-runtime.ps1 `
  -CygwinRoot $CygwinRoot `
  -OutputDirectory .\FOG.Engine\artifacts\win-x64
```

The build uses the pinned source in `FOG.Engine/upstream`; it does not download a prebuilt Engine executable. This does not establish byte-for-byte reproducibility or complete redistribution compliance. Retain provenance/notices and resolve the [distribution gates](LICENSE-COMPLIANCE.md), including corresponding source for exact WinDivert/Cygwin binaries and permission for packet/list assets.

## Package

The destination must be absent or empty:

```powershell
.\build\package.ps1 `
  -EngineRuntime .\FOG.Engine\artifacts\win-x64 `
  -OutputDirectory .\release\FOG-Prime `
  -Version 2.0.3

.\build\verify-repository.ps1 -PackageDirectory .\release\FOG-Prime
```

`-Version` is required and validated; there is no default version. Pass the intended version explicitly, such as `2.0.3` or `2.0.3-candidate.1`. The example labels a local candidate, not a public release. Packaging generates the trusted runtime manifest, embeds it and profiles in Agent, then embeds the packaged Agent hash in UI. It publishes self-contained win-x64 applications, so the packaged app does not require a separately installed .NET runtime; WebView2 is still required.

Experimental `-CompressBundles` enables Microsoft's built-in single-file
assembly compression for UI and Agent. It is **off by default**: the local
size experiment halved the extracted package but increased measured private
memory. It does not trim assemblies, modify the driver, use UPX/obfuscation or
remove the requirement for WebView2. See [size study](SIZE-STUDY-2.0.3.md).
`app/build-info.json` records the actual `singleFileCompression` mode. Always
retest startup, integrity, signatures and memory for the exact artifact.

The candidate package root contains `FOG Prime.exe`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, `notices/` and `app/`. The `app/` directory contains `FOG.Agent.exe`, `profiles.json`, `runtime.manifest.json`, `build-info.json`, `payload-provenance.json`, `game-filter-provenance.json` and `runtime/`. Engine notices and provenance are at `notices/ENGINE-NOTICES.md` and `notices/UPSTREAM.md`; dependency license texts remain in `notices/`. The current profile set still produces 33 files; moving backend files does not add duplicates.

The UI starts only `app/FOG.Agent.exe` with `app/` as its working directory. Agent's runtime/configuration resolution remains relative to its own application base directory. `app/build-info.json` hashes every other package file using paths relative to the package root, including `app/` prefixes; signing and driver metadata use those same package-root-relative paths. The runtime manifest retains flat filenames relative to `app/runtime/`.

The source ICO has nine frames (16 through 256 pixels). `test-app-icon.ps1` checks those frames; packaging and package verification also extract and check the icon embedded in `FOG Prime.exe`. The WPF window uses the same icon resource. Close the old FOG instance and extract into a separate empty folder before testing the new layout; do not overlay an old package.

Runtime input copying is allowlisted to the four Engine/Cygwin/WinDivert binaries and files referenced by the selected profiles; verified bundled payload/game assets are added separately. The unused `discord-ip-discovery-with-port.bin` sample is not included for the current profiles. Do not add it back or delete integrity/provenance/configuration files to trim a ZIP; regenerate a trusted package if its contents change.

Release notes, security/optimization reports and `LICENSE-COMPLIANCE.md` remain repository documentation, not files in the user ZIP. In particular, packaging does not copy `RELEASE-NOTES.md`, `SECURITY-REVIEW.md`, `OPTIMIZATION.md` or `LICENSE-COMPLIANCE.md` into the package. Read release notes on GitHub and complete the repository's distribution gates before publication.

## Integration tests

Integration tests are separate from the 97 scoped harness tests. As of 2026-11-27, disposable-VM driver tests and live VPN transitions have not been run; end-to-end audio, NAT and anti-cheat behavior remain unverified. Use an authorized disposable Windows VM, not just a temporary package folder.

**`test-startup-reliability.ps1` is retired.** It now fails before launching/stopping processes because its former shared-pipe setup was obsolete. It is not a current candidate acceptance test and its retirement is not a verification result.

Native builds require Cygwin gcc-core, make, zlib-devel, libintl-devel and
libiconv-devel. The original v2.0.3 tag workflow failed because libintl-devel
was missing; the subsequent main-branch fix does not replace published assets.

`test-window-sleep.ps1 -PackageDirectory <package>` tests real WebView2 suspension,
rapid minimize/restore and clean UI/Agent shutdown with native Engine autostart
disabled. Close existing FOG instances first; the script refuses to stop them.
It briefly restores the test window and checks that the original Agent survives.
This is not a live network, game, voice or memory-savings test.

`test-ui-lifecycle.ps1` requires `-AllowLiveNetworkTests`, refuses existing FOG instances, scopes cleanup to package EXE paths, and reads state from `%LOCALAPPDATA%\FOG Prime\state.json`. As of 2026-11-27, this live lifecycle test has not been run; its safeguards are not acceptance evidence. It is not run by the examples below.

For an authorized package-security run on a disposable package:

```powershell
.\build\test-package-security.ps1 -PackageDirectory .\release\FOG-Prime
```

Review the script before execution. Package-security checks use temporary copies and start real processes. Their recorded results do not prove driver/VPN compatibility or replace live acceptance tests.

## Create a GitHub release

Release publication is a separate maintainer operation after testing, source/license gates and artifact review. The release workflow creates drafts only: a tag does not automatically promote a stable release. Follow [Patch and Release Acceptance](PATCH-CHECKLIST.md), including review of the exact downloadable ZIP/hash and manual promotion after all required gates pass. A signed Git tag is not an Authenticode signature on binaries. Do not push a candidate tag merely to try these instructions. Version 2.0.3 is published; [release-status.json](release-status.json) records stable/candidate status.

Before 2.0.3 publication, the API check found only the ZIP on v2.0.2, without a checksum asset. The current 2.0.3 release includes ZIP, SHA-256 and separate native source archives. The public ZIP was downloaded again and matched daca6de1372939b021814692a6ab713c205bd1f00b8bf176d340626828fd4fad. A full native-code audit remains incomplete; scoped managed tests do not establish native audit coverage.

## Optional Application Signing

Application signing requires PowerShell 7.4 or newer (.NET 8 or newer), plus
Windows SDK SignTool. Unsigned packaging retains its existing requirements.

`build/package.ps1` accepts `-CertificateThumbprint` and `-SignToolPath` for
a code-signing certificate in `Cert:\CurrentUser\My`. No PFX password or private
key is written into the package. For a production certificate, also supply an
HTTPS RFC 3161 `-TimestampServer`; Windows trust verification must succeed.

For a self-signed test certificate, explicitly add `-AllowDevelopmentSignature`.
That flag does not establish Windows trust or kernel-driver signing eligibility.
It records the actual signature trust status in `build-info.json`. Do not import
a development certificate into a user's trusted roots to disguise this limit.

Signing occurs before dependent integrity hashes. Do not sign packaged EXEs
after packaging: doing so can invalidate the runtime manifest or embedded Agent
hash. The driver and third-party DLL signatures are left unchanged.

The hosted GitHub runner does not have the local signing key. Pushing a tag
does not automatically make its CI-built files signed; the current workflow
packages without a signing certificate. Do not replace tested, locally signed
assets with that unsigned CI artifact or put a private key into the repository.
