# Security policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Do not disclose vulnerabilities in public issues. Use GitHub's private vulnerability reporting for `DenisRapira/FOG-Prime`:

`https://github.com/DenisRapira/FOG-Prime/security/advisories/new`

Include the affected version, reproduction steps, impact, and any suggested mitigation. Remove access tokens, private domains, IP addresses, and personal data from reports.

## Trust model

- CI scans the fetched Git history for credentials with a checksum-pinned
  Gitleaks version. Findings are redacted; only three exact, reviewed public
  examples are allowlisted. This is a detection aid, not proof that no secret
  or personal information can ever be present. Never commit local environment
  files, certificate private keys, tokens or unredacted diagnostic reports.
- If a real credential has been published, revoke or rotate it first. Removing
  a file or rewriting Git history does not invalidate leaked credentials or
  remove copies in existing clones, forks, caches or release assets.

- Install releases only from this repository's Releases page.
- Compare the downloaded archive with its release `.sha256` asset when available.
  As verified on 2026-11-27, v2.0.2 has no separate checksum asset; do not
  mistake that missing evidence for a successful integrity check. Future
  draft releases include a checksum for manual acceptance before publication.
- The UI verifies `FOG.Agent.exe` against a hash embedded at build time.
- Agent compares external profiles and manifest with trusted embedded copies, then verifies every runtime file before execution.
- In the 2.0.3 candidate, verified runtime files and their ancestor directories stay pinned for the Agent lifetime. Package paths containing reparse points are rejected. Stop FOG before replacing files.
- The 2.0.3 candidate targets .NET 10 LTS; build requirements are selected by global.json.
- Local pipe requests require a random per-session token and are restricted to the current Windows user.
- The current application, including its WebView2 UI, runs elevated because
  the packet-diversion driver operates at the Windows network layer. This
  increases impact if the UI is compromised. Separating an unelevated UI from
  a privileged broker remains a hardening task, not an implemented boundary.
- FOG Prime does not require a remote control server and does not intentionally collect telemetry.

## Current limitation

Application EXEs are not Authenticode-signed yet. The bundled original driver
has a separate upstream signature; it does not sign the FOG application.
Windows can therefore show an unknown publisher warning. A checksum and the
embedded trust chain can detect file replacement after a trusted UI binary
has been obtained, but do not replace authenticated distribution or publisher
code signing. A checksum served beside a compromised ZIP is not independent
proof of publisher identity.

File locks are not a security boundary against administrator/kernel compromise
or code executing inside FOG. Native-driver and long-session behavior require
separate testing. See docs/SECURITY-REVIEW-2.0.3.md for the scoped candidate
review and its residual risks; no claim of zero vulnerabilities is made.
