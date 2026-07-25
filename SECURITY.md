# Security policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Do not disclose vulnerabilities in public issues. Use GitHub's private vulnerability reporting for `DenisRapira/FOG-Prime`:

`https://github.com/DenisRapira/FOG-Prime/security/advisories/new`

Include the affected version, reproduction steps, impact, and any suggested mitigation. Remove access tokens, private domains, IP addresses, and personal data from reports.

## Trust model

- Install releases only from this repository's Releases page.
- The UI verifies the packaged Agent before launch; Agent verifies embedded profiles,
  the trusted runtime manifest, and every runtime file before opening IPC.
- Local IPC is limited to the current Windows user and authenticated with a random
  per-launch session token.
- The application requires administrator privileges because the packet-diversion driver operates at the Windows network layer.
- FOG Prime does not require a remote control server and does not intentionally collect telemetry.
