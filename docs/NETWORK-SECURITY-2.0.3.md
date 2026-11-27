# FOG Prime 2.0.3 - Network and Release Security Check

Scope: local optimized-candidate.2, focused network and managed-code source
review. Status as of 2026-11-27. No file upload, driver loading, firewall/antivirus
exclusions, VPN changes or trust-store changes were performed.

This is a repository report, not a document shipped in the minimal candidate ZIP. Package dependency documents are under `notices/`, including `ENGINE-NOTICES.md` and `UPSTREAM.md`; root licenses and integrity/provenance JSON remain included. The results below describe the recorded candidate, not a new verification of a subsequently repackaged artifact.

## Results

- Package security tests pass: authenticated IPC, unauthorized-request rejection,
  pinned runtime EXE/DLL/manifest and rejection of modified release components.
- All six EXE/DLL/SYS files match the recorded package build hashes.
- WinDivert DLL and driver match the official WinDivert 2.2.2-A archive exactly.
  The driver has a Windows-valid Authenticode signature. The UI, Agent, Engine,
  Cygwin DLL and WinDivert DLL have no embedded Authenticode signature.
- NuGet's current configured feed reports no known vulnerable managed packages
  including transitive dependencies. Native binaries are outside that result.
- Source review found no certificate-validation bypass in the application HTTP/
  WebSocket paths. Probes use fixed service identities; update links are HTTPS
  and restricted to the project release path. Update redirects are disabled.
- The local control channel is a current-user named pipe with a random session
  token, PID/correlation checks, bounded request size and I/O deadlines, not a
  public TCP control endpoint. Process execution uses argument lists.
- Game-filter IP exclusions include loopback, private/local and link-local
  ranges. This does not make the broad destination filter process-specific or
  establish compatibility with every game or VPN.

## Build Supply Chain Fix

prepare-runtime.ps1 previously fetched a release ZIP over HTTPS without a
build-time content pin. It now verifies its fixed size and SHA-256 using
verify-windivert-archive.ps1 before extraction and has a download deadline.
The official archive passed this verification locally. A separate tampered-ZIP
fixture was not run.

Official release:
https://github.com/basil00/WinDivert/releases/tag/v2.2.2

Archive SHA-256:
63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15

Driver SHA-256:
8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2

DLL SHA-256:
c1e060ee19444a259b2162f8af0f3fe8c4428a1c6f694dce20de194ac8d7d9a2

## VirusTotal Status: NOT VERIFIED

The user authorized local checks and hash lookup only, not uploading files.
The public web reports could not be retrieved in this environment and neither
VIRUSTOTAL_API_KEY nor VT_API_KEY is configured. Therefore no detection counts,
clean verdict or successful VirusTotal scan is claimed.

build/audit-release-security.ps1 creates a binary inventory, signature/hash
results and SHA-256 report links. Optional -VirusTotalLookup uses only the
official authenticated GET /api/v3/files/{sha256} endpoint. It has no file-upload
or rescan request. Set the API key locally in the process environment, not in
source or chat. Missing reports/keys and API failures remain explicit statuses.

Example (PowerShell 7, from the repository root; stage the named candidate under
`artifacts/`; output path must not already exist):

```powershell
pwsh -NoProfile -File build/audit-release-security.ps1 `
  -PackageDirectory ./artifacts/FOG-Prime-2.0.3-optimized-candidate.2 `
  -VirusTotalLookup -OutputPath ./diagnostics/security-report.json
```

API documentation: https://docs.virustotal.com/reference/file-info

## Local Antivirus Status: NOT SCANNED

WinDefend and WdNisSvc were stopped. Get-MpComputerStatus returned an error.
No settings were changed and no local antivirus scan is claimed. Enabling or
repairing Windows security is a separate user-visible system change; it was
not performed automatically. Hash verification is not malware scanning.

## Remaining Work

- Obtain publisher-controlled trusted code signing for application EXEs; a
  self-signed development certificate is not public publisher verification.
- Obtain actual per-hash VirusTotal reports and examine individual detections
  and behavior. Do not assume a detection is false merely because the component
  has a legitimate purpose; do not assume zero detections prove safety.
- If confirmed false positives appear, submit the exact affected build to each
  vendor with source/build provenance and behavior documentation. Do not add
  obfuscation, encrypt binaries, rename components to evade detection, or
  disable antivirus protection to obtain a lower detection count.
- Review the native Engine, Cygwin and driver independently. The present check
  is not a kernel/native vulnerability audit or live traffic-capture test.
- Elevated UI remains an architectural hardening opportunity: a future design
  can separate the standard-user UI from a narrowly privileged Agent.
- Preserve the license/source-availability gates in the repository's
  [distribution checklist](LICENSE-COMPLIANCE.md) and
  manual provider/game acceptance. No release was published by this check.

Microsoft developer guidance:
https://learn.microsoft.com/en-us/defender-xdr/developer-faq
