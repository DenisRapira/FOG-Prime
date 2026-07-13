# Changelog

All notable changes are documented here. This project follows Semantic Versioning.

## [Unreleased]

### Foundation

- Source-built FOG Engine runtime.
- Local FOG Agent with restricted named-pipe protocol.
- Automatic Discord profile selection and health checks.
- Runtime SHA-256 verification.
- Minimal WebView2 user interface.
- Reproducible GitHub Actions build and release workflows.

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
