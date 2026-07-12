# Security policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Do not disclose vulnerabilities in public issues. Use GitHub's private vulnerability reporting for `DenisRapira/FOG-Prime`:

`https://github.com/DenisRapira/FOG-Prime/security/advisories/new`

Include the affected version, reproduction steps, impact, and any suggested mitigation. Remove access tokens, private domains, IP addresses, and personal data from reports.

## Trust model

- Install releases only from this repository's Releases page.
- Runtime files are verified against `runtime.manifest.json` before execution.
- The application requires administrator privileges because the packet-diversion driver operates at the Windows network layer.
- FOG Prime does not require a remote control server and does not intentionally collect telemetry.
