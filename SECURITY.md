# Security policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Do not disclose vulnerabilities in public issues. Use GitHub's private vulnerability reporting for `DenisRapira/FOG-Prime`:

`https://github.com/DenisRapira/FOG-Prime/security/advisories/new`

Include the affected version, reproduction steps, impact, and any suggested mitigation. Remove access tokens, private domains, IP addresses, and personal data from reports.

## Trust model

- Install releases only from this repository's Releases page.
- Compare the downloaded archive with the published `.sha256` file.
- The UI verifies `FOG.Agent.exe` against a hash embedded at build time.
- Agent compares external profiles and manifest with trusted embedded copies, then verifies every runtime file before execution.
- Local pipe requests require a random per-session token and are restricted to the current Windows user.
- The application requires administrator privileges because the packet-diversion driver operates at the Windows network layer.
- FOG Prime does not require a remote control server and does not intentionally collect telemetry.

## Current limitation

Release binaries are not Authenticode-signed yet. Windows can therefore show an unknown publisher warning. The published SHA-256 checksum and embedded trust chain detect accidental or malicious file replacement after a trusted UI binary has been obtained, but they do not replace publisher code signing.
