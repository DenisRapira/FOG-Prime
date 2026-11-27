# FOG Prime 2.0.3 - Focused Security Review

Original review: 2026-11-25. Updated verification: 2026-11-27. Local candidate, not a full security certification.

This report stays in the repository, not in the minimal candidate ZIP. Packaged Engine notices/provenance are `notices/ENGINE-NOTICES.md` and `notices/UPSTREAM.md`; integrity/provenance JSON and root license notices remain included. Recorded results below do not verify a later repackaged ZIP.

## Addressed Findings

- Unsupported .NET 8: migrated projects, locked restores and CI to .NET 10 LTS.
- Verification/launch file-replacement race: Agent retains read-only handles
  to the external manifest and every verified runtime file for its lifetime.
  Ancestor directory handles prevent rename-based path replacement. Reparse
  points are rejected. UI similarly pins the Agent file/path through startup
  and authentication. Hash checks happen while handles are retained.
- Repeated startup hashing: subsequent verifier calls reuse the retained lease;
  disposal releases it. No measured startup-latency improvement is claimed.
- Defense in depth: IPC has cancellable read/write deadlines and rejects empty
  commands; WebView messages require the embedded page origin and bounded input.
- Update handling: 128 KiB body limit, whole-request six-second deadline,
  caller cancellation, no HTTP redirects and stricter URL authority validation.
- Start/Stop intent race: both running-intent updates occur under the supervisor
  gate; cancelled gate waiters leave the desired state unchanged.
- Stale network evidence: direct, regular, startup and confirmation probes are
  checked against captured network identity/topology generation. Changed
  topology invalidates in-flight/cached evidence before readiness, profile
  scoring or profile-memory reuse, and requests fresh checks. Cancelled probe
  results are not published. An established Engine is not replaced solely
  because stale evidence was discarded.

## Evidence

- Latest verification, 2026-11-27: Release build with zero warnings/errors;
  97/97 scoped compatibility tests passed.
- Eight race-regression test entries cover queued Start/Stop ordering,
  cancelled gate waiters and in-flight probes, stale topology across tunnel/
  Engine probe paths, cache invalidation and unchanged-topology acceptance.
  They use controlled fixtures/inert child processes, not a live VPN or driver.
- Ten new lease tests cover read access, write/delete/rename denial, ancestor
  replacement, disposal, manifest bounds, unsafe entries, bad hashes, missing
  files, cancellation, existing writers and fixture-local reparse points.
- Two new update tests cover oversized responses with/without Content-Length,
  stalled-body deadlines, caller cancellation, valid/missing fields and URLs.
- Packaged Agent rejects unauthorized IPC and altered Agent/profile/manifest/
  runtime/voice/game assets. Running-Agent tests confirm EXE, DLL and manifest
  remain pinned without starting the native Engine or loading its driver.
- Existing native parameter dry-runs and three-viewport UI tests remain part
  of acceptance. UI displays only a Game Filter switch, not a game list.
- NuGet vulnerability checks assess reported managed-package advisories only,
  not native runtime binaries, installed WebView2 or driver correctness.

## Scope and Residual Risks

The review covers managed application and network-control behavior.
This is a focused application audit, not exhaustive native-code,
kernel-driver, installer-ACL, anti-cheat or long-session penetration testing.
The original signed driver/native binaries were not changed. Their signature
does not establish absence of vulnerabilities. Application EXEs remain unsigned.

File sharing protects against ordinary replacement through Windows file APIs;
it is not a boundary against an attacker with arbitrary code execution inside
FOG, administrator/kernel capabilities, or control over the initial UI binary.
Do not replace a trusted package or its libraries while FOG is running. Fully
close it first. Rebuild with a regenerated manifest to test modified libraries.

Exact corresponding-source availability for bundled native dependencies and
permissions for referenced packet/list assets remain publication gates in
the repository's [distribution checklist](LICENSE-COMPLIANCE.md). No stable publication is claimed by this report.

No live game/driver loading, DNS/route/VPN changes, game restarts, trust-store
changes or boot-security changes were performed for these checks. Real provider
and game behavior still require manual testing without VPN.
