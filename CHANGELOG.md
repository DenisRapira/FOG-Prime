# Changelog

All notable changes are documented here. This project follows Semantic Versioning.

## [Unreleased]

## [0.1.9] - 2026-08-13

- Fixed automatic recovery after an external VPN is disconnected while its
  process or virtual adapter remains present in Windows.
- Added tunnel topology fingerprints and fast direct-mode checks so FOG can
  switch from VPN transport to FOG Engine without restarting the application.
- Prevented successful probes made through a running FOG Engine from being
  reused as proof that a stale VPN route is healthy.
- Added bounded UI recovery that restarts an unresponsive local Agent only
  after three consecutive authenticated IPC failures.
- Increased VPN transition monitoring frequency while retaining a quiet
  20-second health interval for stable Engine sessions.
- Updated Microsoft WebView2 and pinned GitHub Actions dependencies to current
  stable revisions.
- Expanded automated coverage to 28 compatibility, VPN transition, recovery,
  persistence, startup-policy, and package-integrity tests.
- Added repeatable EAC-under-VPN and runtime stability checks covering stable
  process IDs, bounded memory/handle growth, and clean shutdown.

## [0.1.0] - 2026-07-11

### Foundation

- Source-built FOG Engine runtime.
- Local FOG Agent with restricted named-pipe protocol.
- Automatic Discord profile selection and health checks.
- Runtime SHA-256 verification.
- Minimal WebView2 user interface.
- Reproducible GitHub Actions build and release workflows.

## [0.1.8] - 2026-08-03

- Remembers the last profile that passed Discord and YouTube readiness checks,
  tries it first on the next launch, and replaces it automatically when it no
  longer works.
- Reduced the startup readiness window to a single parallel check bounded to
  two seconds per profile, cutting the full reserve cycle to about eight seconds.
- Separated startup readiness from long-session health so an unusable initial
  profile rotates quickly while transient background probes never restart a
  working Discord voice session.
- Added authenticated Engine session adoption after an unexpected Agent restart,
  without tying the Engine lifetime to Agent-owned output pipes.
- Added precise PID, profile, uptime, and hexadecimal exit-code diagnostics with
  deduplicated tunnel and probe logging.
- Simplified connection-state wording and retained automatic VPN/EAC handling,
  package integrity checks, and clean close behavior.
- Expanded automated coverage to 25 compatibility, persistence, recovery, and
  startup-policy tests plus package tamper and EAC lifecycle tests.

## [0.1.7] - 2026-07-22

- Fixed a regression that could report a normal connection as ready and skip
  FOG Engine even though Discord voice or YouTube media still required it.
- Direct mode is now restricted to a detected, healthy external VPN; after the
  VPN closes, FOG Engine starts immediately without restarting the application.
- Added bounded local diagnostics for UI, Agent, profile, probe, and Engine
  failures without displaying technical noise in the interface.
- Fixed the startup IPC timeout that could terminate the Agent before the first
  profile started, then leave the interface polling a stopped replacement Agent.
- Normal mode now starts FOG Engine immediately without a redundant blocked-network
  probe; busy Agents are preserved and automatic start is retried after recovery.
- Added EAC-aware lifecycle handling so protected-game detection does not restart
  or rewrite a live validated Engine profile.
- Added generic VPN/tunnel detection, a one-time warning, and direct mode that
  keeps WinDivert stopped while an external tunnel is active.
- Added stable repeated Discord and YouTube probes, reserve profile rotation,
  and bounded recovery after WinDivert failures.
- Added authenticated current-user IPC v3 and embedded trust checks for the
  Agent, profiles, runtime manifest, and runtime files.
- Added a safe GitHub Releases update check that only opens the verified official
  release page after user confirmation.
- Added package tamper tests, EAC lifecycle coverage, and verified clean shutdown
  with no FOG processes left in the background.

## [0.1.6] - 2026-07-13

- Added automatic YouTube TLS and QUIC handling with a live connectivity probe.
- Added a degraded-mode fallback that keeps the proven Discord profile running
  when the YouTube probe is unavailable.
- Generalized the minimal UI wording for Discord and YouTube connectivity.

## [0.1.5] - 2026-07-13

- Replaced broad RTP priming with a validated, narrow Discord voice handshake.
- Limited media interception to UDP `19294-19344` and `50000-50100` so established
  audio and unrelated high UDP ports are no longer modified.
- Restored the ordered STUN and QUIC voice decoys with three repeats.
- Added a dedicated `discord.media` transport profile for TCP ports
  `2053`, `2083`, `2087`, `2096`, and `8443`.

## [0.1.4] - 2026-07-13

- Extended Discord voice priming through the first RTP packets to prevent the
  media flow from degrading from normal latency to 5000 ms after connection.
- Switched Discord, STUN, and unknown-UDP voice decoys to the current Google
  QUIC pattern with six repeats and a four-packet cutoff.
- Prevented SSRC synchronization from corrupting non-Discord QUIC decoys.
- Added automatic Engine recovery after an unexpected process exit.
- Clarified that closing the window stops FOG and should only be done after the
  Discord session is finished.

## [0.1.3] - 2026-07-13

- Replaced the valid Discord discovery fake with server-ignored STUN and QUIC
  decoys, preventing a false-positive voice connection with silent media.
- Limited Discord voice desynchronization to the initial discovery packet and
  aligned its repeat count with the verified ALT12 strategy.
- Closing the FOG Prime window now stops the Engine, shuts down the Agent, and
  cleans up stale FOG background processes.
- Added repository checks for voice decoy packaging and close-time cleanup.

## [0.1.1] - 2026-07-12

- Fixed Discord Voice `No Route` failures by covering the complete media UDP port range.
- Added clean handoff from the v1 background agent when launching an updated package.
- Added cleanup for orphaned Engine processes left by an interrupted previous session.
- Prevented FOG Engine output buffers from blocking long-running sessions.
- Added regression checks for Discord voice profile coverage.

## [0.1.2] - 2026-07-13

- Fixed Discord Voice `NO_ROUTE` caused by a static SSRC in generated discovery packets.
- Confirmed DNS resolution against Google Public DNS without forcing system DNS changes.
- Added a repository check that requires dynamic Discord SSRC synchronization in FOG Engine.

## [0.1.0] - 2026-07-12

- Initial public-ready build.
