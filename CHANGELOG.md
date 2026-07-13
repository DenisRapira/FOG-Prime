# Changelog

All notable changes are documented here. This project follows Semantic Versioning.

## [Unreleased]

### Added

- Source-built FOG Engine runtime.
- Local FOG Agent with restricted named-pipe protocol.
- Automatic Discord profile selection and health checks.
- Runtime SHA-256 verification.
- Minimal WebView2 user interface.
- Reproducible GitHub Actions build and release workflows.

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
